using System.Net;

namespace Lantern.Models;

public class Device
{
    public Guid Id { get; }
    public string Name { get; }
    public IPAddress IpAddress { get; private set; }
    public int Port { get; private set; }

    public DateTime LastSeen { get; private set; }
    public DeviceStatus Status { get; private set; }

    public Device(Guid id, string name, IPAddress ipAddress, int port) {
        Id = id;
        Name = name;
        IpAddress = ipAddress;
        Port = port;

        LastSeen = DateTime.Now;
        Status = DeviceStatus.Unknown;
    }
    
    public void ChangePort(int newPort) {
        Port = newPort;
    }
    
    public void UpdateStatus(DeviceStatus newStatus) {
        Status = newStatus;
        LastSeen = DateTime.Now;
    }
    
    public void UpdateIpAddress(IPAddress newIpAddress) {
        IpAddress = newIpAddress;
    }
}