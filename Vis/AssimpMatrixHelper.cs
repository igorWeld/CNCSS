using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    internal static class AssimpMatrixHelper
    {
        public static Assimp.Matrix4x4 Multiply(Assimp.Matrix4x4 left, Assimp.Matrix4x4 right)
        {
            return new Assimp.Matrix4x4(
                left.A1 * right.A1 + left.A2 * right.B1 + left.A3 * right.C1 + left.A4 * right.D1,
                left.A1 * right.A2 + left.A2 * right.B2 + left.A3 * right.C2 + left.A4 * right.D2,
                left.A1 * right.A3 + left.A2 * right.B3 + left.A3 * right.C3 + left.A4 * right.D3,
                left.A1 * right.A4 + left.A2 * right.B4 + left.A3 * right.C4 + left.A4 * right.D4,
                left.B1 * right.A1 + left.B2 * right.B1 + left.B3 * right.C1 + left.B4 * right.D1,
                left.B1 * right.A2 + left.B2 * right.B2 + left.B3 * right.C2 + left.B4 * right.D2,
                left.B1 * right.A3 + left.B2 * right.B3 + left.B3 * right.C3 + left.B4 * right.D3,
                left.B1 * right.A4 + left.B2 * right.B4 + left.B3 * right.C4 + left.B4 * right.D4,
                left.C1 * right.A1 + left.C2 * right.B1 + left.C3 * right.C1 + left.C4 * right.D1,
                left.C1 * right.A2 + left.C2 * right.B2 + left.C3 * right.C2 + left.C4 * right.D2,
                left.C1 * right.A3 + left.C2 * right.B3 + left.C3 * right.C3 + left.C4 * right.D3,
                left.C1 * right.A4 + left.C2 * right.B4 + left.C3 * right.C4 + left.C4 * right.D4,
                left.D1 * right.A1 + left.D2 * right.B1 + left.D3 * right.C1 + left.D4 * right.D1,
                left.D1 * right.A2 + left.D2 * right.B2 + left.D3 * right.C2 + left.D4 * right.D2,
                left.D1 * right.A3 + left.D2 * right.B3 + left.D3 * right.C3 + left.D4 * right.D3,
                left.D1 * right.A4 + left.D2 * right.B4 + left.D3 * right.C4 + left.D4 * right.D4);
        }

        public static Point3D TransformPoint(Assimp.Matrix4x4 matrix, Point3D point) =>
            new(
                matrix.A1 * point.X + matrix.A2 * point.Y + matrix.A3 * point.Z + matrix.A4,
                matrix.B1 * point.X + matrix.B2 * point.Y + matrix.B3 * point.Z + matrix.B4,
                matrix.C1 * point.X + matrix.C2 * point.Y + matrix.C3 * point.Z + matrix.C4);
    }
}
