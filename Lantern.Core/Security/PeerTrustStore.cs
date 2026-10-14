using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Lantern.Configuration;

namespace Lantern.Security;

/// <summary>Explicit pairing only. Obtain fingerprints over an independently trusted channel.</summary>
public sealed class PeerTrustStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private Dictionary<Guid, string> _pins;

    public PeerTrustStore(string path)
    {
        _path = Path.GetFullPath(path);
        _pins = File.Exists(_path)
            ? JsonFile.Read<Dictionary<Guid, string>>(_path)
                ?? throw new InvalidDataException("Invalid peer store.")
            : new();
        foreach (var pin in _pins)
            Validate(pin.Key, pin.Value);
    }

    public IReadOnlyDictionary<Guid, string> GetTrustedPeers()
    {
        lock (_sync)
            return new Dictionary<Guid, string>(_pins);
    }

    public void TrustPeer(Guid deviceId, string fingerprint)
    {
        Validate(deviceId, fingerprint);
        lock (_sync)
        {
            var updated = new Dictionary<Guid, string>(_pins)
            {
                [deviceId] = fingerprint.ToUpperInvariant(),
            };
            JsonFile.Write(_path, updated);
            _pins = updated;
        }
    }

    public void RemovePeer(Guid deviceId)
    {
        lock (_sync)
        {
            var updated = new Dictionary<Guid, string>(_pins);
            updated.Remove(deviceId);
            JsonFile.Write(_path, updated);
            _pins = updated;
        }
    }

    internal bool ValidateCertificate(X509Certificate? certificate, Guid? expectedId = null)
    {
        if (certificate is null)
            return false;
        try
        {
            using var peer = new X509Certificate2(certificate);
            var id = PeerIdentity.GetDeviceId(peer);
            if (expectedId.HasValue && id != expectedId)
                return false;
            if (
                DateTime.UtcNow < peer.NotBefore.ToUniversalTime()
                || DateTime.UtcNow > peer.NotAfter.ToUniversalTime()
            )
                return false;
            lock (_sync)
                return _pins.TryGetValue(id, out var pin)
                    && CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(pin),
                        peer.GetCertHash(HashAlgorithmName.SHA256)
                    );
        }
        catch (Exception ex)
            when (ex is CryptographicException or InvalidDataException or FormatException)
        {
            return false;
        }
    }

    private static void Validate(Guid id, string fingerprint)
    {
        if (
            id == Guid.Empty
            || fingerprint is null
            || fingerprint.Length != 64
            || !fingerprint.All(Uri.IsHexDigit)
        )
            throw new ArgumentException(
                "A device ID and 64-character SHA-256 fingerprint are required."
            );
    }
}
