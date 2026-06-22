// CNCSS.Logic/Voxel/CutVisualSynchronizer.cs
using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data.Config;
using CNCSS.Data.Tools;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;

namespace CNCSS.Logic.Voxel
{
    public sealed record SimulationFrameResult(
        bool ContactMeshReady,
        int MeshesBuilt,
        double PlaybackScale);

    public sealed class CutVisualSynchronizer
    {
        private double _smoothedFrameMs = VoxelConstants.PlaybackFrameBudgetMs;
        private double _smoothedScale = 1.0;

        public SimulationFrameResult ProcessFrame(
            IStockVolume? stock,
            bool isDryRun,
            bool canRemoveMaterial,
            CutMotionDescriptor motion,
            FluteCutProfile profile,
            Color toolColor,
            float deltaMs)
        {
            if (stock == null || isDryRun)
            {
                return new SimulationFrameResult(true, 0, 1.0);
            }

            if (canRemoveMaterial && IsActiveCut(motion.Start, motion.End))
            {
                stock.ApplyCutMotion(motion, profile, toolColor);
            }

            if (stock is VoxelStockVolume voxelStock)
            {
                // Учитываем mesh/apply только когда визуал реально в работе, иначе не душим playback
                // старыми тяжелыми метриками прошлого кадра.
                bool hasVisualWork = voxelStock.HasPendingVisualWork;
                double rawFrameMs = voxelStock.LastCutMs
                    + (hasVisualWork ? voxelStock.LastRenderMs : 0.0)
                    + (hasVisualWork ? voxelStock.LastHelixSyncMs : 0.0);
                _smoothedFrameMs = Lerp(_smoothedFrameMs, rawFrameMs, 0.22);

                bool ready = _smoothedFrameMs <= VoxelConstants.PlaybackFrameBudgetMs;
                double targetScale = ready
                    ? 1.0
                    : Math.Max(
                        VoxelConstants.PlaybackMeshBackpressureFloor,
                        VoxelConstants.PlaybackFrameBudgetMs / Math.Max(_smoothedFrameMs, 1.0));

                // Плавный backpressure, чтобы не было рывков скорости интерполяции.
                _smoothedScale = Lerp(_smoothedScale, targetScale, 0.28);
                _smoothedScale = Math.Clamp(_smoothedScale, VoxelConstants.PlaybackMeshBackpressureFloor, 1.0);
                return new SimulationFrameResult(ready, 0, _smoothedScale);
            }

            return new SimulationFrameResult(true, 0, 1.0);
        }

        public static FluteCutProfile BuildProfile(ToolViewModel tool) =>
            new()
            {
                Kind = tool.SelectedType == ToolType.Drill ? FluteProfileKind.Drill : FluteProfileKind.EndMill,
                Radius = tool.Diameter / 2.0,
                FluteLength = tool.FluteLength,
                PointAngleDegrees = tool.PointAngle
            };

        private static bool IsActiveCut(Point3D from, Point3D to)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            return dx * dx + dy * dy + dz * dz >= VoxelConstants.CutMotionEpsilonMm * VoxelConstants.CutMotionEpsilonMm;
        }

        private static double Lerp(double from, double to, double alpha) =>
            from + (to - from) * Math.Clamp(alpha, 0.0, 1.0);
    }
}
