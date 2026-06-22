namespace CNCSS.Data.Tools
{
    /// <summary>Концевая фреза (цилиндрическая).</summary>
    public class EndMill : ToolBase
    {
        public override ToolType Type => ToolType.EndMill;

        public EndMill(double diameter, double fluteLength, double overallLength) 
            : base(diameter, fluteLength, overallLength)
        {
            Name = $"End Mill D{diameter}";
        }
    }
}
