using Cascade.Sim;

namespace Cascade.Sim.Tests;

public class SetupTests
{
    [Fact]
    public void SimLibraryLoads()
    {
        Assert.Equal("The Veyl Crossing", SimInfo.Slice);
    }

    // The sim must never depend on Godot: the game reads snapshots and sends orders, nothing more.
    [Fact]
    public void SimHasNoGodotReference()
    {
        var refs = typeof(SimInfo).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, r => r.Name!.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }
}
