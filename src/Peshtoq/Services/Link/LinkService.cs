using System.Net;
using System.Net.Sockets;

namespace Peshtoq.Services.Link;

public enum LinkState { Idle, Listening, Handshaking, Pairing, Connected, Disconnected }

/// <summary>What the UI shows the user when an unpaired peer connects: compare this SAS on both screens.</summary>
public sealed record PairingPrompt(string PeerName, string Sas, bool Incoming);

/// <summary>
/// Stage-0 orchestrator for Peshtoq Link: owns the identity, the paired allow-list, LAN discovery
/// and a TLS listener, and drives the connect → SAS/pin → heartbeat flow for a single peer.
/// Input capture/injection is NOT here yet (stage 2). Nothing starts until Start() is called, so
/// the feature stays dormant and opt-in. See docs/mouse-without-borders.md and docs/link-pairing.md.
/// </summary>
public sealed class LinkService : IDisposable
{
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(4);

    private readonly IdentityService _identity;
    private readonly PairedStore _paired;
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private Discovery? _discovery;
    private LinkConnection? _active;

    /// <summary>UI supplies this: show the SAS and return true if the user confirms the codes match.</summary>
    public Func<PairingPrompt, Task<bool>>? ConfirmPairing { get; set; }

    public event Action<LinkState>? StateChanged;
    public event Action<string>? Status;
    public event Action<DiscoveredPeer>? PeerSeen;

    public LinkState State { get; private set; } = LinkState.Idle;
    public int Port { get; private set; }
    public IReadOnlyList<DiscoveredPeer> Peers => _discovery?.Peers ?? Array.Empty<DiscoveredPeer>();
    public IReadOnlyList<PairedDevice> Paired => _paired.Devices;

    public LinkService(IdentityService identity, PairedStore paired)
    {
        _identity = identity;
        _paired = paired;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();

        _listener = new TcpListener(IPAddress.Any, 0); // 0 = OS picks a free port
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _discovery = new Discovery(_identity.SpkiHash, Port);
        _discovery.PeerSeen += p => PeerSeen?.Invoke(p);
        _discovery.Start();

        Set(LinkState.Listening);
        Log($"Tinglayapti — port {Port}, id {_identity.SpkiHash[..8]}…");
        _ = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Unpair(string spkiHash) => _paired.Unpair(spkiHash);

    /// <summary>Drop the active peer connection, if any; the listener keeps running.</summary>
    public void Disconnect()
    {
        _active?.Dispose();
        _active = null;
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch { break; }

            _ = Task.Run(async () =>
            {
                try
                {
                    var conn = await LinkTransport.AcceptAsync(tcp, _identity.Certificate, ct).ConfigureAwait(false);
                    await HandleConnectionAsync(conn, incoming: true, ct).ConfigureAwait(false);
                }
                catch (Exception ex) { Log($"Kiruvchi ulanish xatosi: {ex.Message}"); tcp.Dispose(); }
            }, ct);
        }
    }

    public async Task ConnectAsync(DiscoveredPeer peer)
    {
        if (_cts is null) throw new InvalidOperationException("Start() avval chaqirilishi kerak");
        var ct = _cts.Token;
        Set(LinkState.Handshaking);
        try
        {
            var conn = await LinkTransport.ConnectAsync(peer.Address.ToString(), peer.Port, _identity.Certificate, ct).ConfigureAwait(false);
            await HandleConnectionAsync(conn, incoming: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"Ulanib bo'lmadi: {ex.Message}");
            Set(LinkState.Listening);
        }
    }

    private async Task HandleConnectionAsync(LinkConnection conn, bool incoming, CancellationToken ct)
    {
        // Read the peer's Hello so we have a friendly name for the prompt.
        await Protocol.WriteFrameAsync(conn.Stream, PacketType.Hello, Protocol.HelloPayload(Environment.MachineName), ct).ConfigureAwait(false);
        var hello = await Protocol.ReadFrameAsync(conn.Stream, ct).ConfigureAwait(false);
        string peerName = hello is { } h && h.type == PacketType.Hello ? Protocol.ParseHello(h.payload).hostname : "Noma'lum";

        bool trusted = _paired.IsPinned(conn.PeerSpkiHash);
        if (!trusted)
        {
            Set(LinkState.Pairing);
            Log($"Juftlash so'rovi: {peerName} — kod {conn.Sas}");
            bool ok = ConfirmPairing is not null && await ConfirmPairing(new PairingPrompt(peerName, conn.Sas, incoming)).ConfigureAwait(false);
            if (!ok)
            {
                Log("Juftlash rad etildi");
                conn.Dispose();
                Set(LinkState.Listening);
                return;
            }
            _paired.Pin(peerName, conn.PeerSpkiHash);
            Log($"Juftlandi: {peerName}");
        }
        else
        {
            // Returning peer: the pinned key already matched (SPKI hash is derived from the cert).
            _paired.MarkSeen(conn.PeerSpkiHash);
        }

        _active?.Dispose();
        _active = conn;
        Set(LinkState.Connected);
        Log($"Ulandi: {peerName}");
        await RunConnection(conn, ct).ConfigureAwait(false);
    }

    private async Task RunConnection(LinkConnection conn, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var beat = Task.Run(() => HeartbeatSender(conn, linked.Token), linked.Token);
        var last = DateTime.UtcNow;
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var readTask = Protocol.ReadFrameAsync(conn.Stream, linked.Token);
                var done = await Task.WhenAny(readTask, Task.Delay(PeerTimeout, linked.Token)).ConfigureAwait(false);
                if (done != readTask)
                {
                    if (DateTime.UtcNow - last > PeerTimeout) { Log("Peer javob bermadi — uzildi"); break; }
                    continue;
                }
                var frame = await readTask.ConfigureAwait(false);
                if (frame is null) { Log("Peer ulanishni yopdi"); break; }
                last = DateTime.UtcNow;
                // Stage 0: only Hello/Heartbeat are expected; later stages dispatch here.
            }
        }
        catch (Exception ex) { Log($"Ulanish xatosi: {ex.Message}"); }
        finally
        {
            linked.Cancel();
            conn.Dispose();
            if (_active == conn) _active = null;
            Set(LinkState.Listening);
        }
    }

    private async Task HeartbeatSender(LinkConnection conn, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Protocol.WriteFrameAsync(conn.Stream, PacketType.Heartbeat, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false); }
            catch { break; }
            try { await Task.Delay(HeartbeatEvery, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    private void Set(LinkState s) { State = s; StateChanged?.Invoke(s); }
    private void Log(string msg) => Status?.Invoke(msg);

    public void Dispose()
    {
        _cts?.Cancel();
        _active?.Dispose();
        try { _listener?.Stop(); } catch { }
        _discovery?.Dispose();
        _cts?.Dispose();
        _cts = null;
    }
}
