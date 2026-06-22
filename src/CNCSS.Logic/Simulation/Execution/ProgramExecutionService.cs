using CNCSS.Data;
using CNCSS.Machine.Model;
using CNCSS.Simulation.Bus;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Решение «следующего шага» программы: нужен ли переход, координаты цели, особые остановки.</summary>
    public sealed class ProgramMoveDecision
    {
        public bool HasMove { get; init; }
        public bool StopForOptional { get; init; }
        public bool EndOfProgram { get; init; }
        public int? EndProgramMCode { get; init; }
        public bool RewindToStart { get; init; }
        public int NextIndex { get; init; } // UI line index (0-based)
        public MotionBlockKind BlockKind { get; init; }
        public double TargetX { get; init; }
        public double TargetY { get; init; }
        public double TargetZ { get; init; }
    }

    /// <summary>
    /// Выполнение УП по разобранным <see cref="ParsedCommand"/>: индекс текущего кадра, single block, optional stop, публикация движений в шину.
    /// </summary>
    public sealed class ProgramExecutionService
    {
        private readonly ISimulationBus _bus;
        private readonly List<MotionBlock> _blocks = new();
        private readonly List<ParsedCommand> _commands = new();
        private bool _singleBlockEnabled;
        private bool _optionalStopEnabled;
        private double _homeX;
        private double _homeY;
        private double _homeZ;
        private int _singleBlockStopCommandIndex = -1;
        private bool _pendingHomeReturnStage2;
        private int _pendingHomeReturnCommandIndex = -1;
        private int _currentCommandIndex = -1;

        public ProgramExecutionService(ISimulationBus bus)
        {
            _bus = bus;
        }

        public void SetSingleBlock(bool enabled) => _singleBlockEnabled = enabled;
        public void SetOptionalStop(bool enabled) => _optionalStopEnabled = enabled;

        /// <summary>
        /// Sets the physical HOME target from the machine profile (per-axis HOME in MCS + McsZeroOffset).
        /// Used for G28 and M6 retracts instead of global constants.
        /// </summary>
        public void SetHomeTarget(double x, double y, double z)
        {
            _homeX = x;
            _homeY = y;
            _homeZ = z;
        }

        public void LoadProgram(IReadOnlyList<ParsedCommand> commands)
        {
            _commands.Clear();
            _commands.AddRange(commands);
            _blocks.Clear();
            foreach (var cmd in commands)
            {
                _blocks.Add(MotionBlockFactory.FromParsedCommand(cmd));
            }

            _currentCommandIndex = _commands.Count > 0 ? 0 : -1;
            ClearSingleBlockStop();
        }

        public void SetCurrentIndex(int uiLineIndex)
        {
            if (_commands.Count == 0)
            {
                _currentCommandIndex = -1;
                return;
            }

            int lineNumber = Math.Max(1, uiLineIndex + 1);
            int idx = _commands.FindLastIndex(c => c.LineNumber <= lineNumber);
            _currentCommandIndex = idx >= 0 ? idx : 0;
        }

        public ParsedCommand? GetCurrentCommand()
        {
            if (_currentCommandIndex < 0 || _currentCommandIndex >= _commands.Count)
            {
                return null;
            }

            return _commands[_currentCommandIndex];
        }

        public void BeginCycle()
        {
            _singleBlockStopCommandIndex = _singleBlockEnabled
                ? Math.Min(_commands.Count - 1, _currentCommandIndex + 1)
                : -1;
        }

        public void ClearSingleBlockStop()
        {
            _singleBlockStopCommandIndex = -1;
            _pendingHomeReturnStage2 = false;
            _pendingHomeReturnCommandIndex = -1;
        }

        public bool ShouldHoldForSingleBlock(double interpolationProgress)
        {
            return _singleBlockEnabled &&
                   _singleBlockStopCommandIndex >= 0 &&
                   interpolationProgress >= 1.0 &&
                   _currentCommandIndex >= _singleBlockStopCommandIndex;
        }

        public ProgramMoveDecision GetNextMoveDecision(double currentX, double currentY, double currentZ)
        {
            if (_pendingHomeReturnStage2 && _currentCommandIndex == _pendingHomeReturnCommandIndex)
            {
                _pendingHomeReturnStage2 = false;
                _pendingHomeReturnCommandIndex = -1;
                int uiIndex = Math.Max(0, _commands[_currentCommandIndex].LineNumber - 1);

                return new ProgramMoveDecision
                {
                    HasMove = true,
                    NextIndex = uiIndex,
                    BlockKind = MotionBlockKind.Rapid,
                    TargetX = _homeX,
                    TargetY = _homeY,
                    TargetZ = _homeZ
                };
            }

            if (_commands.Count == 0 || _currentCommandIndex >= _commands.Count - 1)
            {
                int? endMCode = null;
                if (_commands.Count > 0 && _currentCommandIndex >= 0 && _currentCommandIndex < _commands.Count)
                {
                    var finalCommand = _commands[_currentCommandIndex];
                    endMCode = MotionBlockFactory.FromParsedCommand(finalCommand).EndProgramMCode;
                }

                return new ProgramMoveDecision
                {
                    EndOfProgram = true,
                    EndProgramMCode = endMCode,
                    RewindToStart = endMCode == 30
                };
            }

            int nextCommandIndex = _currentCommandIndex + 1;
            _currentCommandIndex = nextCommandIndex;
            var cmd = _commands[nextCommandIndex];
            var block = _blocks[nextCommandIndex];
            int nextUiIndex = Math.Max(0, cmd.LineNumber - 1);
            _bus.Publish(new ProgramLineExecutedEvent(cmd.LineNumber, DateTime.UtcNow));

            if (_optionalStopEnabled && block.IsOptionalStop)
            {
                return new ProgramMoveDecision
                {
                    StopForOptional = true,
                    NextIndex = nextUiIndex,
                    BlockKind = block.Kind
                };
            }

            bool isHomeReturnLike = cmd.GCodes.Any(g => g.Number == 28) || cmd.MCodes.Any(m => m.Number == 6);
            bool needZFirst = isHomeReturnLike && Math.Abs(currentZ - _homeZ) > 0.001;

            if (needZFirst)
            {
                _pendingHomeReturnStage2 = true;
                _pendingHomeReturnCommandIndex = nextCommandIndex;
                return new ProgramMoveDecision
                {
                    HasMove = true,
                    NextIndex = nextUiIndex,
                    BlockKind = block.Kind,
                    TargetX = currentX,
                    TargetY = currentY,
                    TargetZ = _homeZ
                };
            }

            if (isHomeReturnLike)
            {
                return new ProgramMoveDecision
                {
                    HasMove = true,
                    NextIndex = nextUiIndex,
                    BlockKind = block.Kind,
                    TargetX = _homeX,
                    TargetY = _homeY,
                    TargetZ = _homeZ
                };
            }

            return new ProgramMoveDecision
            {
                HasMove = true,
                NextIndex = nextUiIndex,
                BlockKind = block.Kind,
                TargetX = block.TargetX,
                TargetY = block.TargetY,
                TargetZ = block.TargetZ
            };
        }
    }
}
