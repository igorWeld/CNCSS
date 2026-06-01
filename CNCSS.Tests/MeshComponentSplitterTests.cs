using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class MeshComponentSplitterTests
{
    [Fact]
    public void FromModel_single_geometry_returns_one_component()
    {
        Model3D model = CreateBoxModel();
        IReadOnlyList<MeshComponent> components = MeshComponentSplitter.FromModel(model, "Part");
        Assert.Single(components);
        Assert.Equal("Part", components[0].DisplayName);
    }

    [Fact]
    public void FromModel_group_with_two_children_returns_two_components()
    {
        var group = new Model3DGroup();
        group.Children.Add(CreateBoxModel());
        group.Children.Add(CreateBoxModel());

        IReadOnlyList<MeshComponent> components = MeshComponentSplitter.FromModel(group, "Asm");
        Assert.Equal(2, components.Count);
        Assert.Equal("Asm (1)", components[0].DisplayName);
        Assert.Equal("Asm (2)", components[1].DisplayName);
    }

    private static GeometryModel3D CreateBoxModel()
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(0, 0, 0),
                new(1, 0, 0),
                new(0, 1, 0)
            },
            TriangleIndices = new Int32Collection { 0, 1, 2 }
        };
        var material = new DiffuseMaterial(Brushes.LightGray);
        return new GeometryModel3D { Geometry = mesh, Material = material };
    }
}
