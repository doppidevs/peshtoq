using Peshtoq.Native;
using static Peshtoq.Native.NativeMethods;

namespace Peshtoq.Services.Link;

/// <summary>
/// Stage-2a input relay. While Link is connected it runs global hooks; a Scroll Lock double-tap
/// hands this machine's mouse/keyboard to the peer (Controller), and the peer replays them
/// (Target). Ctrl+Alt x3 is an always-available kill-switch. Control is auto-released when the
/// peer drops. See docs/link-input.md.
/// </summary>
public sealed class InputController : IInputHandler, IDisposable
{
    private const long ToggleWindowMs = 500;
    private const long KillWindowMs = 1200;
    private const int KillChords = 3;

    private readonly LinkService _link;
    private readonly InputCapture _capture;
    private readonly object _gate = new();

    private volatile bool _controlling;    // we send our input to the peer
    private volatile bool _beingControlled; // the peer sends us input
    private bool _capturing;

    private int _centerX, _centerY;

    // detection state (capture thread)
    private bool _ctrlDown, _altDown;
    private readonly List<long> _chordTimes = new();
    private long _lastScrollDown;

    // injected-state tracking on the Target, for release cleanup (guarded by _gate)
    private readonly HashSet<(ushort scan, bool ext)> _pressedKeys = new();
    private readonly HashSet<int> _pressedButtons = new();

    public event Action<bool>? ControllingChanged;
    public event Action<bool>? BeingControlledChanged;
    public event Action<string>? Toast;

    public bool Controlling => _controlling;
    public bool BeingControlled => _beingControlled;

    public InputController(LinkService link)
    {
        _link = link;
        _capture = new InputCapture(this);
        _link.StateChanged += OnStateChanged;
        _link.InputPacket += OnInputPacket;
    }

    private void OnStateChanged(LinkState s)
    {
        if (s == LinkState.Connected) EnsureCapture();
        else ReleaseAllAndStop();
    }

    private void EnsureCapture()
    {
        lock (_gate)
        {
            if (_capturing) return;
            _capturing = true;
            _capture.Start();
        }
    }

    private void ReleaseAllAndStop()
    {
        bool wasControlling, wasControlled;
        lock (_gate)
        {
            wasControlling = _controlling;
            wasControlled = _beingControlled;
            _controlling = false;
            _beingControlled = false;
            ReleasePressed_NoLock();
            _ctrlDown = _altDown = false;
            _chordTimes.Clear();
            if (_capturing) { _capturing = false; _capture.Stop(); }
        }
        if (wasControlling) ControllingChanged?.Invoke(false);
        if (wasControlled) BeingControlledChanged?.Invoke(false);
    }

    // ---------- capture side (runs on the hook thread; stays fast) ----------

    bool IInputHandler.OnMouseMove(int absX, int absY, bool injected)
    {
        if (injected || !_controlling) return false;
        int dx = Math.Clamp(absX - _centerX, short.MinValue, short.MaxValue);
        int dy = Math.Clamp(absY - _centerY, short.MinValue, short.MaxValue);
        if (dx != 0 || dy != 0)
        {
            _link.EnqueueSend(PacketType.MouseMove, Protocol.MouseMove((short)dx, (short)dy));
            SetCursorPos(_centerX, _centerY); // recenter so deltas stay clean and the local cursor is pinned
        }
        return true;
    }

    bool IInputHandler.OnMouseButton(int button, bool down, bool injected)
    {
        if (injected || !_controlling) return false;
        _link.EnqueueSend(PacketType.MouseButton, Protocol.MouseButton((byte)button, down));
        return true;
    }

    bool IInputHandler.OnWheel(int delta, bool horizontal, bool injected)
    {
        if (injected || !_controlling) return false;
        _link.EnqueueSend(PacketType.Wheel, Protocol.Wheel((short)Math.Clamp(delta, short.MinValue, short.MaxValue), horizontal));
        return true;
    }

    bool IInputHandler.OnKey(ushort vk, ushort scan, bool down, bool extended, bool injected)
    {
        if (injected) return false; // never recapture our own SendInput

        UpdateKill(vk, down); // may trigger the kill-switch

        if (vk == VK_SCROLL)
        {
            if (down) DetectToggle();
            return true; // reserve Scroll Lock for the toggle; never forwarded
        }

        if (_controlling)
        {
            _link.EnqueueSend(PacketType.Key, Protocol.Key(vk, scan, down, extended));
            return true; // suppress locally while controlling the peer
        }
        return false;
    }

    private void DetectToggle()
    {
        long now = Environment.TickCount64;
        if (_lastScrollDown != 0 && now - _lastScrollDown <= ToggleWindowMs)
        {
            _lastScrollDown = 0;
            ToggleControl();
        }
        else _lastScrollDown = now;
    }

    private void UpdateKill(ushort vk, bool down)
    {
        bool isCtrl = vk is 0x11 or 0xA2 or 0xA3;
        bool isAlt = vk is 0x12 or 0xA4 or 0xA5;
        if (!isCtrl && !isAlt) return;

        bool wasBoth = _ctrlDown && _altDown;
        if (isCtrl) _ctrlDown = down;
        if (isAlt) _altDown = down;
        bool isBoth = _ctrlDown && _altDown;

        if (!wasBoth && isBoth)
        {
            long now = Environment.TickCount64;
            _chordTimes.Add(now);
            _chordTimes.RemoveAll(t => now - t > KillWindowMs);
            if (_chordTimes.Count >= KillChords) { _chordTimes.Clear(); DoKill(); }
        }
    }

    private void ToggleControl()
    {
        bool turnOn;
        lock (_gate)
        {
            turnOn = !_controlling;
            _controlling = turnOn;
            if (turnOn)
            {
                _beingControlled = false; // taking control overrides being controlled
                _centerX = GetSystemMetrics(SM_CXSCREEN) / 2;
                _centerY = GetSystemMetrics(SM_CYSCREEN) / 2;
                SetCursorPos(_centerX, _centerY);
            }
        }
        _link.EnqueueSend(turnOn ? PacketType.ControlAcquire : PacketType.ControlRelease, new[] { (byte)0 });
        ControllingChanged?.Invoke(turnOn);
        Toast?.Invoke(turnOn ? "Boshqaruv peer'ga berildi (Scroll Lock ×2 — qaytarish)" : "Boshqaruv qaytarildi");
    }

    private void DoKill()
    {
        bool wasControlling, wasControlled;
        lock (_gate)
        {
            wasControlling = _controlling;
            wasControlled = _beingControlled;
            _controlling = false;
            _beingControlled = false;
            ReleasePressed_NoLock();
        }
        if (wasControlling) _link.EnqueueSend(PacketType.ControlRelease, new[] { (byte)1 });
        if (wasControlling) ControllingChanged?.Invoke(false);
        if (wasControlled) BeingControlledChanged?.Invoke(false);
        Toast?.Invoke("Boshqaruv uzildi (kill-switch)");
    }

    // ---------- target side (runs on the Link read thread) ----------

    private void OnInputPacket(PacketType type, byte[] payload)
    {
        switch (type)
        {
            case PacketType.ControlAcquire:
                if (!_beingControlled) { _beingControlled = true; BeingControlledChanged?.Invoke(true); Toast?.Invoke("Peer bu qurilmani boshqaryapti"); }
                break;
            case PacketType.ControlRelease:
                if (_beingControlled)
                {
                    lock (_gate) ReleasePressed_NoLock();
                    _beingControlled = false;
                    BeingControlledChanged?.Invoke(false);
                    Toast?.Invoke("Boshqaruv tugadi");
                }
                break;
            case PacketType.MouseMove when _beingControlled:
                var (dx, dy) = Protocol.ReadMouseMove(payload);
                InputInjector.MoveRelative(dx, dy);
                break;
            case PacketType.MouseButton when _beingControlled:
                var (btn, bdown) = Protocol.ReadMouseButton(payload);
                if (btn >= 0) { TrackButton(btn, bdown); InputInjector.Button(btn, bdown); }
                break;
            case PacketType.Wheel when _beingControlled:
                var (wd, wh) = Protocol.ReadWheel(payload);
                InputInjector.Wheel(wd, wh);
                break;
            case PacketType.Key when _beingControlled:
                var (_, scan, kdown, ext) = Protocol.ReadKey(payload);
                TrackKey(scan, ext, kdown);
                InputInjector.Key(scan, kdown, ext);
                break;
        }
    }

    private void TrackKey(ushort scan, bool ext, bool down)
    {
        lock (_gate)
        {
            if (down) _pressedKeys.Add((scan, ext));
            else _pressedKeys.Remove((scan, ext));
        }
    }

    private void TrackButton(int button, bool down)
    {
        lock (_gate)
        {
            if (down) _pressedButtons.Add(button);
            else _pressedButtons.Remove(button);
        }
    }

    /// <summary>Release every key/button we are still holding down on the Target. Call under _gate.</summary>
    private void ReleasePressed_NoLock()
    {
        foreach (var (scan, ext) in _pressedKeys) InputInjector.Key(scan, down: false, ext);
        foreach (var btn in _pressedButtons) InputInjector.Button(btn, down: false);
        _pressedKeys.Clear();
        _pressedButtons.Clear();
    }

    public void Dispose()
    {
        _link.StateChanged -= OnStateChanged;
        _link.InputPacket -= OnInputPacket;
        ReleaseAllAndStop();
        _capture.Dispose();
    }
}
