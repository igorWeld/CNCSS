using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    /// <summary>Workpiece / fixture placement on the table mounting plane (table mesh-local, mm).</summary>
    public static class WorkpieceMountHelper
    {
        /// <summary>XY at mesh bbox center, Z from picked face point.</summary>
        public static MachineGeometryPoint ComputeFromFacePlane(Point3D meshCenterLocal, Point3D pickPointMeshLocal) =>
            new()
            {
                X = meshCenterLocal.X,
                Y = meshCenterLocal.Y,
                Z = pickPointMeshLocal.Z
            };

        /// <summary>Default mount: XY at bbox center, Z on top face of table mesh.</summary>
        public static MachineGeometryPoint ComputeDefaultFromBounds(Rect3D meshBoundsLocal)
        {
            if (meshBoundsLocal.IsEmpty)
            {
                return MachineGeometryPoint.Zero;
            }

            return new MachineGeometryPoint
            {
                X = meshBoundsLocal.X + meshBoundsLocal.SizeX * 0.5,
                Y = meshBoundsLocal.Y + meshBoundsLocal.SizeY * 0.5,
                Z = meshBoundsLocal.Z + meshBoundsLocal.SizeZ
            };
        }

        /// <summary>Quick reference on table mesh bbox: Xm, Xp, Ym, Yp, Zm, Zp (center of face).</summary>
        public static MachineGeometryPoint PointOnTableBoundsFace(Rect3D bounds, string faceTag)
        {
            if (bounds.IsEmpty)
            {
                return MachineGeometryPoint.Zero;
            }

            double cx = bounds.X + bounds.SizeX * 0.5;
            double cy = bounds.Y + bounds.SizeY * 0.5;
            double cz = bounds.Z + bounds.SizeZ * 0.5;
            return faceTag switch
            {
                "Xm" => new MachineGeometryPoint { X = bounds.X, Y = cy, Z = cz },
                "Xp" => new MachineGeometryPoint { X = bounds.X + bounds.SizeX, Y = cy, Z = cz },
                "Ym" => new MachineGeometryPoint { X = cx, Y = bounds.Y, Z = cz },
                "Yp" => new MachineGeometryPoint { X = cx, Y = bounds.Y + bounds.SizeY, Z = cz },
                "Zm" => new MachineGeometryPoint { X = cx, Y = cy, Z = bounds.Z },
                "Zp" => new MachineGeometryPoint { X = cx, Y = cy, Z = bounds.Z + bounds.SizeZ },
                _ => new MachineGeometryPoint { X = cx, Y = cy, Z = bounds.Z + bounds.SizeZ }
            };
        }

        public static MachineGeometryPoint ResolveMountLocal(MachineGeometryPoint stored, Rect3D? tableMeshBoundsLocal)
        {
            if (!stored.IsNearlyZero())
            {
                return stored.Clone();
            }

            return tableMeshBoundsLocal is { IsEmpty: false } bounds
                ? ComputeDefaultFromBounds(bounds)
                : stored.Clone();
        }
    }
}
