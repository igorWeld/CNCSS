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

        public void LoadProgram(IReadOnlyList<ParsedCommand> commands)
        {
            _commands.Clear();
            _commands.AddRange(commands);
            _blocks.Clear();
            foreach (var cmd in commands)
            {
                var kind = MotionBlockKind.Auxiliary;
                if (cmd.GCodes.Any(g => g.Number == 0)) kind = MotionBlockKind.Rapid;
                else if (cmd.GCodes.Any(g => g.Number == 1)) kind = MotionBlockKind.Linear;
                else if (cmd.GCodes.Any(g => g.Number is 2 or 3)) kind = MotionBlockKind.Arc;
                else if (cmd.MCodes.Any(m => m.Number == 6)) kind = MotionBlockKind.ToolChange;

                _blocks.Add(new MotionBlock
                {
                    LineNumber = cmd.LineNumber,
                    Kind = kind,
                    RawLine = cmd.RawLine
                });
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
                    TargetX = MachineState.HOME_X,
                    TargetY = MachineState.HOME_Y,
                    TargetZ = MachineState.HOME_Z
                };
            }

            if (_commands.Count == 0 || _currentCommandIndex >= _commands.Count - 1)
            {
                int? endMCode = null;
                if (_commands.Count > 0 && _currentCommandIndex >= 0 && _currentCommandIndex < _commands.Count)
                {
                    var finalCommand = _commands[_currentCommandIndex];
                    if (finalCommand.MCodes.Any(m => m.Number == 30))
                    {
                        endMCode = 30;
                    }
                    else if (finalCommand.MCodes.Any(m => m.Number == 2))
                    {
                        endMCode = 2;
                    }
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

            if (_optionalStopEnabled && cmd.MCodes.Any(m => m.Number == 1))
            {
                return new ProgramMoveDecision
                {
                    StopForOptional = true,
                    NextIndex = nextUiIndex,
                    BlockKind = block.Kind
                };
            }

            bool isHomeReturnLike = cmd.GCodes.Any(g => g.Number == 28) || cmd.MCodes.Any(m => m.Number == 6);
            bool needZFirst = isHomeReturnLike && Math.Abs(currentZ - MachineState.HOME_Z) > 0.001;

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
                    TargetZ = MachineState.HOME_Z
                };
            }

            if (isHomeReturnLike)
            {
                return new ProgramMoveDecision
                {
                    HasMove = true,
                    NextIndex = nextUiIndex,
                    BlockKind = block.Kind,
                    TargetX = MachineState.HOME_X,
                    TargetY = MachineState.HOME_Y,
                    TargetZ = MachineState.HOME_Z
                };
            }

            return new ProgramMoveDecision
            {
                HasMove = true,
                NextIndex = nextUiIndex,
                BlockKind = block.Kind,
                TargetX = cmd.EndState.X,
                TargetY = cmd.EndState.Y,
                TargetZ = cmd.EndState.Z
            };
        }
    }
}
