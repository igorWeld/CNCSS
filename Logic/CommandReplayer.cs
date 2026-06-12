using CNCSS.Data;
using CNCSS.Machine.Model;

namespace CNCSS.Logic
{
    /// <summary>Повтор команд для получения состояния до/после строки (как при разборе файла).</summary>
    public static class CommandReplayer
    {
        public static MachineState StateBeforeCommand(GCodeParser parsed, int commandIndex)
        {
            var p = new GCodeParser();
            for (int i = 0; i < commandIndex && i < parsed.Commands.Count; i++)
                ReplayCommand(p, parsed.Commands[i]);
            return p.State;
        }

        public static MachineState StateAfterCommand(GCodeParser parsed, int commandIndex)
        {
            var p = new GCodeParser();
            for (int i = 0; i <= commandIndex && i < parsed.Commands.Count; i++)
                ReplayCommand(p, parsed.Commands[i]);
            return p.State;
        }

        public static void ReplayCommand(GCodeParser parser, ParsedCommand cmd)
        {
            if (TryApplyWorkOffsetCommand(parser.State, cmd))
            {
                return;
            }

            foreach (var gCode in cmd.GCodes)
                ApplyGCodeToState(parser.State, gCode.Number);

            foreach (var mCode in cmd.MCodes)
                ApplyMCodeToState(parser.State, mCode.Number);

            if (cmd.FeedRate.HasValue)
                parser.State.SetFeedRate(cmd.FeedRate.Value);

            if (cmd.SpindleSpeed.HasValue)
                parser.State.SetSpindleSpeed(cmd.SpindleSpeed.Value);

            if (cmd.ToolNumber.HasValue)
                parser.State.ToolNumber = cmd.ToolNumber;

            if (cmd.ToolLengthOffset.HasValue)
                parser.State.ToolLengthOffset = cmd.ToolLengthOffset;

            if (cmd.ToolRadiusOffset.HasValue)
                parser.State.ToolRadiusOffset = cmd.ToolRadiusOffset;

            if (cmd.X.HasValue || cmd.Y.HasValue || cmd.Z.HasValue ||
                cmd.A.HasValue || cmd.B.HasValue || cmd.C.HasValue)
            {
                // Mirror GCodeParser coordinate resolution:
                // - In absolute mode, add active WCS offset (unless G53 on this block) and MCS zero offset.
                // - In incremental mode, apply deltas directly.
                bool useMachineCoordinatesThisBlock = cmd.GCodes.Any(g => g.Number == 53);
                var activeWcs = parser.State.GetActiveWorkOffset();
                double wX = useMachineCoordinatesThisBlock ? 0 : activeWcs.X;
                double wY = useMachineCoordinatesThisBlock ? 0 : activeWcs.Y;
                double wZ = useMachineCoordinatesThisBlock ? 0 : activeWcs.Z;
                double mX = parser.State.IsAbsolute ? parser.State.MachineZeroOffsetX : 0;
                double mY = parser.State.IsAbsolute ? parser.State.MachineZeroOffsetY : 0;
                double mZ = parser.State.IsAbsolute ? parser.State.MachineZeroOffsetZ : 0;

                parser.State.SavePreviousPosition();
                if (cmd.X.HasValue) parser.State.X = parser.State.IsAbsolute ? cmd.X.Value + (wX + mX) : parser.State.X + cmd.X.Value;
                if (cmd.Y.HasValue) parser.State.Y = parser.State.IsAbsolute ? cmd.Y.Value + (wY + mY) : parser.State.Y + cmd.Y.Value;
                if (cmd.Z.HasValue)
                {
                    double newZ = useMachineCoordinatesThisBlock
                        ? (parser.State.IsAbsolute
                            ? cmd.Z.Value + (wZ + mZ)
                            : parser.State.Z + cmd.Z.Value)
                        : parser.State.ResolveMachineZFromProgramValue(cmd.Z.Value, parser.State.Z);
                    if (useMachineCoordinatesThisBlock)
                    {
                        parser.State.ApplyToolLengthCompensationToMachineZ(ref newZ);
                    }

                    parser.State.Z = newZ;
                    if (parser.State.ToolLengthCompensation.Number is 43 or 44)
                    {
                        parser.State.IsMachineZSyncedWithLengthComp = true;
                    }
                }
                else if (cmd.GCodes.Any(g => g.Number is 43 or 44))
                {
                    parser.State.IsMachineZSyncedWithLengthComp = false;
                }
                if (cmd.A.HasValue) parser.State.A = parser.State.IsAbsolute ? cmd.A.Value : parser.State.A + cmd.A.Value;
                if (cmd.B.HasValue) parser.State.B = parser.State.IsAbsolute ? cmd.B.Value : parser.State.B + cmd.B.Value;
                if (cmd.C.HasValue) parser.State.C = parser.State.IsAbsolute ? cmd.C.Value : parser.State.C + cmd.C.Value;
            }

            if (cmd.GCodes.Any(g => g.Number == 28) || cmd.MCodes.Any(m => m.Number == 6))
            {
                var (homeX, homeY, homeZ) = parser.State.GetG28PhysicalPosition();
                parser.State.SetPosition(homeX, homeY, homeZ, parser.State.A, parser.State.B, parser.State.C);
            }
        }

        private static bool TryApplyWorkOffsetCommand(MachineState state, ParsedCommand cmd)
        {
            if (!cmd.GCodes.Any(g => g.Number == 10))
            {
                return false;
            }

            if (!cmd.Parameters.TryGetValue("L", out var lValue) || Math.Abs(lValue - 2.0) > 0.0001)
            {
                return false;
            }

            if (!cmd.Parameters.TryGetValue(GCodeRegistry.PARAM_P, out var pValue))
            {
                return false;
            }

            int pNumber = (int)pValue;
            if (pNumber < 1 || pNumber > 6)
            {
                return false;
            }

            int systemNumber = 53 + pNumber;
            double? x = cmd.Parameters.TryGetValue(GCodeRegistry.PARAM_X, out var px) ? px : null;
            double? y = cmd.Parameters.TryGetValue(GCodeRegistry.PARAM_Y, out var py) ? py : null;
            double? z = cmd.Parameters.TryGetValue(GCodeRegistry.PARAM_Z, out var pz) ? pz : null;
            state.SetWorkOffset(systemNumber, x, y, z);
            return true;
        }

        public static void ApplyGCodeToState(MachineState state, int number)
        {
            if (number == 90) state.SetCoordinateMode(true);
            else if (number == 91) state.SetCoordinateMode(false);
            else if (number == 20) state.SetUnits(false);
            else if (number == 21) state.SetUnits(true);
            else if (number is >= 54 and <= 59)
                state.SetCoordinateSystem(GCodeRegistry.GetGCode(number) ?? state.CurrentCoordinateSystem);
            else if (number is >= 17 and <= 19)
                state.SetPlane(GCodeRegistry.GetGCode(number) ?? state.CurrentPlane);
            else if (number is >= 40 and <= 42)
                state.SetCutterCompensation(GCodeRegistry.GetGCode(number) ?? state.CutterCompensation);
            else if (number is 43 or 44 or 49)
                state.SetToolLengthCompensation(GCodeRegistry.GetGCode(number) ?? state.ToolLengthCompensation);
            else if (number is >= 0 and <= 3)
                state.SetMotionMode(GCodeRegistry.GetGCode(number) ?? state.CurrentMotionMode);
        }

        public static void ApplyMCodeToState(MachineState state, int number)
        {
            if (number == 3) state.SetSpindle(true, true);
            else if (number == 4) state.SetSpindle(true, false);
            else if (number == 5) state.SetSpindle(false);
            else if (number == 7 || number == 8) state.SetCoolant(true);
            else if (number == 9) state.SetCoolant(false);
        }
    }
}
