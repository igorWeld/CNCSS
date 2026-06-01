using CNCSS.Data;
using CNCSS.Machine.Model;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Builds canonical motion blocks from parsed G-code commands for execution services.</summary>
    public static class MotionBlockFactory
    {
        public static MotionBlock FromParsedCommand(ParsedCommand command)
        {
            var kind = MotionBlockKind.Auxiliary;
            if (command.GCodes.Any(g => g.Number == 0)) kind = MotionBlockKind.Rapid;
            else if (command.GCodes.Any(g => g.Number == 1)) kind = MotionBlockKind.Linear;
            else if (command.GCodes.Any(g => g.Number is 2 or 3)) kind = MotionBlockKind.Arc;
            else if (command.GCodes.Any(g => g.Number == 4)) kind = MotionBlockKind.Dwell;
            else if (command.MCodes.Any(m => m.Number == 6)) kind = MotionBlockKind.ToolChange;

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
    }
}
