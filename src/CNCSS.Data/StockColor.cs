namespace CNCSS.Data
{
    /// <summary>Цвет заготовки без зависимости от WPF (ARGB).</summary>
    public readonly record struct StockColor(byte A, byte R, byte G, byte B)
    {
        /// <summary>Серый по умолчанию (как LightGray).</summary>
        public static StockColor LightGray => new(255, 211, 211, 211);
    }
}
