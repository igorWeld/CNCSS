using CNCSS.Logic;

namespace CNCSS.Simulation.Execution
{
    /// <summary>
    /// Возвращает <see cref="GCodeParser"/>, состояние которого (<see cref="GCodeParser.State"/>) соответствует выполнению УП до заданной строки.
    /// </summary>
    public sealed class ProgramStateService
    {
        public GCodeParser BuildStateAtLine(GCodeParser sourceParser, int selectedLine)
        {
            var stateParser = new GCodeParser();
            foreach (var cmd in sourceParser.Commands)
            {
                if (cmd.LineNumber <= selectedLine)
                {
                    CommandReplayer.ReplayCommand(stateParser, cmd);
                }
                else
                {
                    break;
                }
            }

            return stateParser;
        }
    }
}
