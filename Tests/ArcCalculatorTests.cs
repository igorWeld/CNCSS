using CNCSS.Data;
using CNCSS.Logic;

namespace CNCSS.Tests;

/// <summary>
/// Тесты для ArcCalculator - расчет геометрии дуг G2/G3.
/// </summary>
public class ArcCalculatorTests
{
    private const double Epsilon = 1e-6;

    [Fact]
    public void TryComputeArc_G17_Clockwise_CenterFromIJK_ReturnsTrue()
    {
        // Arrange: дуга в плоскости XY (G17), по часовой стрелке (G2)
        // Начало в (10, 0), конец в (0, 10), центр в (0, 0) через I=-10, J=0
        double startX = 10, startY = 0, startZ = 0;
        double endX = 0, endY = 10, endZ = 0;
        var plane = GCodeRegistry.G17;
        bool isG2 = true; // CW
        double? i = -10, j = 0, k = null;
        double? r = null;

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.True(result);
        Assert.NotNull(arc);
        Assert.Equal(17, arc.Plane);
        Assert.Equal(10, arc.Radius, 5);
        Assert.True(arc.IsClockwise);
        Assert.Equal(0, arc.CenterU, 5);
        Assert.Equal(0, arc.CenterV, 5);
        Assert.Equal(-Math.PI / 2, arc.SweepAngleRad, 4); // CW 90 градусов
    }

    [Fact]
    public void TryComputeArc_G17_CounterClockwise_CenterFromIJK_ReturnsTrue()
    {
        // Arrange: дуга в плоскости XY (G17), против часовой (G3)
        // Начало в (10, 0), конец в (0, 10), центр в (0, 0)
        double startX = 10, startY = 0, startZ = 0;
        double endX = 0, endY = 10, endZ = 0;
        var plane = GCodeRegistry.G17;
        bool isG2 = false; // CCW
        double? i = -10, j = 0, k = null;
        double? r = null;

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.True(result);
        Assert.NotNull(arc);
        Assert.False(arc.IsClockwise);
        Assert.Equal(Math.PI / 2, arc.SweepAngleRad, 4); // CCW 90 градусов
    }

    [Fact]
    public void TryComputeArc_G18_Plane_XZ_CenterFromIJK_ReturnsTrue()
    {
        // Arrange: дуга в плоскости XZ (G18)
        double startX = 10, startY = 0, startZ = 0;
        double endX = 0, endY = 0, endZ = 10;
        var plane = GCodeRegistry.G18;
        bool isG2 = true;
        double? i = -10, j = null, k = 0;
        double? r = null;

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.True(result);
        Assert.NotNull(arc);
        Assert.Equal(18, arc.Plane);
        Assert.Equal(0, arc.CenterU, 5); // X
        Assert.Equal(0, arc.CenterV, 5); // Z
    }

    [Fact]
    public void TryComputeArc_G19_Plane_YZ_CenterFromIJK_ReturnsTrue()
    {
        // Arrange: дуга в плоскости YZ (G19)
        double startX = 0, startY = 10, startZ = 0;
        double endX = 0, endY = 0, endZ = 10;
        var plane = GCodeRegistry.G19;
        bool isG2 = false;
        double? i = null, j = -10, k = 0;
        double? r = null;

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.True(result);
        Assert.NotNull(arc);
        Assert.Equal(19, arc.Plane);
        Assert.Equal(0, arc.CenterU, 5); // Y
        Assert.Equal(0, arc.CenterV, 5); // Z
    }

    [Fact]
    public void TryComputeArc_WithR_MinorArc_ReturnsTrue()
    {
        // Arrange: дуга через радиус R (минорная, <= 180°)
        double startX = 10, startY = 0, startZ = 0;
        double endX = 0, endY = 10, endZ = 0;
        var plane = GCodeRegistry.G17;
        bool isG2 = true;
        double? i = null, j = null, k = null;
        double? r = 10; // R > 0 означает минорную дугу

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.True(result);
        Assert.NotNull(arc);
        Assert.Equal(10, arc.Radius, 5);
    }

    [Fact]
    public void TryComputeArc_WithR_Negative_MajorArc_ReturnsTrue()
    {
        // Arrange: дуга через радиус R (мажорная, > 180°)
        double startX = 10, startY = 0, startZ = 0;
        double endX = 0, endY = 10, endZ = 0;
        var plane = GCodeRegistry.G17;
        bool isG2 = true;
        double? i = null, j = null, k = null;
        double? r = -10; // R < 0 означает мажорную дугу

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.True(result);
        Assert.NotNull(arc);
        Assert.Equal(10, arc.Radius, 5);
        Assert.True(Math.Abs(arc.SweepAngleRad) > Math.PI); // > 180°
    }

    [Fact]
    public void TryComputeArc_NoIJK_NoR_ReturnsFalse()
    {
        // Arrange: нет данных о центре или радиусе
        double startX = 10, startY = 0, startZ = 0;
        double endX = 0, endY = 10, endZ = 0;
        var plane = GCodeRegistry.G17;
        bool isG2 = true;
        double? i = null, j = null, k = null;
        double? r = null;

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.False(result);
        Assert.Null(arc);
    }

    [Fact]
    public void TryComputeArc_InvalidPlane_ReturnsFalse()
    {
        // Arrange: неверная плоскость
        double startX = 10, startY = 0, startZ = 0;
        double endX = 0, endY = 10, endZ = 0;
        var plane = new GCodeTemplate { Letter = "G", Number = 99 };
        bool isG2 = true;
        double? i = -10, j = 0, k = null;
        double? r = null;

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.False(result);
        Assert.Null(arc);
    }

    [Fact]
    public void TryComputeArc_FullCircle_StartEqualsEnd_IJK_ReturnsTrue()
    {
        // Arrange: полная окружность (начало = конец)
        double startX = 10, startY = 0, startZ = 0;
        double endX = 10, endY = 0, endZ = 0;
        var plane = GCodeRegistry.G17;
        bool isG2 = false;
        double? i = -10, j = 0, k = null;
        double? r = null;

        // Act
        var result = ArcCalculator.TryComputeArc(
            startX, startY, startZ,
            endX, endY, endZ,
            plane, isG2, i, j, k, r,
            out var arc);

        // Assert
        Assert.True(result);
        Assert.NotNull(arc);
        Assert.Equal(2 * Math.PI, Math.Abs(arc.SweepAngleRad), 4);
    }
}
