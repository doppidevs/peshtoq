using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Peshtoq.Services.Link;

/// <summary>
/// This machine's stable Link identity: a self-signed ECDSA P-256 certificate created once
/// on first use and reused forever. Its public key (SPKI) is the device's permanent id that
/// pairing pins. The PFX (with the private key) is stored DPAPI-protected and never leaves disk.
/// See docs/link-pairing.md.
/// </summary>
public sealed class IdentityService
{
    private static string PfxPath => Path.Combine(AppPaths.DataDir, "identity.pfx");

    public X509Certificate2 Certificate { get; }

    /// <summary>Hex SHA-256 of our own SubjectPublicKeyInfo — our device id.</summary>
    public string SpkiHash { get; }

    public IdentityService()
    {
        Certificate = LoadOrCreate();
        SpkiHash = SpkiHashOf(Certificate);
    }

    private IdentityService(X509Certificate2 cert)
    {
        Certificate = cert;
        SpkiHash = SpkiHashOf(cert);
    }

    /// <summary>A fresh in-memory identity that never touches disk — for tests/loopback.</summary>
    public static IdentityService CreateEphemeral() => new(Create());

    private static X509Certificate2 LoadOrCreate()
    {
        if (File.Exists(PfxPath))
        {
            try
            {
                var protectedBytes = File.ReadAllBytes(PfxPath);
                var pfx = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                // Exportable + the default user key set: EphemeralKeySet does NOT work with
                // SslStream server auth on Windows (Schannel), so avoid it here.
                return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
            }
            catch
            {
                // corrupt or undecryptable (e.g. restored on another account): regenerate below
            }
        }

        var cert = Create();
        try
        {
            var pfx = cert.Export(X509ContentType.Pfx);
            var protectedBytes = ProtectedData.Protect(pfx, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(PfxPath, protectedBytes);
        }
        catch
        {
            // if we cannot persist, we still return a working (session-only) identity
        }
        return cert;
    }

    private static X509Certificate2 Create()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN=Peshtoq-{Environment.MachineName}", ec, HashAlgorithmName.SHA256);
        var now = DateTimeOffset.UtcNow;
        var cert = req.CreateSelfSigned(now.AddDays(-1), now.AddYears(20));

        // Round-trip through PFX so the returned cert carries an exportable, SslStream-usable key.
        var pfx = cert.Export(X509ContentType.Pfx);
        return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>Hex SHA-256 of a certificate's SubjectPublicKeyInfo (DER). Used as the pinned id.</summary>
    public static string SpkiHashOf(X509Certificate2 cert)
    {
        byte[] spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
        return Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
    }
}
