using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.UI.Commands;
using CNCSS.UI.Dialogs;
using CNCSS.Machine.Model;
using CNCSS.Machine.Configuration;
using CNCSS.Vis;
using HelixToolkit.Wpf;

namespace CNCSS.UI.ViewModels
{
    /// <summary>ViewModel конструктора заготовки: форма, материал, цвет, превью на столе станка.</summary>
    public sealed class StockConstructorViewModel : BaseViewModel
    {
        private HelixViewport3D? _viewport;
        private readonly ModelVisual3D _previewRoot = new();
        private Model3D? _tableModel;
        private MachineDefinition? _machineDefinition;
        private Rect3D? _tableMeshBoundsLocal;
        private double _poseX;
        private double _poseY;
        private double _poseZ;
        private readonly bool _hasInitialConfig;

        public event EventHandler<bool>? RequestClose;

        public StockConstructorViewModel(StockConstructorConfig? initial = null)
        {
            _hasInitialConfig = initial != null;
            Materials = new ObservableCollection<string>(StockConstructorMaterials.All);
            ColorSwatches = new ObservableCollection<Color>
            {
                Colors.DimGray,
                Colors.LimeGreen,
                Colors.DarkOrange,
                Colors.MediumPurple
            };

            SelectedMaterial = initial?.MaterialName ?? StockConstructorMaterials.Default;
            SelectedColor = initial?.Color ?? ColorSwatches[0];

            VerificationResolution = initial?.VerificationResolutionMm ?? 0.10;

            if (initial != null)
            {
                ShapeType = initial.ShapeType;
                Param1Text = initial.Param1Mm.ToString(CultureInfo.InvariantCulture);
                Param2Text = initial.Param2Mm.ToString(CultureInfo.InvariantCulture);
                Param3Text = initial.Param3Mm.ToString(CultureInfo.InvariantCulture);
                CenterXText = initial.CenterXMcsMm.ToString(CultureInfo.InvariantCulture);
                CenterYText = initial.CenterYMcsMm.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                ShapeType = StockShapeType.Rectangular;
                Param1Text = "100";
                Param2Text = "100";
                Param3Text = "50";
                CenterXText = "0";
                CenterYText = "0";
            }

            ApplyCommand = new RelayCommand(_ => Apply(), _ => CanApply);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(this, false));

            SetColorCommand = new RelayCommand(o =>
            {
                if (o is Color c)
                {
                    SelectedColor = c;
                }
            });

            PickCustomColorCommand = new RelayCommand(_ =>
            {
                if (ToolColorPickerInterop.TryPickColor(SelectedColor, out Color picked))
                {
                    SelectedColor = picked;
                }
            });

            RecomputeLabels();
            RebuildPreview();
        }

        public ObservableCollection<string> Materials { get; }

        public ObservableCollection<Color> ColorSwatches { get; }

        public RelayCommand ApplyCommand { get; }

        public RelayCommand CancelCommand { get; }

        public RelayCommand SetColorCommand { get; }

        public RelayCommand PickCustomColorCommand { get; }

        private StockShapeType _shapeType;
        public StockShapeType ShapeType
        {
            get => _shapeType;
            set
            {
                if (!SetProperty(ref _shapeType, value))
                {
                    return;
                }
                RecomputeLabels();
                ApplyDefaultsForShapeIfNeeded(value);
                ValidateAndPreview();
            }
        }

        private void ApplyDefaultsForShapeIfNeeded(StockShapeType shapeType)
        {
            if (_hasInitialConfig)
            {
                return;
            }

            // Труба по умолчанию: наружный Ø 100, внутренний 30, высота 50 мм.
            if (shapeType == StockShapeType.Tube)
            {
                if (Param1Text.Trim() == "100" && Param2Text.Trim() == "100" && Param3Text.Trim() == "50")
                {
                    Param1Text = "100";
                    Param2Text = "30";
                    Param3Text = "50";
                }
            }
        }

        public bool IsRectSelected
        {
            get => ShapeType == StockShapeType.Rectangular;
            set { if (value) ShapeType = StockShapeType.Rectangular; }
        }

        public bool IsHexSelected
        {
            get => ShapeType == StockShapeType.Hexagonal;
            set { if (value) ShapeType = StockShapeType.Hexagonal; }
        }

        public bool IsRoundSelected
        {
            get => ShapeType == StockShapeType.Round;
            set { if (value) ShapeType = StockShapeType.Round; }
        }

        public bool IsTubeSelected
        {
            get => ShapeType == StockShapeType.Tube;
            set { if (value) ShapeType = StockShapeType.Tube; }
        }

        private string _shapeHeader = "Прямоугольный прокат";
        public string ShapeHeader { get => _shapeHeader; private set => SetProperty(ref _shapeHeader, value); }

        private string _param1Label = "Длина";
        public string Param1Label { get => _param1Label; private set => SetProperty(ref _param1Label, value); }

        private string _param2Label = "Ширина";
        public string Param2Label { get => _param2Label; private set => SetProperty(ref _param2Label, value); }

        private string _param3Label = "Толщина";
        public string Param3Label { get => _param3Label; private set => SetProperty(ref _param3Label, value); }

        private bool _hasParam2 = true;
        public bool HasParam2 { get => _hasParam2; private set => SetProperty(ref _hasParam2, value); }

        private bool _hasParam3 = true;
        public bool HasParam3 { get => _hasParam3; private set => SetProperty(ref _hasParam3, value); }

        private string _param1Text = "";
        public string Param1Text { get => _param1Text; set { if (SetProperty(ref _param1Text, value)) ValidateAndPreview(); } }

        private string _param2Text = "";
        public string Param2Text { get => _param2Text; set { if (SetProperty(ref _param2Text, value)) ValidateAndPreview(); } }

        private string _param3Text = "";
        public string Param3Text { get => _param3Text; set { if (SetProperty(ref _param3Text, value)) ValidateAndPreview(); } }

        private string _centerXText = "";
        public string CenterXText { get => _centerXText; set { if (SetProperty(ref _centerXText, value)) ValidateAndPreview(); } }

        private string _centerYText = "";
        public string CenterYText { get => _centerYText; set { if (SetProperty(ref _centerYText, value)) ValidateAndPreview(); } }

        private double _verificationResolution;
        public double VerificationResolution
        {
            get => _verificationResolution;
            set
            {
                value = Math.Clamp(value, 0.05, 1.0);
                if (Math.Abs(_verificationResolution - value) < 1e-12)
                {
                    return;
                }
                _verificationResolution = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VerificationResolutionText));
            }
        }

        public string VerificationResolutionText => _verificationResolution.ToString("0.00", CultureInfo.InvariantCulture);

        private string _selectedMaterial = StockConstructorMaterials.Default;
        public string SelectedMaterial
        {
            get => _selectedMaterial;
            set => SetProperty(ref _selectedMaterial, value);
        }

        private Color _selectedColor;
        public Color SelectedColor
        {
            get => _selectedColor;
            set
            {
                if (SetProperty(ref _selectedColor, value))
                {
                    ValidateAndPreview();
                }
            }
        }

        private string _validationSummary = "";
        public string ValidationSummary { get => _validationSummary; private set => SetProperty(ref _validationSummary, value); }

        private bool _canApply;
        public bool CanApply
        {
            get => _canApply;
            private set
            {
                if (SetProperty(ref _canApply, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public StockConstructorConfig? Result { get; private set; }

        public void AttachViewport(HelixViewport3D viewport)
        {
            _viewport = viewport;
            if (!_viewport.Children.Contains(_previewRoot))
            {
                _viewport.Children.Add(_previewRoot);
            }
            RebuildPreview();
        }

        public void SetTableModel(Model3D? tableModel)
        {
            _tableModel = tableModel;
            ValidateAndPreview();
        }

        public void SetPlacementContext(MachineDefinition definition, Rect3D? tableMeshBoundsLocal, double machineX, double machineY, double machineZ)
        {
            _machineDefinition = definition;
            _tableMeshBoundsLocal = tableMeshBoundsLocal;
            _poseX = machineX;
            _poseY = machineY;
            _poseZ = machineZ;
            ValidateAndPreview();
        }

        private void RecomputeLabels()
        {
            switch (ShapeType)
            {
                case StockShapeType.Rectangular:
                    ShapeHeader = "Прямоугольный прокат";
                    Param1Label = "Длина";
                    Param2Label = "Ширина";
                    Param3Label = "Толщина";
                    HasParam2 = true;
                    HasParam3 = true;
                    break;
                case StockShapeType.Hexagonal:
                    ShapeHeader = "Шестигранный прокат";
                    Param1Label = "Размер под ключ";
                    Param2Label = "Толщина";
                    Param3Label = "";
                    HasParam2 = true;
                    HasParam3 = false;
                    break;
                case StockShapeType.Round:
                    ShapeHeader = "Круглый прокат";
                    Param1Label = "Диаметр";
                    Param2Label = "Толщина";
                    Param3Label = "";
                    HasParam2 = true;
                    HasParam3 = false;
                    break;
                case StockShapeType.Tube:
                    ShapeHeader = "Трубный прокат";
                    Param1Label = "Диаметр наружный";
                    Param2Label = "Диаметр отверстия";
                    Param3Label = "Толщина";
                    HasParam2 = true;
                    HasParam3 = true;
                    break;
            }
        }

        private static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private void ValidateAndPreview()
        {
            Validate(out StockConstructorConfig? config, out string summary);
            ValidationSummary = summary;
            CanApply = config != null;
            if (config != null)
            {
                RebuildPreview(config);
            }
            else
            {
                RebuildPreview(null);
            }
        }

        private void Validate(out StockConstructorConfig? config, out string summary)
        {
            config = null;
            summary = string.Empty;

            if (!TryParseDouble(Param1Text, out double p1))
            {
                summary = "Введите числовые значения параметров.";
                return;
            }

            double p2 = 0;
            if (HasParam2 && !TryParseDouble(Param2Text, out p2))
            {
                summary = "Введите числовые значения параметров.";
                return;
            }

            double p3 = 0;
            if (HasParam3 && !TryParseDouble(Param3Text, out p3))
            {
                summary = "Введите числовые значения параметров.";
                return;
            }

            if (!TryParseDouble(CenterXText, out double cx) || !TryParseDouble(CenterYText, out double cy))
            {
                summary = "Введите числовые значения параметров.";
                return;
            }

            double param2 = HasParam2 ? p2 : 0;
            double param3 = HasParam3 ? p3 : 0;

            if (p1 <= 0 || (HasParam2 && param2 <= 0) || (HasParam3 && param3 <= 0))
            {
                summary = "Размеры должны быть больше нуля.";
                return;
            }

            if (ShapeType == StockShapeType.Tube)
            {
                if (param2 <= 0 || param2 >= p1)
                {
                    summary = "Диаметр отверстия должен быть больше 0 и меньше наружного диаметра.";
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(SelectedMaterial))
            {
                summary = "Выберите материал.";
                return;
            }

            Color color = SelectedColor;

            config = new StockConstructorConfig(
                ShapeType,
                p1,
                param2,
                param3,
                cx,
                cy,
                VerificationResolution,
                SelectedMaterial,
                color);
        }

        private void Apply()
        {
            Validate(out StockConstructorConfig? config, out string summary);
            ValidationSummary = summary;
            if (config == null)
            {
                return;
            }

            Result = config;
            RequestClose?.Invoke(this, true);
        }

        private void RebuildPreview() => ValidateAndPreview();

        private void RebuildPreview(StockConstructorConfig? config)
        {
            if (_viewport == null)
            {
                return;
            }

            var group = new Model3DGroup();
            group.Children.Add(StockConstructorPreviewBuilder.BuildTableModel(_tableModel));
            if (config != null)
            {
                if (_machineDefinition != null &&
                    StockConstructorPreviewBuilder.TryBuildStockPreviewModelOnMount(
                        _machineDefinition,
                        _tableMeshBoundsLocal,
                        _poseX,
                        _poseY,
                        _poseZ,
                        config,
                        out Model3D? stockModel))
                {
                    group.Children.Add(stockModel);
                }
                else
                {
                    group.Children.Add(StockConstructorPreviewBuilder.BuildStockPreviewModel(config));
                }
            }

            group.Freeze();
            _previewRoot.Content = group;
            _viewport.ZoomExtents(250);
        }
    }

    /// <summary>Справочник материалов заготовки для комбобокса конструктора (ГОСТ).</summary>
    internal static class StockConstructorMaterials
    {
        public const string Default = "Д16Т ГОСТ 4784-2019";

        public static readonly string[] All =
        [
            // Алюминиевые сплавы
            "АМг2 ГОСТ 4784-97",
            "АД0 ГОСТ 4784-2019",
            "Д16Т ГОСТ 4784-2019",
            "АМг6 ГОСТ 4784-2019",
            "В95 ГОСТ 4784-2019",
            "АК5М (АЛ5) ГОСТ 1583-93",

            // Стали углеродистые (конструкционные)
            "Сталь 20 ГОСТ 1050-88",
            "Сталь 45 ГОСТ 1050-88",
            "Сталь 65Г ГОСТ 14959-79",

            // Стали легированные (конструкционные)
            "Сталь 15Х ГОСТ 4543-71",
            "Сталь 40Х ГОСТ 4543-71",
            "Сталь 30ХГСА ГОСТ 4543-71",

            // Стали нержавеющие (коррозионно-стойкие)
            "Сталь 12Х18Н9Т ГОСТ 5632-2014",
            "Сталь 08Х18Н10 ГОСТ 5632-2014",
            "Сталь 10Х17Н13М2Т ГОСТ 5632-2014",
            "Сталь 08Х22Н6Т ГОСТ 5632-2014",
            "Сталь 12Х13 ГОСТ 5632-2014",
            "Сталь 20Х13 ГОСТ 5632-2014",
            "Сталь 30Х13 ГОСТ 5632-2014",
            "Сталь 40Х13 ГОСТ 5632-2014",
            "Сталь 12Х18Н9 ГОСТ 5632-2014",

            // Чугуны
            "СЧ20 ГОСТ 1412-85",

            // Латуни и бронзы
            "ЛС59-1 ГОСТ 15527-2004",
            "Л63 ГОСТ 15527-2004",
            "БрАЖ9-4 ГОСТ 18175-78",
            "БрОЦС5-5-5 ГОСТ 613-79",

            // Пластики и композиты
            "Капролон (ПА 6) ГОСТ 11262-80",
            "Фторопласт-4 ГОСТ 10007-80",
            "Текстолит ПТК ГОСТ 5-78"
        ];
    }
}

