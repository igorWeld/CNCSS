namespace CNCSS.Data.Tools
{
    public class Drill : ToolBase
    {
        public override ToolType Type => ToolType.Drill;
        
        /// <summary>
        /// Угол при вершине сверла (обычно 118 или 135 градусов)
        /// </summary>
        public double PointAngle { get; set; } = 118.0;

        public Drill(double diameter, double fluteLength, double overallLength, double pointAngle = 118.0) 
            : base(diameter, fluteLength, overallLength)
        {
            PointAngle = pointAngle;
            Name = $"Drill D{diameter}";
        }
    }
}
