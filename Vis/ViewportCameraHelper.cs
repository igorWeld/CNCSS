using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Standard camera views for Helix viewports.</summary>
    public static class ViewportCameraHelper
    {
        private static readonly Vector3D MainWindowEyeOffset = new(1, -1, 0.72);
        private static readonly Vector3D MachineSetupEyeOffset = new(-1, -1, 0.72);

        /// <summary>Main window: front-right-top (+X, −Y, +Z), Z up.</summary>
        public static void SetDefaultMainSceneView(HelixViewport3D viewport, double distanceFactor = 2.15) =>
            ApplyView(viewport, MainWindowEyeOffset, distanceFactor, new Point3D(900, -900, 650));

        /// <summary>Machine setup preview: front-left-top (−X, −Y, +Z), Y+ away, Z up.</summary>
        public static void SetMachineSetupPreviewView(HelixViewport3D viewport, double distanceFactor = 2.15) =>
            ApplyView(viewport, MachineSetupEyeOffset, distanceFactor, new Point3D(-900, -900, 650));

        private static void ApplyView(
            HelixViewport3D viewport,
            Vector3D eyeOffset,
            double distanceFactor,
            Point3D fallbackPosition)
        {
            ArgumentNullException.ThrowIfNull(viewport);

            Rect3D bounds = Visual3DHelper.FindBounds(viewport.Children);
            if (bounds.IsEmpty || bounds.SizeX + bounds.SizeY + bounds.SizeZ < 1e-6)
            {
                ApplyFallbackView(viewport, fallbackPosition);
                return;
            }

            Point3D center = bounds.GetCenter();
            double radius = 0.5 * Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            if (radius < 1)
            {
                radius = 400;
            }

            double distance = Math.Max(radius * distanceFactor, 200);
            eyeOffset.Normalize();

            var camera = viewport.Camera as PerspectiveCamera ?? new PerspectiveCamera { FieldOfView = 45 };
            camera.Position = center + eyeOffset * distance;
            camera.LookDirection = center - camera.Position;
            camera.UpDirection = new Vector3D(0, 0, 1);
            viewport.Camera = camera;
        }

        private static void ApplyFallbackView(HelixViewport3D viewport, Point3D position)
        {
            var camera = viewport.Camera as PerspectiveCamera ?? new PerspectiveCamera { FieldOfView = 45 };
            camera.Position = position;
            camera.LookDirection = new Vector3D(-position.X, -position.Y, -position.Z);
            camera.UpDirection = new Vector3D(0, 0, 1);
            viewport.Camera = camera;
        }
    }
}
