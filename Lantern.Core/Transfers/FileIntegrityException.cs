namespace Lantern.Transfers;

public sealed class FileIntegrityException(string message) : IOException(message);
