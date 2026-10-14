using Xunit;

namespace Lantern.Tests;

// These tests bind the same discovery UDP port; keep their socket lifetimes separate.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiscoveryTestCollection
{
    public const string Name = "LAN discovery with shared UDP port";
}
