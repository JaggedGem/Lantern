using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Lantern.Transfers;

public static class TransferCertificate
{
    public static X509Certificate2 LoadOrCreate(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "identity.pfx");
        if (File.Exists(path)) return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=Lantern", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        var bytes = certificate.Export(X509ContentType.Pfx);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var file = new FileStream(path, options)) file.Write(bytes);
        return X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.Exportable);
    }

    public static string Fingerprint(X509Certificate certificate) => Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
}
