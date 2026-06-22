using CNCSS.Data;
using CNCSS.Machine.Model;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Builds canonical motion blocks from parsed G-code commands for execution services.</summary>
    public static class MotionBlockFactory
    {
        public static MotionBlock FromParsedCommand(ParsedCommand command)
        {
            var kind = ClassifyBlockKind(command);

            int? endMCode = null;
            if (command.MCodes.Any(m => m.Number == 30)) endMCode = 30;
            else if (command.MCodes.Any(m => m.Number == 2)) endMCode = 2;

            return new MotionBlock
            {
                LineNumber = command.LineNumber,
                Kind = kind,
                RawLine = command.RawLine,
                HasTarget = command.HasCoordinates || command.GCodes.Any(g => g.Number == 28) || command.MCodes.Any(m => m.Number == 6),
                TargetX = command.EndState.X,
                TargetY = command.EndState.Y,
                TargetZ = command.EndState.Z,
                IsProgramStop = command.MCodes.Any(m => m.Number == 0),
                IsOptionalStop = command.MCodes.Any(m => m.Number == 1),
                IsProgramEnd = endMCode.HasValue,
                EndProgramMCode = endMCode
            };
        }

        private static MotionBlockKind ClassifyBlockKind(ParsedCommand command)
        {
            if (command.GCodes.Any(g => g.Number == 0)) return MotionBlockKind.Rapid;
            if (command.GCodes.Any(g => g.Number == 1)) return MotionBlockKind.Linear;
            if (command.GCodes.Any(g => g.Number is 2 or 3)) return MotionBlockKind.Arc;
            if (command.GCodes.Any(g => g.Number == 4)) return MotionBlockKind.Dwell;
            if (command.MCodes.Any(m => m.Number == 6)) return MotionBlockKind.ToolChange;

            // Модальный G0/G1/G2/G3: на строке только X/Y/Z без повторного G-слова.
            if (command.HasCoordinates)
            {
                return command.EndState.CurrentMotionMode.Number switch
                {
                    0 => MotionBlockKind.Rapid,
                    1 => MotionBlockKind.Linear,
                    2 or 3 => MotionBlockKind.Arc,
                    4 => MotionBlockKind.Dwell,
                    _ => MotionBlockKind.Auxiliary
                };
            }

            return MotionBlockKind.Auxiliary;
        }
    }
}
