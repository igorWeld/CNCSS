using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    /// <summary>Splits a loaded <see cref="Model3D"/> into per-body <see cref="MeshComponent"/> entries.</summary>
    public static class MeshComponentSplitter
    {
        public static IReadOnlyList<MeshComponent> FromModel(Model3D? model, string defaultName, Func<int, string>? nameForIndex = null)
        {
            if (model == null)
            {
                return Array.Empty<MeshComponent>();
            }

            nameForIndex ??= i => $"{defaultName} ({i + 1})";

            if (model is Model3DGroup group)
            {
                var bodies = new List<Model3D>();
                foreach (Model3D child in group.Children)
                {
                    if (IsRenderable(child))
                    {
                        bodies.Add(child);
                    }
                }

                if (bodies.Count >= 2)
                {
                    return BuildList(bodies, nameForIndex);
                }
            }

            if (!IsRenderable(model))
            {
                return Array.Empty<MeshComponent>();
            }

            return
            [
                CreateComponent(0, defaultName, model, null)
            ];
        }

        private static IReadOnlyList<MeshComponent> BuildList(IReadOnlyList<Model3D> bodies, Func<int, string> nameForIndex)
        {
            var list = new List<MeshComponent>(bodies.Count);
            for (int i = 0; i < bodies.Count; i++)
            {
                list.Add(CreateComponent(i, nameForIndex(i), bodies[i], null));
            }

            return list;
        }

        private static MeshComponent CreateComponent(int index, string displayName, Model3D model, string? hint) =>
            new()
            {
                Index = index,
                DisplayName = displayName,
                Model = model,
                SourceHint = hint,
                TriangleCount = CountTriangles(model)
            };

        private static bool IsRenderable(Model3D model)
        {
            if (model is GeometryModel3D geom)
            {
                return geom.Geometry is MeshGeometry3D mesh
                       && mesh.Positions != null
                       && mesh.Positions.Count >= 3
                       && mesh.TriangleIndices != null
                       && mesh.TriangleIndices.Count >= 3;
            }

            if (model is Model3DGroup group)
            {
                return group.Children.Any(IsRenderable);
            }

            return false;
        }

        private static int CountTriangles(Model3D model)
        {
            if (model is GeometryModel3D geom && geom.Geometry is MeshGeometry3D mesh)
            {
                return mesh.TriangleIndices.Count / 3;
            }

            if (model is Model3DGroup group)
            {
                return group.Children.Sum(CountTriangles);
            }

            return 0;
        }
    }
}
