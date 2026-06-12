using System.IO;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class ToolpathProfileIntegrationTests
{
    [Fact]
    public void O0001_WithDefaultProfile_ToolpathSpansXyAndZ()
    {
        string profilePath = Path.Combine(
            MachineProfileStore.GetDefaultRootDirectory(),
            "default",
            "machine.json");
        if (!File.Exists(profilePath))
        {
            return;
        }

        string ncPath = Path.Combine(AppContext.BaseDirectory, "O0001.nc");
        if (!File.Exists(ncPath))
        {
            ncPath = Path.Combine(Directory.GetCurrentDirectory(), "O0001.nc");
        }

        if (!File.Exists(ncPath))
        {
            return;
        }

        var store = new MachineProfileStore();
        MachineDefinition profile = store.Load("default");

        var seed = new MachineState();
        profile.ApplyHomeToMachineState(seed);

        var parser = new GCodeParser(seed.Clone());
        parser.ProcessFile(ncPath);

        var segments = ToolpathBuilder.BuildWithLineNumbers(parser, seed, profile);
        Assert.NotEmpty(segments);

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;

        foreach (var item in segments)
        {
            foreach (Point3D p in item.Segment.Points)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
            }
        }

        Assert.True(maxX - minX > 50, $"X span too small: {minX}..{maxX}");
        Assert.True(maxY - minY > 50, $"Y span too small: {minY}..{maxY}");
        Assert.True(maxZ - minZ > 1, $"Z span too small: {minZ}..{maxZ}");
    }
}
