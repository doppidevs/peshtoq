using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Peshtoq.Services.Link;

/// <summary>
/// An established mutual-TLS link to one peer, plus the SAS code the user verifies on first pair.
/// Owns the underlying socket; dispose to close.
/// </summary>
public sealed class LinkConnection : IDisposable
{
    public required SslStream Stream { get; init; }
    public required X509Certificate2 PeerCertificate { get; init; }
    public required string PeerSpkiHash { get; init; }
    public required string Sas { get; init; }   // "042 918" — compare on both screens
    private TcpClient? _tcp;

    internal void Own(TcpClient tcp) => _tcp = tcp;

    public void Dispose()
    {
        try { Stream.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
    }
}

/// <summary>
/// Opens mutual-TLS links (client and server side) and computes the SAS. The TLS chain is NOT
/// validated against a CA — trust comes from the SAS on first pair and from the pinned key
/// afterwards, both checked by the caller (LinkService). See docs/link-pairing.md.
/// </summary>
public static class LinkTransport
{
    private static readonly System.Security.Authentication.SslProtocols Protocols =
        System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;

    public static async Task<LinkConnection> ConnectAsync(string host, int port, X509Certificate2 identity, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        tcp.NoDelay = true;
        await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);

        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, AcceptAnyCert);
        var opts = new SslClientAuthenticationOptions
        {
            TargetHost = "peshtoq", // not validated; we authenticate via SAS/pin
            ClientCertificates = new X509CertificateCollection { identity },
            EnabledSslProtocols = Protocols,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };
        await ssl.AuthenticateAsClientAsync(opts, ct).ConfigureAwait(false);

        var conn = Build(ssl, identity);
        conn.Own(tcp);
        return conn;
    }

    public static async Task<LinkConnection> AcceptAsync(TcpClient tcp, X509Certificate2 identity, CancellationToken ct = default)
    {
        tcp.NoDelay = true;
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, AcceptAnyCert);
        var opts = new SslServerAuthenticationOptions
        {
            ServerCertificate = identity,
            ClientCertificateRequired = true,
            EnabledSslProtocols = Protocols,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };
        await ssl.AuthenticateAsServerAsync(opts, ct).ConfigureAwait(false);

        var conn = Build(ssl, identity);
        conn.Own(tcp);
        return conn;
    }

    private static bool AcceptAnyCert(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors) => true;

    private static LinkConnection Build(SslStream ssl, X509Certificate2 identity)
    {
        if (ssl.RemoteCertificate is null)
            throw new AuthenticationExceptionLite("peer presented no certificate");

        var peer = ssl.RemoteCertificate as X509Certificate2 ?? new X509Certificate2(ssl.RemoteCertificate);
        string peerHash = IdentityService.SpkiHashOf(peer);
        string sas = ComputeSas(identity, peer);

        return new LinkConnection
        {
            Stream = ssl,
            PeerCertificate = peer,
            PeerSpkiHash = peerHash,
            Sas = sas,
        };
    }

    /// <summary>
    /// SAS = first 4 bytes of SHA256( sort(SPKI_a, SPKI_b) ) as a 6-digit number, grouped "ddd ddd".
    /// Sorting the two SubjectPublicKeyInfo blobs makes both honest sides compute the same code;
    /// a MITM sees a different key pair on each leg and the two screens disagree.
    /// </summary>
    public static string ComputeSas(X509Certificate2 a, X509Certificate2 b)
    {
        byte[] sa = a.PublicKey.ExportSubjectPublicKeyInfo();
        byte[] sb = b.PublicKey.ExportSubjectPublicKeyInfo();

        (byte[] x, byte[] y) = Compare(sa, sb) <= 0 ? (sa, sb) : (sb, sa);
        var combined = new byte[x.Length + y.Length];
        x.CopyTo(combined, 0);
        y.CopyTo(combined, x.Length);

        byte[] h = SHA256.HashData(combined);
        uint n = BinaryPrimitives.ReadUInt32BigEndian(h) % 1_000_000;
        return $"{n / 1000:D3} {n % 1000:D3}";
    }

    private static int Compare(byte[] a, byte[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
            if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
        return a.Length.CompareTo(b.Length);
    }
}

/// <summary>A lightweight auth failure that does not pull in extra using directives at call sites.</summary>
public sealed class AuthenticationExceptionLite : Exception
{
    public AuthenticationExceptionLite(string message) : base(message) { }
}
