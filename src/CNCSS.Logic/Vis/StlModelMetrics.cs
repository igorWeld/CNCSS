using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    public static class StlModelMetrics
    {
        public static double GetMaxExtent(Model3D model)
        {
            Rect3D bounds = model.Bounds;
            if (bounds.IsEmpty || bounds.SizeX <= 0 && bounds.SizeY <= 0 && bounds.SizeZ <= 0)
            {
                bounds = ComputeBoundsRecursive(model);
            }

            return Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
        }

        public static Point3D GetCenter(Model3D model)
        {
            Rect3D bounds = ComputeBoundsRecursive(model);
            if (bounds.IsEmpty)
            {
                return new Point3D(0, 0, 0);
            }

            return new Point3D(
                bounds.X + bounds.SizeX * 0.5,
                bounds.Y + bounds.SizeY * 0.5,
                bounds.Z + bounds.SizeZ * 0.5);
        }

        public static Rect3D ComputeBoundsRecursive(Model3D model)
        {
            if (model is Model3DGroup group)
            {
                bool hasAny = false;
                Rect3D combined = Rect3D.Empty;
                foreach (Model3D child in group.Children)
                {
                    Rect3D childBounds = ComputeBoundsRecursive(child);
                    if (childBounds.IsEmpty)
                    {
                        continue;
                    }

                    combined = hasAny ? Rect3D.Union(combined, childBounds) : childBounds;
                    hasAny = true;
                }

                return combined;
            }

            return model.Bounds;
        }
    }
}
