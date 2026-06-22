using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Logic.Voxel;

/// <summary>Преобразование table-local (абсолютные координаты заготовки) ↔ локальные координаты воксельного объёма.</summary>
public static class StockVolumeCoordinates
{
    public static Point3D ToVolumeLocal(StockVolumeConfig volume, Point3D tableLocal) =>
        new(
            tableLocal.X - volume.MinX,
            tableLocal.Y - volume.MinY,
            tableLocal.Z - volume.MinZ);

    public static CutMotionDescriptor ToVolumeLocal(StockVolumeConfig volume, CutMotionDescriptor motion) =>
        motion.Kind == CutMotionKind.Arc && motion.Arc != null
            ? CutMotionDescriptor.FromArc(
                ToVolumeLocal(volume, motion.Start),
                ToVolumeLocal(volume, motion.End),
                motion.Arc)
            : CutMotionDescriptor.Linear(
                ToVolumeLocal(volume, motion.Start),
                ToVolumeLocal(volume, motion.End));
}
