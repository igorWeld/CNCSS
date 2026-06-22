using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Logic.ProgramLoading;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Текущее NC-пространство приложения: загруженная программа, parser и производные индексы.</summary>
    public sealed class ProgramWorkspace
    {
        private readonly ProgramLoader _programLoader;
        private readonly ProgramStateService _programStateService;
        private ProgramLoadResult? _current;

        public ProgramWorkspace(ProgramLoader programLoader, ProgramStateService programStateService)
        {
            _programLoader = programLoader ?? throw new ArgumentNullException(nameof(programLoader));
            _programStateService = programStateService ?? throw new ArgumentNullException(nameof(programStateService));
        }

        public bool HasProgram => _current != null;
        public GCodeParser? Parser => _current?.Parser;
        public string[] Lines => _current?.Lines ?? Array.Empty<string>();
        public IReadOnlyList<ParsedCommand> Commands => _current?.Parser.Commands ?? (IReadOnlyList<ParsedCommand>)Array.Empty<ParsedCommand>();
        public IReadOnlyList<int> ToolNumbers => _current?.ToolNumbers ?? Array.Empty<int>();
        public string? LoadedPath => _current?.FullPath;

        public ProgramLoadResult Load(string filePath)
        {
            _current = _programLoader.Load(filePath);
            return _current;
        }

        /// <summary>Устанавливает уже разобранную УП (например после фоновой подготовки).</summary>
        public void ApplyLoadResult(ProgramLoadResult loadResult)
        {
            _current = loadResult ?? throw new ArgumentNullException(nameof(loadResult));
        }

        /// <summary>
        /// Rebuilds the current parser using the same loaded lines but with a seeded machine state.
        /// This is used when external WCS (G54..G59) offsets change and we need to recompute EndState
        /// coordinates for motion execution and contour rendering.
        /// </summary>
        public ProgramLoadResult? ReparseWithSeed(MachineState seedState)
        {
            if (_current == null)
            {
                return null;
            }

            var parser = new GCodeParser(seedState.Clone());
            for (int i = 0; i < _current.Lines.Length; i++)
            {
                parser.ProcessLine(_current.Lines[i], i + 1);
            }

            int[] toolNumbers = parser.Commands
                .Where(c => c.ToolNumber.HasValue)
                .Select(c => c.ToolNumber!.Value)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            _current = new ProgramLoadResult
            {
                FilePath = _current.FilePath,
                FullPath = _current.FullPath,
                Lines = _current.Lines,
                Parser = parser,
                ToolNumbers = toolNumbers
            };

            return _current;
        }

        public void Clear()
        {
            _current = null;
        }

        public Point3DState GetPositionAtUiLine(int selectedLine)
        {
            var parser = Parser;
            if (parser == null)
            {
                return new Point3DState(0, 0, 0);
            }

            if (selectedLine <= 0)
            {
                var home = parser.State.GetG28PhysicalPosition();
                return new Point3DState(home.X, home.Y, home.Z);
            }

            var lastCmd = parser.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);
            if (lastCmd != null)
            {
                return new Point3DState(lastCmd.EndState.X, lastCmd.EndState.Y, lastCmd.EndState.Z);
            }

            var origin = parser.State.GetG28PhysicalPosition();
            return new Point3DState(origin.X, origin.Y, origin.Z);
        }

        public MachineState? GetStateAtUiLine(int selectedLine)
        {
            var parser = Parser;
            if (parser == null || selectedLine <= 0)
            {
                return null;
            }

            return _programStateService.BuildStateAtLine(parser, selectedLine).State;
        }
    }

    public readonly record struct Point3DState(double X, double Y, double Z);
}
