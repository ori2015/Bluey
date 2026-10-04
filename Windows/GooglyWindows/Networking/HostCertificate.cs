using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GooglyWindows.Security;
namespace GooglyWindows.Networking;
public sealed class HostCertificate
{
    public static async Task<X509Certificate2> LoadAsync(ProtectedStore store, CancellationToken ct)
    {
        var saved = await store.ReadAsync<byte[]>("host-certificate", ct);
        if (saved is not null) return Import(saved);
        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=Googly Eyes", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var pfx = certificate.Export(X509ContentType.Pfx);
        await store.SaveAsync("host-certificate", pfx, ct);
        return Import(pfx);
    }
    // Schannel cannot use ephemeral private keys. Windows imports into the
    // current user's OS-protected key store; without PersistKeySet the backing
    // key is removed when the certificate is disposed. The durable PFX is DPAPI.
    private static X509Certificate2 Import(byte[] pfx) => X509CertificateLoader.LoadPkcs12(pfx, null,
        OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
    public static string Fingerprint(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));
}
