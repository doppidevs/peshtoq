using NAudio.CoreAudioApi;

namespace Peshtoq.Services;

/// <summary>Master volume of the default playback device (Core Audio via NAudio).</summary>
public sealed class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private string? _deviceId;
    private DateTime _lastLookup = DateTime.MinValue;

    private MMDevice? Device()
    {
        // The default device can change (headphones plugged in); look it up again every few seconds.
        if (_device is not null && (DateTime.Now - _lastLookup).TotalSeconds < 5) return _device;
        _lastLookup = DateTime.Now;
        try
        {
            var d = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (d.ID != _deviceId)
            {
                _device?.Dispose();
                _device = d;
                _deviceId = d.ID;
            }
            else
            {
                d.Dispose();
            }
        }
        catch
        {
            _device = null;
            _deviceId = null;
        }
        return _device;
    }

    public (int Percent, bool Muted)? Get()
    {
        try
        {
            var v = Device()?.AudioEndpointVolume;
            return v is null ? null : ((int)Math.Round(v.MasterVolumeLevelScalar * 100), v.Mute);
        }
        catch
        {
            _device = null;
            return null;
        }
    }

    public void Change(int deltaPercent)
    {
        try
        {
            var v = Device()?.AudioEndpointVolume;
            if (v is null) return;
            v.Mute = false;
            v.MasterVolumeLevelScalar = Math.Clamp(v.MasterVolumeLevelScalar + deltaPercent / 100f, 0f, 1f);
        }
        catch { _device = null; }
    }

    public void ToggleMute()
    {
        try
        {
            var v = Device()?.AudioEndpointVolume;
            if (v is not null) v.Mute = !v.Mute;
        }
        catch { _device = null; }
    }

    public void Dispose()
    {
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
