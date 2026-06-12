namespace CNCSS.Machine.Model
{
    public enum StockWorkOriginXY
    {
        Center,
        CornerMinXMinY,
        CornerMaxXMinY,
        CornerMinXMaxY,
        CornerMaxXMaxY
    }

    public enum StockWorkOriginZ
    {
        Top,
        Bottom,
        Center
    }

    /// <summary>Точка нуля WCS в MCS по габаритам заготовки (меню «Симуляция → Ноль WCS…»).</summary>
    public static class StockWorkOrigin
    {
        public static MachineGeometryPoint Compute(
            WorkpiecePlacement.StockBounds bounds,
            StockWorkOriginXY xy,
            StockWorkOriginZ z)
        {
            double x = xy switch
            {
                StockWorkOriginXY.Center => (bounds.MinX + bounds.MaxX) * 0.5,
                StockWorkOriginXY.CornerMinXMinY => bounds.MinX,
                StockWorkOriginXY.CornerMaxXMinY => bounds.MaxX,
                StockWorkOriginXY.CornerMinXMaxY => bounds.MinX,
                StockWorkOriginXY.CornerMaxXMaxY => bounds.MaxX,
                _ => (bounds.MinX + bounds.MaxX) * 0.5
            };

            double y = xy switch
            {
                StockWorkOriginXY.Center => (bounds.MinY + bounds.MaxY) * 0.5,
                StockWorkOriginXY.CornerMinXMinY => bounds.MinY,
                StockWorkOriginXY.CornerMaxXMinY => bounds.MinY,
                StockWorkOriginXY.CornerMinXMaxY => bounds.MaxY,
                StockWorkOriginXY.CornerMaxXMaxY => bounds.MaxY,
                _ => (bounds.MinY + bounds.MaxY) * 0.5
            };

            double zCoord = z switch
            {
                StockWorkOriginZ.Top => bounds.MaxZ,
                StockWorkOriginZ.Center => (bounds.MinZ + bounds.MaxZ) * 0.5,
                _ => bounds.MinZ
            };
            return new MachineGeometryPoint { X = x, Y = y, Z = zCoord };
        }
    }
}
