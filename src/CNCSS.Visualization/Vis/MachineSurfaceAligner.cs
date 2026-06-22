using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;

namespace CNCSS.Vis
{
    public static class MachineSurfaceAligner
    {
        public sealed record MeshLayout(
            MachineGeometryPoint Offset,
            MachineGeometryPoint RotationDegrees,
            double MeshScale);

        public static MeshLayout AlignMobileFaceToReference(
            MeshFacePick reference,
            MeshFacePick mobile,
            Transform3D kinematicTransform,
            Transform3D meshTransform)
        {
            Matrix3D kin = GetMatrix(kinematicTransform);
            Matrix3D mesh = GetMatrix(meshTransform);

            Vector3D refNormal = reference.NormalWorld;
            refNormal.Normalize();
            Vector3D targetNormal = -refNormal;

            Vector3D mobileNormalWorld = TransformNormal(kin * mesh, mobile.NormalMeshLocal);
            mobileNormalWorld.Normalize();

            Matrix3D rotWorld = RotationMatrixFromTo(mobileNormalWorld, targetNormal);
            Matrix3D kinInverse = InvertMatrix(kin);
            Matrix3D newMesh = kinInverse * rotWorld * kin * mesh;

            Point3D pAfterRotation = (kin * newMesh).Transform(mobile.PointMeshLocal);
            Vector3D translationWorld = reference.PointWorld - pAfterRotation;
            newMesh.Translate(kinInverse.Transform(translationWorld));

            DecomposeMeshMatrix(newMesh, meshTransform, out MachineGeometryPoint offset, out MachineGeometryPoint rotation);
            double scale = ExtractUniformScale(meshTransform);
            return new MeshLayout(offset, rotation, scale);
        }

        private static Vector3D TransformNormal(Matrix3D matrix, Vector3D normal)
        {
            Vector3D transformed = matrix.Transform(normal);
            if (transformed.LengthSquared < 1e-12)
            {
                return transformed;
            }

            transformed.Normalize();
            return transformed;
        }

        private static Matrix3D RotationMatrixFromTo(Vector3D from, Vector3D to)
        {
            from.Normalize();
            to.Normalize();
            double dot = Vector3D.DotProduct(from, to);
            if (dot > 0.999999)
            {
                return Matrix3D.Identity;
            }

            if (dot < -0.999999)
            {
                Vector3D axis = Math.Abs(from.X) < 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
                axis = Vector3D.CrossProduct(from, axis);
                axis.Normalize();
                return CreateRotationMatrix(axis, 180);
            }

            Vector3D cross = Vector3D.CrossProduct(from, to);
            double angleRad = Math.Acos(Math.Clamp(dot, -1, 1));
            cross.Normalize();
            return CreateRotationMatrix(cross, angleRad * 180 / Math.PI);
        }

        private static Matrix3D CreateRotationMatrix(Vector3D axis, double angleDegrees)
        {
            var rotation = new AxisAngleRotation3D(axis, angleDegrees);
            return new RotateTransform3D(rotation).Value;
        }

        private static void DecomposeMeshMatrix(
            Matrix3D matrix,
            Transform3D originalMesh,
            out MachineGeometryPoint offset,
            out MachineGeometryPoint rotation)
        {
            double scale = ExtractUniformScale(originalMesh);
            if (scale > 1e-9)
            {
                matrix.Scale(new Vector3D(1 / scale, 1 / scale, 1 / scale));
            }

            offset = new MachineGeometryPoint
            {
                X = matrix.OffsetX,
                Y = matrix.OffsetY,
                Z = matrix.OffsetZ
            };

            double yaw = Math.Atan2(matrix.M12, matrix.M11) * 180 / Math.PI;
            double pitch = Math.Atan2(-matrix.M23, Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22)) * 180 / Math.PI;
            double roll = Math.Atan2(matrix.M21, matrix.M22) * 180 / Math.PI;
            rotation = new MachineGeometryPoint { X = pitch, Y = yaw, Z = roll };
        }

        private static double ExtractUniformScale(Transform3D meshTransform)
        {
            if (meshTransform is Transform3DGroup group)
            {
                foreach (Transform3D child in group.Children)
                {
                    if (child is ScaleTransform3D scale)
                    {
                        return scale.ScaleX;
                    }
                }
            }

            return 1;
        }

        private static Matrix3D GetMatrix(Transform3D transform) =>
            transform is MatrixTransform3D matrix ? matrix.Value : Matrix3D.Identity;

        private static Matrix3D InvertMatrix(Matrix3D matrix)
        {
            if (!matrix.HasInverse)
            {
                return Matrix3D.Identity;
            }

            matrix.Invert();
            return matrix;
        }
    }
}
