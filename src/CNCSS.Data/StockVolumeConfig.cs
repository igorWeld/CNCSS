namespace CNCSS.Data
{
    /// <summary>Ось-выровненные габариты заготовки (мм).</summary>
    public readonly record struct StockVolumeConfig(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);
}
