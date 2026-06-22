namespace CNCSS.Data.Tools
{
    /// <summary>Сверло с углом при вершине (для параметров в UI и отображения).</summary>
    public class Drill : ToolBase
    {
        public override ToolType Type => ToolType.Drill;
        
        /// <summary>
        /// Угол при вершине сверла (обычно 118 или 135 градусов)
        /// </summary>
        public double PointAngle { get; set; } = 120.0;

        public Drill(double diameter, double fluteLength, double overallLength, double pointAngle = 120.0) 
            : base(diameter, fluteLength, overallLength)
        {
            PointAngle = pointAngle;
            Name = $"Drill D{diameter}";
        }
    }
}
