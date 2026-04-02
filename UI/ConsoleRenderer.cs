using CNCSS.Data;
using CNCSS.Logic;

namespace CNCSS.UI
{
    public static class ConsoleRenderer
    {
        public static void RenderHeader(string title) => Console.WriteLine($"=== {title} ===\n");

        public static void RenderInfo(string message) => Console.WriteLine(message);

        public static void RenderSeparator() => Console.WriteLine(new string('-', 80));

        private static ConsoleColor GetMovementColor(GCodeTemplate? gCode)
        {
            if (!gCode.HasValue)
                return ConsoleColor.White;

            return gCode.Value.Number switch
            {
                0 => ConsoleColor.Red,
                1 => ConsoleColor.Green,
                2 => ConsoleColor.Blue,
                3 => ConsoleColor.Blue,
                _ => ConsoleColor.White
            };
        }

        public static void PrintLine(int lineNumber, string? comment, ParsedCommand command, MachineState prevState, MachineState currentState)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"N{lineNumber:D3}: ");
            Console.ResetColor();

            GCodeTemplate? movementCode = null;
            if (command.GCodes.Any(g => g.Number is >= 0 and <= 3))
                movementCode = command.GCodes.First(g => g.Number is >= 0 and <= 3);
            else if (command.HasCoordinates)
                movementCode = GCodeRegistry.G1;

            ConsoleColor coordinateColor = GetMovementColor(movementCode);

            foreach (var gCode in command.GCodes)
            {
                if (gCode == GCodeRegistry.G0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"{gCode} ");
                    Console.ResetColor();
                }
                else if (gCode == GCodeRegistry.G1)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"{gCode} ");
                    Console.ResetColor();
                }
                else if (gCode == GCodeRegistry.G2 || gCode == GCodeRegistry.G3)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.Write($"{gCode} ");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"{gCode} ");
                    Console.ResetColor();
                }
            }

            foreach (var mCode in command.MCodes)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write($"{mCode} ");
                Console.ResetColor();
            }

            Console.ForegroundColor = coordinateColor;
            if (command.X.HasValue) Console.Write($"{GCodeRegistry.PARAM_X}{command.X.Value:F3} ");
            if (command.Y.HasValue) Console.Write($"{GCodeRegistry.PARAM_Y}{command.Y.Value:F3} ");
            if (command.Z.HasValue) Console.Write($"{GCodeRegistry.PARAM_Z}{command.Z.Value:F3} ");
            if (command.A.HasValue) Console.Write($"{GCodeRegistry.PARAM_A}{command.A.Value:F3} ");
            if (command.B.HasValue) Console.Write($"{GCodeRegistry.PARAM_B}{command.B.Value:F3} ");
            if (command.C.HasValue) Console.Write($"{GCodeRegistry.PARAM_C}{command.C.Value:F3} ");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            if (command.I.HasValue) Console.Write($"{GCodeRegistry.PARAM_I}{command.I.Value:F4} ");
            if (command.J.HasValue) Console.Write($"{GCodeRegistry.PARAM_J}{command.J.Value:F4} ");
            if (command.K.HasValue) Console.Write($"{GCodeRegistry.PARAM_K}{command.K.Value:F4} ");
            if (command.R.HasValue) Console.Write($"{GCodeRegistry.PARAM_R}{command.R.Value:F4} ");
            Console.ResetColor();

            if (command.FeedRate.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{GCodeRegistry.PARAM_F}{command.FeedRate.Value:F1} ");
                Console.ResetColor();
            }

            if (command.SpindleSpeed.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{GCodeRegistry.PARAM_S}{command.SpindleSpeed.Value:F0} ");
                Console.ResetColor();
            }

            if (!string.IsNullOrEmpty(comment))
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.Write($"; {comment}");
                Console.ResetColor();
            }

            Console.WriteLine();

            PrintChangedState(prevState, currentState, command, coordinateColor);
        }

        public static void PrintChangedState(MachineState prevState, MachineState currentState, ParsedCommand command, ConsoleColor movementColor)
        {
            var changes = new List<string>();

            foreach (var gCode in command.GCodes)
            {
                if (gCode == GCodeRegistry.G90) changes.Add($"{GCodeRegistry.G90} (Абсолютный)");
                else if (gCode == GCodeRegistry.G91) changes.Add($"{GCodeRegistry.G91} (Относительный)");
                else if (gCode == GCodeRegistry.G20) changes.Add($"{GCodeRegistry.G20} (дюймы)");
                else if (gCode == GCodeRegistry.G21) changes.Add($"{GCodeRegistry.G21} (мм)");
                else if (gCode.Number is >= 54 and <= 59) changes.Add($"{gCode} (Система координат)");
                else if (gCode.Number is >= 17 and <= 19) changes.Add($"{gCode} (Плоскость)");
                else if (gCode.Number is >= 40 and <= 42) changes.Add($"{gCode} (Коррекция радиуса)");
                else if (gCode == GCodeRegistry.G43 || gCode == GCodeRegistry.G44 || gCode == GCodeRegistry.G49)
                    changes.Add($"{gCode} (Коррекция длины)");
                else if (gCode == GCodeRegistry.G0) changes.Add($"{gCode} (Ускоренное)");
                else if (gCode == GCodeRegistry.G1) changes.Add($"{gCode} (Рабочая подача)");
                else if (gCode == GCodeRegistry.G2) changes.Add($"{gCode} (Дуга по часовой)");
                else if (gCode == GCodeRegistry.G3) changes.Add($"{gCode} (Дуга против часовой)");
            }

            foreach (var mCode in command.MCodes)
            {
                if (mCode == GCodeRegistry.M3) changes.Add($"{GCodeRegistry.M3} (Шпиндель ВКЛ по часовой)");
                else if (mCode == GCodeRegistry.M4) changes.Add($"{GCodeRegistry.M4} (Шпиндель ВКЛ против)");
                else if (mCode == GCodeRegistry.M5) changes.Add($"{GCodeRegistry.M5} (Шпиндель ВЫКЛ)");
                else if (mCode == GCodeRegistry.M6) changes.Add($"{GCodeRegistry.M6} (Смена инструмента)");
                else if (mCode == GCodeRegistry.M7) changes.Add($"{GCodeRegistry.M7} (Охлаждение туман ВКЛ)");
                else if (mCode == GCodeRegistry.M8) changes.Add($"{GCodeRegistry.M8} (Охлаждение СОЖ ВКЛ)");
                else if (mCode == GCodeRegistry.M9) changes.Add($"{GCodeRegistry.M9} (Охлаждение ВЫКЛ)");
                else if (mCode == GCodeRegistry.M0) changes.Add($"{GCodeRegistry.M0} (Остановка программы)");
                else if (mCode == GCodeRegistry.M1) changes.Add($"{GCodeRegistry.M1} (Условная остановка)");
                else if (mCode == GCodeRegistry.M2) changes.Add($"{GCodeRegistry.M2} (Конец программы)");
                else if (mCode == GCodeRegistry.M30) changes.Add($"{GCodeRegistry.M30} (Конец программы с возвратом)");
            }

            if (command.FeedRate.HasValue && Math.Abs(command.FeedRate.Value - prevState.FeedRate) > 0.01)
                changes.Add($"{GCodeRegistry.PARAM_F}{command.FeedRate.Value:F1}");

            if (command.SpindleSpeed.HasValue && Math.Abs(command.SpindleSpeed.Value - prevState.SpindleSpeed) > 1)
                changes.Add($"{GCodeRegistry.PARAM_S}{command.SpindleSpeed.Value:F0}");

            if (command.ToolNumber.HasValue && command.ToolNumber != prevState.ToolNumber)
                changes.Add($"{GCodeRegistry.PARAM_T}{command.ToolNumber}");

            if (command.ToolLengthOffset.HasValue && command.ToolLengthOffset != prevState.ToolLengthOffset)
                changes.Add($"{GCodeRegistry.PARAM_H}{command.ToolLengthOffset}");

            if (command.ToolRadiusOffset.HasValue && command.ToolRadiusOffset != prevState.ToolRadiusOffset)
                changes.Add($"{GCodeRegistry.PARAM_D}{command.ToolRadiusOffset}");

            if (currentState.HasPositionChanged())
                changes.Add($"{GCodeRegistry.PARAM_X}{currentState.X:F3} {GCodeRegistry.PARAM_Y}{currentState.Y:F3} {GCodeRegistry.PARAM_Z}{currentState.Z:F3}");

            if (command.Arc != null)
            {
                var a = command.Arc;
                string plane = a.Plane switch
                {
                    17 => "XY",
                    18 => "XZ",
                    19 => "YZ",
                    _ => $"G{a.Plane}"
                };
                changes.Add(
                    $"Дуга {plane} R={a.Radius:F4} Δ={a.SweepAngleRad * 180 / Math.PI:F1}° C=({a.CenterU:F3},{a.CenterV:F3})");
            }

            if (changes.Count > 0)
            {
                Console.ForegroundColor = changes.Any(c => c.Contains("G0")) ? ConsoleColor.Red :
                    changes.Any(c => c.Contains("G1")) ? ConsoleColor.Green :
                    changes.Any(c => c.Contains("G2") || c.Contains("G3")) ? ConsoleColor.Blue :
                    ConsoleColor.Cyan;
                Console.WriteLine($" → {string.Join(" | ", changes)}");
                Console.ResetColor();
            }
        }

        public static void RenderFinalState(MachineState state)
        {
            Console.WriteLine("=== Финальное состояние ===");
            Console.WriteLine($"Позиция: {GCodeRegistry.PARAM_X}={state.X:F3}, {GCodeRegistry.PARAM_Y}={state.Y:F3}, {GCodeRegistry.PARAM_Z}={state.Z:F3}");
            Console.WriteLine($"Режим: {(state.IsAbsolute ? GCodeRegistry.G90 : GCodeRegistry.G91)}");
            Console.WriteLine($"Единицы: {(state.IsMetric ? GCodeRegistry.G21 : GCodeRegistry.G20)}");
            Console.WriteLine($"Подача: {GCodeRegistry.PARAM_F}{state.FeedRate:F1}");
            Console.WriteLine($"Шпиндель: {(state.IsSpindleOn ? "Вкл" : "Выкл")} {GCodeRegistry.PARAM_S}{state.SpindleSpeed:F0}");
            Console.WriteLine($"СОЖ: {(state.IsCoolantOn ? "Вкл" : "Выкл")}");
            Console.WriteLine($"Инструмент: {GCodeRegistry.PARAM_T}{state.ToolNumber?.ToString() ?? "-"}");
            Console.WriteLine();
        }

        public static void RenderStatistics(List<ParsedCommand> commands)
        {
            Console.WriteLine("=== Статистика ===");
            Console.WriteLine($"Всего команд: {commands.Count}");
            Console.WriteLine($"Перемещений: {commands.Count(c => c.IsMovement)}");
            Console.WriteLine($"Комментариев: {commands.Count(c => !string.IsNullOrEmpty(c.Comment) && !c.GCodes.Any() && !c.MCodes.Any())}");
            Console.WriteLine($"M-команд: {commands.Sum(c => c.MCodes.Count)}");

            Console.WriteLine($"Ускоренных (G0): {commands.Count(c => c.GCodes.Any(g => g == GCodeRegistry.G0))}");
            Console.WriteLine($"Рабочих подач (G1): {commands.Count(c => c.GCodes.Any(g => g == GCodeRegistry.G1))}");
            Console.WriteLine($"Дуг (G2/G3): {commands.Count(c => c.GCodes.Any(g => g == GCodeRegistry.G2 || g == GCodeRegistry.G3))}");
            Console.WriteLine($"Дуг с расчётом геометрии: {commands.Count(c => c.Arc != null)}");
        }

        public static void RenderExitPrompt() => Console.WriteLine("\nНажмите любую клавишу для выхода...");

        public static void RenderFileCreated(string filePath) => Console.WriteLine($"Создан тестовый файл: {filePath}\n");
    }
}
