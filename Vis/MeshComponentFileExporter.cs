using System.IO;
using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    /// <summary>Writes a WPF <see cref="Model3D"/> to a temporary STL for profile storage.</summary>
    public static class MeshComponentFileExporter
    {
        public static string ExportToTempStl(Model3D model)
        {
            string path = Path.Combine(Path.GetTempPath(), "cncss_import_" + Guid.NewGuid().ToString("N") + ".stl");
            BinaryStlExporter.Write(path, model);
            return path;
        }

        public static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup of temp export files.
            }
        }
    }
}
