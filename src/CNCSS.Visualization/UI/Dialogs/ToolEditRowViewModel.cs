using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data.Tools;
using CNCSS.UI.Commands;
using CNCSS.UI.ViewModels;
using WpfColors = System.Windows.Media.Colors;

namespace CNCSS.UI.Dialogs
{
    /// <summary>
    /// Одна строка диалога TOOL DATA: текстовые поля, палитра цвета, превью <see cref="PreviewModel"/>.
    /// При «Применить» копирует проверенные значения в <see cref="Source"/>.
    /// </summary>
    public sealed class ToolEditRowViewModel : INotifyPropertyChanged
    {
        private string _numberText = "1";
        private string _diameterText = "10";
        private string _shankDiameterText = "10";
        private string _fluteLengthText = "30";
        private string _overallLengthText = "75";
        private string _flutesText = "2";
        private string _pointAngleText = "120";
        private Color _selectedFluteColor = WpfColors.Goldenrod;
        private ToolType _selectedType;

        private Model3DGroup _previewModel;

        public ToolEditRowViewModel(ToolViewModel source)
        {
            Source = source;
            _numberText = source.Number.ToString(CultureInfo.InvariantCulture);
            _diameterText = source.Diameter.ToString(CultureInfo.InvariantCulture);
            _shankDiameterText = source.ShankDiameter.ToString(CultureInfo.InvariantCulture);
            _fluteLengthText = source.FluteLength.ToString(CultureInfo.InvariantCulture);
            _overallLengthText = source.OverallLength.ToString(CultureInfo.InvariantCulture);
            _flutesText = source.Flutes.ToString(CultureInfo.InvariantCulture);
            _pointAngleText = source.PointAngle.ToString(CultureInfo.InvariantCulture);
            _selectedType = source.SelectedType;
            _selectedFluteColor = source.FluteColor;
            _previewModel = BuildPreviewModels();

            SetFluteColorCommand = new RelayCommand(o =>
            {
                if (o is Color c)
                {
                    SelectedFluteColor = c;
                }
            });

            PickCustomColorCommand = new RelayCommand(_ =>
            {
                if (ToolColorPickerInterop.TryPickColor(SelectedFluteColor, out Color picked))
                {
                    SelectedFluteColor = picked;
                }
            });
        }

        public ICommand SetFluteColorCommand { get; }

        public ICommand PickCustomColorCommand { get; }

        public ToolViewModel Source { get; }

        public Model3DGroup PreviewModel
        {
            get => _previewModel;
            private set
            {
                _previewModel = value;
                OnPropertyChanged();
            }
        }

        public string NumberText
        {
            get => _numberText;
            set { _numberText = value ?? string.Empty; OnPropertyChanged(); RebuildPreviewSafe(); }
        }

        public string DiameterText
        {
            get => _diameterText;
            set { _diameterText = value ?? string.Empty; OnPropertyChanged(); RebuildPreviewSafe(); }
        }

        public string ShankDiameterText
        {
            get => _shankDiameterText;
            set { _shankDiameterText = value ?? string.Empty; OnPropertyChanged(); RebuildPreviewSafe(); }
        }

        public string FluteLengthText
        {
            get => _fluteLengthText;
            set { _fluteLengthText = value ?? string.Empty; OnPropertyChanged(); RebuildPreviewSafe(); }
        }

        public string OverallLengthText
        {
            get => _overallLengthText;
            set { _overallLengthText = value ?? string.Empty; OnPropertyChanged(); RebuildPreviewSafe(); }
        }

        public string FlutesText
        {
            get => _flutesText;
            set { _flutesText = value ?? string.Empty; OnPropertyChanged(); RebuildPreviewSafe(); }
        }

        public string PointAngleText
        {
            get => _pointAngleText;
            set { _pointAngleText = value ?? string.Empty; OnPropertyChanged(); RebuildPreviewSafe(); }
        }

        public IEnumerable<ToolType> ToolKinds => Enum.GetValues(typeof(ToolType)).Cast<ToolType>();

        public ToolType SelectedType
        {
            get => _selectedType;
            set
            {
                var prev = _selectedType;
                _selectedType = value;
                OnPropertyChanged();
                if (value == ToolType.FaceMill && prev != ToolType.FaceMill)
                {
                    DiameterText = "30";
                    ShankDiameterText = "28";
                    FluteLengthText = "10";
                    OverallLengthText = "75";
                    FlutesText = "5";
                }

                OnPropertyChanged(nameof(IsDrillRow));
                RebuildPreviewSafe();
            }
        }

        public bool IsDrillRow => SelectedType == ToolType.Drill;

        public IReadOnlyList<Color> PaletteSwatches => ToolPaletteSwatches.Swatches;

        public Color SelectedFluteColor
        {
            get => _selectedFluteColor;
            set
            {
                if (value.Equals(_selectedFluteColor))
                {
                    return;
                }

                _selectedFluteColor = value;
                OnPropertyChanged();
                PreviewModel = BuildPreviewModels();
            }
        }

        public bool TryPeekToolNumber(out int toolNum, out string error)
        {
            if (!TryParsePositiveInt(NumberText, out toolNum))
            {
                error = "Номер инструмента — целое число от 1 (как номер после T в УП).";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryApply(out string error)
        {
            if (!TryParsePositiveInt(NumberText, out int toolNum))
            {
                error = "Номер инструмента — целое число от 1 (как номер после T в УП).";
                return false;
            }

            if (!TryInvariantDouble(DiameterText, out double diameter) || diameter <= 0)
            {
                error = "Укажите положительный диаметр.";
                return false;
            }

            if (!TryInvariantDouble(ShankDiameterText, out double shankDiameter) || shankDiameter <= 0)
            {
                error = "Укажите положительный диаметр державки.";
                return false;
            }

            if (!TryInvariantDouble(FluteLengthText, out double fluteLen) || fluteLen <= 0)
            {
                error = "Укажите положительную длину режущей части.";
                return false;
            }

            if (!TryInvariantDouble(OverallLengthText, out double overall) || overall <= 0)
            {
                error = "Укажите положительную общую длину.";
                return false;
            }

            if (overall + 1e-9 < fluteLen)
            {
                error = "Общая длина должна быть не меньше длины режущей части.";
                return false;
            }

            if (!TryParsePositiveInt(FlutesText, out int flutes))
            {
                error = "Число зубьев — целое число от 1.";
                return false;
            }

            double pointAngle = Source.PointAngle;
            if (SelectedType == ToolType.Drill)
            {
                if (!TryInvariantDouble(PointAngleText, out pointAngle) || pointAngle < 1.0 || pointAngle >= 179.5)
                {
                    error = "Угол при вершине для сверла — от 1° до 179°.";
                    return false;
                }
            }

            Source.SelectedType = SelectedType;
            Source.Number = toolNum;
            Source.Diameter = diameter;
            Source.ShankDiameter = shankDiameter;
            Source.FluteLength = fluteLen;
            Source.OverallLength = overall;
            Source.Flutes = flutes;
            Source.PointAngle = pointAngle;
            Source.FluteColor = SelectedFluteColor;

            error = string.Empty;
            return true;
        }

        private void RebuildPreviewSafe() => PreviewModel = BuildPreviewModels();

        private Model3DGroup BuildPreviewModels()
        {
            double dForPreview = PreviewOr(Source.Diameter, DiameterText);
            double flForPreview = PreviewOr(Source.FluteLength, FluteLengthText);
            double olForPreview = PreviewOr(Source.OverallLength, OverallLengthText);

            if (olForPreview < flForPreview)
            {
                olForPreview = flForPreview;
            }

            double fluteRadius = Math.Max(0.1, dForPreview / 2.0);
            double shankForPreview = PreviewOr(Source.ShankDiameter, ShankDiameterText);
            double shankRadius = Math.Max(0.1, shankForPreview / 2.0);
            Color flute = SelectedFluteColor;
            if (SelectedType == ToolType.Drill)
            {
                double pt = PreviewOr(Source.PointAngle, PointAngleText);
                pt = Math.Clamp(pt, 1.0, 179.0);
                return ToolPreviewGeometry.BuildDrillTool(fluteRadius, shankRadius, flForPreview, olForPreview, pt, flute, WpfColors.Gray);
            }

            return ToolPreviewGeometry.BuildCylinderTool(fluteRadius, shankRadius, flForPreview, olForPreview, flute, WpfColors.Gray);
        }

        private static double PreviewOr(double fallback, string text)
        {
            return TryInvariantDouble(text, out double v) && v > 0 ? v : fallback;
        }

        private static bool TryInvariantDouble(string? s, out double v)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                v = 0;
                return false;
            }

            return double.TryParse(
                s.Trim().Replace(',', '.'),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out v);
        }

        private static bool TryParsePositiveInt(string? s, out int v)
        {
            return int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= 1;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
