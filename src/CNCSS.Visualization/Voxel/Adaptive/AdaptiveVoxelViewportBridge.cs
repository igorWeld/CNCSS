using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Simulation.Execution;
using HelixToolkit.Wpf;

namespace CNCSS.Vis;

/// <summary>Связка VoxelSimulationEngine -> HelixViewport3D (обновление меша и модели инструмента).</summary>
public sealed class AdaptiveVoxelViewportBridge
{
    private readonly Material _stockMaterial;
    private readonly GeometryModel3D _stockModel;
    private readonly ModelVisual3D _stockVisual;
    private readonly TranslateTransform3D _toolTransform = new();

    public AdaptiveVoxelViewportBridge(Color stockColor)
    {
        _stockMaterial = MaterialHelper.CreateMaterial(stockColor);
        _stockModel = new GeometryModel3D { Material = _stockMaterial, BackMaterial = _stockMaterial };
        _stockVisual = new ModelVisual3D { Content = _stockModel };
        ToolVisual = new ModelVisual3D { Content = BuildToolModel() };
    }

    public ModelVisual3D StockVisual => _stockVisual;

    public ModelVisual3D ToolVisual { get; }

    public void Attach(HelixViewport3D viewport)
    {
        if (!viewport.Children.Contains(_stockVisual))
        {
            viewport.Children.Add(_stockVisual);
        }

        if (!viewport.Children.Contains(ToolVisual))
        {
            viewport.Children.Add(ToolVisual);
        }
    }

    public void Bind(VoxelSimulationEngine engine)
    {
        engine.MeshReady += mesh =>
        {
            if (_stockVisual.Dispatcher.CheckAccess())
            {
                _stockModel.Geometry = mesh;
            }
            else
            {
                _stockVisual.Dispatcher.BeginInvoke(() => _stockModel.Geometry = mesh);
            }
        };
    }

    public void UpdateToolPosition(Point3D tipPosition)
    {
        _toolTransform.OffsetX = tipPosition.X;
        _toolTransform.OffsetY = tipPosition.Y;
        _toolTransform.OffsetZ = tipPosition.Z;
    }

    private Model3D BuildToolModel()
    {
        var builder = new MeshBuilder(false, false);
        builder.AddCylinder(new Point3D(0, 0, 0), new Point3D(0, 0, -20), 5, 20);
        builder.AddCylinder(new Point3D(0, 0, -20), new Point3D(0, 0, -60), 6, 20);
        MeshGeometry3D mesh = builder.ToMesh();
        mesh.Freeze();

        var group = new Model3DGroup();
        group.Children.Add(new GeometryModel3D(mesh, MaterialHelper.CreateMaterial(Colors.SlateGray)));
        group.Transform = _toolTransform;
        return group;
    }
}
