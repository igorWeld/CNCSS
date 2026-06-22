namespace CNCSS.Vis
{
    public sealed class MeshImportResult
    {
        public required string SourcePath { get; init; }

        public bool Success { get; init; }

        public string? ErrorMessage { get; init; }

        public IReadOnlyList<MeshComponent> Components { get; init; } = Array.Empty<MeshComponent>();

        public static MeshImportResult Failed(string sourcePath, string message) => new()
        {
            SourcePath = sourcePath,
            Success = false,
            ErrorMessage = message
        };

        public static MeshImportResult Ok(string sourcePath, IReadOnlyList<MeshComponent> components) => new()
        {
            SourcePath = sourcePath,
            Success = true,
            Components = components
        };
    }
}
