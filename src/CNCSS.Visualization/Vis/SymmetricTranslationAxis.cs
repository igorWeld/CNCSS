namespace CNCSS.Vis
{
    [Flags]
    public enum SymmetricTranslationAxis
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 4,
        Xy = X | Y
    }
}
