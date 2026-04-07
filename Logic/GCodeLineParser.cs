using System.Globalization;
using System.Text.RegularExpressions;
using CNCSS.Data;

namespace CNCSS.Logic
{
    /// <summary>
    /// Отвечает за парсинг одной строки G-кода.
    /// Выделяет комментарии, коды G/M и параметры.
    /// Не изменяет состояние станка.
    /// </summary>
    public class GCodeLineParser
    {
        public ParsedLine Parse(string line, int lineNumber)
        {
            var result = new ParsedLine { LineNumber = lineNumber };
            
            if (string.IsNullOrWhiteSpace(line))
                return result;

            // Очищаем строку
            result.CleanLine = CleanLine(line);
            
            // Извлекаем комментарий
            result.Comment = ExtractComment(ref result.CleanLine);

            if (string.IsNullOrWhiteSpace(result.CleanLine))
                return result;

            // Парсим параметры
            result.Parameters = ParseParameters(result.CleanLine);
            
            // Извлекаем G-коды
            result.GCodes = ExtractAllCodes(result.CleanLine, GCodeRegistry.LETTER_G);
            
            // Извлекаем M-коды
            result.MCodes = ExtractAllCodes(result.CleanLine, GCodeRegistry.LETTER_M);

            return result;
        }

        private static string CleanLine(string line) => line.Trim().ToUpperInvariant();

        private string? ExtractComment(ref string line)
        {
            var matchParen = Regex.Match(line, @"\(([^)]*)\)");
            if (matchParen.Success)
            {
                line = line.Remove(matchParen.Index, matchParen.Length);
                return matchParen.Groups[1].Value.Trim();
            }

            int semiIndex = line.IndexOf(';');
            if (semiIndex >= 0)
            {
                string comment = line[(semiIndex + 1)..].Trim();
                line = line[..semiIndex];
                return comment;
            }

            return null;
        }

        private static Dictionary<string, double> ParseParameters(string line)
        {
            var parameters = new Dictionary<string, double>();
            var matches = Regex.Matches(line, @"([A-Z])(-?\d+\.?\d*)");

            foreach (Match match in matches)
            {
                string letter = match.Groups[1].Value;
                if (letter is "G" or "M")
                    continue;

                if (double.TryParse(match.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                    parameters[letter] = value;
            }

            return parameters;
        }

        private List<GCodeTemplate> ExtractAllCodes(string line, string letter)
        {
            var codes = new List<GCodeTemplate>();
            var matches = Regex.Matches(line, $@"({letter})(\d+)");

            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[2].Value, out int number))
                {
                    GCodeTemplate? code = letter == GCodeRegistry.LETTER_G
                        ? GCodeRegistry.GetGCode(number)
                        : GCodeRegistry.GetMCode(number);

                    if (code.HasValue)
                        codes.Add(code.Value);
                }
            }

            return codes;
        }
    }

    /// <summary>
    /// Результат парсинга одной строки G-кода.
    /// </summary>
    public class ParsedLine
    {
        public int LineNumber { get; set; }
        public string CleanLine { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public Dictionary<string, double> Parameters { get; set; } = new();
        public List<GCodeTemplate> GCodes { get; set; } = new();
        public List<GCodeTemplate> MCodes { get; set; } = new();
        
        public bool IsEmpty => string.IsNullOrWhiteSpace(CleanLine) && 
                               Parameters.Count == 0 && 
                               GCodes.Count == 0 && 
                               MCodes.Count == 0;
    }
}
