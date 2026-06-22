using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;

namespace CNCSS.Vis
{
    /// <summary>Preserves relative layout from CAD assembly when mapping meshes onto machine nodes.</summary>
    public static class MeshAssemblyPlacement
    {
        public static StlAnalysisResult ComputeSharedScale(IReadOnlyList<MeshComponent> components)
        {
            Rect3D union = Rect3D.Empty;
            bool hasBounds = false;
            foreach (MeshComponent component in components)
            {
                Rect3D bounds = component.Bounds;
                if (bounds.IsEmpty)
                {
                    continue;
                }

                union = hasBounds ? Rect3D.Union(union, bounds) : bounds;
                hasBounds = true;
            }

            if (!hasBounds)
            {
                return new StlAnalysisResult(0, 1, 1);
            }

            double sourceExtent = Math.Max(union.SizeX, Math.Max(union.SizeY, union.SizeZ));
            double targetMm = StlUnitScaleHelper.InferTargetMaxExtentMm(sourceExtent);
            double scale = StlUnitScaleHelper.ComputeMeshScale(sourceExtent, targetMm);
            return new StlAnalysisResult(sourceExtent, targetMm, scale);
        }

        /// <summary>
        /// Mesh offset in node space so that, at the given axis pose, the mesh centroid stays at assembly coordinates (scaled).
        /// </summary>
        public static MachineGeometryPoint ComputeMeshOffset(
            IReadOnlyDictionary<string, Transform3D> nodeTransforms,
            string nodeId,
            Point3D assemblyCentroid,
            double meshScale) =>
            ComputeMeshOffset(nodeTransforms, nodeId, assemblyCentroid, assemblyCentroid, meshScale);

        /// <summary>
        /// Same as <see cref="ComputeMeshOffset(IReadOnlyDictionary{string, Transform3D}, string, Point3D, double)"/>,
        /// but allows a distinct mesh centroid (e.g. after STL round-trip).
        /// </summary>
        public static MachineGeometryPoint ComputeMeshOffset(
            IReadOnlyDictionary<string, Transform3D> nodeTransforms,
            string nodeId,
            Point3D assemblyCentroid,
            Point3D meshCentroid,
            double meshScale)
        {
            if (!nodeTransforms.TryGetValue(nodeId, out Transform3D? kinematic) || kinematic == null)
            {
                kinematic = Transform3D.Identity;
            }

            Point3D targetWorld = ScalePoint(assemblyCentroid, meshScale);
            Point3D scaledMeshCenter = ScalePoint(meshCentroid, meshScale);

            Matrix3D kin = GetMatrix(kinematic);
            if (!kin.HasInverse)
            {
                return new MachineGeometryPoint { X = 0, Y = 0, Z = 0 };
            }

            kin.Invert();
            Point3D offset = kin.Transform(targetWorld);
            offset.X -= scaledMeshCenter.X;
            offset.Y -= scaledMeshCenter.Y;
            offset.Z -= scaledMeshCenter.Z;

            return new MachineGeometryPoint { X = offset.X, Y = offset.Y, Z = offset.Z };
        }

        private static Point3D ScalePoint(Point3D point, double scale) =>
            new(point.X * scale, point.Y * scale, point.Z * scale);

        private static Matrix3D GetMatrix(Transform3D transform)
        {
            if (transform is MatrixTransform3D matrixTransform)
            {
                return matrixTransform.Matrix;
            }

            return transform.Value;
        }
    }
}
