using CNCSS.Data;
using CNCSS.Data.Config;
using CNCSS.Data.Tools;
using CNCSS.Geometry.SweptVolume;
using CNCSS.Logic.Voxel;
using CNCSS.UI;
using CNCSS.UI.Presenters;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CNCSS.Tests;

public sealed class VoxelStockVisualizationTests
{
    [Fact]
    public void Stock_approach_distance_is_zero_inside_bounds()
    {
        var volume = new StockVolumeConfig(0, 20, 0, 20, 0, 10);
        double inside = StockApproachHelper.DistancePointToBoundsMm(10, 10, 5, volume);
        Assert.Equal(0, inside, 3);
    }

    [Fact]
    public void Stock_approach_distance_measures_outside_gap()
    {
        var volume = new StockVolumeConfig(0, 20, 0, 20, 0, 10);
        double outside = StockApproachHelper.DistancePointToBoundsMm(-5, 10, 5, volume);
        Assert.Equal(5, outside, 3);
    }

    [Fact]
    public async Task VoxelStock_apply_cut_removes_voxels()
    {
        var volume = new StockVolumeConfig(0, 20, 0, 20, 0, 10);
        using var stock = VoxelStockVolume.Create(volume, null, Colors.LightGray, 1.0);
        stock.ApplyCutMotion(
            CutMotionDescriptor.Linear(new Point3D(10, 10, 10), new Point3D(10, 10, 3)),
            new FluteCutProfile { Kind = FluteProfileKind.EndMill, Radius = 3, FluteLength = 10 },
            Colors.Red);

        Assert.True(stock.ShellDisplayed);
        await stock.UpdateVisualsAsync();
        Assert.True(stock.AdaptiveStock.Octree.EnumerateAllChunks().Count > 0);
    }

    [Fact]
    public void VoxelStock_uses_constructor_color_when_default_color_omitted()
    {
        var volume = new StockVolumeConfig(0, 20, 0, 20, 0, 10);
        var lime = new StockColor(255, 80, 200, 50);
        var constructor = new StockConstructorConfig(
            StockShapeType.Rectangular,
            20, 20, 10, 0, 0,
            VoxelConstants.VoxelResolutionMm,
            "ColorTest",
            lime);

        using var stock = VoxelStockVolume.Create(volume, constructor, defaultColor: null);

        Assert.Equal(lime.R, stock.StockColor.R);
        Assert.Equal(lime.G, stock.StockColor.G);
        Assert.Equal(lime.B, stock.StockColor.B);
    }

    [Fact]
    public void CutSynchronizer_applies_backpressure_for_slow_d3d_frame()
    {
        var volume = new StockVolumeConfig(0, 20, 0, 20, 0, 10);
        using var stock = VoxelStockVolume.Create(volume, null, Colors.LightGray, 1.0);
        stock.LastRenderMs = VoxelConstants.PlaybackFrameBudgetMs * 2;

        var synchronizer = new CutVisualSynchronizer();
        SimulationFrameResult result = synchronizer.ProcessFrame(
            stock,
            isDryRun: false,
            canRemoveMaterial: false,
            CutMotionDescriptor.Linear(new Point3D(0, 0, 5), new Point3D(0, 0, 4)),
            new FluteCutProfile { Kind = FluteProfileKind.EndMill, Radius = 1, FluteLength = 10 },
            Colors.Red,
            deltaMs: 16f);

        Assert.True(result.PlaybackScale < 1.0);
    }

    [Fact]
    public void CutSynchronizer_removes_voxels_on_linear_motion()
    {
        var synchronizer = new CutVisualSynchronizer();
        var stock = new RecordingStockVolume();
        synchronizer.ProcessFrame(
            stock,
            isDryRun: false,
            canRemoveMaterial: true,
            CutMotionDescriptor.Linear(new Point3D(1, 2, 3), new Point3D(1, 2, 1)),
            new FluteCutProfile { Kind = FluteProfileKind.EndMill, Radius = 2, FluteLength = 10 },
            Colors.Red,
            deltaMs: 16f);

        Assert.Equal(1, stock.CutCount);
    }

    [Fact]
    public void StockRenderService_processes_cut_step()
    {
        var service = new StockRenderService();
        var tool = new ToolViewModel { Diameter = 10, FluteLength = 20, SelectedType = ToolType.EndMill };
        var stock = new RecordingStockVolume();

        SimulationFrameResult result = service.ProcessCutStep(
            isDryRunEnabled: false,
            stock,
            tool,
            canRemoveMaterial: true,
            CutMotionDescriptor.Linear(new Point3D(0, 0, 5), new Point3D(1, 0, 5)),
            deltaMs: 16f);

        Assert.True(result.ContactMeshReady);
        Assert.Equal(1, stock.CutCount);
    }

    [Fact]
    public void Flute_cut_envelope_contains_tool_sweep()
    {
        var profile = new FluteCutProfile { Kind = FluteProfileKind.EndMill, Radius = 5, FluteLength = 20 };
        var motion = CutMotionDescriptor.Linear(new Point3D(0, 0, 10), new Point3D(0, 0, 0));
        var sweep = SweptVolumeBuilder.BuildCylinderSweep(
            motion.Start,
            motion.End,
            profile.Radius,
            profile.FluteLength);
        var field = sweep.Field;

        Assert.True(field.Contains(new Point3D(0, 0, 5)));
        Assert.False(field.Contains(new Point3D(20, 0, 5)));
    }

    [Fact]
    public void Voxel_memory_budget_scales_with_volume()
    {
        var small = new StockVolumeConfig(0, 10, 0, 10, 0, 10);
        var large = new StockVolumeConfig(0, 100, 0, 100, 0, 50);
        ulong smallBudget = VoxelMemoryBudget.ComputeBudgetBytes(small);
        ulong largeBudget = VoxelMemoryBudget.ComputeBudgetBytes(large);
        Assert.True(largeBudget > smallBudget);
    }

    [Fact]
    public void Display_lod_stride_scales_for_fine_simulation_resolution()
    {
        int stride = VoxelConstants.ComputeDisplayLodStride(0.10);
        Assert.True(stride >= 3);
    }

    [Fact]
    public void Adaptive_display_grid_presets_match_requested_values()
    {
        Assert.Equal(0.50, VoxelConstants.NormalizeDisplayGridMm(0.5), 3);
        Assert.Equal(0.30, VoxelConstants.NormalizeDisplayGridMm(0.3), 3);
        Assert.Equal(0.10, VoxelConstants.NormalizeDisplayGridMm(0.1), 3);

        Assert.Equal(5, VoxelConstants.ComputeDisplayLodStride(0.10, 0.50));
        Assert.Equal(3, VoxelConstants.ComputeDisplayLodStride(0.10, 0.30));
        Assert.Equal(1, VoxelConstants.ComputeDisplayLodStride(0.10, 0.10));
    }

    private sealed class RecordingStockVolume : IStockVolume
    {
        public int CutCount { get; private set; }
        public Model3D MainModel { get; } = new Model3DGroup();
        public bool IsDirty => CutCount > 0;
        public int DirtyChunkCount => 0;
        public bool IsCutsFrozen => false;

        public void CutCylinder(Point3D start, Point3D end, double radius, double fluteLength, Color toolColor) =>
            ApplyCutMotion(
                CutMotionDescriptor.Linear(start, end),
                new FluteCutProfile { Kind = FluteProfileKind.EndMill, Radius = radius, FluteLength = fluteLength },
                toolColor);

        public void ApplyCutMotion(CutMotionDescriptor motion, FluteCutProfile profile, Color toolColor) =>
            CutCount++;

        public Task UpdateVisualsAsync() => Task.CompletedTask;
        public void AttachBoundingSolidPlaceholder(Color diffuse) { }
        public void FreezeModel() { }
    }
}
