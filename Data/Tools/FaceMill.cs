namespace CNCSS.Data.Tools
{
    public class FaceMill : ToolBase
    {
        public override ToolType Type => ToolType.FaceMill;

        public FaceMill(double diameter, double fluteLength, double overallLength) 
            : base(diameter, fluteLength, overallLength)
        {
            Name = $"Face Mill D{diameter}";
        }
    }
}
