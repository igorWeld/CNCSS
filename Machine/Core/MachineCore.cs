using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Machine.Model;
using CNCSS.Simulation.Bus;

namespace CNCSS.Machine.Core
{
    public sealed class MachineCore : IMachineCore, IDisposable
    {
        private readonly ISimulationBus _bus;
        private readonly IDisposable _controllerCommandSubscription;
        private const double SoftLimitMinX = -500.0;
        private const double SoftLimitMaxX = 500.0;
        private const double SoftLimitMinY = -500.0;
        private const double SoftLimitMaxY = 500.0;
        private const double SoftLimitMinZ = -300.0;
        private const double SoftLimitMaxZ = 300.0;
        private GCodeParser _mdiParser;

        public MachineAxesState State { get; } = new()
        {
            X = MachineState.HOME_X,
            Y = MachineState.HOME_Y,
            Z = MachineState.HOME_Z
        };

        public MachineCore(ISimulationBus bus)
        {
            _bus = bus;
            _controllerCommandSubscription = _bus.Subscribe<ControllerCommandEvent>(HandleControllerCommand);
            _mdiParser = new GCodeParser();
            PublishWorkOffsetsChanged();
        }

        public void Tick(double deltaTimeSeconds)
        {
            PublishMachineStateChanged();
        }

        public void UpdatePosition(double x, double y, double z)
        {
            State.X = x;
            State.Y = y;
            State.Z = z;
        }

        public void SyncRuntimeFromState(MachineState source)
        {
            State.ToolNumber = source.ToolNumber;
            State.FeedRate = source.EffectiveFeedRate;
            State.SpindleSpeed = source.SpindleSpeed;
            State.IsSpindleOn = source.IsSpindleOn;
            State.IsSpindleCW = source.IsSpindleCW;
            State.IsCoolantOn = source.IsCoolantOn;
            _mdiParser.State.CurrentCoordinateSystem = source.CurrentCoordinateSystem;
            for (int i = MachineState.MinWorkOffsetNumber; i <= MachineState.MaxWorkOffsetNumber; i++)
            {
                var srcOffset = source.GetWorkOffset(i);
                _mdiParser.State.SetWorkOffset(i, srcOffset.X, srcOffset.Y, srcOffset.Z);
            }
            PublishWorkOffsetsChanged();
        }

        public void Reset()
        {
            State.IsRunning = false;
            State.ToolNumber = null;
            State.FeedRate = 0;
            State.SpindleSpeed = 0;
            State.IsSpindleOn = false;
            State.IsSpindleCW = false;
            State.IsCoolantOn = false;
            Home();
            _mdiParser = new GCodeParser();
            PublishWorkOffsetsChanged();
        }

        public void Home()
        {
            State.X = MachineState.HOME_X;
            State.Y = MachineState.HOME_Y;
            State.Z = MachineState.HOME_Z;
        }

        public void Dispose()
        {
            _controllerCommandSubscription.Dispose();
        }

        private void HandleControllerCommand(ControllerCommandEvent evt)
        {
            switch (evt.Command)
            {
                case "CycleStart":
                    State.IsRunning = true;
                    break;
                case "FeedHold":
                    State.IsRunning = false;
                    break;
                case "Reset":
                    Reset();
                    break;
                case "Jog":
                    ApplyJog(evt.Payload);
                    break;
                case "MdiExec":
                    ApplyMdiCommand(evt.Payload);
                    break;
            }
        }

        private void ApplyJog(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            var parts = payload.Split(':');
            if (parts.Length != 2 || !double.TryParse(parts[1], out var delta))
            {
                return;
            }

            switch (parts[0].ToUpperInvariant())
            {
                case "X":
                    if (!TryApplyJogAxis("X", State.X, delta, SoftLimitMinX, SoftLimitMaxX, out var nextX))
                    {
                        return;
                    }

                    State.X = nextX;
                    break;
                case "Y":
                    if (!TryApplyJogAxis("Y", State.Y, delta, SoftLimitMinY, SoftLimitMaxY, out var nextY))
                    {
                        return;
                    }

                    State.Y = nextY;
                    break;
                case "Z":
                    if (!TryApplyJogAxis("Z", State.Z, delta, SoftLimitMinZ, SoftLimitMaxZ, out var nextZ))
                    {
                        return;
                    }

                    State.Z = nextZ;
                    break;
                default:
                    return;
            }

            PublishMachineStateChanged();
        }

        private bool TryApplyJogAxis(string axis, double current, double delta, double minLimit, double maxLimit, out double next)
        {
            next = current + delta;
            if (next < minLimit || next > maxLimit)
            {
                _bus.Publish(new AlarmRaisedEvent(
                    "SOFT_LIMIT",
                    $"{axis}-axis limit [{minLimit:F3}; {maxLimit:F3}] exceeded (requested {next:F3})",
                    DateTime.UtcNow));
                return false;
            }

            return true;
        }

        private void ApplyMdiCommand(string? mdiCommand)
        {
            if (string.IsNullOrWhiteSpace(mdiCommand))
            {
                return;
            }

            // Прогоняем MDI-строку через основной парсер, чтобы поддержать
            // весь уже реализованный в проекте набор G/M-команд и модальностей.
            var tempState = _mdiParser.State.Clone();
            tempState.SetPosition(State.X, State.Y, State.Z);
            var tempParser = new GCodeParser(tempState);
            tempParser.ProcessLine(mdiCommand, 1);

            var parsed = tempParser.Commands.LastOrDefault();
            if (parsed == null)
            {
                _bus.Publish(new AlarmRaisedEvent("MDI_PARSE", $"Unsupported MDI command: {mdiCommand}", DateTime.UtcNow));
                return;
            }

            double nextX = parsed.EndState.X;
            double nextY = parsed.EndState.Y;
            double nextZ = parsed.EndState.Z;

            if (!ValidateAxisLimit("X", nextX, SoftLimitMinX, SoftLimitMaxX) ||
                !ValidateAxisLimit("Y", nextY, SoftLimitMinY, SoftLimitMaxY) ||
                !ValidateAxisLimit("Z", nextZ, SoftLimitMinZ, SoftLimitMaxZ))
            {
                return;
            }

            _mdiParser = tempParser;
            State.X = nextX;
            State.Y = nextY;
            State.Z = nextZ;
            State.ToolNumber = parsed.EndState.ToolNumber;
            State.FeedRate = parsed.EndState.EffectiveFeedRate;
            State.SpindleSpeed = parsed.EndState.SpindleSpeed;
            State.IsSpindleOn = parsed.EndState.IsSpindleOn;
            State.IsSpindleCW = parsed.EndState.IsSpindleCW;
            State.IsCoolantOn = parsed.EndState.IsCoolantOn;
            _bus.Publish(new MdiModeChangedEvent(parsed.EndState.IsAbsolute ? "G90" : "G91", DateTime.UtcNow));
            PublishWorkOffsetsChanged();
            PublishMachineStateChanged();
        }

        private bool ValidateAxisLimit(string axis, double requested, double minLimit, double maxLimit)
        {
            if (requested < minLimit || requested > maxLimit)
            {
                _bus.Publish(new AlarmRaisedEvent(
                    "SOFT_LIMIT",
                    $"{axis}-axis limit [{minLimit:F3}; {maxLimit:F3}] exceeded (requested {requested:F3})",
                    DateTime.UtcNow));
                return false;
            }

            return true;
        }

        private void PublishMachineStateChanged()
        {
            var activeOffset = _mdiParser.State.GetActiveWorkOffset();
            var coordSystem = $"{_mdiParser.State.CurrentCoordinateSystem.Letter}{_mdiParser.State.CurrentCoordinateSystem.Number}";
            _bus.Publish(new MachineStateChangedEvent(
                State.X,
                State.Y,
                State.Z,
                State.IsRunning,
                State.ToolNumber,
                State.FeedRate,
                State.SpindleSpeed,
                State.IsSpindleOn,
                State.IsSpindleCW,
                State.IsCoolantOn,
                coordSystem,
                activeOffset.X,
                activeOffset.Y,
                activeOffset.Z,
                DateTime.UtcNow));
        }

        private void PublishWorkOffsetsChanged()
        {
            var offsets = new List<WorkOffsetSnapshot>();
            for (int i = MachineState.MinWorkOffsetNumber; i <= MachineState.MaxWorkOffsetNumber; i++)
            {
                var value = _mdiParser.State.GetWorkOffset(i);
                offsets.Add(new WorkOffsetSnapshot(i, value.X, value.Y, value.Z));
            }

            _bus.Publish(new WorkOffsetsChangedEvent(offsets, DateTime.UtcNow));
        }
    }
}
