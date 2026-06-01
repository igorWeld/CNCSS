using System.IO;

namespace CNCSS.Logic.NcPrograms
{
    /// <summary>Filesystem operations for NC program catalog used by FANUC DIR workflow.</summary>
    public sealed class NcProgramCatalogService
    {
        private static readonly string[] SupportedExtensions = [".nc", ".ngc", ".tap"];

        public string ResolveProgramsDirectory(string? currentProgramSourcePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(currentProgramSourcePath))
                {
                    string? d = Path.GetDirectoryName(currentProgramSourcePath);
                    if (!string.IsNullOrEmpty(d))
                    {
                        return Path.GetFullPath(d);
                    }
                }
            }
            catch
            {
            }

            return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        }

        public bool TryResolveProgramFile(string? currentProgramSourcePath, string normalizedOLine, out string? fullPath)
        {
            fullPath = null;
            if (string.IsNullOrEmpty(normalizedOLine))
            {
                return false;
            }

            string dir = ResolveProgramsDirectory(currentProgramSourcePath);
            try
            {
                if (!Directory.Exists(dir))
                {
                    string direct = Path.GetFullPath(Path.Combine(dir, normalizedOLine + ".nc"));
                    if (File.Exists(direct))
                    {
                        fullPath = direct;
                        return true;
                    }

                    return false;
                }

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string ext in SupportedExtensions)
                {
                    foreach (string f in Directory.EnumerateFiles(dir, "*" + ext))
                    {
                        set.Add(Path.GetFullPath(f));
                    }
                }

                foreach (string fp in set.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(fp), normalizedOLine, StringComparison.OrdinalIgnoreCase))
                    {
                        fullPath = fp;
                        return true;
                    }
                }

                string fallback = Path.GetFullPath(Path.Combine(dir, normalizedOLine + ".nc"));
                if (Path.Exists(fallback))
                {
                    fullPath = fallback;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public string CreateProgram(string? currentProgramSourcePath, string normalizedOLine)
        {
            string fileName = normalizedOLine + ".nc";
            if (string.IsNullOrEmpty(normalizedOLine) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                fileName.Contains(Path.DirectorySeparatorChar) ||
                fileName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new InvalidOperationException("PROGRAM NAME INVALID");
            }

            string dir = ResolveProgramsDirectory(currentProgramSourcePath);
            string path = Path.Combine(dir, fileName);
            if (File.Exists(path))
            {
                throw new InvalidOperationException($"PROGRAM FILE EXISTS ({fileName})");
            }

            string fullPath = Path.GetFullPath(path);
            string? parentDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            File.WriteAllText(path, normalizedOLine + Environment.NewLine);
            return fullPath;
        }

        public void DeleteProgram(string path)
        {
            File.Delete(path);
        }
    }
}
