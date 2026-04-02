using CNCSS.Data;

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
                parser.State.UpdatePosition(cmd.X, cmd.Y, cmd.Z, cmd.A, cmd.B, cmd.C);
            }
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
            else if (number == 28)
                state.SetPosition(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z, state.A, state.B, state.C);
        }

        public static void ApplyMCodeToState(MachineState state, int number)
        {
            if (number == 3) state.SetSpindle(true, true);
            else if (number == 4) state.SetSpindle(true, false);
            else if (number == 5) state.SetSpindle(false);
            else if (number == 6) state.SetPosition(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z, state.A, state.B, state.C);
            else if (number == 7 || number == 8) state.SetCoolant(true);
            else if (number == 9) state.SetCoolant(false);
        }
    }
}
