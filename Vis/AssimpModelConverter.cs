using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Converts Assimp scenes to WPF <see cref="Model3D"/> for STEP/IGES and other CAD formats.</summary>
    internal static class AssimpModelConverter
    {
        private static readonly Assimp.PostProcessSteps ImportFlags =
            Assimp.PostProcessSteps.Triangulate
            | Assimp.PostProcessSteps.JoinIdenticalVertices
            | Assimp.PostProcessSteps.ImproveCacheLocality
            | Assimp.PostProcessSteps.GenerateNormals;

        public static Model3D? LoadFromFile(string fullPath)
        {
            IReadOnlyList<MeshComponent> components = LoadComponentsFromFile(fullPath);
            if (components.Count == 0)
            {
                return null;
            }

            if (components.Count == 1)
            {
                return components[0].Model;
            }

            var group = new Model3DGroup();
            foreach (MeshComponent component in components)
            {
                group.Children.Add(component.Model);
            }

            return group;
        }

        public static IReadOnlyList<MeshComponent> LoadComponentsFromFile(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return Array.Empty<MeshComponent>();
            }

            if (MeshImportFormats.IsAssimpPath(fullPath))
            {
                IReadOnlyList<MeshComponent> occtComponents = OcctCadModelConverter.LoadComponentsFromFile(fullPath);
                if (occtComponents.Count > 0)
                {
                    return occtComponents;
                }

                if (!string.IsNullOrWhiteSpace(OcctCadModelConverter.LastError))
                {
                    return Array.Empty<MeshComponent>();
                }
            }

            return LoadComponentsAssimpOnly(fullPath);
        }

        public static IReadOnlyList<MeshComponent> LoadComponentsAssimpOnly(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return Array.Empty<MeshComponent>();
            }

            using var context = new Assimp.AssimpContext();
            Assimp.Scene? scene;
            try
            {
                scene = context.ImportFile(fullPath, ImportFlags);
            }
            catch (Assimp.AssimpException)
            {
                return Array.Empty<MeshComponent>();
            }

            if (scene == null || !scene.HasMeshes)
            {
                return Array.Empty<MeshComponent>();
            }

            return BuildComponentsFromScene(scene);
        }

        private static IReadOnlyList<MeshComponent> BuildComponentsFromScene(Assimp.Scene scene)
        {
            Material defaultMaterial = MaterialHelper.CreateMaterial(Colors.LightSteelBlue);
            var list = new List<MeshComponent>();
            int meshIndex = 0;
            var identity = new Assimp.Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            CollectSceneMeshes(scene.RootNode, identity, scene, defaultMaterial, list, ref meshIndex);
            return list;
        }

        private static void CollectSceneMeshes(
            Assimp.Node node,
            Assimp.Matrix4x4 parentTransform,
            Assimp.Scene scene,
            Material defaultMaterial,
            List<MeshComponent> output,
            ref int meshIndex)
        {
            Assimp.Matrix4x4 world = AssimpMatrixHelper.Multiply(parentTransform, node.Transform);

            foreach (int meshIndexInScene in node.MeshIndices)
            {
                if (meshIndexInScene < 0 || meshIndexInScene >= scene.MeshCount)
                {
                    continue;
                }

                Assimp.Mesh mesh = scene.Meshes[meshIndexInScene];
                if (mesh.VertexCount < 3 || mesh.FaceCount == 0)
                {
                    continue;
                }

                MeshGeometry3D? geometry = BuildGeometry(mesh, world);
                if (geometry == null)
                {
                    continue;
                }

                Material material = defaultMaterial;
                if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount)
                {
                    Material? mapped = TryCreateMaterial(scene.Materials[mesh.MaterialIndex]);
                    if (mapped != null)
                    {
                        material = mapped;
                    }
                }

                string name = !string.IsNullOrWhiteSpace(mesh.Name)
                    ? mesh.Name
                    : !string.IsNullOrWhiteSpace(node.Name)
                        ? node.Name
                        : "Компонент " + (meshIndex + 1);

                var body = new GeometryModel3D
                {
                    Geometry = geometry,
                    Material = material,
                    BackMaterial = material
                };

                output.Add(new MeshComponent
                {
                    Index = meshIndex,
                    DisplayName = name,
                    Model = body,
                    SourceHint = mesh.Name,
                    TriangleCount = geometry.TriangleIndices.Count / 3
                });
                meshIndex++;
            }

            foreach (Assimp.Node child in node.Children)
            {
                CollectSceneMeshes(child, world, scene, defaultMaterial, output, ref meshIndex);
            }
        }

        private static MeshGeometry3D? BuildGeometry(Assimp.Mesh mesh, Assimp.Matrix4x4 worldTransform)
        {
            var positions = new Point3DCollection(mesh.VertexCount);
            foreach (Assimp.Vector3D v in mesh.Vertices)
            {
                var local = new Point3D(v.X, v.Y, v.Z);
                positions.Add(AssimpMatrixHelper.TransformPoint(worldTransform, local));
            }

            var indices = new Int32Collection();
            foreach (Assimp.Face face in mesh.Faces)
            {
                if (face.IndexCount < 3)
                {
                    continue;
                }

                for (int i = 1; i < face.IndexCount - 1; i++)
                {
                    indices.Add(face.Indices[0]);
                    indices.Add(face.Indices[i]);
                    indices.Add(face.Indices[i + 1]);
                }
            }

            if (positions.Count < 3 || indices.Count < 3)
            {
                return null;
            }

            var geometry = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices
            };

            if (mesh.HasNormals && mesh.Normals.Count == mesh.VertexCount)
            {
                var normals = new Vector3DCollection(mesh.VertexCount);
                foreach (Assimp.Vector3D n in mesh.Normals)
                {
                    var normal = new Vector3D(n.X, n.Y, n.Z);
                    Point3D rotated = AssimpMatrixHelper.TransformPoint(
                        worldTransform,
                        new Point3D(normal.X, normal.Y, normal.Z));
                    Point3D origin = AssimpMatrixHelper.TransformPoint(worldTransform, new Point3D(0, 0, 0));
                    normals.Add(new Vector3D(rotated.X - origin.X, rotated.Y - origin.Y, rotated.Z - origin.Z));
                }

                geometry.Normals = normals;
            }
            else
            {
                geometry.Normals = MeshGeometryHelper.CalculateNormals(geometry);
            }

            return geometry;
        }

        private static Material? TryCreateMaterial(Assimp.Material mat)
        {
            Assimp.Color4D color = mat.ColorDiffuse;
            var brush = new SolidColorBrush(Color.FromScRgb(
                Math.Clamp(color.A, 0, 1),
                Math.Clamp(color.R, 0, 1),
                Math.Clamp(color.G, 0, 1),
                Math.Clamp(color.B, 0, 1)));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return new DiffuseMaterial(brush);
        }
    }
}
