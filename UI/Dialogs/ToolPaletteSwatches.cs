using System.Linq;
using System.Windows.Media;
using WpfColors = System.Windows.Media.Colors;

namespace CNCSS.UI.Dialogs
{
    /// <summary>Фиксированная палитра в окне инструментов (8 образцов); остальные цвета — через «Другой цвет…».</summary>
    public static class ToolPaletteSwatches
    {
        /// <summary>Основные быстрые цвета в таблице инструментов.</summary>
        public static IReadOnlyList<Color> Swatches { get; } =
        [
            WpfColors.Goldenrod,
            WpfColors.SteelBlue,
            WpfColors.IndianRed,
            WpfColors.LimeGreen,
            WpfColors.DarkOrange,
            WpfColors.MediumPurple,
            WpfColors.DimGray,
            WpfColors.White
        ];

        /// <summary>
        /// Случайный насыщенный цвет; по возможности отличается от уже заданных (евклидово расстояние в RGB).
        /// </summary>
        public static Color NextRandomDistinctFluteColor(IEnumerable<Color> existingColors)
        {
            var existing = existingColors as IList<Color> ?? existingColors.ToList();
            const int minDistSq = 40 * 40; // ~отличаемые оттенки
            const int maxAttempts = 64;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Color c = RandomVibrantRgb();
                bool ok = true;
                for (int i = 0; i < existing.Count; i++)
                {
                    if (ColorDistanceSq(c, existing[i]) < minDistSq)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    return c;
                }
            }

            return RandomVibrantRgb();
        }

        private static Color RandomVibrantRgb()
        {
            double h = Random.Shared.NextDouble() * 360.0;
            double s = 0.55 + Random.Shared.NextDouble() * 0.45;
            double v = 0.50 + Random.Shared.NextDouble() * 0.45;
            return FromHsv(h, s, v);
        }

        private static int ColorDistanceSq(Color a, Color b)
        {
            int dr = a.R - b.R;
            int dg = a.G - b.G;
            int db = a.B - b.B;
            return dr * dr + dg * dg + db * db;
        }

        private static Color FromHsv(double h, double S, double V)
        {
            while (h < 0)
            {
                h += 360;
            }

            while (h >= 360)
            {
                h -= 360;
            }

            double C = V * S;
            double hp = h / 60.0;
            double X = C * (1 - Math.Abs((hp % 2) - 1));
            double m = V - C;
            double r1 = 0, g1 = 0, b1 = 0;
            if (hp < 1)
            {
                r1 = C;
                g1 = X;
            }
            else if (hp < 2)
            {
                r1 = X;
                g1 = C;
            }
            else if (hp < 3)
            {
                g1 = C;
                b1 = X;
            }
            else if (hp < 4)
            {
                g1 = X;
                b1 = C;
            }
            else if (hp < 5)
            {
                r1 = X;
                b1 = C;
            }
            else
            {
                r1 = C;
                b1 = X;
            }

            byte R = (byte)Math.Clamp((int)((r1 + m) * 255), 0, 255);
            byte G = (byte)Math.Clamp((int)((g1 + m) * 255), 0, 255);
            byte B = (byte)Math.Clamp((int)((b1 + m) * 255), 0, 255);
            return Color.FromRgb(R, G, B);
        }
    }
}
