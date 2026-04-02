namespace CNCSS.Data.Tools
{
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
