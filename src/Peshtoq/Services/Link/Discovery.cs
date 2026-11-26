using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Peshtoq.Services.Link;

public sealed class DiscoveredPeer
{
    public string Id { get; init; } = "";       // peer SPKI hash
    public string Host { get; init; } = "";      // friendly hostname
    public IPAddress Address { get; init; } = IPAddress.None;
    public int Port { get; init; }               // peer's TLS listener port
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// LAN presence over UDP broadcast: each instance announces {id, host, port} on a fixed port
/// and listens for others. No security here — discovery only says "a Peshtoq is at this address";
/// trust is established later by pairing (SAS) over TLS. Only private/link-local senders are kept.
/// </summary>
public sealed class Discovery : IDisposable
{
    public const int DiscoveryPort = 8788;
    private static readonly TimeSpan AnnounceEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PeerTtl = TimeSpan.FromSeconds(8);

    private readonly string _selfId;
    private readonly int _tcpPort;
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, DiscoveredPeer> _peers = new();
    private readonly object _gate = new();

    public event Action<DiscoveredPeer>? PeerSeen;

    public Discovery(string selfId, int tcpPort)
    {
        _selfId = selfId;
        _tcpPort = tcpPort;
        _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
    }

    public IReadOnlyList<DiscoveredPeer> Peers
    {
        get { lock (_gate) return _peers.Values.ToList(); }
    }

    public void Start()
    {
        _ = Task.Run(ReceiveLoop);
        _ = Task.Run(AnnounceLoop);
    }

    private async Task AnnounceLoop()
    {
        var ep = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var msg = JsonSerializer.SerializeToUtf8Bytes(new Announce { Id = _selfId, Host = Environment.MachineName, Port = _tcpPort });
                await _udp.SendAsync(msg, msg.Length, ep).ConfigureAwait(false);
            }
            catch { }
            ExpireStale();
            try { await Task.Delay(AnnounceEvery, _cts.Token).ConfigureAwait(false); } catch { break; }
        }
    }

    private async Task ReceiveLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await _udp.ReceiveAsync(_cts.Token).ConfigureAwait(false); }
            catch { break; }

            if (!IsLocalRange(res.RemoteEndPoint.Address)) continue;
            Announce? a;
            try { a = JsonSerializer.Deserialize<Announce>(res.Buffer); } catch { continue; }
            if (a is null || string.IsNullOrEmpty(a.Id) || a.Id == _selfId) continue; // skip self

            DiscoveredPeer peer;
            lock (_gate)
            {
                if (!_peers.TryGetValue(a.Id, out var existing))
                {
                    existing = new DiscoveredPeer { Id = a.Id };
                    _peers[a.Id] = existing;
                }
                peer = new DiscoveredPeer
                {
                    Id = a.Id,
                    Host = a.Host,
                    Address = res.RemoteEndPoint.Address,
                    Port = a.Port,
                    LastSeen = DateTime.UtcNow,
                };
                _peers[a.Id] = peer;
            }
            PeerSeen?.Invoke(peer);
        }
    }

    private void ExpireStale()
    {
        lock (_gate)
        {
            var dead = _peers.Where(kv => DateTime.UtcNow - kv.Value.LastSeen > PeerTtl).Select(kv => kv.Key).ToList();
            foreach (var id in dead) _peers.Remove(id);
        }
    }

    private static bool IsLocalRange(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        byte[] b = ip.GetAddressBytes();
        if (b.Length != 4) return false; // IPv4 only for now
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254); // link-local
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
        _cts.Dispose();
    }

    private sealed class Announce
    {
        public string Id { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; }
    }
}
