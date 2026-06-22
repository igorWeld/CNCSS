using System.IO;

namespace CNCSS.Vis
{
    /// <summary>Supported 3D mesh import extensions for machine node models.</summary>
    public static class MeshImportFormats
    {
        public static readonly string[] AllExtensions =
        [
            ".stl",
            ".obj",
            ".step",
            ".stp",
            ".iges",
            ".igs"
        ];

        public const string OpenFileDialogFilter =
            "3D модели|*.stl;*.obj;*.step;*.stp;*.iges;*.igs|" +
            "STL (*.stl)|*.stl|" +
            "Wavefront OBJ (*.obj)|*.obj|" +
            "STEP (*.step;*.stp)|*.step;*.stp|" +
            "IGES (*.iges;*.igs)|*.iges;*.igs|" +
            "Все файлы|*.*";

        public static bool IsSupported(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string ext = Path.GetExtension(path);
            return AllExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsAssimpPath(string path) => IsStepPath(path) || IsIgesPath(path);

        public static bool IsStepPath(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".step", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".stp", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsIgesPath(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".iges", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".igs", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsObjPath(string path) =>
            Path.GetExtension(path).Equals(".obj", StringComparison.OrdinalIgnoreCase);

        public static bool IsStlPath(string path) =>
            Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase);
    }
}
