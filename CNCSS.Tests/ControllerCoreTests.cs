using CNCSS.Controller.Core;
using CNCSS.Controller.Model;
using CNCSS.Simulation.Bus;

namespace CNCSS.Tests;

public sealed class ControllerCoreTests
{
    [Fact]
    public void CycleStart_InMemMode_PublishesCommandAndSetsRunning()
    {
        var bus = new SimulationBus();
        var commands = new List<ControllerCommandEvent>();
        using var sub = bus.Subscribe<ControllerCommandEvent>(commands.Add);
        var controller = new ControllerCore(bus);

        bool accepted = controller.CycleStart();

        Assert.True(accepted);
        Assert.True(controller.IsCycleRunning);
        Assert.Single(commands);
        Assert.Equal("CycleStart", commands[0].Command);
    }

    [Fact]
    public void CycleStart_InJogMode_IsRejectedWithAlarm()
    {
        var bus = new SimulationBus();
        var alarms = new List<AlarmRaisedEvent>();
        using var sub = bus.Subscribe<AlarmRaisedEvent>(alarms.Add);
        var controller = new ControllerCore(bus);

        Assert.True(controller.SetMode("Jog"));
        bool accepted = controller.CycleStart();

        Assert.False(accepted);
        Assert.False(controller.IsCycleRunning);
        Assert.Single(alarms);
        Assert.Equal("CYCLE_START_MODE_LOCK", alarms[0].Code);
    }

    [Fact]
    public void Jog_IsAllowedOnlyInJogOrHandleMode()
    {
        var bus = new SimulationBus();
        var commands = new List<ControllerCommandEvent>();
        using var sub = bus.Subscribe<ControllerCommandEvent>(commands.Add);
        var controller = new ControllerCore(bus);

        Assert.False(controller.Jog("X", 1.0));
        Assert.True(controller.SetMode("Handle"));
        Assert.True(controller.Jog("X", 1.0));

        Assert.Contains(commands, c => c.Command == "Jog" && c.Payload == "X:1");
    }

    [Fact]
    public void ExecuteMdi_IsAllowedOnlyInMdiMode()
    {
        var bus = new SimulationBus();
        var commands = new List<ControllerCommandEvent>();
        using var sub = bus.Subscribe<ControllerCommandEvent>(commands.Add);
        var controller = new ControllerCore(bus);

        Assert.False(controller.ExecuteMdi("G0 X1"));
        Assert.True(controller.SetMode(ControllerMode.Mdi.ToString()));
        Assert.True(controller.ExecuteMdi(" G0 X1 "));

        Assert.Contains(commands, c => c.Command == "MdiExec" && c.Payload == "G0 X1");
    }
}
