using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CNCSS.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CNCSS.Logic
{
    /// <summary>
    /// Основной парсер G-кода.
    /// Отвечает за чтение файлов, разбор строк на команды и параметры,
    /// а также за отслеживание состояния станка в процессе разбора.
    /// </summary>
    public class GCodeParser : IGCodeParser
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        private static bool _consoleAllocated = false;

        /// <summary>Текущее состояние станка в процессе парсинга.</summary>
        public MachineState State { get; }
        /// <summary>Список всех разобранных команд из файла.</summary>
        public List<ParsedCommand> Commands { get; } = new();

        /// <summary>Инициализирует новый экземпляр парсера с начальным состоянием.</summary>
        public GCodeParser()
        {
            State = new MachineState();
            EnsureConsole();
        }

        /// <summary>Инициализирует парсер с заданным начальным состоянием.</summary>
        public GCodeParser(MachineState state)
        {
            State = state ?? new MachineState();
            EnsureConsole();
        }

        /// <summary>Выделяет консоль для отладочного вывода, если она еще не выделена.</summary>
        private void EnsureConsole()
        {
            if (!_consoleAllocated)
            {
                AllocConsole();
                _consoleAllocated = true;
                System.Console.WriteLine("=== CNCSS G-Code Parser Console ===");
            }
        }

        /// <summary>
        /// Обрабатывает файл G-кода целиком.
        /// </summary>
        /// <param name="filePath">Путь к файлу .nc или .txt.</param>
        /// <returns>Итоговое состояние станка после выполнения всех команд.</returns>
        public MachineState ProcessFile(string filePath)
        {
            State.Reset();
            Commands.Clear();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден: {filePath}");

            System.Console.WriteLine($"Загрузка файла: {filePath}");
            var lines = File.ReadAllLines(filePath);

            for (int i = 0; i < lines.Length; i++)
                ProcessLine(lines[i], i + 1);

            System.Console.WriteLine($"Загрузка завершена. Команд: {Commands.Count}");
            return State;
        }

        /// <summary>
        /// Разбирает одну строку G-кода.
        /// </summary>
        /// <param name="line">Текст строки.</param>
        /// <param name="lineNumber">Порядковый номер строки в файле.</param>
        public void ProcessLine(string line, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            line = CleanLine(line);
            string? comment = ExtractComment(ref line);

            if (string.IsNullOrWhiteSpace(line))
            {
                if (!string.IsNullOrEmpty(comment))
                    Commands.Add(new ParsedCommand { LineNumber = lineNumber, Comment = comment });
                return;
            }

            var parameters = ParseParameters(line);
            var gCodes = ExtractAllCodes(line, GCodeRegistry.LETTER_G);
            var mCodes = ExtractAllCodes(line, GCodeRegistry.LETTER_M);

            var command = new ParsedCommand
            {
                LineNumber = lineNumber,
                RawLine = line,
                Comment = comment,
                Parameters = parameters,
                GCodes = gCodes,
                MCodes = mCodes,
                StartState = State.Clone()
            };

            ApplyParameters(parameters, command);
            command.EndState = State.Clone();
            Commands.Add(command);
        }

        /// <summary>Извлекает все коды (G или M) из строки по заданной букве.</summary>
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

        /// <summary>Очищает строку от лишних пробелов и приводит к верхнему регистру.</summary>
        private static string CleanLine(string line) => line.Trim().ToUpperInvariant();

        /// <summary>Извлекает комментарии из строки (в скобках или после точки с запятой).</summary>
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
                string c = line[(semiIndex + 1)..].Trim();
                line = line[..semiIndex];
                return c;
            }

            return null;
        }

        /// <summary>Разбирает параметры строки (X, Y, Z, F, S и т.д.) в словарь.</summary>
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

        /// <summary>Применяет разобранные параметры и коды к текущему состоянию станка.</summary>
        private void ApplyParameters(Dictionary<string, double> parameters, ParsedCommand command)
        {
            State.SavePreviousPosition();

            double newX = State.X;
            double newY = State.Y;
            double newZ = State.Z;
            double newA = State.A;
            double newB = State.B;
            double newC = State.C;

            foreach (var gCode in command.GCodes)
                ApplyGCode(gCode.Number, command);

            foreach (var mCode in command.MCodes)
                ApplyMCode(mCode.Number, command);

            if (parameters.ContainsKey(GCodeRegistry.PARAM_F))
            {
                State.SetFeedRate(parameters[GCodeRegistry.PARAM_F]);
                command.FeedRate = parameters[GCodeRegistry.PARAM_F];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_S))
            {
                State.SetSpindleSpeed(parameters[GCodeRegistry.PARAM_S]);
                command.SpindleSpeed = parameters[GCodeRegistry.PARAM_S];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_T))
            {
                command.ToolNumber = (int)parameters[GCodeRegistry.PARAM_T];
                State.ToolNumber = command.ToolNumber;
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_H))
            {
                State.ToolLengthOffset = (int)parameters[GCodeRegistry.PARAM_H];
                command.ToolLengthOffset = (int)parameters[GCodeRegistry.PARAM_H];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_D))
            {
                State.ToolRadiusOffset = (int)parameters[GCodeRegistry.PARAM_D];
                command.ToolRadiusOffset = (int)parameters[GCodeRegistry.PARAM_D];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_I)) command.I = parameters[GCodeRegistry.PARAM_I];
            if (parameters.ContainsKey(GCodeRegistry.PARAM_J)) command.J = parameters[GCodeRegistry.PARAM_J];
            if (parameters.ContainsKey(GCodeRegistry.PARAM_K)) command.K = parameters[GCodeRegistry.PARAM_K];
            if (parameters.ContainsKey(GCodeRegistry.PARAM_R)) command.R = parameters[GCodeRegistry.PARAM_R];

            if (parameters.ContainsKey(GCodeRegistry.PARAM_X))
            {
                newX = State.IsAbsolute ? parameters[GCodeRegistry.PARAM_X] : State.X + parameters[GCodeRegistry.PARAM_X];
                command.X = parameters[GCodeRegistry.PARAM_X];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_Y))
            {
                newY = State.IsAbsolute ? parameters[GCodeRegistry.PARAM_Y] : State.Y + parameters[GCodeRegistry.PARAM_Y];
                command.Y = parameters[GCodeRegistry.PARAM_Y];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_Z))
            {
                newZ = State.IsAbsolute ? parameters[GCodeRegistry.PARAM_Z] : State.Z + parameters[GCodeRegistry.PARAM_Z];
                command.Z = parameters[GCodeRegistry.PARAM_Z];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_A))
            {
                newA = State.IsAbsolute ? parameters[GCodeRegistry.PARAM_A] : State.A + parameters[GCodeRegistry.PARAM_A];
                command.A = parameters[GCodeRegistry.PARAM_A];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_B))
            {
                newB = State.IsAbsolute ? parameters[GCodeRegistry.PARAM_B] : State.B + parameters[GCodeRegistry.PARAM_B];
                command.B = parameters[GCodeRegistry.PARAM_B];
            }

            if (parameters.ContainsKey(GCodeRegistry.PARAM_C))
            {
                newC = State.IsAbsolute ? parameters[GCodeRegistry.PARAM_C] : State.C + parameters[GCodeRegistry.PARAM_C];
                command.C = parameters[GCodeRegistry.PARAM_C];
            }

            command.Arc = null;
            if (GCodeRegistry.IsArcCode(State.CurrentMotionMode))
            {
                bool hasArcData = command.I.HasValue || command.J.HasValue || command.K.HasValue || command.R.HasValue;
                if (hasArcData &&
                    ArcCalculator.TryComputeArc(
                        State.X, State.Y, State.Z,
                        newX, newY, newZ,
                        State.CurrentPlane,
                        State.CurrentMotionMode.Number == 2,
                        command.I, command.J, command.K, command.R,
                        out ArcGeometry? arc))
                {
                    command.Arc = arc;
                }
            }

            if (command.GCodes.Any(g => g.Number == 28) || command.MCodes.Any(m => m.Number == 6))
            {
                // Смена инструмента или G28 принудительно устанавливают координаты в точку смены
                newX = MachineState.HOME_X;
                newY = MachineState.HOME_Y;
                newZ = MachineState.HOME_Z;
                State.SetPosition(newX, newY, newZ, newA, newB, newC);
                return;
            }

            State.SetPosition(newX, newY, newZ, newA, newB, newC);

            string log = $"L{command.LineNumber}: {command.RawLine} -> X{newX:F3} Y{newY:F3} Z{newZ:F3} (ABS: {State.IsAbsolute})";
            System.Console.WriteLine(log);
        }

        /// <summary>Обрабатывает G-коды и обновляет модальные группы состояния станка.</summary>
        private void ApplyGCode(int number, ParsedCommand command)
        {
            var gCode = GCodeRegistry.GetGCode(number);
            if (!gCode.HasValue) return;

            if (!command.GCodes.Any(g => g.Number == number))
                command.GCodes.Add(gCode.Value);

            if (gCode == GCodeRegistry.G90) State.SetCoordinateMode(true);
            else if (gCode == GCodeRegistry.G91) State.SetCoordinateMode(false);
            else if (gCode == GCodeRegistry.G20) State.SetUnits(false);
            else if (gCode == GCodeRegistry.G21) State.SetUnits(true);
            else if (number is >= 54 and <= 59) State.SetCoordinateSystem(gCode.Value);
            else if (number is >= 17 and <= 19) State.SetPlane(gCode.Value);
            else if (GCodeRegistry.IsMovementCode(gCode)) State.SetMotionMode(gCode.Value);
            else if (number is >= 40 and <= 42) State.SetCutterCompensation(gCode.Value);
            else if (gCode == GCodeRegistry.G43 || gCode == GCodeRegistry.G44 || gCode == GCodeRegistry.G49)
                State.SetToolLengthCompensation(gCode.Value);
        }

        /// <summary>Обрабатывает M-коды (шпиндель, СОЖ и др.).</summary>
        private void ApplyMCode(int number, ParsedCommand command)
        {
            var mCode = GCodeRegistry.GetMCode(number);
            if (!mCode.HasValue) return;

            if (!command.MCodes.Any(m => m.Number == number))
                command.MCodes.Add(mCode.Value);

            if (mCode == GCodeRegistry.M3) State.SetSpindle(true, true);
            else if (mCode == GCodeRegistry.M4) State.SetSpindle(true, false);
            else if (mCode == GCodeRegistry.M5) State.SetSpindle(false);
            else if (mCode == GCodeRegistry.M7 || mCode == GCodeRegistry.M8) State.SetCoolant(true);
            else if (mCode == GCodeRegistry.M9) State.SetCoolant(false);
        }
    }
}
