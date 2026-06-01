using CNCSS.Machine.Model;

namespace CNCSS.Tests;

public sealed class MachineAssemblyMetricsTests
{
    [Fact]
    public void RelocateMcsMarker_PreservesPhysicalHome_RecalculatesHomeMcs()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 100, Y = 50, Z = 200 };
        def.Axes.First(a => a.Name == "X").Home = 0;
        def.Axes.First(a => a.Name == "Y").Home = 0;
        def.Axes.First(a => a.Name == "Z").Home = 0;
        def.SyncHomePositionFromAxes();

        var physicalBefore = def.GetPhysicalHomePosition();

        def.RelocateMcsMarkerPreservingPhysicalHome(new MachineGeometryPoint { X = 400, Y = 200, Z = 100 });

        var physicalAfter = def.GetPhysicalHomePosition();
        Assert.Equal(physicalBefore.X, physicalAfter.X, 3);
        Assert.Equal(physicalBefore.Y, physicalAfter.Y, 3);
        Assert.Equal(physicalBefore.Z, physicalAfter.Z, 3);

        Assert.Equal(physicalBefore.X - 400, def.GetAxisHomeMcs("X"), 3);
        Assert.Equal(physicalBefore.Y - 200, def.GetAxisHomeMcs("Y"), 3);
        Assert.Equal(physicalBefore.Z - 100, def.GetAxisHomeMcs("Z"), 3);
    }
}
