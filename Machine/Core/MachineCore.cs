using System.Globalization;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Machine.Model;
using CNCSS.Simulation.Bus;

namespace CNCSS.Machine.Core
{
    /// <summary>
    /// Реализация <see cref="IMachineCore"/>: подписывается на <see cref="ControllerCommandEvent"/> и публикует
    /// <see cref="MachineStateChangedEvent"/> и связанные смещения; обрабатывает MDI через локальный парсер.
    /// </summary>
    public sealed class MachineCore : IMachineCore, IDisposable
    {
        private readonly ISimulationBus _bus;
        private readonly IDisposable _controllerCommandSubscription;
        private Vmc3AxisKinematicsModel _kinematics;
        private GCodeParser _mdiParser;

        public MachineAxesState State { get; } = new();

        public MachineCore(ISimulationBus bus)
            : this(bus, Vmc3AxisKinematicsModel.Default)
        {
        }

        public MachineCore(ISimulationBus bus, Vmc3AxisKinematicsModel kinematics)
        {
            _bus = bus;
            _kinematics = kinematics;
            _controllerCommandSubscription = _bus.Subscribe<ControllerCommandEvent>(HandleControllerCommand);
            _mdiParser = new GCodeParser();
            // Minimal defaults for early compensation behavior testing (can be overwritten later from UI).
            _mdiParser.State.SetToolLengthOffsetValue(1, geom: 100.0);
            _mdiParser.State.SetToolRadiusOffsetValue(1, geom: 5.0);
            Home();
            PublishWorkOffsetsChanged();
        }

        public (double X, double Y, double Z) GetHomePhysicalPosition() => _mdiParser.State.GetG28PhysicalPosition();

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

        public void ApplyProfileHome(MachineDefinition profile) =>
            profile.ApplyHomeToMachineState(_mdiParser.State);

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
            // Preserve per-profile settings across controller reset.
            double zx = _mdiParser.State.MachineZeroOffsetX;
            double zy = _mdiParser.State.MachineZeroOffsetY;
            double zz = _mdiParser.State.MachineZeroOffsetZ;
            double hx = _mdiParser.State.AxisHomeMcsX;
            double hy = _mdiParser.State.AxisHomeMcsY;
            double hz = _mdiParser.State.AxisHomeMcsZ;
            var workOffsets = new Dictionary<int, MachineState.WorkOffset>();
            for (int i = MachineState.MinWorkOffsetNumber; i <= MachineState.MaxWorkOffsetNumber; i++)
            {
                workOffsets[i] = _mdiParser.State.GetWorkOffset(i).Clone();
            }
            var hGeom = new Dictionary<int, double>(_mdiParser.State.ToolLengthGeom);
            var hWear = new Dictionary<int, double>(_mdiParser.State.ToolLengthWear);
            var dGeom = new Dictionary<int, double>(_mdiParser.State.ToolRadiusGeom);
            var dWear = new Dictionary<int, double>(_mdiParser.State.ToolRadiusWear);

            State.IsRunning = false;
            State.ToolNumber = null;
            State.FeedRate = 0;
            State.SpindleSpeed = 0;
            State.IsSpindleOn = false;
            State.IsSpindleCW = false;
            State.IsCoolantOn = false;
            Home();
            _mdiParser = new GCodeParser();
            _mdiParser.State.MachineZeroOffsetX = zx;
            _mdiParser.State.MachineZeroOffsetY = zy;
            _mdiParser.State.MachineZeroOffsetZ = zz;
            _mdiParser.State.AxisHomeMcsX = hx;
            _mdiParser.State.AxisHomeMcsY = hy;
            _mdiParser.State.AxisHomeMcsZ = hz;
            foreach (var pair in workOffsets)
            {
                _mdiParser.State.SetWorkOffset(pair.Key, pair.Value.X, pair.Value.Y, pair.Value.Z);
            }
            foreach (var p in hGeom) _mdiParser.State.ToolLengthGeom[p.Key] = p.Value;
            foreach (var p in hWear) _mdiParser.State.ToolLengthWear[p.Key] = p.Value;
            foreach (var p in dGeom) _mdiParser.State.ToolRadiusGeom[p.Key] = p.Value;
            foreach (var p in dWear) _mdiParser.State.ToolRadiusWear[p.Key] = p.Value;
            Home();
            PublishWorkOffsetsChanged();
        }

        public void Home()
        {
            var (x, y, z) = GetHomePhysicalPosition();
            HomeTo(x, y, z);
        }

        public void HomeTo(double x, double y, double z)
        {
            State.X = x;
            State.Y = y;
            State.Z = z;
            PublishMachineStateChanged();
        }

        public void UpdateKinematics(Vmc3AxisKinematicsModel kinematics)
        {
            _kinematics = kinematics ?? throw new ArgumentNullException(nameof(kinematics));
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
                case "WorkOffsetSet":
                    ApplyWorkOffsetSet(evt.Payload);
                    break;
                case "ToolOffsetSet":
                    ApplyToolOffsetSet(evt.Payload);
                    break;
                case "MachineZeroSet":
                    ApplyMachineZeroSet();
                    break;
                case "MachineZeroReset":
                    ApplyMachineZeroReset();
                    break;
                case "MachineZeroSetTo":
                    ApplyMachineZeroSetTo(evt.Payload);
                    break;
            }
        }

        private void ApplyMachineZeroSet()
        {
            // Current physical axis pose becomes MCS zero.
            // IMPORTANT: do not move the machine visually/physically; only shift the coordinate system.
            // Keep the current MCS position the same by compensating axis pose by the delta of offsets.
            double oldX = _mdiParser.State.MachineZeroOffsetX;
            double oldY = _mdiParser.State.MachineZeroOffsetY;
            double oldZ = _mdiParser.State.MachineZeroOffsetZ;

            _mdiParser.State.SetMachineZeroFromPhysical(State.X, State.Y, State.Z);

            double dx = _mdiParser.State.MachineZeroOffsetX - oldX;
            double dy = _mdiParser.State.MachineZeroOffsetY - oldY;
            double dz = _mdiParser.State.MachineZeroOffsetZ - oldZ;

            State.X -= dx;
            State.Y -= dy;
            State.Z -= dz;
            PublishMachineStateChanged();
        }

        private void ApplyMachineZeroReset()
        {
            _mdiParser.State.ResetMachineZero();
            PublishMachineStateChanged();
        }

        private void ApplyMachineZeroSetTo(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            // Payload: "x;y;z" (InvariantCulture)
            var parts = payload.Split(';');
            if (parts.Length != 3
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
            {
                return;
            }

            double oldX = _mdiParser.State.MachineZeroOffsetX;
            double oldY = _mdiParser.State.MachineZeroOffsetY;
            double oldZ = _mdiParser.State.MachineZeroOffsetZ;

            _mdiParser.State.MachineZeroOffsetX = x;
            _mdiParser.State.MachineZeroOffsetY = y;
            _mdiParser.State.MachineZeroOffsetZ = z;

            double dx = x - oldX;
            double dy = y - oldY;
            double dz = z - oldZ;

            State.X -= dx;
            State.Y -= dy;
            State.Z -= dz;
            PublishMachineStateChanged();
        }

        private void ApplyToolOffsetSet(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            // Payload: "{row}:{colToken}:{value}"
            // colToken: GeomH | WearH | GeomD | WearD
            var parts = payload.Split(':');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out int row)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return;
            }

            string col = parts[1].Trim();
            if (row <= 0)
            {
                return;
            }

            switch (col)
            {
                case "GeomH":
                    _mdiParser.State.SetToolLengthOffsetValue(row, geom: value);
                    break;
                case "WearH":
                    _mdiParser.State.SetToolLengthOffsetValue(row, wear: value);
                    break;
                case "GeomD":
                    _mdiParser.State.SetToolRadiusOffsetValue(row, geom: value);
                    break;
                case "WearD":
                    _mdiParser.State.SetToolRadiusOffsetValue(row, wear: value);
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
                    if (!TryApplyJogAxis("X", State.X, delta, out var nextX))
                    {
                        return;
                    }

                    State.X = nextX;
                    break;
                case "Y":
                    if (!TryApplyJogAxis("Y", State.Y, delta, out var nextY))
                    {
                        return;
                    }

                    State.Y = nextY;
                    break;
                case "Z":
                    if (!TryApplyJogAxis("Z", State.Z, delta, out var nextZ))
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

        private bool TryApplyJogAxis(string axis, double current, double delta, out double next)
        {
            next = current + delta;
            if (!_kinematics.IsWithinLimits(axis, next, out var limits))
            {
                PublishSoftLimitAlarm(limits, next);
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

            if (!ValidateAxisLimit("X", nextX) ||
                !ValidateAxisLimit("Y", nextY) ||
                !ValidateAxisLimit("Z", nextZ))
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

        private void ApplyWorkOffsetSet(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            // Payload format: "54:X:Y:Z" (system number is 54..59, values are invariant doubles)
            string[] parts = payload.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 4
                || !int.TryParse(parts[0], out int systemNumber)
                || systemNumber < MachineState.MinWorkOffsetNumber
                || systemNumber > MachineState.MaxWorkOffsetNumber)
            {
                _bus.Publish(new AlarmRaisedEvent("WCS_PAYLOAD", $"Invalid WorkOffsetSet payload: {payload}", DateTime.UtcNow));
                return;
            }

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
            {
                _bus.Publish(new AlarmRaisedEvent("WCS_PARSE", $"Invalid WCS values: {payload}", DateTime.UtcNow));
                return;
            }

            _mdiParser.State.SetWorkOffset(systemNumber, x, y, z);
            PublishWorkOffsetsChanged();
            PublishMachineStateChanged();
        }

        private bool ValidateAxisLimit(string axis, double requested)
        {
            if (!_kinematics.IsWithinLimits(axis, requested, out var limits))
            {
                PublishSoftLimitAlarm(limits, requested);
                return false;
            }

            return true;
        }

        private void PublishSoftLimitAlarm(AxisTravelLimits limits, double requested)
        {
            _bus.Publish(new AlarmRaisedEvent(
                "SOFT_LIMIT",
                $"{limits.Axis}-axis limit [{limits.Min:F3}; {limits.Max:F3}] exceeded (requested {requested:F3})",
                DateTime.UtcNow));
        }

        private void PublishMachineStateChanged()
        {
            var activeOffset = _mdiParser.State.GetActiveWorkOffset();
            var coordSystem = $"{_mdiParser.State.CurrentCoordinateSystem.Letter}{_mdiParser.State.CurrentCoordinateSystem.Number}";
            _bus.Publish(new MachineStateChangedEvent(
                State.X,
                State.Y,
                State.Z,
                _mdiParser.State.MachineZeroOffsetX,
                _mdiParser.State.MachineZeroOffsetY,
                _mdiParser.State.MachineZeroOffsetZ,
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
