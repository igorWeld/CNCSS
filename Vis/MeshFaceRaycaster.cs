using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    public readonly struct NodeMeshPickTarget
    {
        public required string NodeId { get; init; }

        public required NodeSlotInfo Slot { get; init; }

        public required GeometryModel3D Geometry { get; init; }
    }

    internal static class MeshFaceRaycaster
    {
        private const double NormalDotTolerance = 0.998;
        private const double MinPlaneDistanceToleranceMm = 0.05;
        private const double MaxPlaneDistanceToleranceMm = 0.25;
        private const double DepthSlopMm = 0.35;

        private readonly struct RayHitCandidate
        {
            public required NodeMeshPickTarget Target { get; init; }
            public required int TriangleIndex { get; init; }
            public required double Distance { get; init; }
            public required int PatchTriangleCount { get; init; }
        }

        public static bool TryPick(
            Ray3D rayWorld,
            IReadOnlyList<NodeMeshPickTarget> targets,
            out MeshFacePick pick,
            out string nodeId,
            out NodeSlotInfo slot)
        {
            pick = null!;
            nodeId = string.Empty;
            slot = null!;

            var candidates = new List<RayHitCandidate>();
            foreach (NodeMeshPickTarget target in targets)
            {
                CollectRayHits(rayWorld, target, candidates);
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            RayHitCandidate best = SelectBestCandidate(candidates);
            return TryBuildPick(rayWorld, best.Target, best.TriangleIndex, out pick, out nodeId, out slot);
        }

        private static void CollectRayHits(
            Ray3D rayWorld,
            NodeMeshPickTarget target,
            List<RayHitCandidate> candidates)
        {
            if (target.Geometry.Geometry is not MeshGeometry3D mesh)
            {
                return;
            }

            Matrix3D meshToWorld = GetMatrix(target.Slot.MeshToWorld);
            if (!meshToWorld.HasInverse)
            {
                return;
            }

            Matrix3D worldToMesh = meshToWorld;
            worldToMesh.Invert();

            Point3D origin = worldToMesh.Transform(rayWorld.Origin);
            Point3D far = worldToMesh.Transform(rayWorld.Origin + rayWorld.Direction);
            Vector3D direction = far - origin;
            if (direction.LengthSquared < 1e-18)
            {
                return;
            }

            direction.Normalize();

            int triangleCount = GetTriangleCount(mesh);
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                GetTriangle(mesh, triangleIndex, out Point3D a, out Point3D b, out Point3D c, out _);
                if (!TryIntersectRayTriangle(origin, direction, a, b, c, out double distance, out _))
                {
                    continue;
                }

                if (distance < 1e-6)
                {
                    continue;
                }

                int patchCount = CollectCoplanarPatchTriangleIndices(mesh, triangleIndex).Count;
                candidates.Add(new RayHitCandidate
                {
                    Target = target,
                    TriangleIndex = triangleIndex,
                    Distance = distance,
                    PatchTriangleCount = patchCount
                });
            }
        }

        private static RayHitCandidate SelectBestCandidate(IReadOnlyList<RayHitCandidate> candidates)
        {
            double minDistance = candidates.Min(c => c.Distance);
            double depthSlop = Math.Max(DepthSlopMm, minDistance * 0.012);
            return candidates
                .Where(c => c.Distance <= minDistance + depthSlop)
                .OrderByDescending(c => c.PatchTriangleCount)
                .ThenBy(c => c.Distance)
                .First();
        }

        private static bool TryBuildPick(
            Ray3D rayWorld,
            NodeMeshPickTarget target,
            int triangleIndex,
            out MeshFacePick pick,
            out string nodeId,
            out NodeSlotInfo slot)
        {
            pick = null!;
            nodeId = string.Empty;
            slot = null!;

            if (target.Geometry.Geometry is not MeshGeometry3D mesh)
            {
                return false;
            }

            Matrix3D meshToWorld = GetMatrix(target.Slot.MeshToWorld);
            if (!meshToWorld.HasInverse)
            {
                return false;
            }

            Matrix3D worldToMesh = meshToWorld;
            worldToMesh.Invert();

            ExpandCoplanarPatch(mesh, triangleIndex, out Point3D patchCenterLocal, out Vector3D patchNormalLocal);
            patchNormalLocal.Normalize();

            Point3D hitWorld = meshToWorld.Transform(patchCenterLocal);
            Point3D normalEnd = meshToWorld.Transform(patchCenterLocal + patchNormalLocal);
            Vector3D normalWorld = normalEnd - hitWorld;
            if (normalWorld.LengthSquared > 1e-12)
            {
                normalWorld.Normalize();
            }
            else
            {
                normalWorld = new Vector3D(0, 0, 1);
            }

            OrientNormalTowardPoint(ref normalWorld, rayWorld.Origin, hitWorld);
            patchNormalLocal = worldToMesh.Transform(hitWorld + normalWorld) - worldToMesh.Transform(hitWorld);
            if (patchNormalLocal.LengthSquared > 1e-12)
            {
                patchNormalLocal.Normalize();
            }

            pick = new MeshFacePick
            {
                NodeId = target.NodeId,
                PointWorld = hitWorld,
                NormalWorld = normalWorld,
                PointMeshLocal = patchCenterLocal,
                NormalMeshLocal = patchNormalLocal,
                SeedTriangleIndex = triangleIndex,
                SourceMesh = mesh
            };
            nodeId = target.NodeId;
            slot = target.Slot;
            return true;
        }

        internal static void ExpandCoplanarPatch(
            MeshGeometry3D mesh,
            int seedTriangleIndex,
            out Point3D centroidLocal,
            out Vector3D normalLocal)
        {
            IReadOnlyList<int> triangles = CollectCoplanarPatchTriangleIndices(mesh, seedTriangleIndex);
            if (triangles.Count == 0)
            {
                GetTriangle(mesh, seedTriangleIndex, out Point3D a, out Point3D b, out Point3D c, out Vector3D seedNormal);
                centroidLocal = new Point3D((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3);
                normalLocal = seedNormal.LengthSquared > 1e-12 ? seedNormal : new Vector3D(0, 0, 1);
                if (normalLocal.LengthSquared > 1e-12)
                {
                    normalLocal.Normalize();
                }

                return;
            }

            GetTriangle(mesh, triangles[0], out Point3D ta, out Point3D tb, out Point3D tc, out Vector3D fallbackNormal);
            if (fallbackNormal.LengthSquared < 1e-12)
            {
                fallbackNormal = new Vector3D(0, 0, 1);
            }
            else
            {
                fallbackNormal.Normalize();
            }

            var centroid = default(Point3D);
            var normalSum = default(Vector3D);
            foreach (int triangleIndex in triangles)
            {
                GetTriangle(mesh, triangleIndex, out Point3D t0, out Point3D t1, out Point3D t2, out Vector3D triNormal);
                centroid.X += (t0.X + t1.X + t2.X) / 3;
                centroid.Y += (t0.Y + t1.Y + t2.Y) / 3;
                centroid.Z += (t0.Z + t1.Z + t2.Z) / 3;
                if (triNormal.LengthSquared > 1e-12)
                {
                    triNormal.Normalize();
                    normalSum += triNormal;
                }
            }

            double inv = 1.0 / triangles.Count;
            centroidLocal = new Point3D(centroid.X * inv, centroid.Y * inv, centroid.Z * inv);
            normalLocal = normalSum;
            if (normalLocal.LengthSquared > 1e-12)
            {
                normalLocal.Normalize();
            }
            else
            {
                normalLocal = fallbackNormal;
            }
        }

        public static IReadOnlyList<int> CollectCoplanarPatchTriangleIndices(MeshGeometry3D mesh, int seedTriangleIndex)
        {
            GetTriangle(mesh, seedTriangleIndex, out Point3D a, out Point3D b, out Point3D c, out Vector3D seedNormal);
            if (seedNormal.LengthSquared < 1e-12)
            {
                return [seedTriangleIndex];
            }

            seedNormal.Normalize();
            double planeOffset = -Dot(normal: seedNormal, point: a);
            double planeTolerance = ComputePlaneDistanceTolerance(mesh);
            IReadOnlyDictionary<int, int[]> adjacency = BuildTriangleAdjacency(mesh);
            var included = new HashSet<int> { seedTriangleIndex };
            var queue = new Queue<int>();
            queue.Enqueue(seedTriangleIndex);

            while (queue.Count > 0)
            {
                int triangleIndex = queue.Dequeue();
                if (!adjacency.TryGetValue(triangleIndex, out int[]? neighbors))
                {
                    continue;
                }

                foreach (int neighbor in neighbors)
                {
                    if (included.Contains(neighbor))
                    {
                        continue;
                    }

                    GetTriangle(mesh, neighbor, out Point3D ta, out Point3D tb, out Point3D tc, out Vector3D triNormal);
                    if (triNormal.LengthSquared < 1e-12)
                    {
                        continue;
                    }

                    triNormal.Normalize();
                    if (Vector3D.DotProduct(triNormal, seedNormal) < NormalDotTolerance)
                    {
                        continue;
                    }

                    Point3D center = new(
                        (ta.X + tb.X + tc.X) / 3,
                        (ta.Y + tb.Y + tc.Y) / 3,
                        (ta.Z + tb.Z + tc.Z) / 3);
                    double planeDistance = Math.Abs(Dot(seedNormal, center) + planeOffset);
                    if (planeDistance > planeTolerance)
                    {
                        continue;
                    }

                    included.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            return included.Count == 0 ? [seedTriangleIndex] : included.ToList();
        }

        private static double ComputePlaneDistanceTolerance(MeshGeometry3D mesh)
        {
            Rect3D bounds = mesh.Bounds;
            if (bounds.IsEmpty)
            {
                bounds = StlModelMetrics.ComputeBoundsRecursive(new GeometryModel3D { Geometry = mesh });
            }

            if (bounds.IsEmpty)
            {
                return MaxPlaneDistanceToleranceMm;
            }

            double diagonal = Math.Sqrt(
                bounds.SizeX * bounds.SizeX
                + bounds.SizeY * bounds.SizeY
                + bounds.SizeZ * bounds.SizeZ);
            double adaptive = diagonal * 1e-5;
            return Math.Clamp(adaptive, MinPlaneDistanceToleranceMm, MaxPlaneDistanceToleranceMm);
        }

        private static IReadOnlyDictionary<int, int[]> BuildTriangleAdjacency(MeshGeometry3D mesh)
        {
            Point3DCollection positions = mesh.Positions ?? new Point3DCollection();
            double edgeTolerance = ComputeEdgeMatchTolerance(mesh);
            var edgeToTriangles = new Dictionary<EdgeKey, List<int>>();
            int triangleCount = GetTriangleCount(mesh);
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                GetTriangleIndices(mesh, triangleIndex, out int i0, out int i1, out int i2);
                Point3D p0 = positions[i0];
                Point3D p1 = positions[i1];
                Point3D p2 = positions[i2];
                AddEdge(edgeToTriangles, p0, p1, edgeTolerance, triangleIndex);
                AddEdge(edgeToTriangles, p1, p2, edgeTolerance, triangleIndex);
                AddEdge(edgeToTriangles, p2, p0, edgeTolerance, triangleIndex);
            }

            var adjacency = new Dictionary<int, List<int>>(triangleCount);
            foreach (List<int> triangles in edgeToTriangles.Values)
            {
                if (triangles.Count < 2)
                {
                    continue;
                }

                for (int i = 0; i < triangles.Count; i++)
                {
                    for (int j = i + 1; j < triangles.Count; j++)
                    {
                        Link(adjacency, triangles[i], triangles[j]);
                        Link(adjacency, triangles[j], triangles[i]);
                    }
                }
            }

            return adjacency.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Distinct().ToArray());
        }

        private static double ComputeEdgeMatchTolerance(MeshGeometry3D mesh)
        {
            Rect3D bounds = mesh.Bounds;
            if (bounds.IsEmpty)
            {
                bounds = StlModelMetrics.ComputeBoundsRecursive(new GeometryModel3D { Geometry = mesh });
            }

            if (bounds.IsEmpty)
            {
                return 1e-3;
            }

            double diagonal = Math.Sqrt(
                bounds.SizeX * bounds.SizeX
                + bounds.SizeY * bounds.SizeY
                + bounds.SizeZ * bounds.SizeZ);
            return Math.Clamp(diagonal * 1e-6, 1e-5, 0.05);
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public EdgeKey(long ax, long ay, long az, long bx, long by, long bz)
            {
                Ax = ax;
                Ay = ay;
                Az = az;
                Bx = bx;
                By = by;
                Bz = bz;
            }

            public long Ax { get; }
            public long Ay { get; }
            public long Az { get; }
            public long Bx { get; }
            public long By { get; }
            public long Bz { get; }

            public bool Equals(EdgeKey other) =>
                Ax == other.Ax && Ay == other.Ay && Az == other.Az
                && Bx == other.Bx && By == other.By && Bz == other.Bz;

            public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Ax, Ay, Az, Bx, By, Bz);
        }

        private static EdgeKey CreateEdgeKey(Point3D a, Point3D b, double tolerance)
        {
            long ax = Quantize(a.X, tolerance);
            long ay = Quantize(a.Y, tolerance);
            long az = Quantize(a.Z, tolerance);
            long bx = Quantize(b.X, tolerance);
            long by = Quantize(b.Y, tolerance);
            long bz = Quantize(b.Z, tolerance);

            if (Compare(ax, ay, az, bx, by, bz) > 0)
            {
                (ax, ay, az, bx, by, bz) = (bx, by, bz, ax, ay, az);
            }

            return new EdgeKey(ax, ay, az, bx, by, bz);
        }

        private static long Quantize(double value, double tolerance) =>
            (long)Math.Round(value / tolerance);

        private static int Compare(long ax, long ay, long az, long bx, long by, long bz)
        {
            int cmp = ax.CompareTo(bx);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = ay.CompareTo(by);
            if (cmp != 0)
            {
                return cmp;
            }

            return az.CompareTo(bz);
        }

        private static void AddEdge(
            Dictionary<EdgeKey, List<int>> map,
            Point3D a,
            Point3D b,
            double tolerance,
            int triangleIndex)
        {
            EdgeKey key = CreateEdgeKey(a, b, tolerance);
            if (!map.TryGetValue(key, out List<int>? list))
            {
                list = [];
                map[key] = list;
            }

            list.Add(triangleIndex);
        }

        private static void Link(Dictionary<int, List<int>> adjacency, int a, int b)
        {
            if (!adjacency.TryGetValue(a, out List<int>? list))
            {
                list = [];
                adjacency[a] = list;
            }

            list.Add(b);
        }

        private static void GetTriangleIndices(MeshGeometry3D mesh, int triangleIndex, out int i0, out int i1, out int i2)
        {
            Int32Collection? indices = mesh.TriangleIndices;
            if (indices != null && indices.Count >= 3)
            {
                int i = triangleIndex * 3;
                i0 = indices[i];
                i1 = indices[i + 1];
                i2 = indices[i + 2];
                return;
            }

            int baseIndex = triangleIndex * 3;
            i0 = baseIndex;
            i1 = baseIndex + 1;
            i2 = baseIndex + 2;
        }

        public static MeshGeometry3D BuildCoplanarPatchWorld(
            MeshGeometry3D mesh,
            int seedTriangleIndex,
            Transform3D meshToWorld)
        {
            IReadOnlyList<int> triangles = CollectCoplanarPatchTriangleIndices(mesh, seedTriangleIndex);
            var positions = new Point3DCollection(triangles.Count * 3);
            var indices = new Int32Collection(triangles.Count * 3);

            foreach (int triangleIndex in triangles)
            {
                GetTriangle(mesh, triangleIndex, out Point3D a, out Point3D b, out Point3D c, out _);
                int baseIndex = positions.Count;
                positions.Add(meshToWorld.Transform(a));
                positions.Add(meshToWorld.Transform(b));
                positions.Add(meshToWorld.Transform(c));
                indices.Add(baseIndex);
                indices.Add(baseIndex + 1);
                indices.Add(baseIndex + 2);
            }

            return new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices
            };
        }

        private static bool TryIntersectRayTriangle(
            Point3D origin,
            Vector3D direction,
            Point3D v0,
            Point3D v1,
            Point3D v2,
            out double distance,
            out Vector3D normal)
        {
            normal = Vector3D.CrossProduct(v1 - v0, v2 - v0);
            const double epsilon = 1e-9;

            if (TryIntersectRayTriangleSide(origin, direction, v0, v1, v2, epsilon, out distance))
            {
                return true;
            }

            return TryIntersectRayTriangleSide(origin, direction, v0, v2, v1, epsilon, out distance);
        }

        private static bool TryIntersectRayTriangleSide(
            Point3D origin,
            Vector3D direction,
            Point3D v0,
            Point3D v1,
            Point3D v2,
            double epsilon,
            out double distance)
        {
            distance = 0;
            Vector3D edge1 = v1 - v0;
            Vector3D edge2 = v2 - v0;
            Vector3D pvec = Vector3D.CrossProduct(direction, edge2);
            double det = Vector3D.DotProduct(edge1, pvec);
            if (Math.Abs(det) < epsilon)
            {
                return false;
            }

            double invDet = 1.0 / det;
            Vector3D tvec = origin - v0;
            double u = Vector3D.DotProduct(tvec, pvec) * invDet;
            if (u < 0 || u > 1)
            {
                return false;
            }

            Vector3D qvec = Vector3D.CrossProduct(tvec, edge1);
            double v = Vector3D.DotProduct(direction, qvec) * invDet;
            if (v < 0 || u + v > 1)
            {
                return false;
            }

            distance = Vector3D.DotProduct(edge2, qvec) * invDet;
            return distance > epsilon;
        }

        private static int GetTriangleCount(MeshGeometry3D mesh)
        {
            Int32Collection? indices = mesh.TriangleIndices;
            if (indices != null && indices.Count >= 3)
            {
                return indices.Count / 3;
            }

            return mesh.Positions?.Count / 3 ?? 0;
        }

        private static void GetTriangle(
            MeshGeometry3D mesh,
            int triangleIndex,
            out Point3D a,
            out Point3D b,
            out Point3D c,
            out Vector3D normal)
        {
            Point3DCollection positions = mesh.Positions ?? new Point3DCollection();
            Int32Collection? indices = mesh.TriangleIndices;
            if (indices != null && indices.Count >= 3)
            {
                int i = triangleIndex * 3;
                a = positions[indices[i]];
                b = positions[indices[i + 1]];
                c = positions[indices[i + 2]];
            }
            else
            {
                int i = triangleIndex * 3;
                a = positions[i];
                b = positions[i + 1];
                c = positions[i + 2];
            }

            normal = Vector3D.CrossProduct(b - a, c - a);
        }

        private static Matrix3D GetMatrix(Transform3D transform) =>
            transform is MatrixTransform3D matrixTransform ? matrixTransform.Matrix : transform.Value;

        private static double Dot(Vector3D normal, Point3D point) =>
            normal.X * point.X + normal.Y * point.Y + normal.Z * point.Z;

        private static void OrientNormalTowardPoint(ref Vector3D normalWorld, Point3D viewerWorld, Point3D surfaceWorld)
        {
            Vector3D toViewer = viewerWorld - surfaceWorld;
            if (toViewer.LengthSquared < 1e-12)
            {
                return;
            }

            toViewer.Normalize();
            if (Vector3D.DotProduct(normalWorld, toViewer) < 0)
            {
                normalWorld = -normalWorld;
            }
        }
    }
}
