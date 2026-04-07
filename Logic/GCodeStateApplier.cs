using CNCSS.Data;

namespace CNCSS.Logic
{
    /// <summary>
    /// Отвечает за применение разобранных параметров и кодов к состоянию станка.
    /// Использует данные из ParsedCommand для обновления MachineState.
    /// </summary>
    public class GCodeStateApplier
    {
        private readonly MachineState _state;

        public GCodeStateApplier(MachineState state)
        {
            _state = state;
        }

        public void Apply(ParsedCommand command)
        {
            _state.SavePreviousPosition();

            double newX = _state.X;
            double newY = _state.Y;
            double newZ = _state.Z;
            double newA = _state.A;
            double newB = _state.B;
            double newC = _state.C;

            // Применяем G-коды
            foreach (var gCode in command.GCodes)
                ApplyGCode(gCode.Number, command);

            // Применяем M-коды
            foreach (var mCode in command.MCodes)
                ApplyMCode(mCode.Number, command);

            // Применяем параметры подачи и шпинделя
            ApplyFeedAndSpindle(command);

            // Применяем параметры инструмента
            ApplyToolParameters(command);

            // Применяем параметры дуги
            ApplyArcParameters(command);

            // Вычисляем новые координаты
            (newX, newY, newZ, newA, newB, newC) = CalculateNewPosition(command, newX, newY, newZ, newA, newB, newC);

            // Вычисляем дугу если нужно
            TryComputeArc(command, newX, newY, newZ);

            // Обрабатываем особые команды (G28, M6)
            if (IsSpecialMoveCommand(command))
            {
                newX = MachineState.HOME_X;
                newY = MachineState.HOME_Y;
                newZ = MachineState.HOME_Z;
                _state.SetPosition(newX, newY, newZ, newA, newB, newC);
                return;
            }

            // Устанавливаем новую позицию
            _state.SetPosition(newX, newY, newZ, newA, newB, newC);

            // Логирование
            LogCommand(command, newX, newY, newZ);
        }

        private void ApplyFeedAndSpindle(ParsedCommand command)
        {
            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_F))
            {
                _state.SetFeedRate(command.Parameters[GCodeRegistry.PARAM_F]);
                command.FeedRate = command.Parameters[GCodeRegistry.PARAM_F];
            }

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_S))
            {
                _state.SetSpindleSpeed(command.Parameters[GCodeRegistry.PARAM_S]);
                command.SpindleSpeed = command.Parameters[GCodeRegistry.PARAM_S];
            }
        }

        private void ApplyToolParameters(ParsedCommand command)
        {
            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_T))
            {
                command.ToolNumber = (int)command.Parameters[GCodeRegistry.PARAM_T];
                _state.ToolNumber = command.ToolNumber;
            }

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_H))
            {
                _state.ToolLengthOffset = (int)command.Parameters[GCodeRegistry.PARAM_H];
                command.ToolLengthOffset = (int)command.Parameters[GCodeRegistry.PARAM_H];
            }

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_D))
            {
                _state.ToolRadiusOffset = (int)command.Parameters[GCodeRegistry.PARAM_D];
                command.ToolRadiusOffset = (int)command.Parameters[GCodeRegistry.PARAM_D];
            }
        }

        private void ApplyArcParameters(ParsedCommand command)
        {
            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_I)) command.I = command.Parameters[GCodeRegistry.PARAM_I];
            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_J)) command.J = command.Parameters[GCodeRegistry.PARAM_J];
            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_K)) command.K = command.Parameters[GCodeRegistry.PARAM_K];
            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_R)) command.R = command.Parameters[GCodeRegistry.PARAM_R];
        }

        private (double x, double y, double z, double a, double b, double c) CalculateNewPosition(
            ParsedCommand command, 
            double currentX, double currentY, double currentZ,
            double currentA, double currentB, double currentC)
        {
            double newX = currentX;
            double newY = currentY;
            double newZ = currentZ;
            double newA = currentA;
            double newB = currentB;
            double newC = currentC;

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_X))
            {
                newX = _state.IsAbsolute 
                    ? command.Parameters[GCodeRegistry.PARAM_X] 
                    : currentX + command.Parameters[GCodeRegistry.PARAM_X];
                command.X = command.Parameters[GCodeRegistry.PARAM_X];
            }

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_Y))
            {
                newY = _state.IsAbsolute 
                    ? command.Parameters[GCodeRegistry.PARAM_Y] 
                    : currentY + command.Parameters[GCodeRegistry.PARAM_Y];
                command.Y = command.Parameters[GCodeRegistry.PARAM_Y];
            }

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_Z))
            {
                newZ = _state.IsAbsolute 
                    ? command.Parameters[GCodeRegistry.PARAM_Z] 
                    : currentZ + command.Parameters[GCodeRegistry.PARAM_Z];
                command.Z = command.Parameters[GCodeRegistry.PARAM_Z];
            }

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_A))
            {
                newA = _state.IsAbsolute 
                    ? command.Parameters[GCodeRegistry.PARAM_A] 
                    : currentA + command.Parameters[GCodeRegistry.PARAM_A];
                command.A = command.Parameters[GCodeRegistry.PARAM_A];
            }

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_B))
            {
                newB = _state.IsAbsolute 
                    ? command.Parameters[GCodeRegistry.PARAM_B] 
                    : currentB + command.Parameters[GCodeRegistry.PARAM_B];
                command.B = command.Parameters[GCodeRegistry.PARAM_B];
            }

            if (command.Parameters.ContainsKey(GCodeRegistry.PARAM_C))
            {
                newC = _state.IsAbsolute 
                    ? command.Parameters[GCodeRegistry.PARAM_C] 
                    : currentC + command.Parameters[GCodeRegistry.PARAM_C];
                command.C = command.Parameters[GCodeRegistry.PARAM_C];
            }

            return (newX, newY, newZ, newA, newB, newC);
        }

        private void TryComputeArc(ParsedCommand command, double newX, double newY, double newZ)
        {
            command.Arc = null;
            if (GCodeRegistry.IsArcCode(_state.CurrentMotionMode))
            {
                bool hasArcData = command.I.HasValue || command.J.HasValue || command.K.HasValue || command.R.HasValue;
                if (hasArcData &&
                    ArcCalculator.TryComputeArc(
                        _state.X, _state.Y, _state.Z,
                        newX, newY, newZ,
                        _state.CurrentPlane,
                        _state.CurrentMotionMode.Number == 2,
                        command.I, command.J, command.K, command.R,
                        out ArcGeometry? arc))
                {
                    command.Arc = arc;
                }
            }
        }

        private bool IsSpecialMoveCommand(ParsedCommand command)
        {
            return command.GCodes.Any(g => g.Number == 28) || command.MCodes.Any(m => m.Number == 6);
        }

        private void ApplyGCode(int number, ParsedCommand command)
        {
            var gCode = GCodeRegistry.GetGCode(number);
            if (!gCode.HasValue) return;

            if (!command.GCodes.Any(g => g.Number == number))
                command.GCodes.Add(gCode.Value);

            if (gCode == GCodeRegistry.G90) _state.SetCoordinateMode(true);
            else if (gCode == GCodeRegistry.G91) _state.SetCoordinateMode(false);
            else if (gCode == GCodeRegistry.G20) _state.SetUnits(false);
            else if (gCode == GCodeRegistry.G21) _state.SetUnits(true);
            else if (number is >= 54 and <= 59) _state.SetCoordinateSystem(gCode.Value);
            else if (number is >= 17 and <= 19) _state.SetPlane(gCode.Value);
            else if (GCodeRegistry.IsMovementCode(gCode)) _state.SetMotionMode(gCode.Value);
            else if (number is >= 40 and <= 42) _state.SetCutterCompensation(gCode.Value);
            else if (gCode == GCodeRegistry.G43 || gCode == GCodeRegistry.G44 || gCode == GCodeRegistry.G49)
                _state.SetToolLengthCompensation(gCode.Value);
        }

        private void ApplyMCode(int number, ParsedCommand command)
        {
            var mCode = GCodeRegistry.GetMCode(number);
            if (!mCode.HasValue) return;

            if (!command.MCodes.Any(m => m.Number == number))
                command.MCodes.Add(mCode.Value);

            if (mCode == GCodeRegistry.M3) _state.SetSpindle(true, true);
            else if (mCode == GCodeRegistry.M4) _state.SetSpindle(true, false);
            else if (mCode == GCodeRegistry.M5) _state.SetSpindle(false);
            else if (mCode == GCodeRegistry.M7 || mCode == GCodeRegistry.M8) _state.SetCoolant(true);
            else if (mCode == GCodeRegistry.M9) _state.SetCoolant(false);
        }

        private void LogCommand(ParsedCommand command, double newX, double newY, double newZ)
        {
            string log = $"L{command.LineNumber}: {command.RawLine} -> X{newX:F3} Y{newY:F3} Z{newZ:F3} (ABS: {_state.IsAbsolute})";
            System.Console.WriteLine(log);
        }
    }
}
