namespace CNCSS.Machine.Model
{
    /// <summary>Point in millimeters in a node-local coordinate system.</summary>
    public sealed class MachineGeometryPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public MachineGeometryPoint Clone() => new() { X = X, Y = Y, Z = Z };

        public static MachineGeometryPoint Zero => new();

        public bool IsNearlyZero() =>
            Math.Abs(X) < 1e-9 && Math.Abs(Y) < 1e-9 && Math.Abs(Z) < 1e-9;
    }
}
