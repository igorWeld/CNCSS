using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;
using CNCSS.Vis;
using HelixToolkit.Wpf;

namespace CNCSS.Tests;

public sealed class ToolMountHelperTests
{
    [Fact]
    public void CenterXY_UsesMeshCenterInNodeFrame()
    {
        var mesh = new MeshBuilder(false, false);
        mesh.AddBox(new Point3D(100, 200, 300), 20, 40, 60);
        var model = new GeometryModel3D { Geometry = mesh.ToMesh() };

        var meshTransform = new TranslateTransform3D(10, 20, 30);
        Point3D stlCenter = StlModelMetrics.GetCenter(model);
        Point3D centerInNode = meshTransform.Transform(stlCenter);

        MachineGeometryPoint result = ToolMountHelper.CenterXY(
            centerInNode,
            new MachineGeometryPoint { X = 999, Y = 888, Z = 50 });

        Assert.Equal(centerInNode.X, result.X, 3);
        Assert.Equal(centerInNode.Y, result.Y, 3);
        Assert.Equal(50, result.Z, 3);
    }
}
