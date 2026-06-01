namespace CNCSS.Vis
{
    /// <summary>Maps STL file units to simulation millimeters (G-code, stock, toolpath).</summary>
    public static class StlUnitScaleHelper
    {
        /// <summary>Below this extent, treat file units as meters (e.g. 0.635 m → 635 mm).</summary>
        public const double MeterToMmThreshold = 5.0;

        /// <summary>Above this extent, treat as oversized raw units and scale down toward mm.</summary>
        public const double OversizedUnitsThreshold = 5000.0;

        public static double InferTargetMaxExtentMm(double fileMaxExtent)
        {
            if (fileMaxExtent <= 1e-9)
            {
                return 1.0;
            }

            if (fileMaxExtent < MeterToMmThreshold)
            {
                return fileMaxExtent * 1000.0;
            }

            if (fileMaxExtent > OversizedUnitsThreshold)
            {
                return fileMaxExtent / 1000.0;
            }

            return fileMaxExtent;
        }

        public static double ComputeMeshScale(double fileMaxExtent, double targetMaxExtentMm)
        {
            if (fileMaxExtent <= 1e-9 || targetMaxExtentMm <= 1e-9)
            {
                return 1.0;
            }

            return targetMaxExtentMm / fileMaxExtent;
        }
    }

    public readonly record struct StlAnalysisResult(
        double SourceMaxExtent,
        double SuggestedTargetMaxExtentMm,
        double SuggestedMeshScale);
}
