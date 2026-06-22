using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;

namespace CNCSS.Vis
{
    public static class MachineSymmetricAligner
    {
        private const double DominantAxisRatio = 1.5;

        public static MachineSurfaceAligner.MeshLayout CenterMeshBetweenFaces(
            MeshFacePick faceA,
            MeshFacePick faceB,
            Point3D meshCenterMeshLocal,
            Transform3D kinematicTransform,
            Transform3D meshTransform) =>
            CenterMeshOnFaceMidpoint(
                faceA,
                faceB,
                meshCenterMeshLocal,
                kinematicTransform,
                meshTransform,
                DetectSymmetricAxis(faceA, faceB, includeZ: true));

        /// <summary>Moves mesh center to the midpoint between two face picks on the detected symmetry axis; Z of the part center is unchanged.</summary>
        public static MachineSurfaceAligner.MeshLayout CenterMeshOnFaceMidpointXY(
            MeshFacePick faceA,
            MeshFacePick faceB,
            Point3D meshCenterMeshLocal,
            Transform3D kinematicTransform,
            Transform3D meshTransform) =>
            CenterMeshOnFaceMidpoint(
                faceA,
                faceB,
                meshCenterMeshLocal,
                kinematicTransform,
                meshTransform,
                DetectSymmetricAxis(faceA, faceB, includeZ: false));

        /// <summary>Which world axes to move the part center along, from separation and normals of the two symmetry faces.</summary>
        public static SymmetricTranslationAxis DetectSymmetricAxis(
            MeshFacePick faceA,
            MeshFacePick faceB,
            bool includeZ = false)
        {
            Vector3D separation = faceB.PointWorld - faceA.PointWorld;
            SymmetricTranslationAxis fromSeparation = DominantAxes(
                Math.Abs(separation.X),
                Math.Abs(separation.Y),
                Math.Abs(separation.Z),
                includeZ);

            Vector3D normalA = faceA.NormalWorld;
            Vector3D normalB = faceB.NormalWorld;
            SymmetricTranslationAxis fromNormals = DominantAxes(
                (Math.Abs(normalA.X) + Math.Abs(normalB.X)) * 0.5,
                (Math.Abs(normalA.Y) + Math.Abs(normalB.Y)) * 0.5,
                (Math.Abs(normalA.Z) + Math.Abs(normalB.Z)) * 0.5,
                includeZ);

            if (fromSeparation != SymmetricTranslationAxis.None
                && fromSeparation != SymmetricTranslationAxis.Xy
                && fromSeparation == fromNormals)
            {
                return fromSeparation;
            }

            if (fromSeparation != SymmetricTranslationAxis.None && fromSeparation != SymmetricTranslationAxis.Xy)
            {
                return fromSeparation;
            }

            if (fromNormals != SymmetricTranslationAxis.None && fromNormals != SymmetricTranslationAxis.Xy)
            {
                return fromNormals;
            }

            return includeZ
                ? SymmetricTranslationAxis.X | SymmetricTranslationAxis.Y | SymmetricTranslationAxis.Z
                : SymmetricTranslationAxis.Xy;
        }

        private static SymmetricTranslationAxis DominantAxes(double ax, double ay, double az, bool includeZ)
        {
            if (!includeZ)
            {
                az = 0;
            }

            if (ax >= ay * DominantAxisRatio && ax >= az * DominantAxisRatio)
            {
                return SymmetricTranslationAxis.X;
            }

            if (ay >= ax * DominantAxisRatio && ay >= az * DominantAxisRatio)
            {
                return SymmetricTranslationAxis.Y;
            }

            if (includeZ && az >= ax * DominantAxisRatio && az >= ay * DominantAxisRatio)
            {
                return SymmetricTranslationAxis.Z;
            }

            if (!includeZ && ax >= az * DominantAxisRatio && ay >= az * DominantAxisRatio)
            {
                return SymmetricTranslationAxis.Xy;
            }

            return SymmetricTranslationAxis.None;
        }

        private static MachineSurfaceAligner.MeshLayout CenterMeshOnFaceMidpoint(
            MeshFacePick faceA,
            MeshFacePick faceB,
            Point3D meshCenterMeshLocal,
            Transform3D kinematicTransform,
            Transform3D meshTransform,
            SymmetricTranslationAxis axes)
        {
            Matrix3D kin = GetMatrix(kinematicTransform);
            Matrix3D mesh = GetMatrix(meshTransform);
            Point3D centerWorld = (kin * mesh).Transform(meshCenterMeshLocal);

            double midX = (faceA.PointWorld.X + faceB.PointWorld.X) * 0.5;
            double midY = (faceA.PointWorld.Y + faceB.PointWorld.Y) * 0.5;
            double midZ = (faceA.PointWorld.Z + faceB.PointWorld.Z) * 0.5;

            Point3D targetWorld = new(
                axes.HasFlag(SymmetricTranslationAxis.X) ? midX : centerWorld.X,
                axes.HasFlag(SymmetricTranslationAxis.Y) ? midY : centerWorld.Y,
                axes.HasFlag(SymmetricTranslationAxis.Z) ? midZ : centerWorld.Z);

            Vector3D deltaWorld = targetWorld - centerWorld;

            Matrix3D kinInverse = InvertMatrix(kin);
            Matrix3D newMesh = mesh;
            newMesh.Translate(kinInverse.Transform(deltaWorld));

            return Decompose(newMesh, meshTransform);
        }

        private static MachineSurfaceAligner.MeshLayout Decompose(Matrix3D matrix, Transform3D originalMesh)
        {
            double scale = 1;
            if (originalMesh is Transform3DGroup group)
            {
                foreach (Transform3D child in group.Children)
                {
                    if (child is ScaleTransform3D scaleTransform)
                    {
                        scale = scaleTransform.ScaleX;
                        break;
                    }
                }
            }

            if (scale > 1e-9)
            {
                matrix.Scale(new Vector3D(1 / scale, 1 / scale, 1 / scale));
            }

            var offset = new MachineGeometryPoint
            {
                X = matrix.OffsetX,
                Y = matrix.OffsetY,
                Z = matrix.OffsetZ
            };

            double yaw = Math.Atan2(matrix.M12, matrix.M11) * 180 / Math.PI;
            double pitch = Math.Atan2(-matrix.M23, Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22)) * 180 / Math.PI;
            double roll = Math.Atan2(matrix.M21, matrix.M22) * 180 / Math.PI;
            var rotation = new MachineGeometryPoint { X = pitch, Y = yaw, Z = roll };
            return new MachineSurfaceAligner.MeshLayout(offset, rotation, scale);
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
