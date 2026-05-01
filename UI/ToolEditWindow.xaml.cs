using System;
using System.Globalization;
using System.Windows;
using CNCSS.UI.ViewModels;
using CNCSS.Data.Tools;
using System.Linq;

namespace CNCSS.UI
{
    /// <summary>Отдельное окно редактирования параметров одного инструмента (альтернатива строкам в окне TOOL DATA).</summary>
    public partial class ToolEditWindow : Window
    {
        private readonly ToolViewModel _tool;

        public ToolEditWindow(ToolViewModel tool)
        {
            InitializeComponent();
            _tool = tool;

            TypeCombo.ItemsSource = Enum.GetValues(typeof(ToolType));
            TypeCombo.SelectedItem = _tool.SelectedType;

            NumberInput.Text = _tool.Number.ToString();
            DiameterInput.Text = _tool.Diameter.ToString(CultureInfo.InvariantCulture);
            FluteLengthInput.Text = _tool.FluteLength.ToString(CultureInfo.InvariantCulture);
            ShankDiameterInput.Text = _tool.ShankDiameter.ToString(CultureInfo.InvariantCulture);
            TotalLengthInput.Text = _tool.OverallLength.ToString(CultureInfo.InvariantCulture);
            FlutesInput.Text = _tool.Flutes.ToString();
            PointAngleInput.Text = _tool.PointAngle.ToString(CultureInfo.InvariantCulture);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(NumberInput.Text, out int n)) _tool.Number = n;
            if (TypeCombo.SelectedItem is ToolType type) _tool.SelectedType = type;
            if (double.TryParse(DiameterInput.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) _tool.Diameter = d;
            if (double.TryParse(FluteLengthInput.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double fl)) _tool.FluteLength = fl;
            if (double.TryParse(ShankDiameterInput.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double sd)) _tool.ShankDiameter = sd;
            if (double.TryParse(TotalLengthInput.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double tl)) _tool.OverallLength = tl;
            if (int.TryParse(FlutesInput.Text, out int f)) _tool.Flutes = f;
            if (double.TryParse(PointAngleInput.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double pa)) _tool.PointAngle = pa;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
