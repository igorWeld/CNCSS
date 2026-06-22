using System.Windows.Media.Media3D;
using CNCSS.Geometry.BRep;

namespace CNCSS.Geometry.SweptVolume;

public static class SweptVolumeBuilder
{
    public static BrepSolid BuildCylinderSweep(Point3D start, Point3D end, double radius, double fluteLength)
    {
        var field = new CylinderSweepField(start, end, radius, fluteLength);
        // Для MVP topology берём оболочку как AABB swept-а (полнота формы обеспечивается field.Contains в boolean).
        return BrepPrimitives.CreateBox(field.Bounds).WithField(field);
    }

    private static BrepSolid WithField(this BrepSolid src, ISolidField field)
    {
        src = new BrepSolid
        {
            Vertices = src.Vertices,
            HalfEdges = src.HalfEdges,
            Faces = src.Faces,
            Mesh = src.Mesh,
            Field = field
        };
        return src;
    }
}
