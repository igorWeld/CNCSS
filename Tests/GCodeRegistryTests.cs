using CNCSS.Data;

namespace CNCSS.Tests;

/// <summary>
/// Тесты для GCodeRegistry - реестр G и M кодов.
/// </summary>
public class GCodeRegistryTests
{
    [Fact]
    public void GetGCode_ValidNumber_ReturnsCorrectCode()
    {
        // Arrange & Act
        var g0 = GCodeRegistry.GetGCode(0);
        var g1 = GCodeRegistry.GetGCode(1);
        var g90 = GCodeRegistry.GetGCode(90);

        // Assert
        Assert.Equal(GCodeRegistry.G0, g0);
        Assert.Equal(GCodeRegistry.G1, g1);
        Assert.Equal(GCodeRegistry.G90, g90);
    }

    [Fact]
    public void GetGCode_InvalidNumber_ReturnsNull()
    {
        // Arrange & Act
        var result = GCodeRegistry.GetGCode(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetMCode_ValidNumber_ReturnsCorrectCode()
    {
        // Arrange & Act
        var m3 = GCodeRegistry.GetMCode(3);
        var m6 = GCodeRegistry.GetMCode(6);
        var m30 = GCodeRegistry.GetMCode(30);

        // Assert
        Assert.Equal(GCodeRegistry.M3, m3);
        Assert.Equal(GCodeRegistry.M6, m6);
        Assert.Equal(GCodeRegistry.M30, m30);
    }

    [Fact]
    public void GetMCode_InvalidNumber_ReturnsNull()
    {
        // Arrange & Act
        var result = GCodeRegistry.GetMCode(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void IsMovementCode_G0G1G2G3_ReturnsTrue()
    {
        // Arrange & Act & Assert
        Assert.True(GCodeRegistry.IsMovementCode(GCodeRegistry.G0));
        Assert.True(GCodeRegistry.IsMovementCode(GCodeRegistry.G1));
        Assert.True(GCodeRegistry.IsMovementCode(GCodeRegistry.G2));
        Assert.True(GCodeRegistry.IsMovementCode(GCodeRegistry.G3));
    }

    [Fact]
    public void IsMovementCode_OtherCodes_ReturnsFalse()
    {
        // Arrange & Act & Assert
        Assert.False(GCodeRegistry.IsMovementCode(GCodeRegistry.G4));
        Assert.False(GCodeRegistry.IsMovementCode(GCodeRegistry.G90));
        Assert.False(GCodeRegistry.IsMovementCode(GCodeRegistry.M3));
        Assert.False(GCodeRegistry.IsMovementCode(null));
    }

    [Fact]
    public void IsArcCode_G2G3_ReturnsTrue()
    {
        // Arrange & Act & Assert
        Assert.True(GCodeRegistry.IsArcCode(GCodeRegistry.G2));
        Assert.True(GCodeRegistry.IsArcCode(GCodeRegistry.G3));
    }

    [Fact]
    public void IsArcCode_OtherCodes_ReturnsFalse()
    {
        // Arrange & Act & Assert
        Assert.False(GCodeRegistry.IsArcCode(GCodeRegistry.G0));
        Assert.False(GCodeRegistry.IsArcCode(GCodeRegistry.G1));
        Assert.False(GCodeRegistry.IsArcCode(GCodeRegistry.M3));
        Assert.False(GCodeRegistry.IsArcCode(null));
    }

    [Fact]
    public void AllGCodes_ContainsAllDefinedGCodes()
    {
        // Arrange
        var expectedCodes = new[] { 0, 1, 2, 3, 4, 17, 18, 19, 20, 21, 28, 30, 40, 41, 42, 43, 44, 49, 54, 55, 56, 57, 58, 59, 80, 81, 82, 83, 84, 85, 90, 91 };

        // Act
        var actualCodes = GCodeRegistry.AllGCodes.Select(g => g.Number).OrderBy(x => x);

        // Assert
        Assert.Equal(expectedCodes.OrderBy(x => x), actualCodes);
    }

    [Fact]
    public void AllMCodes_ContainsAllDefinedMCodes()
    {
        // Arrange
        var expectedCodes = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 30, 98, 99 };

        // Act
        var actualCodes = GCodeRegistry.AllMCodes.Select(m => m.Number).OrderBy(x => x);

        // Assert
        Assert.Equal(expectedCodes.OrderBy(x => x), actualCodes);
    }

    [Theory]
    [InlineData("G", 0)]
    [InlineData("G", 1)]
    [InlineData("G", 90)]
    [InlineData("M", 3)]
    [InlineData("M", 6)]
    public void GCodeTemplate_HasCorrectLetterAndNumber(string expectedLetter, int expectedNumber)
    {
        // Arrange
        var template = new GCodeTemplate(expectedLetter, expectedNumber, CommandModality.Modal);

        // Assert
        Assert.Equal(expectedLetter, template.Letter);
        Assert.Equal(expectedNumber, template.Number);
    }
}
