using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Lantern.Networking;

namespace Lantern.Security;

/// <summary>Mutual certificate pinning. Pair both installations through an independently trusted channel.</summary>
public sealed class TransferSecurity(PeerIdentity identity, PeerTrustStore trust)
{
    public PeerIdentity Identity { get; } =
        identity ?? throw new ArgumentNullException(nameof(identity));
    public PeerTrustStore Trust { get; } = trust ?? throw new ArgumentNullException(nameof(trust));

    internal async Task<Guid> AuthenticateAsync(
        Connection connection,
        bool server,
        Guid? expectedId,
        CancellationToken token
    )
    {
        var stream = new SslStream(
            connection.TransportStream,
            false,
            (_, certificate, _, _) => Trust.ValidateCertificate(certificate, expectedId)
        );
        try
        {
            if (server)
                await stream
                    .AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions
                        {
                            ServerCertificate = Identity.Certificate,
                            ClientCertificateRequired = true,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        },
                        token
                    )
                    .ConfigureAwait(false);
            else
                await stream
                    .AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions
                        {
                            TargetHost = expectedId!.Value.ToString("D"),
                            ClientCertificates = new() { Identity.Certificate },
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        },
                        token
                    )
                    .ConfigureAwait(false);
            using var peer = new X509Certificate2(stream.RemoteCertificate!);
            var id = PeerIdentity.GetDeviceId(peer);
            connection.UseAuthenticatedStream(stream);
            return id;
        }
        catch (IOException error)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new NetworkConnectionException("The TLS connection was interrupted.", error);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
