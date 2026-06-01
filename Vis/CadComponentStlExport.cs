namespace CNCSS.Vis
{
    /// <summary>Intermediate CAD import result: tessellated STL on disk (safe for background thread).</summary>
    internal sealed class CadComponentStlExport
    {
        public required int Index { get; init; }

        public required string DisplayName { get; init; }

        public required string TempStlPath { get; init; }
    }
}
