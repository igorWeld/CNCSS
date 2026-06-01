namespace CNCSS.Machine.Model
{
    /// <summary>How program axes map to moving nodes.</summary>
    public enum MachineKinematicsScheme
    {
        /// <summary>Table moves in X/Y; spindle moves in Z (typical VMC).</summary>
        TableXySpindleZ = 0
    }
}
