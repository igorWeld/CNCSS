using System.Windows.Media;
using CNCSS.Data;

namespace CNCSS.UI
{
    /// <summary>Конвертация между <see cref="StockColor"/> (Data) и WPF <see cref="Color"/>.</summary>
    public static class StockColorExtensions
    {
        /// <summary>WPF Color → StockColor.</summary>
        public static StockColor ToStockColor(this Color color) => new(color.A, color.R, color.G, color.B);

        /// <summary>StockColor → WPF Color.</summary>
        public static Color ToWpfColor(this StockColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);
    }
}
