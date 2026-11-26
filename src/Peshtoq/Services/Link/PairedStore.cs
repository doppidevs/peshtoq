using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Peshtoq.Services.Link;

public sealed class PairedDevice
{
    public string Name { get; set; } = "";
    public string SpkiHash { get; set; } = "";
    public DateTime PairedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The allow-list of paired peers, keyed by pinned SPKI hash. Stored DPAPI-protected at
/// %AppData%\Peshtoq\paired.json. Only hashes are kept — the full key arrives on every TLS
/// handshake, so a hash is enough to verify a returning peer. See docs/link-pairing.md.
/// </summary>
public sealed class PairedStore
{
    private readonly string? _filePath;
    private readonly object _gate = new();
    private List<PairedDevice> _devices = new();

    public PairedStore() => _filePath = Path.Combine(AppPaths.DataDir, "paired.json");

    /// <summary>Custom path, or null for an in-memory store that never persists — for tests.</summary>
    internal PairedStore(string? filePath) => _filePath = filePath;

    public IReadOnlyList<PairedDevice> Devices
    {
        get { lock (_gate) return _devices.ToList(); }
    }

    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (_filePath is null || !File.Exists(_filePath)) return;
                var protectedBytes = File.ReadAllBytes(_filePath);
                var json = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                _devices = JsonSerializer.Deserialize<List<PairedDevice>>(json) ?? new();
            }
            catch
            {
                _devices = new(); // start empty on a broken/undecryptable file
            }
        }
    }

    public bool IsPinned(string spkiHash)
    {
        lock (_gate) return _devices.Any(d => d.SpkiHash == spkiHash);
    }

    public void Pin(string name, string spkiHash)
    {
        lock (_gate)
        {
            var existing = _devices.FirstOrDefault(d => d.SpkiHash == spkiHash);
            if (existing is not null) { existing.Name = name; existing.LastSeen = DateTime.UtcNow; }
            else _devices.Add(new PairedDevice { Name = name, SpkiHash = spkiHash });
            Save();
        }
    }

    public void MarkSeen(string spkiHash)
    {
        lock (_gate)
        {
            var d = _devices.FirstOrDefault(x => x.SpkiHash == spkiHash);
            if (d is null) return;
            d.LastSeen = DateTime.UtcNow;
            Save();
        }
    }

    public void Unpair(string spkiHash)
    {
        lock (_gate)
        {
            _devices.RemoveAll(d => d.SpkiHash == spkiHash);
            Save();
        }
    }

    private void Save()
    {
        if (_filePath is null) return; // in-memory
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(_devices);
            var protectedBytes = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_filePath, protectedBytes);
        }
        catch { }
    }
}
