using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    public static class MeshFacePickHelper
    {
        public static bool TryPickMeshFace(
            HelixViewport3D viewport,
            MachineVisualCoordinator coordinator,
            Point screenPoint,
            out MeshFacePick pick)
        {
            pick = null!;
            Viewport3D? viewport3d = HelixViewportProjection.ResolveViewport3D(viewport);
            if (viewport3d == null)
            {
                return false;
            }

            Ray3D? ray = Viewport3DHelper.GetRay(viewport3d, screenPoint);
            if (ray != null
                && MeshFaceRaycaster.TryPick(
                    ray,
                    coordinator.GetMeshPickTargets(),
                    out pick,
                    out _,
                    out _))
            {
                return true;
            }

            return TryPickWithHelixHits(viewport3d, coordinator, screenPoint, out pick);
        }

        public static bool TryPickNode(
            HelixViewport3D viewport,
            MachineVisualCoordinator coordinator,
            Point screenPoint,
            out string nodeId)
        {
            nodeId = string.Empty;
            Viewport3D? viewport3d = HelixViewportProjection.ResolveViewport3D(viewport);
            if (viewport3d == null)
            {
                return false;
            }

            IList<Viewport3DHelper.HitResult> hits = Viewport3DHelper.FindHits(viewport3d, screenPoint);
            foreach (Viewport3DHelper.HitResult? hit in hits.OrderBy(h => h.Distance))
            {
                if (hit?.Model == null)
                {
                    continue;
                }

                if (coordinator.IsOverlayVisual(hit.Visual))
                {
                    continue;
                }

                if (coordinator.TryResolveNodeForPick(hit.Model, hit.Visual, hit.Mesh, out nodeId, out _))
                {
                    if (coordinator.IsNodeHidden(nodeId))
                    {
                        nodeId = string.Empty;
                        continue;
                    }
                    return true;
                }
            }

            Ray3D? ray = Viewport3DHelper.GetRay(viewport3d, screenPoint);
            return ray != null && coordinator.TryPickWireframeNode(ray, out nodeId);
        }

        private static bool TryPickWithHelixHits(
            Viewport3D viewport3d,
            MachineVisualCoordinator coordinator,
            Point screenPoint,
            out MeshFacePick pick)
        {
            pick = null!;
            Ray3D? pickRay = Viewport3DHelper.GetRay(viewport3d, screenPoint);
            IList<Viewport3DHelper.HitResult> hits = Viewport3DHelper.FindHits(viewport3d, screenPoint);
            foreach (Viewport3DHelper.HitResult? hit in hits.OrderBy(h => h.Distance))
            {
                if (hit?.Model == null)
                {
                    continue;
                }

                if (coordinator.IsOverlayVisual(hit.Visual))
                {
                    continue;
                }

                if (!coordinator.TryResolveNodeForPick(hit.Model, hit.Visual, hit.Mesh, out string nodeId, out NodeSlotInfo slot))
                {
                    continue;
                }
                if (coordinator.IsNodeHidden(nodeId))
                {
                    continue;
                }

                if (!TryComputeFace(hit, slot, nodeId, pickRay, out pick))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool TryComputeFace(
            Viewport3DHelper.HitResult hit,
            NodeSlotInfo slot,
            string nodeId,
            Ray3D? pickRay,
            out MeshFacePick pick)
        {
            pick = null!;
            if (hit.Mesh is not MeshGeometry3D mesh)
            {
                return false;
            }

            Point3D pointWorld = hit.Position;
            Vector3D normalWorld = hit.Normal;
            int triangleIndex = TryGetTriangleIndex(hit, mesh);
            if (triangleIndex >= 0)
            {
                MeshFaceRaycaster.ExpandCoplanarPatch(mesh, triangleIndex, out Point3D patchCenterLocal, out Vector3D patchNormalLocal);
                patchNormalLocal.Normalize();

                Point3D patchWorld = slot.MeshToWorld.Transform(patchCenterLocal);
                Point3D normalEnd = slot.MeshToWorld.Transform(patchCenterLocal + patchNormalLocal);
                normalWorld = normalEnd - patchWorld;
                if (normalWorld.LengthSquared > 1e-12)
                {
                    normalWorld.Normalize();
                    pointWorld = patchWorld;
                }
            }

            if (normalWorld.LengthSquared < 1e-12)
            {
                normalWorld = EstimateNormalFromMesh(mesh, pointWorld, slot.MeshToWorld);
            }

            normalWorld.Normalize();
            if (pickRay != null)
            {
                OrientNormalTowardPoint(ref normalWorld, pickRay.Origin, pointWorld);
            }
            GeneralTransform3D? worldToMesh = slot.MeshToWorld.Inverse;
            if (worldToMesh == null)
            {
                return false;
            }

            Point3D pointMeshLocal = worldToMesh.Transform(pointWorld);
            Point3D normalEndMesh = worldToMesh.Transform(pointWorld + normalWorld);
            Vector3D normalMeshLocal = normalEndMesh - pointMeshLocal;
            if (normalMeshLocal.LengthSquared > 1e-12)
            {
                normalMeshLocal.Normalize();
            }

            pick = new MeshFacePick
            {
                NodeId = nodeId,
                PointWorld = pointWorld,
                NormalWorld = normalWorld,
                PointMeshLocal = pointMeshLocal,
                NormalMeshLocal = normalMeshLocal,
                SeedTriangleIndex = triangleIndex,
                SourceMesh = mesh
            };
            return true;
        }

        private static int TryGetTriangleIndex(Viewport3DHelper.HitResult hit, MeshGeometry3D mesh)
        {
            RayMeshGeometry3DHitTestResult? rayHit = hit.RayHit;
            if (rayHit == null)
            {
                return -1;
            }

            int i1 = rayHit.VertexIndex1;
            int i2 = rayHit.VertexIndex2;
            int i3 = rayHit.VertexIndex3;
            Int32Collection? indices = mesh.TriangleIndices;
            if (indices == null || indices.Count < 3)
            {
                return -1;
            }

            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                if (MatchesTriangle(indices[i], indices[i + 1], indices[i + 2], i1, i2, i3))
                {
                    return i / 3;
                }
            }

            return -1;
        }

        private static bool MatchesTriangle(int a, int b, int c, int i1, int i2, int i3)
        {
            return (a == i1 && b == i2 && c == i3)
                   || (a == i1 && b == i3 && c == i2)
                   || (a == i2 && b == i1 && c == i3)
                   || (a == i2 && b == i3 && c == i1)
                   || (a == i3 && b == i1 && c == i2)
                   || (a == i3 && b == i2 && c == i1);
        }

        private static Vector3D EstimateNormalFromMesh(MeshGeometry3D mesh, Point3D pointWorld, Transform3D meshToWorld)
        {
            Point3D bestA = default;
            Point3D bestB = default;
            Point3D bestC = default;
            double bestDist = double.MaxValue;

            Point3DCollection? positions = mesh.Positions;
            Int32Collection? indices = mesh.TriangleIndices;
            if (positions == null || positions.Count < 3)
            {
                return new Vector3D(0, 0, 1);
            }

            if (indices == null || indices.Count < 3)
            {
                for (int i = 0; i + 2 < positions.Count; i += 3)
                {
                    ConsiderTriangle(positions[i], positions[i + 1], positions[i + 2]);
                }
            }
            else
            {
                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    ConsiderTriangle(
                        positions[indices[i]],
                        positions[indices[i + 1]],
                        positions[indices[i + 2]]);
                }
            }

            Vector3D edge1 = bestB - bestA;
            Vector3D edge2 = bestC - bestA;
            Vector3D normal = Vector3D.CrossProduct(edge1, edge2);
            if (normal.LengthSquared < 1e-12)
            {
                return new Vector3D(0, 0, 1);
            }

            normal.Normalize();
            return normal;

            void ConsiderTriangle(Point3D a, Point3D b, Point3D c)
            {
                Point3D aw = meshToWorld.Transform(a);
                Point3D bw = meshToWorld.Transform(b);
                Point3D cw = meshToWorld.Transform(c);
                Point3D center = new(
                    (aw.X + bw.X + cw.X) / 3,
                    (aw.Y + bw.Y + cw.Y) / 3,
                    (aw.Z + bw.Z + cw.Z) / 3);
                double dist = (center - pointWorld).LengthSquared;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestA = aw;
                    bestB = bw;
                    bestC = cw;
                }
            }
        }

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
