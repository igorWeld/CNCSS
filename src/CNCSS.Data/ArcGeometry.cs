namespace CNCSS.Data
{
    /// <summary>
    /// Результат расчёта дуги G2/G3 в текущей плоскости (G17/G18/G19).
    /// Углы в радианах; положительный обход — против часовой стрелки в UV (как G3 в G17 при виде с +Z).
    /// </summary>
    public sealed class ArcGeometry
    {
        public int Plane { get; init; }

        public double Radius { get; init; }

        /// <summary>Координаты центра в плоскости: для G17 (X,Y), G18 (X,Z), G19 (Y,Z).</summary>
        public double CenterU { get; init; }

        public double CenterV { get; init; }

        public double StartAngleRad { get; init; }

        /// <summary>Размах дуги; знак: положительный — как G3 (CCW в плоскости), отрицательный — как G2 (CW).</summary>
        public double SweepAngleRad { get; init; }

        public bool IsClockwise { get; init; }

        public double StartX { get; init; }
        public double StartY { get; init; }
        public double StartZ { get; init; }

        public double EndX { get; init; }
        public double EndY { get; init; }
        public double EndZ { get; init; }

        public override string ToString()
        {
            string planeName = Plane switch
            {
                17 => "XY",
                18 => "XZ",
                19 => "YZ",
                _ => $"G{Plane}"
            };
            return $"Arc {planeName} R={Radius:F4} ∠={SweepAngleRad * 180 / Math.PI:F2}° C=({CenterU:F4},{CenterV:F4})";
        }
    }
}
