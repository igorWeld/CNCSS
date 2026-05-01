using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CNCSS.UI.OperatorStation
{
    public partial class OperatorStationPanel : UserControl
    {
        private bool _suppressToggle;
        private bool _suppressWorkOvEvent;
        private bool _suppressSpindleOvEvent;
        private bool _suppressSbM1;
        private bool _suppressIncRadio;
        private double _jogMm = 0.1;
        private bool _rapidJog;
        private string _selectedAxis = "X";

        public event EventHandler<string>? ModeSelected;
        public event EventHandler<(string Axis, double Delta)>? JogAxisDelta;
        public event EventHandler? CycleStart;
        public event EventHandler? FeedHold;
        public event EventHandler? CycleStopRequested;
        public event EventHandler? EmergencyResetRequested;
        public event EventHandler<double>? FeedWorkOverridePercentChanged;
        public event EventHandler<double>? SpindleOverridePercentChanged;
        public event EventHandler<bool>? SingleBlockChanged;
        public event EventHandler<bool>? OptionalStopChanged;

        public OperatorStationPanel()
        {
            InitializeComponent();

            WorkOvSlider.ValueChanged += WorkOv_ValueChanged;
            SpindleOvSlider.ValueChanged += SpindleOv_ValueChanged;

            _suppressIncRadio = true;
            Inc10.IsChecked = true;
            _suppressIncRadio = false;
            _jogMm = 0.1;
            UpdateOvTexts();
            UpdateDialNeedles();
            SyncG0ToggleVisual();
        }

        public void SyncMachiningToggles(bool singleBlock, bool optionalStop)
        {
            _suppressSbM1 = true;
            try
            {
                BtnSb.IsChecked = singleBlock;
                BtnM1.IsChecked = optionalStop;
            }
            finally
            {
                _suppressSbM1 = false;
            }
        }

        public void HighlightMode(string controllerModeFromMain)
        {
            string u = controllerModeFromMain.Trim().ToUpperInvariant();
            _suppressToggle = true;

            try
            {
                switch (u)
                {
                    case "EDIT":
                        ModeEdit.IsChecked = true;
                        break;
                    case "MDI":
                        ModeMdi.IsChecked = true;
                        break;
                    case "ZERO_RETURN":
                    case "ZRN":
                        ModeRef.IsChecked = true;
                        break;
                    case "JOG":
                        ModeJog.IsChecked = true;
                        break;
                    case "HANDLE":
                    case "HNDL":
                        ModeHnd.IsChecked = true;
                        break;
                    default:
                        ModeAuto.IsChecked = true;
                        break;
                }
            }
            finally
            {
                _suppressToggle = false;
            }
        }

        public void SyncWorkOverrideSlider(double percent)
        {
            if (WorkOvSlider == null || FeedOvText == null)
            {
                return;
            }

            double v = Math.Clamp(percent, WorkOvSlider.Minimum, WorkOvSlider.Maximum);
            _suppressWorkOvEvent = true;
            try
            {
                if (Math.Abs(WorkOvSlider.Value - v) > 0.01)
                {
                    WorkOvSlider.Value = v;
                }

                UpdateOvTexts();
                UpdateDialNeedles();
            }
            finally
            {
                _suppressWorkOvEvent = false;
            }
        }

        public void SyncSpindleOverrideSlider(double percent)
        {
            if (SpindleOvSlider == null || SpindleOvText == null)
            {
                return;
            }

            double v = Math.Clamp(percent, SpindleOvSlider.Minimum, SpindleOvSlider.Maximum);
            _suppressSpindleOvEvent = true;
            try
            {
                if (Math.Abs(SpindleOvSlider.Value - v) > 0.01)
                {
                    SpindleOvSlider.Value = v;
                }

                UpdateOvTexts();
                UpdateDialNeedles();
            }
            finally
            {
                _suppressSpindleOvEvent = false;
            }
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressToggle || sender is not RadioButton rb || rb.Tag is not string tag)
            {
                return;
            }

            string mode = tag.ToUpperInvariant() switch
            {
                "AUTO" => "MEM",
                "EDIT" => "EDIT",
                "MDI" => "MDI",
                "REF" => "ZERO_RETURN",
                "JOG" => "JOG",
                "INC" => "JOG",
                "HND" => "HANDLE",
                _ => "MEM",
            };

            ModeSelected?.Invoke(this, mode);
        }

        private void MachiningToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressSbM1)
            {
                return;
            }

            if (ReferenceEquals(sender, BtnSb))
            {
                SingleBlockChanged?.Invoke(this, BtnSb.IsChecked == true);
            }
            else if (ReferenceEquals(sender, BtnM1))
            {
                OptionalStopChanged?.Invoke(this, BtnM1.IsChecked == true);
            }
        }

        private void G0Rapid_Changed(object sender, RoutedEventArgs e)
        {
            _rapidJog = BtnG0Rapid.IsChecked == true;
            SyncG0ToggleVisual();
        }

        private void SyncG0ToggleVisual()
        {
            // Шаблон OpcToggleLed уже подсвечивает LED; при необходимости усиливаем фон программно
            if (_rapidJog)
            {
                BtnG0Rapid.FontWeight = FontWeights.Bold;
            }
            else
            {
                BtnG0Rapid.FontWeight = FontWeights.SemiBold;
            }
        }

        private void IncRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressIncRadio || sender is not RadioButton rb || rb.Tag is not string tag)
            {
                return;
            }

            _jogMm = tag switch
            {
                "µ1" => 0.001,
                "µ10" => 0.01,
                "µ100" => 0.1,
                "µ1000" => 1.0,
                _ => 0.1,
            };
        }

        private void AxisRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string axis)
            {
                _selectedAxis = axis;
            }
        }

        private void JogMinus_Click(object sender, RoutedEventArgs e) => PulseJog(-1);

        private void JogPlus_Click(object sender, RoutedEventArgs e) => PulseJog(+1);

        private void PulseJog(int dir)
        {
            double step = _jogMm * (_rapidJog ? 10 : 1) * dir;
            JogAxisDelta?.Invoke(this, (_selectedAxis, step));
        }

        private void CycleStart_Click(object sender, RoutedEventArgs e) => CycleStart?.Invoke(this, EventArgs.Empty);

        private void FeedHold_Click(object sender, RoutedEventArgs e) => FeedHold?.Invoke(this, EventArgs.Empty);

        private void CycleStop_Click(object sender, RoutedEventArgs e) => CycleStopRequested?.Invoke(this, EventArgs.Empty);

        private void EStop_Click(object sender, RoutedEventArgs e) => EmergencyResetRequested?.Invoke(this, EventArgs.Empty);

        private void SpinCw_Click(object sender, RoutedEventArgs e) { }

        private void SpinStop_Click(object sender, RoutedEventArgs e) { }

        private void SpinCcw_Click(object sender, RoutedEventArgs e) { }

        private void WorkOv_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WorkOvSlider == null)
            {
                return;
            }

            UpdateOvTexts();
            UpdateDialNeedles();
            if (_suppressWorkOvEvent)
            {
                return;
            }

            FeedWorkOverridePercentChanged?.Invoke(this, WorkOvSlider.Value);
        }

        private void SpindleOv_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpindleOvSlider == null)
            {
                return;
            }

            UpdateOvTexts();
            UpdateDialNeedles();
            if (_suppressSpindleOvEvent)
            {
                return;
            }

            SpindleOverridePercentChanged?.Invoke(this, SpindleOvSlider.Value);
        }

        private void UpdateOvTexts()
        {
            if (FeedOvText == null || SpindleOvText == null || WorkOvSlider == null || SpindleOvSlider == null)
            {
                return;
            }

            FeedOvText.Text = $"{(int)Math.Round(WorkOvSlider.Value)}%";
            SpindleOvText.Text = $"{(int)Math.Round(SpindleOvSlider.Value)}%";
        }

        private void UpdateDialNeedles()
        {
            if (WorkOvNeedleRotate == null || SpindleOvNeedleRotate == null || WorkOvSlider == null || SpindleOvSlider == null)
            {
                return;
            }

            WorkOvNeedleRotate.Angle = PercentToDialAngle(WorkOvSlider.Value, WorkOvSlider.Minimum, WorkOvSlider.Maximum);
            SpindleOvNeedleRotate.Angle = PercentToDialAngle(SpindleOvSlider.Value, SpindleOvSlider.Minimum, SpindleOvSlider.Maximum);
        }

        private static double PercentToDialAngle(double value, double min, double max)
        {
            if (Math.Abs(max - min) < 0.0001)
            {
                return -135;
            }

            double t = (value - min) / (max - min);
            return -135 + t * 270;
        }
    }
}
