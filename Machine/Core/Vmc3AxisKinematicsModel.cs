namespace CNCSS.Machine.Core
{
    /// <summary>Travel envelope and simple validation for a vertical 3-axis mill.</summary>
    public sealed class Vmc3AxisKinematicsModel
    {
        public static Vmc3AxisKinematicsModel Default { get; } = new(
            minX: -300.0,
            maxX: 300.0,
            minY: -200.0,
            maxY: 200.0,
            minZ: -500.0,
            maxZ: 0.0);

        public Vmc3AxisKinematicsModel(double minX, double maxX, double minY, double maxY, double minZ, double maxZ)
        {
            X = new AxisTravelLimits("X", minX, maxX);
            Y = new AxisTravelLimits("Y", minY, maxY);
            Z = new AxisTravelLimits("Z", minZ, maxZ);
        }

        public AxisTravelLimits X { get; }
        public AxisTravelLimits Y { get; }
        public AxisTravelLimits Z { get; }

        public bool IsWithinLimits(string axis, double requested, out AxisTravelLimits limits)
        {
            limits = axis.ToUpperInvariant() switch
            {
                "X" => X,
                "Y" => Y,
                "Z" => Z,
                _ => throw new ArgumentOutOfRangeException(nameof(axis), $"Unknown axis: {axis}")
            };

            return requested >= limits.Min && requested <= limits.Max;
        }
    }

    public readonly record struct AxisTravelLimits(string Axis, double Min, double Max);
}
