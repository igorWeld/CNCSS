namespace CNCSS.Machine.Model
{
    [Flags]
    public enum MachineProgramAxisMask
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 4,
        Xy = X | Y,
        All = X | Y | Z
    }
}
