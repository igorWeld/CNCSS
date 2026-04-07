using CNCSS.Data;

namespace CNCSS.Tests;

/// <summary>
/// Тесты для MachineState - состояние станка с ЧПУ.
/// </summary>
public class MachineStateTests
{
    private const double Epsilon = 0.001;

    [Fact]
    public void Constructor_InitialState_HasDefaultValues()
    {
        // Arrange & Act
        var state = new MachineState();

        // Assert
        Assert.True(state.IsAbsolute);
        Assert.True(state.IsMetric);
        Assert.Equal(GCodeRegistry.G54, state.CurrentCoordinateSystem);
        Assert.Equal(GCodeRegistry.G17, state.CurrentPlane);
        Assert.Equal(GCodeRegistry.G0, state.CurrentMotionMode);
        Assert.Equal(GCodeRegistry.G40, state.CutterCompensation);
        Assert.Equal(GCodeRegistry.G49, state.ToolLengthCompensation);
        Assert.Equal(MachineState.HOME_X, state.X, 5);
        Assert.Equal(MachineState.HOME_Y, state.Y, 5);
        Assert.Equal(MachineState.HOME_Z, state.Z, 5);
        Assert.Equal(0, state.A);
        Assert.Equal(0, state.B);
        Assert.Equal(0, state.C);
        Assert.False(state.IsSpindleOn);
        Assert.False(state.IsCoolantOn);
        Assert.Null(state.ToolNumber);
    }

    [Fact]
    public void Reset_ResetsAllPropertiesToDefaults()
    {
        // Arrange
        var state = new MachineState();
        state.UpdatePosition(100, 200, 300, 10, 20, 30);
        state.SetCoordinateMode(false); // G91
        state.SetUnits(false); // G20
        state.SetMotionMode(GCodeRegistry.G1);
        state.SetFeedRate(500);
        state.SetSpindle(true, false);
        state.SetCoolant(true);
        state.SetTool(5, 2, 3);

        // Act
        state.Reset();

        // Assert
        Assert.True(state.IsAbsolute);
        Assert.True(state.IsMetric);
        Assert.Equal(GCodeRegistry.G0, state.CurrentMotionMode);
        Assert.Equal(MachineState.HOME_X, state.X, 5);
        Assert.Equal(MachineState.HOME_Y, state.Y, 5);
        Assert.Equal(MachineState.HOME_Z, state.Z, 5);
        Assert.Equal(0, state.FeedRate);
        Assert.False(state.IsSpindleOn);
        Assert.False(state.IsCoolantOn);
        Assert.Null(state.ToolNumber);
    }

    [Fact]
    public void UpdatePosition_AbsoluteMode_UpdatesCoordinates()
    {
        // Arrange
        var state = new MachineState();
        state.IsAbsolute = true;

        // Act
        state.UpdatePosition(50, 60, 70);

        // Assert
        Assert.Equal(50, state.X, 5);
        Assert.Equal(60, state.Y, 5);
        Assert.Equal(70, state.Z, 5);
    }

    [Fact]
    public void UpdatePosition_RelativeMode_AddsToCoordinates()
    {
        // Arrange
        var state = new MachineState();
        state.IsAbsolute = false;
        state.UpdatePosition(10, 20, 30);

        // Act
        state.UpdatePosition(5, 10, 15);

        // Assert
        Assert.Equal(15, state.X, 5);
        Assert.Equal(30, state.Y, 5);
        Assert.Equal(45, state.Z, 5);
    }

    [Fact]
    public void SetPosition_ForcesAbsolutePosition()
    {
        // Arrange
        var state = new MachineState();
        state.UpdatePosition(100, 200, 300);

        // Act
        state.SetPosition(999, 888, 777);

        // Assert
        Assert.Equal(999, state.X, 5);
        Assert.Equal(888, state.Y, 5);
        Assert.Equal(777, state.Z, 5);
    }

    [Fact]
    public void SavePreviousPosition_SavesCurrentCoordinates()
    {
        // Arrange
        var state = new MachineState();
        state.UpdatePosition(10, 20, 30, 1, 2, 3);

        // Act
        state.SavePreviousPosition();

        // Assert
        Assert.Equal(10, state.PreviousX, 5);
        Assert.Equal(20, state.PreviousY, 5);
        Assert.Equal(30, state.PreviousZ, 5);
        Assert.Equal(1, state.PreviousA, 5);
        Assert.Equal(2, state.PreviousB, 5);
        Assert.Equal(3, state.PreviousC, 5);
    }

    [Fact]
    public void HasPositionChanged_ReturnsTrue_WhenPositionChanged()
    {
        // Arrange
        var state = new MachineState();
        state.UpdatePosition(10, 20, 30);

        // Act & Assert
        Assert.True(state.HasPositionChanged());
    }

    [Fact]
    public void HasPositionChanged_ReturnsFalse_WhenPositionNotChanged()
    {
        // Arrange
        var state = new MachineState();

        // Act & Assert
        Assert.False(state.HasPositionChanged());
    }

    [Fact]
    public void GetDelta_ReturnsCorrectDifferences()
    {
        // Arrange
        var state = new MachineState();
        state.UpdatePosition(10, 20, 30, 1, 2, 3);

        // Act
        var delta = state.GetDelta();

        // Assert
        Assert.Equal(10, delta[0], 5);
        Assert.Equal(20, delta[1], 5);
        Assert.Equal(30, delta[2], 5);
        Assert.Equal(1, delta[3], 5);
        Assert.Equal(2, delta[4], 5);
        Assert.Equal(3, delta[5], 5);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new MachineState();
        original.UpdatePosition(100, 200, 300);
        original.SetFeedRate(500);
        original.SetSpindle(true, true);
        original.SetCoolant(true);
        original.SetTool(3, 1, 2);

        // Act
        var clone = original.Clone();

        // Assert - значения совпадают
        Assert.Equal(original.X, clone.X, 5);
        Assert.Equal(original.Y, clone.Y, 5);
        Assert.Equal(original.Z, clone.Z, 5);
        Assert.Equal(original.FeedRate, clone.FeedRate, 5);
        Assert.Equal(original.IsSpindleOn, clone.IsSpindleOn);
        Assert.Equal(original.IsCoolantOn, clone.IsCoolantOn);
        Assert.Equal(original.ToolNumber, clone.ToolNumber);

        // Act - изменяем оригинал
        original.UpdatePosition(999, 888, 777);

        // Assert - клон не изменился
        Assert.NotEqual(original.X, clone.X, 5);
        Assert.Equal(100, clone.X, 5);
    }

    [Fact]
    public void SetCoordinateMode_ChangesIsAbsolute()
    {
        // Arrange
        var state = new MachineState();

        // Act
        state.SetCoordinateMode(false);

        // Assert
        Assert.False(state.IsAbsolute);
    }

    [Fact]
    public void SetUnits_ChangesIsMetric()
    {
        // Arrange
        var state = new MachineState();

        // Act
        state.SetUnits(false);

        // Assert
        Assert.False(state.IsMetric);
    }

    [Fact]
    public void SetMotionMode_UpdatesMotionMode()
    {
        // Arrange
        var state = new MachineState();

        // Act
        state.SetMotionMode(GCodeRegistry.G1);

        // Assert
        Assert.Equal(GCodeRegistry.G1, state.CurrentMotionMode);
    }

    [Fact]
    public void SetFeedRate_UpdatesFeedRateAndEffectiveFeedRate()
    {
        // Arrange
        var state = new MachineState();

        // Act
        state.SetFeedRate(250);

        // Assert
        Assert.Equal(250, state.FeedRate, 5);
        Assert.Equal(250, state.EffectiveFeedRate, 5);
    }

    [Fact]
    public void EffectiveFeedRate_IsRapidFeed_WhenMotionModeIsG0()
    {
        // Arrange
        var state = new MachineState();
        state.SetFeedRate(500);
        state.CurrentMotionMode = GCodeRegistry.G0;

        // Assert
        Assert.Equal(MachineState.RAPID_FEED, state.EffectiveFeedRate, 5);
    }

    [Fact]
    public void SetSpindle_UpdatesSpindleState()
    {
        // Arrange
        var state = new MachineState();

        // Act
        state.SetSpindle(true, false);

        // Assert
        Assert.True(state.IsSpindleOn);
        Assert.False(state.IsSpindleCW);
    }

    [Fact]
    public void SetCoolant_UpdatesCoolantState()
    {
        // Arrange
        var state = new MachineState();

        // Act
        state.SetCoolant(true);

        // Assert
        Assert.True(state.IsCoolantOn);
    }

    [Fact]
    public void SetTool_UpdatesToolProperties()
    {
        // Arrange
        var state = new MachineState();

        // Act
        state.SetTool(5, 2, 3);

        // Assert
        Assert.Equal(5, state.ToolNumber);
        Assert.Equal(2, state.ToolLengthOffset);
        Assert.Equal(3, state.ToolRadiusOffset);
    }

    [Fact]
    public void GetPosition_ReturnsAllCoordinates()
    {
        // Arrange
        var state = new MachineState();
        state.UpdatePosition(1, 2, 3, 4, 5, 6);

        // Act
        var position = state.GetPosition();

        // Assert
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, position);
    }

    [Fact]
    public void GetXYZ_ReturnsXYZCoordinates()
    {
        // Arrange
        var state = new MachineState();
        state.UpdatePosition(10, 20, 30);

        // Act
        var xyz = state.GetXYZ();

        // Assert
        Assert.Equal(new[] { 10.0, 20.0, 30.0 }, xyz);
    }
}
