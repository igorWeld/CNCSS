using CNCSS.Controller.Model;
using CNCSS.Simulation.Bus;

namespace CNCSS.Controller.Core
{
    /// <summary>
    /// Реализация <see cref="IControllerCore"/>: публикует команды на <see cref="ISimulationBus"/> и отслеживает
    /// <see cref="ControllerMode"/> и флаг цикла.
    /// </summary>
    public sealed class ControllerCore : IControllerCore
    {
        private readonly ISimulationBus _bus;
        private static readonly HashSet<ControllerMode> AllowedCycleStartModes = new()
        {
            ControllerMode.Mem,
            ControllerMode.Mdi
        };
        private static readonly HashSet<ControllerMode> AllowedJogModes = new()
        {
            ControllerMode.Jog,
            ControllerMode.Handle
        };

        public ControllerMode Mode { get; private set; } = ControllerMode.Mem;
        public bool IsCycleRunning { get; private set; }

        public ControllerCore(ISimulationBus bus)
        {
            _bus = bus;
        }

        public bool CycleStart()
        {
            if (IsCycleRunning)
            {
                RaiseAlarm("CYCLE_ALREADY_RUNNING", "Cycle is already running");
                return false;
            }

            if (!AllowedCycleStartModes.Contains(Mode))
            {
                RaiseAlarm("CYCLE_START_MODE_LOCK", $"Cycle start is not allowed in mode {Mode.ToString().ToUpperInvariant()}");
                return false;
            }

            _bus.Publish(new ControllerCommandEvent("CycleStart", null, DateTime.UtcNow));
            IsCycleRunning = true;
            return true;
        }

        public bool FeedHold()
        {
            if (!IsCycleRunning)
            {
                RaiseAlarm("FEEDHOLD_NOT_RUNNING", "Feed hold is available only during cycle run");
                return false;
            }

            _bus.Publish(new ControllerCommandEvent("FeedHold", null, DateTime.UtcNow));
            IsCycleRunning = false;
            return true;
        }

        public void Reset()
        {
            _bus.Publish(new ControllerCommandEvent("Reset", null, DateTime.UtcNow));
            IsCycleRunning = false;
        }

        public bool SetMode(string mode)
        {
            if (IsCycleRunning)
            {
                RaiseAlarm("MODE_CHANGE_WHILE_RUNNING", "Mode change is not allowed while cycle is running");
                return false;
            }

            if (Enum.TryParse<ControllerMode>(mode, true, out var parsedMode))
            {
                Mode = parsedMode;
                _bus.Publish(new ControllerCommandEvent("SetMode", Mode.ToString(), DateTime.UtcNow));
                return true;
            }

            RaiseAlarm("MODE_UNKNOWN", $"Unknown mode: {mode}");
            return false;
        }

        public bool Jog(string axis, double delta)
        {
            if (IsCycleRunning)
            {
                RaiseAlarm("JOG_WHILE_RUNNING", "Jog is not allowed while cycle is running");
                return false;
            }

            if (!AllowedJogModes.Contains(Mode))
            {
                RaiseAlarm("JOG_MODE_LOCK", $"Jog is not allowed in mode {Mode.ToString().ToUpperInvariant()}");
                return false;
            }

            string normalizedAxis = axis.ToUpperInvariant();
            if (normalizedAxis is not ("X" or "Y" or "Z"))
            {
                RaiseAlarm("JOG_AXIS_INVALID", $"Unknown jog axis: {axis}");
                return false;
            }

            _bus.Publish(new ControllerCommandEvent("Jog", $"{normalizedAxis}:{delta}", DateTime.UtcNow));
            return true;
        }

        public bool ExecuteMdi(string command)
        {
            if (IsCycleRunning)
            {
                RaiseAlarm("MDI_WHILE_RUNNING", "MDI execute is not allowed while cycle is running");
                return false;
            }

            if (Mode != ControllerMode.Mdi)
            {
                RaiseAlarm("MDI_MODE_LOCK", $"MDI execute is not allowed in mode {Mode.ToString().ToUpperInvariant()}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                RaiseAlarm("MDI_EMPTY", "MDI command is empty");
                return false;
            }

            _bus.Publish(new ControllerCommandEvent("MdiExec", command.Trim(), DateTime.UtcNow));
            return true;
        }

        private void RaiseAlarm(string code, string message)
        {
            _bus.Publish(new AlarmRaisedEvent(code, message, DateTime.UtcNow));
        }
    }
}
