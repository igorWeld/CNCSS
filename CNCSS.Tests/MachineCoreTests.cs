using CNCSS.Data;
using CNCSS.Machine.Core;
using CNCSS.Simulation.Bus;

namespace CNCSS.Tests;

public sealed class MachineCoreTests
{
    [Fact]
    public void JogCommand_InsideLimits_UpdatesPositionAndPublishesState()
    {
        var bus = new SimulationBus();
        using var machine = new MachineCore(bus);
        var states = new List<MachineStateChangedEvent>();
        using var sub = bus.Subscribe<MachineStateChangedEvent>(states.Add);

        bus.Publish(new ControllerCommandEvent("Jog", "X:5", DateTime.UtcNow));

        Assert.Equal(5, machine.State.X, precision: 6);
        Assert.Single(states);
        Assert.Equal(machine.State.X, states[0].X, precision: 6);
    }

    [Fact]
    public void JogCommand_OutsideLimits_RaisesSoftLimitAlarm()
    {
        var bus = new SimulationBus();
        using var machine = new MachineCore(bus);
        var alarms = new List<AlarmRaisedEvent>();
        using var sub = bus.Subscribe<AlarmRaisedEvent>(alarms.Add);

        bus.Publish(new ControllerCommandEvent("Jog", "X:10000", DateTime.UtcNow));

        Assert.Equal(0, machine.State.X, precision: 6);
        Assert.Single(alarms);
        Assert.Equal("SOFT_LIMIT", alarms[0].Code);
    }

    [Fact]
    public void MdiCommand_UsesGCodeParserAndPublishesRuntimeState()
    {
        var bus = new SimulationBus();
        using var machine = new MachineCore(bus);
        var states = new List<MachineStateChangedEvent>();
        using var sub = bus.Subscribe<MachineStateChangedEvent>(states.Add);

        bus.Publish(new ControllerCommandEvent("MdiExec", "G90 G1 X12 Y3 Z-4 F120 S1500 M3", DateTime.UtcNow));

        Assert.Equal(12, machine.State.X, precision: 6);
        Assert.Equal(3, machine.State.Y, precision: 6);
        Assert.Equal(-4, machine.State.Z, precision: 6);
        Assert.Equal(120, machine.State.FeedRate, precision: 6);
        Assert.Equal(1500, machine.State.SpindleSpeed, precision: 6);
        Assert.True(machine.State.IsSpindleOn);
        Assert.Contains(states, s => Math.Abs(s.X - 12) < 1e-6);
    }

    [Fact]
    public void MdiWorkOffsetCommand_PublishesOffsetSnapshot()
    {
        var bus = new SimulationBus();
        using var machine = new MachineCore(bus);
        var offsets = new List<WorkOffsetsChangedEvent>();
        using var sub = bus.Subscribe<WorkOffsetsChangedEvent>(offsets.Add);

        bus.Publish(new ControllerCommandEvent("MdiExec", "G10 L2 P1 X10 Y20 Z30", DateTime.UtcNow));

        var g54 = Assert.Single(offsets).Offsets.Single(o => o.CoordinateSystemNumber == 54);
        Assert.Equal(10, g54.X, precision: 6);
        Assert.Equal(20, g54.Y, precision: 6);
        Assert.Equal(30, g54.Z, precision: 6);
    }
}
