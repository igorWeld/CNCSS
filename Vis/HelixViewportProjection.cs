using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    internal static class HelixViewportProjection
    {
        public static Point3D? UnProject(HelixViewport3D host, Point screen)
        {
            Viewport3D? viewport = ResolveViewport3D(host);
            return viewport != null ? Viewport3DHelper.UnProject(viewport, screen) : null;
        }

        public static Viewport3D? ResolveViewport3D(HelixViewport3D host)
        {
            if (host.Template != null)
            {
                host.ApplyTemplate();
            }

            return FindViewport3D(host);
        }

        private static Viewport3D? FindViewport3D(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is Viewport3D viewport)
                {
                    return viewport;
                }

                Viewport3D? nested = FindViewport3D(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
