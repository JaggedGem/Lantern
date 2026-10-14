namespace Lantern.Networking;

public sealed class ConnectionAcceptedEventArgs : EventArgs
{
    public ConnectionAcceptedEventArgs(Connection connection)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public Connection Connection { get; }
}

