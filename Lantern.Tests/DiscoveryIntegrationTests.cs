using System.Net;
using Lantern.Models;
using Lantern.Networking.Discovery;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Integration tests for discovery.
/// Note: True cross-instance discovery requires different machines or network interface simulation.
/// These tests verify the behavior under realistic conditions.
/// </summary>
public sealed class DiscoveryIntegrationTests : IAsyncLifetime
{
    private DeviceDiscovery? _discovery1;
    private LocalDevice? _localDevice1;

    public async Task InitializeAsync()
    {
        // Create local device
        _localDevice1 = new LocalDevice(Guid.NewGuid(), "Device1", 5001);

        // Create discovery instance
        _discovery1 = new DeviceDiscovery(_localDevice1);
    }

    public async Task DisposeAsync()
    {
        if (_discovery1 is not null)
        {
            await _discovery1.DisposeAsync();
        }
    }

    [Fact]
    public async Task SingleDiscoveryInstance_StartsAndAnnouncesWithoutErrors()
    {
        Assert.NotNull(_discovery1);
        Assert.NotNull(_localDevice1);

        // Start discovery - this will bind to UDP port and send announcements
        await _discovery1.StartAsync();
        
        // Wait for a few announcements to be sent
        await Task.Delay(1000);

        // Verify it's running
        Assert.True(_discovery1.IsRunning);

        // Get discovered devices (should be empty since we're alone)
        var devices = _discovery1.GetDiscoveredDevices();
        Assert.Empty(devices);

        // Stop discovery
        await _discovery1.StopAsync();
        Assert.False(_discovery1.IsRunning);
    }

    [Fact]
    public async Task Discovery_DoesNotDiscoverItself()
    {
        Assert.NotNull(_discovery1);
        Assert.NotNull(_localDevice1);

        var selfDiscoveredEvent = false;
        _discovery1.DeviceDiscovered += (_, e) =>
        {
            if (e.Device.Id == _localDevice1.Id)
            {
                selfDiscoveredEvent = true;
            }
        };

        await _discovery1.StartAsync();
        
        // Give it time to send its own announcements
        await Task.Delay(1000);

        await _discovery1.StopAsync();

        // Local device should never be in the discovered list
        Assert.False(selfDiscoveredEvent);
        var devices = _discovery1.GetDiscoveredDevices();
        Assert.DoesNotContain(devices, d => d.Id == _localDevice1.Id);
    }

    [Fact]
    public async Task Discovery_SupervisedEventHandling()
    {
        Assert.NotNull(_discovery1);
        Assert.NotNull(_localDevice1);

        int deviceDiscoveredCount = 0;
        int deviceStatusChangedCount = 0;
        int deviceRemovedCount = 0;

        _discovery1.DeviceDiscovered += (_, _) => Interlocked.Increment(ref deviceDiscoveredCount);
        _discovery1.DeviceStatusChanged += (_, _) => Interlocked.Increment(ref deviceStatusChangedCount);
        _discovery1.DeviceRemoved += (_, _) => Interlocked.Increment(ref deviceRemovedCount);

        await _discovery1.StartAsync();
        
        // Run for a brief period
        await Task.Delay(500);

        await _discovery1.StopAsync();

        // Since we're alone, no events should fire
        Assert.Equal(0, deviceDiscoveredCount);
        Assert.Equal(0, deviceStatusChangedCount);
        Assert.Equal(0, deviceRemovedCount);
    }
}



