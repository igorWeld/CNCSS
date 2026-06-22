using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Controls;
using WpfProjectionCamera = System.Windows.Media.Media3D.ProjectionCamera;
using WpfPerspectiveCamera = System.Windows.Media.Media3D.PerspectiveCamera;
using DxProjectionCamera = HelixToolkit.Wpf.SharpDX.ProjectionCamera;
using DxPerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;

namespace CNCSS.Vis.SharpDx;

/// <summary>Синхронизация камеры WPF Helix и SharpDX overlay.</summary>
public static class CameraSyncService
{
    public static void Sync(HelixViewport3D source, Viewport3DX target)
    {
        if (source.Camera is not WpfProjectionCamera src || target.Camera is not DxProjectionCamera dst)
        {
            return;
        }

        CopyCamera(src, dst);
        source.CameraChanged += (_, _) =>
        {
            if (source.Camera is WpfProjectionCamera s && target.Camera is DxProjectionCamera d)
            {
                CopyCamera(s, d);
            }
        };
    }

    private static void CopyCamera(WpfProjectionCamera src, DxProjectionCamera dst)
    {
        dst.Position = src.Position;
        dst.LookDirection = src.LookDirection;
        dst.UpDirection = src.UpDirection;
        dst.NearPlaneDistance = src.NearPlaneDistance;
        dst.FarPlaneDistance = src.FarPlaneDistance;
        if (src is WpfPerspectiveCamera ps && dst is DxPerspectiveCamera pd)
        {
            pd.FieldOfView = ps.FieldOfView;
        }
    }
}
