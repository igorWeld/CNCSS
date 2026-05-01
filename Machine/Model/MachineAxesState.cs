namespace CNCSS.Machine.Model
{
    public sealed class MachineAxesState
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public bool IsRunning { get; set; }
        public int? ToolNumber { get; set; }
        public double FeedRate { get; set; }
        public double SpindleSpeed { get; set; }
        public bool IsSpindleOn { get; set; }
        public bool IsSpindleCW { get; set; }
        public bool IsCoolantOn { get; set; }
    }
}
