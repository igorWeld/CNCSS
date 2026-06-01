using CNCSS.Machine.Model;

namespace CNCSS.Tests;

public sealed class MachineMcsCoordinatesTests
{
    [Fact]
    public void SceneAxisLimit_IsAttachPlusMcsPlusAxisLimit()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 400, Y = 200, Z = 100 };
        def.Table.AttachOnParent = new MachineGeometryPoint { X = 100, Y = 200, Z = 100 };
        MachineAxisDefinition x = def.Axes.First(a => a.Name == "X");
        x.Min = -100;
        x.Max = 100;

        Assert.Equal(600, MachineMcsCoordinates.GetSceneAxisLimit(def, "X", x.Max), 3);
        Assert.Equal(400, MachineMcsCoordinates.GetSceneAxisLimit(def, "X", x.Min), 3);

        var (sceneMin, sceneMax) = def.GetSceneAxisTravel("X");
        Assert.Equal(400, sceneMin, 3);
        Assert.Equal(600, sceneMax, 3);
    }

    [Fact]
    public void ProgramPhysicalLimits_AreMcsLimitsPlusMcsOffset()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 400, Y = 0, Z = 0 };
        def.Axes.First(a => a.Name == "X").Min = -100;
        def.Axes.First(a => a.Name == "X").Max = 100;

        (double min, double max) = MachineMcsCoordinates.GetProgramPhysicalAxisLimits(def, "X");
        Assert.Equal(300, min, 3);
        Assert.Equal(500, max, 3);
    }

    [Fact]
    public void McsToScene_AddsOffsetComponentWise()
    {
        var mcs = new MachineGeometryPoint { X = 400, Y = 200, Z = 100 };
        var point = new MachineGeometryPoint { X = 100, Y = 0, Z = -50 };
        var scene = MachineMcsCoordinates.McsToScene(point, mcs);
        Assert.Equal(500, scene.X, 3);
        Assert.Equal(200, scene.Y, 3);
        Assert.Equal(50, scene.Z, 3);
    }
}
