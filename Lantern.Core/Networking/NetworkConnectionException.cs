namespace Lantern.Networking;

/// <summary>An I/O failure in network transport, distinct from local file I/O.</summary>
public sealed class NetworkConnectionException(string message, Exception innerException)
    : IOException(message, innerException);
