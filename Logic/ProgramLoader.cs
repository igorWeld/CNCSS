using System.IO;
using System.Linq;
using CNCSS.Data;
using CNCSS.Logic.ProgramLoading;

namespace CNCSS.Logic
{
    /// <summary>Загрузка NC-файла и первичная подготовка данных программы без привязки к WPF.</summary>
    public sealed class ProgramLoader
    {
        public ProgramLoadResult Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Path to NC program is empty.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Файл не найден: {filePath}", filePath);
            }

            string fullPath = Path.GetFullPath(filePath);
            string[] lines = File.ReadAllLines(fullPath);
            var parser = new GCodeParser();
            parser.ProcessFile(fullPath);

            int[] toolNumbers = parser.Commands
                .Where(c => c.ToolNumber.HasValue)
                .Select(c => c.ToolNumber!.Value)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            return new ProgramLoadResult
            {
                FilePath = filePath,
                FullPath = fullPath,
                Lines = lines,
                Parser = parser,
                ToolNumbers = toolNumbers
            };
        }
    }
}
