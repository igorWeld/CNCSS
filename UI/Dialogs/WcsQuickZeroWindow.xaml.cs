using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CNCSS.Machine.Model;

namespace CNCSS.UI.Dialogs
{
    public partial class WcsQuickZeroWindow : Window
    {
        public sealed record Result(int SystemNumber, double X, double Y, double Z);

        private readonly WorkpiecePlacement.StockBounds _bounds;

        public Result? DialogResultValue { get; private set; }

        public WcsQuickZeroWindow(WorkpiecePlacement.StockBounds bounds, int initialSystemNumber)
        {
            InitializeComponent();
            _bounds = bounds;

            int idx = Math.Clamp(initialSystemNumber - 54, 0, 5);
            WcsCombo.SelectedIndex = idx;
            UpdateWcsIndicator();
        }

        private void WcsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateWcsIndicator();
        }

        private void UpdateWcsIndicator()
        {
            if (WcsCombo.SelectedItem is ComboBoxItem { Content: string s } && !string.IsNullOrWhiteSpace(s))
            {
                WcsIndicatorText.Text = s;
            }
        }

        private static StockWorkOriginXY GetXy(RadioButton center, RadioButton maxMax, RadioButton maxMin, RadioButton minMax, RadioButton minMin)
        {
            if (maxMax.IsChecked == true) return StockWorkOriginXY.CornerMaxXMaxY;
            if (maxMin.IsChecked == true) return StockWorkOriginXY.CornerMaxXMinY;
            if (minMax.IsChecked == true) return StockWorkOriginXY.CornerMinXMaxY;
            if (minMin.IsChecked == true) return StockWorkOriginXY.CornerMinXMinY;
            return StockWorkOriginXY.Center;
        }

        private static StockWorkOriginZ GetZ(RadioButton top, RadioButton bottom, RadioButton center)
        {
            if (bottom.IsChecked == true) return StockWorkOriginZ.Bottom;
            if (center.IsChecked == true) return StockWorkOriginZ.Center;
            return StockWorkOriginZ.Top;
        }

        private int GetSystemNumber()
        {
            if (WcsCombo.SelectedItem is ComboBoxItem { Content: string s }
                && s.StartsWith("G", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(s[1..], out int n))
            {
                return n;
            }
            return 54;
        }

        private bool TryGetManual(out double x, out double y, out double z)
        {
            x = y = z = 0;
            var style = NumberStyles.Float;
            var c = CultureInfo.InvariantCulture;
            return double.TryParse(ManualX.Text, style, c, out x)
                   && double.TryParse(ManualY.Text, style, c, out y)
                   && double.TryParse(ManualZ.Text, style, c, out z);
        }

        private void Compute_Click(object sender, RoutedEventArgs e)
        {
            var xy = GetXy(XyCenter, XyMaxMax, XyMaxMin, XyMinMax, XyMinMin);
            var zSel = GetZ(ZTop, ZBottom, ZCenter);
            MachineGeometryPoint origin = StockWorkOrigin.Compute(_bounds, xy, zSel);

            ManualX.Text = origin.X.ToString("0.###", CultureInfo.InvariantCulture);
            ManualY.Text = origin.Y.ToString("0.###", CultureInfo.InvariantCulture);
            ManualZ.Text = origin.Z.ToString("0.###", CultureInfo.InvariantCulture);

            StatusText.Text =
                $"Точка на заготовке (СК стола): X={origin.X:0.###} Y={origin.Y:0.###} Z={origin.Z:0.###}. " +
                "При применении будет переведено в MCS.";
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetManual(out double x, out double y, out double z))
            {
                StatusText.Text = "Ошибка: неверный формат X/Y/Z (используйте точку).";
                return;
            }

            DialogResultValue = new Result(GetSystemNumber(), x, y, z);
            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

