namespace CNCSS.Data
{
    /// <summary>Форма заготовки в конструкторе и при построении воксельной маски.</summary>
    public enum StockShapeType
    {
        Rectangular,
        Hexagonal,
        Round,
        Tube
    }

    /// <summary>
    /// Параметры заготовки из конструктора.
    /// Прямоугольник: Param1×Param2×Param3; шестигранник/круг: Param1 (поперечный), Param2 (Z);
    /// труба: Param1 — наружный Ø, Param2 — внутренний Ø, Param3 — высота.
    /// </summary>
    public sealed record StockConstructorConfig(
        StockShapeType ShapeType,
        double Param1Mm,
        double Param2Mm,
        double Param3Mm,
        double CenterXMcsMm,
        double CenterYMcsMm,
        double L2ResolutionMm,
        string MaterialName,
        StockColor Color);
}
