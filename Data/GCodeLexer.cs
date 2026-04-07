using System;
using System.Collections.Generic;
using System.Linq;

namespace CNCSS.Data
{
    /// <summary>
    /// Представляет одну команду G-кода с ее параметрами.
    /// </summary>
    public class GCodeToken
    {
        public string? Code { get; set; } // Например, "G0", "G1", "M3"
        public Dictionary<char, double> Parameters { get; set; } = new();
        public string? Comment { get; set; }
        public int LineNumber { get; set; }

        public override string ToString()
        {
            var paramStr = string.Join(" ", Parameters.Select(kvp => $"{kvp.Key}{kvp.Value:F3}"));
            return $"{Code} {paramStr}".Trim();
        }
    }

    /// <summary>
    /// Отвечает за преобразование сырых строк G-кода в структурированные токены.
    /// Не изменяет состояние станка.
    /// </summary>
    public static class GCodeLexer
    {
        public static List<GCodeToken> ParseLines(IEnumerable<string> lines)
        {
            var tokens = new List<GCodeToken>();
            int lineNum = 0;

            foreach (var rawLine in lines)
            {
                lineNum++;
                var token = ParseLine(rawLine, lineNum);
                if (token != null)
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        private static GCodeToken? ParseLine(string line, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            // Удаляем комментарии в скобках и после точки с запятой
            var cleanLine = line;
            var parenIndex = cleanLine.IndexOf('(');
            if (parenIndex >= 0)
            {
                var closeParen = cleanLine.IndexOf(')', parenIndex);
                if (closeParen >= 0)
                    cleanLine = cleanLine.Remove(parenIndex, closeParen - parenIndex + 1);
                else
                    cleanLine = cleanLine.Remove(parenIndex);
            }

            var semiIndex = cleanLine.IndexOf(';');
            if (semiIndex >= 0)
                cleanLine = cleanLine.Substring(0, semiIndex);

            cleanLine = cleanLine.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(cleanLine)) return null;

            var token = new GCodeToken { LineNumber = lineNumber };
            
            // Извлекаем комментарий в конце строки если он был в оригинале (опционально, здесь упрощено)
            // Парсим слова (Код + Параметры)
            var parts = cleanLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            
            string? currentCode = null;

            foreach (var part in parts)
            {
                if (part.Length < 2) continue;

                char letter = part[0];
                if (char.IsLetter(letter))
                {
                    if (letter == 'G' || letter == 'M' || letter == 'T' || letter == 'S')
                    {
                        // Это код команды
                        if (currentCode != null && token.Code == null)
                        {
                            // Первый найденный код становится основным кодом команды
                            token.Code = currentCode;
                        }
                        else if (token.Code != null)
                        {
                            // Если уже есть код, это может быть модальный код в той же строке (редко, но бывает)
                            // Для простоты пока считаем, что команда одна, либо объединяем логику позже.
                            // В стандартном G-code в строке обычно один основной исполнительный код.
                        }
                        
                        currentCode = part;
                        if (token.Code == null) token.Code = currentCode;
                    }
                    else if (double.TryParse(part.Substring(1), out double value))
                    {
                        // Это параметр (X, Y, Z, F, S, I, J, K, R и т.д.)
                        token.Parameters[letter] = value;
                    }
                }
            }

            if (token.Code == null && !token.Parameters.Any())
            {
                return null; // Пустая или некорректная строка
            }
            
            // Если кода нет, но параметры есть (например, продолжение движения), 
            // код берется из предыдущего состояния (обрабатывается в Интерпретаторе)
            
            return token;
        }
    }
}
