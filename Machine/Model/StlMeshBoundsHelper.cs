using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    public static class StlMeshBoundsHelper
    {
        public static MachineGeometryPoint Center(Rect3D bounds) =>
            new()
            {
                X = bounds.X + bounds.SizeX * 0.5,
                Y = bounds.Y + bounds.SizeY * 0.5,
                Z = bounds.Z + bounds.SizeZ * 0.5
            };

        public static MachineGeometryPoint BottomCenter(Rect3D bounds) =>
            new()
            {
                X = bounds.X + bounds.SizeX * 0.5,
                Y = bounds.Y + bounds.SizeY * 0.5,
                Z = bounds.Z
            };

        public static MachineGeometryPoint TopCenter(Rect3D bounds) =>
            new()
            {
                X = bounds.X + bounds.SizeX * 0.5,
                Y = bounds.Y + bounds.SizeY * 0.5,
                Z = bounds.Z + bounds.SizeZ
            };
    }
}
