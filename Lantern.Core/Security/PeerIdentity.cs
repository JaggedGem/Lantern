using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Lantern.Security;

/// <summary>Stable local certificate. The containing directory must be private to the OS user.</summary>
public sealed class PeerIdentity : IDisposable
{
    // Schannel needs a named private key on Windows. Do not use PersistKeySet:
    // the imported key container should be removed when the certificate is disposed.
    // Other platforms can keep the imported key entirely in memory.
    private static X509KeyStorageFlags KeyStorageFlags =>
        X509KeyStorageFlags.Exportable
        | (OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.UserKeySet
            : X509KeyStorageFlags.EphemeralKeySet);

    public X509Certificate2 Certificate { get; }
    public string Fingerprint => Certificate.GetCertHashString(HashAlgorithmName.SHA256);
    public Guid DeviceId { get; }

    private PeerIdentity(X509Certificate2 certificate)
    {
        Certificate = certificate;
        DeviceId = GetDeviceId(certificate);
    }

    public static PeerIdentity LoadOrCreate(string path, Guid deviceId)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("Device ID cannot be empty.", nameof(deviceId));
        path = Path.GetFullPath(path);
        if (File.Exists(path))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                path,
                null,
                KeyStorageFlags
            );
            if (
                !certificate.HasPrivateKey
                || GetDeviceId(certificate) != deviceId
                || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow
            )
            {
                certificate.Dispose();
                throw new InvalidDataException(
                    "Local certificate is invalid or expired. Restore the identity or explicitly pair a new identity."
                );
            }
            return new(certificate);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={deviceId:D}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true)
        );
        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5)
        );
        // CreateNew prevents concurrent processes from replacing an established identity.
        using (
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
        )
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            file.Write(created.Export(X509ContentType.Pkcs12));
            file.Flush(true);
        }
        return new(
            X509CertificateLoader.LoadPkcs12FromFile(
                path,
                null,
                KeyStorageFlags
            )
        );
    }

    internal static Guid GetDeviceId(X509Certificate2 certificate)
    {
        if (
            !Guid.TryParse(certificate.GetNameInfo(X509NameType.SimpleName, false), out var id)
            || id == Guid.Empty
        )
            throw new InvalidDataException("Certificate has no valid Lantern device ID.");
        return id;
    }

    public void Dispose() => Certificate.Dispose();
}
