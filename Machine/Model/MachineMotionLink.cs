namespace CNCSS.Machine.Model
{
    /// <summary>How an extra node inherits motion from the machine program.</summary>
    public enum MachineMotionLink
    {
        /// <summary>Only follows parent attachment; no program axis motion.</summary>
        Fixed = 0,

        /// <summary>Uses program axes selected in <see cref="MachineExtraNodeDefinition.MotionAxes"/> on the table chain.</summary>
        Table = 1,

        /// <summary>Uses program axes on the spindle chain.</summary>
        Spindle = 2
    }
}
