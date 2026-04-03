using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using System.Windows.Controls.Primitives;
using CNCSS.Logic;
using CNCSS.Vis;
using CNCSS.Data;
using CNCSS.UI.ViewModels;

namespace CNCSS
{
    public partial class MainWindow : Window
    {
        private readonly List<Visual3D> _toolpathVisuals = new();
        private readonly Dictionary<int, List<Visual3D>> _lineVisualsMap = new();
        private readonly ObservableCollection<ToolViewModel> _tools = new();
        private GCodeParser? _currentParser;
        private string[] _currentLines = Array.Empty<string>();
        private System.Windows.Threading.DispatcherTimer _animationTimer;
        private Point3D _lastPosition = new Point3D(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z);
        private double _interpolationProgress = 1.0;
        private Point3D _startInterpolationPos;
        private Point3D _targetInterpolationPos;
        private DateTime _lastStockUpdateTime = DateTime.MinValue;
        private bool _isStockUpdating = false;

        private VoxelStock? _stock;
        private StockCutWorker? _stockCutWorker;
        private readonly ModelVisual3D _stockVisual = new();
        private readonly GeometryModel3D _stockModel = new();

        // Визуализация инструмента
        private ModelVisual3D _toolVisual = new();
        private readonly GeometryModel3D _fluteModel = new();
        private readonly GeometryModel3D _shankModel = new();

        private const double RES_HIGH = ProjectConstants.RES_HIGH;
        private const double RES_MEDIUM = ProjectConstants.RES_MEDIUM;
        private const double RES_COARSE = ProjectConstants.RES_COARSE;

        private double _selectedResolution = RES_MEDIUM;

        // FPS Counter fields
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _fpsSlowdownFactor = 1.0; // Коэффициент замедления при низком FPS

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            CompositionTarget.Rendering += OnRendering;
            ToolsList.ItemsSource = _tools;

            _animationTimer = new System.Windows.Threading.DispatcherTimer();
            _animationTimer.Tick += AnimationTimer_Tick;

            InitToolVisual();
            
            _stockModel.Material = MaterialHelper.CreateMaterial(Colors.LightGray);
            _stockModel.BackMaterial = _stockModel.Material;
            _stockVisual.Content = _stockModel;
            Viewport.Children.Add(_stockVisual);

            UpdateResButtons();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            _frameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            if (elapsed >= 0.5) // Обновляем чаще (раз в полсекунды) для более плавной реакции
            {
                double fps = _frameCount / elapsed;
                if (FpsText != null)
                {
                    FpsText.Text = fps.ToString("F0");
                    if (fps < 15) FpsText.Foreground = Brushes.Red;
                    else if (fps < 30) FpsText.Foreground = Brushes.Orange;
                    else FpsText.Foreground = Brushes.Lime;
                }

                // Динамическое замедление: если FPS < 45, замедляем симуляцию
                const double targetFps = 45.0;
                if (fps < targetFps)
                {
                    // Плавное снижение коэффициента (минимум 0.1)
                    _fpsSlowdownFactor = Math.Max(0.1, fps / targetFps);
                }
                else
                {
                    _fpsSlowdownFactor = 1.0;
                }

                _frameCount = 0;
                _lastFpsUpdate = now;
            }
        }

        private void UpdateResButtons()
        {
            if (FineResButton == null || MediumResButton == null || CoarseResButton == null) return;

            FineResButton.Background = _selectedResolution == RES_HIGH ? Brushes.SkyBlue : Brushes.LightGray;
            FineResButton.FontWeight = _selectedResolution == RES_HIGH ? FontWeights.Bold : FontWeights.Normal;

            MediumResButton.Background = _selectedResolution == RES_MEDIUM ? Brushes.SkyBlue : Brushes.LightGray;
            MediumResButton.FontWeight = _selectedResolution == RES_MEDIUM ? FontWeights.Bold : FontWeights.Normal;

            CoarseResButton.Background = _selectedResolution == RES_COARSE ? Brushes.SkyBlue : Brushes.LightGray;
            CoarseResButton.FontWeight = _selectedResolution == RES_COARSE ? FontWeights.Bold : FontWeights.Normal;

            if (ResolutionStatusText != null)
            {
                string resName = _selectedResolution == RES_HIGH ? "Высокая" :
                                 _selectedResolution == RES_MEDIUM ? "Средняя" : "Грубая";
                ResolutionStatusText.Text = $"Точность: {resName} ({_selectedResolution:F2} мм)";
            }
        }

        

        private void ResButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && double.TryParse(btn.Tag?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double res))
            {
                _selectedResolution = res;
                UpdateResButtons();
                if (IsLoaded) ApplyStock();
            }
        }

        private void ApplyStock_Click(object sender, RoutedEventArgs e) => ApplyStock();

        private void InitToolVisual()
        {
            var group = new Model3DGroup();
            _fluteModel.Material = MaterialHelper.CreateMaterial(Colors.Gold);
            _fluteModel.BackMaterial = _fluteModel.Material;
            _shankModel.Material = MaterialHelper.CreateMaterial(Colors.Gray);
            _shankModel.BackMaterial = _shankModel.Material;
            group.Children.Add(_fluteModel);
            group.Children.Add(_shankModel);
            _toolVisual.Content = group;
        }

        private void UpdateToolGeometry(ToolViewModel? tool, Point3D position)
        {
            if (tool == null) { _fluteModel.Geometry = null; _shankModel.Geometry = null; return; }
            double fluteRadius = tool.Diameter / 2.0;
            double shankRadius = tool.ShankDiameter / 2.0;
            var fluteBuilder = new MeshBuilder();
            fluteBuilder.AddCylinder(new Point3D(0, 0, 0), new Point3D(0, 0, tool.FluteLength), fluteRadius, 20, true, true);
            _fluteModel.Geometry = fluteBuilder.ToMesh();
            var shankBuilder = new MeshBuilder();
            double shankEnd = Math.Max(tool.FluteLength, tool.OverallLength);
            shankBuilder.AddCylinder(new Point3D(0, 0, tool.FluteLength), new Point3D(0, 0, shankEnd), shankRadius, 20, true, true);
            _shankModel.Geometry = shankBuilder.ToMesh();
            _toolVisual.Transform = new TranslateTransform3D(position.X, position.Y, position.Z);
        }

        private void Tool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is ToolViewModel tool && ToolsList.SelectedItem == tool)
                UpdateToolGeometry(tool, GetCurrentPosition());
        }

        private Point3D GetCurrentPosition()
        {
            if (_currentParser == null || GCodeList.SelectedIndex < 0) return new Point3D(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z);
            int selectedLine = GCodeList.SelectedIndex + 1;
            var lastCmd = _currentParser.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);
            return lastCmd != null ? new Point3D(lastCmd.EndState.X, lastCmd.EndState.Y, lastCmd.EndState.Z) : new Point3D(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z);
        }

        private void SyncToolWithState()
        {
            if (_currentParser == null || GCodeList.SelectedIndex < 0) return;
            int selectedLine = GCodeList.SelectedIndex + 1;
            var lastCmd = _currentParser.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);
            if (lastCmd?.EndState.ToolNumber != null)
            {
                if (!Viewport.Children.Contains(_toolVisual)) Viewport.Children.Add(_toolVisual);
                int toolNum = lastCmd.EndState.ToolNumber.Value;
                var toolVM = _tools.FirstOrDefault(t => t.Number == toolNum);
                if (toolVM != null && ToolsList.SelectedItem != toolVM) ToolsList.SelectedItem = toolVM;
            }
            else if (Viewport.Children.Contains(_toolVisual)) Viewport.Children.Remove(_toolVisual);
        }

        private double _speedMultiplier = 2.0;

        private double GetPhysicalSpeed(MachineState state)
        {
            double baseSpeed;
            if (state.CurrentMotionMode.Number == 0)
            {
                double overrideVal = RapidOverrideSlider?.Value ?? 100.0;
                baseSpeed = (MachineState.RAPID_FEED / 60.0) * (overrideVal / 100.0);
            }
            else
            {
                double overrideVal = WorkOverrideSlider?.Value ?? 100.0;
                baseSpeed = (state.FeedRate / 60.0) * (overrideVal / 100.0);
            }
            return baseSpeed * _speedMultiplier;
        }

        private double _simulationMultiplier = 2.0;

        private void SpeedMultiplier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && double.TryParse(tag, out double multiplier))
            {
                _simulationMultiplier = multiplier;
                // Визуальная индикация активного множителя
                if (StatusText != null) StatusText.Text = $"Множитель скорости: x{multiplier}";
                
                if (btn.Parent is System.Windows.Controls.Primitives.UniformGrid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is Button b) b.FontWeight = (b == btn) ? FontWeights.Bold : FontWeights.Normal;
                    }
                }
            }
        }

        private void OverrideSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WorkOverrideText != null && WorkOverrideSlider != null)
                WorkOverrideText.Text = WorkOverrideSlider.Value.ToString("F0") + "%";
            
            if (RapidOverrideText != null && RapidOverrideSlider != null)
                RapidOverrideText.Text = RapidOverrideSlider.Value.ToString("F0") + "%";
        }

        private async void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            // Обновляем прогресс-бар выполнения G-кода
            if (MainProgressBar != null && _currentLines.Length > 0)
            {
                MainProgressBar.Value = (double)(GCodeList.SelectedIndex + 1) / _currentLines.Length * 100;
            }

            ParsedCommand? currentCmd = null;
            if (_interpolationProgress >= 1.0)
            {
                int selectedLine = GCodeList.SelectedIndex + 1;
                currentCmd = _currentParser?.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);

                // Если мы закончили этап Z в G28, переходим к этапу XY
                if (currentCmd != null && currentCmd.GCodes.Any(g => g.Number == 28) && 
                    Math.Abs(_lastPosition.Z - MachineState.HOME_Z) < 0.001 && 
                    (Math.Abs(_lastPosition.X - MachineState.HOME_X) > 0.001 || Math.Abs(_lastPosition.Y - MachineState.HOME_Y) > 0.001))
                {
                    _startInterpolationPos = _lastPosition;
                    _targetInterpolationPos = new Point3D(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z);
                    _interpolationProgress = 0;
                    _animationTimer.Interval = TimeSpan.FromMilliseconds(10);
                }
                else if (GCodeList.SelectedIndex < GCodeList.Items.Count - 1)
                {
                    _startInterpolationPos = _lastPosition;
                    GCodeList.SelectedIndex++;
                    GCodeList.ScrollIntoView(GCodeList.SelectedItem);
                    SyncToolWithState();
                    _targetInterpolationPos = GetCurrentPosition();

                    // Специальная обработка G28 для анимации: разделение на два этапа
                    selectedLine = GCodeList.SelectedIndex + 1;
                    currentCmd = _currentParser?.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);
                    if (currentCmd != null && currentCmd.GCodes.Any(g => g.Number == 28))
                    {
                        if (Math.Abs(_startInterpolationPos.Z - MachineState.HOME_Z) > 0.001)
                        {
                            // Этап 1: только Z
                            _targetInterpolationPos = new Point3D(_startInterpolationPos.X, _startInterpolationPos.Y, MachineState.HOME_Z);
                        }
                    }
                    
                    double dist = (_targetInterpolationPos - _startInterpolationPos).Length;
                    if (dist < 0.0001)
                    {
                        _interpolationProgress = 1.0;
                        _animationTimer.Interval = TimeSpan.FromMilliseconds(100);
                    }
                    else
                    {
                        _interpolationProgress = 0;
                        _animationTimer.Interval = TimeSpan.FromMilliseconds(10);
                    }
                }
                else { StopButton_Click(this, new RoutedEventArgs()); return; }
            }

            Point3D currentPos;
            if (_interpolationProgress < 1.0)
            {
                double dist = (_targetInterpolationPos - _startInterpolationPos).Length;
                if (dist > 0)
                {
                    int selectedLine = GCodeList.SelectedIndex + 1;
                    var lastCmd = _currentParser?.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);
                    double speedMmPerSec = lastCmd != null ? GetPhysicalSpeed(lastCmd.EndState) : 10.0;
                    
                    if (speedMmPerSec <= 0) speedMmPerSec = 0.001;

                    // Применяем множитель скорости симуляции и коэффициент замедления при низком FPS
                    double step = (speedMmPerSec * 0.01 * _simulationMultiplier * _fpsSlowdownFactor) / dist;
                    _interpolationProgress = Math.Min(1.0, _interpolationProgress + step);
                }
                else { _interpolationProgress = 1.0; }
            }

            int currentLine = GCodeList.SelectedIndex + 1;
            currentCmd = _currentParser?.Commands.LastOrDefault(c => c.LineNumber <= currentLine);

            if (currentCmd?.Arc != null && _interpolationProgress < 1.0)
            {
                var arc = currentCmd.Arc;
                double currentAngle = arc.StartAngleRad + arc.SweepAngleRad * _interpolationProgress;
                double u = arc.CenterU + arc.Radius * Math.Cos(currentAngle);
                double v = arc.CenterV + arc.Radius * Math.Sin(currentAngle);
                
                // Интерполяция по третьей оси (линейная)
                double t = _interpolationProgress;
                
                if (arc.Plane == 17) // XY
                    currentPos = new Point3D(u, v, arc.StartZ + (arc.EndZ - arc.StartZ) * t);
                else if (arc.Plane == 18) // XZ
                    currentPos = new Point3D(u, arc.StartY + (arc.EndY - arc.StartY) * t, v);
                else if (arc.Plane == 19) // YZ
                    currentPos = new Point3D(arc.StartX + (arc.EndX - arc.StartX) * t, u, v);
                else
                    currentPos = new Point3D(
                        _startInterpolationPos.X + (_targetInterpolationPos.X - _startInterpolationPos.X) * _interpolationProgress,
                        _startInterpolationPos.Y + (_targetInterpolationPos.Y - _startInterpolationPos.Y) * _interpolationProgress,
                        _startInterpolationPos.Z + (_targetInterpolationPos.Z - _startInterpolationPos.Z) * _interpolationProgress
                    );
            }
            else
            {
                currentPos = new Point3D(
                    _startInterpolationPos.X + (_targetInterpolationPos.X - _startInterpolationPos.X) * _interpolationProgress,
                    _startInterpolationPos.Y + (_targetInterpolationPos.Y - _startInterpolationPos.Y) * _interpolationProgress,
                    _startInterpolationPos.Z + (_targetInterpolationPos.Z - _startInterpolationPos.Z) * _interpolationProgress
                );
            }

            PositionText.Text = $"X: {currentPos.X:F3} Y: {currentPos.Y:F3} Z: {currentPos.Z:F3}";

            if (_currentParser != null && GCodeList.SelectedIndex >= 0)
            {
                int selectedLine = GCodeList.SelectedIndex + 1;
                var lastCmd = _currentParser.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);
                if (lastCmd != null)
                {
                    var state = lastCmd.EndState;
                    FeedStatusText.Text = $"F: {state.EffectiveFeedRate:F0} мм/мин";
                    
                    string spindleDir = "M5";                    if (state.IsSpindleOn)
                    {
                        spindleDir = state.IsSpindleCW ? "M3" : "M4";
                    }
                    SpindleStatusText.Text = $"S: {state.SpindleSpeed:F0} об/мин ({spindleDir})";
                    CoolantText.Text = $"СОЖ: {(state.IsCoolantOn ? "ВКЛ" : "ВЫКЛ")}";
                    
                    if (state.ToolNumber.HasValue)
                        ToolText.Text = $"Инструмент: T{state.ToolNumber.Value}";
                    
                    CoordSystemText.Text = $"СК: {state.CurrentCoordinateSystem.Letter}{state.CurrentCoordinateSystem.Number}";
                    RefSystemText.Text = $"Отсчет: {(state.IsAbsolute ? "ABS (G90)" : "INC (G91)")}";
                }
            }
            if (ToolsList.SelectedItem is ToolViewModel tool)
            {
                UpdateToolGeometry(tool, currentPos);
                if (_stock != null && StockVisibleCheck.IsChecked == true)
                {
                    var pStart = _lastPosition;
                    var pEnd = currentPos;
                    var diam = tool.Diameter;
                    var flute = tool.FluteLength;

                    _stockCutWorker?.EnqueueCut(pStart, pEnd, diam / 2.0, flute);

                    // Увеличиваем порог обновления до 250мс для снижения нагрузки на UI
                    if (_stock.IsDirty && !_isStockUpdating && (DateTime.Now - _lastStockUpdateTime).TotalMilliseconds > 250)
                    {
                        _ = UpdateStockMeshAsync();
                    }
                }
            }
            _lastPosition = currentPos;
        }

        private async Task UpdateStockMeshAsync(bool showProgress = false)
        {
            if (_stock == null || _isStockUpdating) return;
            _isStockUpdating = true;
            
            try
            {
                if (showProgress && StockProgressPanel != null)
                {
                    StockProgressPanel.Visibility = Visibility.Visible;
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                }
                
                // Выполняем тяжелый расчет геометрии в фоне
                await _stock.UpdateVisualsAsync();
                
                // Обновляем визуальный контент в UI-потоке
                if (_stockVisual.Content != _stock.MainModel)
                {
                    _stockVisual.Content = _stock.MainModel;
                }
                
                _lastStockUpdateTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stock update error: {ex.Message}");
            }
            finally 
            { 
                _isStockUpdating = false;
                if (StockProgressPanel != null) StockProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (GCodeList.Items.Count == 0) return;
            if (GCodeList.SelectedIndex >= GCodeList.Items.Count - 1) GCodeList.SelectedIndex = 0;
            _animationTimer.Start();
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            _animationTimer.Stop();
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _animationTimer.Stop();
            GCodeList.SelectedIndex = 0;
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            
            // Сброс позиции инструмента (опционально)
            _lastPosition = new Point3D(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z);
            _interpolationProgress = 1.0;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateResButtons();
            OverrideSlider_ValueChanged(this, null);
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "program.nc");
            if (!File.Exists(path)) path = Path.Combine(Directory.GetCurrentDirectory(), "program.nc");
            if (!File.Exists(path)) CreateDemoNc(path);
            if (File.Exists(path)) LoadAndRender(path);
        }

        private static void CreateDemoNc(string filePath)
        {
            var content = @"O0001

G40G49G80
G21G17
G54

G91 G28 Z0
G90

T1 M6
S3000 M3
F1000

G00 X0 Y-15
Z10 G43 H1

M8

G1 Z0
G91
Y115
X20
Y-100
X20
Y100
X20
Y-100
X20
Y100
X20
Y-100
X20
Y115


G90
G00 Z10

M5 M1 M9

G91 G28 Z0
G90

T2 M6
S5000 M3
F2000
G00 X0 Y0
Z10 G43 H1

G1 Z-20

M8

Y100
X130
Y10
G02 Y0 X120 R10
G1 X0

G0 Z10
X50
Y50
G1 Z1
G91 
X10
G02 X-20 R10 Z-1
G02 X20 R10 Z-1
G02 X-20 R10 Z-1
G02 X20 R10 Z-1
G02 X-20 R10 Z-1
G02 X20 R10 Z-1
G02 X-20 R10 Z-1
G02 X20 R10 Z-1
G02 X-20 R10 Z-1
G02 X20 R10 Z-1
G02 X-20 R10 Z-1
G02 X20 R10 Z-1
G02 X-20 R10 Z-1
G02 X20 R10 Z-1
G02 X-20 R10 Z-1
G02 X20 R10 Z-1
G1 X-10

G90

G00 Z10

M5 M1 M9

G91 G28 Z0
G90

M30";
            File.WriteAllText(filePath, content);
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "G-code (*.nc;*.ngc;*.tap;*.txt)|*.nc;*.ngc;*.tap;*.txt|Все файлы|*.*" };
            if (dlg.ShowDialog(this) == true) LoadAndRender(dlg.FileName);
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();
        private void ZoomExtents_Click(object sender, RoutedEventArgs e) => Viewport.ZoomExtents();

        private void StockVisible_Changed(object sender, RoutedEventArgs e)
        {
            if (_stockVisual != null) _stockVisual.Content = (StockVisibleCheck.IsChecked == true) ? _stockModel : null;
            if (ResolutionStatusText != null) ResolutionStatusText.Visibility = (StockVisibleCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplySettingsTop_Click(object sender, RoutedEventArgs e)
        {
            // Поиск элементов в шаблоне меню может быть сложным, поэтому используем прямой доступ к полям, 
            // если они определены в XAML. Но так как они в DataTemplate, нам нужно найти их в визуальном дереве.
            // Для упрощения я переделал XAML, чтобы использовать те же имена или передавать значения.
            
            // В данном случае, так как это DataTemplate, проще всего найти их через родителя отправителя.
            if (sender is Button btn && btn.Parent is StackPanel panel)
            {
                var rapidInput = panel.Children.OfType<TextBox>().FirstOrDefault(x => x.Name == "RapidFeedInputTop");
                var minArcInput = panel.Children.OfType<TextBox>().FirstOrDefault(x => x.Name == "ArcMinSegmentsInputTop");
                var maxArcInput = panel.Children.OfType<TextBox>().FirstOrDefault(x => x.Name == "ArcMaxSegmentsInputTop");

                if (rapidInput != null && double.TryParse(rapidInput.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rapid))
                    MachineState.RAPID_FEED = rapid;

                if (minArcInput != null && int.TryParse(minArcInput.Text, out int minArc))
                    ToolpathBuilder.MinArcSegments = minArc;

                if (maxArcInput != null && int.TryParse(maxArcInput.Text, out int maxArc))
                    ToolpathBuilder.MaxArcSegments = maxArc;

                MessageBox.Show("Настройки применены.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Settings_Changed(object sender, TextChangedEventArgs e) { }
        private void StockParam_TextChanged(object sender, TextChangedEventArgs e) { }

        private void ApplySettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Используйте меню 'Настройки' в верхней панели для изменения параметров.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void StockColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StockColorCombo == null || _stockModel == null) return;
            if (StockColorCombo.SelectedItem is ComboBoxItem item && item.Tag is string colorName)
            {
                var color = (Color)ColorConverter.ConvertFromString(colorName);
                _stockModel.Material = MaterialHelper.CreateMaterial(color);
                _stockModel.BackMaterial = _stockModel.Material;
            }
        }

        private async void ApplyStock()
        {
            try
            {
                if (StockMinX == null || StockMaxX == null) return;
                double minX = double.Parse(StockMinX.Text);
                double maxX = double.Parse(StockMaxX.Text);
                double minY = double.Parse(StockMinY.Text);
                double maxY = double.Parse(StockMaxY.Text);
                double minZ = double.Parse(StockMinZ.Text);
                double maxZ = double.Parse(StockMaxZ.Text);
                _stock = new VoxelStock(maxX - minX, maxY - minY, maxZ - minZ, _selectedResolution, new Point3D((minX + maxX) / 2, (minY + maxY) / 2, 0), maxZ);
                _stockCutWorker?.Dispose();
                _stockCutWorker = new StockCutWorker(_stock);
                await UpdateStockMeshAsync(true);
            }
            catch { }
        }

        private void LoadAndRender(string filePath)
        {
            try
            {
                ClearToolpath();
                _lineVisualsMap.Clear();
                _currentLines = File.ReadAllLines(filePath);
                GCodeList.ItemsSource = _currentLines;
                _currentParser = new GCodeParser();
                _currentParser.ProcessFile(filePath);
                _tools.Clear();
                var toolNumbers = _currentParser.Commands.Where(c => c.ToolNumber.HasValue).Select(c => c.ToolNumber!.Value).Distinct().OrderBy(n => n);
                foreach (var t in toolNumbers) { var tool = new ToolViewModel { Number = t }; tool.PropertyChanged += Tool_PropertyChanged; _tools.Add(tool); }
                if (_tools.Count > 0) ToolsList.SelectedIndex = 0;
                var segmentsWithLines = ToolpathBuilder.BuildWithLineNumbers(_currentParser);
                double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (var item in segmentsWithLines)
                {
                    if (item.Segment.Kind == ToolpathSegmentKind.Linear || item.Segment.Kind == ToolpathSegmentKind.Arc)
                    {
                        foreach (var p in item.Segment.Points)
                        {
                            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
                        }
                    }
                    var visual = CreateVisualForSegment(item.Segment);
                    Viewport.Children.Add(visual);
                    _toolpathVisuals.Add(visual);
                    if (!_lineVisualsMap.ContainsKey(item.LineNumber)) _lineVisualsMap[item.LineNumber] = new List<Visual3D>();
                    _lineVisualsMap[item.LineNumber].Add(visual);
                }
                if (segmentsWithLines.Any() && (AutoStockCheck?.IsChecked ?? false))
                {
                    StockMinX.Text = (minX - 5).ToString("F1"); StockMaxX.Text = (maxX + 5).ToString("F1");
                    StockMinY.Text = (minY - 5).ToString("F1"); StockMaxY.Text = (maxY + 5).ToString("F1");
                    StockMinZ.Text = (minZ - 5).ToString("F1");
                    double calculatedMaxZ = Math.Abs(maxZ) < 0.001 ? 1.0 : maxZ;
                    StockMaxZ.Text = calculatedMaxZ.ToString("F1");
                    ApplyStock();
                }
                StatsBox.Text = BuildStatsText(filePath, _currentParser, segmentsWithLines.Select(s => s.Segment).ToList());
                UpdateMachineStateUI(_currentParser.State);
                if (ToolsList.SelectedItem is ToolViewModel selectedTool) UpdateToolGeometry(selectedTool, GetCurrentPosition());
                Dispatcher.BeginInvoke(() => Viewport.ZoomExtents(), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private Visual3D CreateVisualForSegment(ToolpathSegment seg)
        {
            var color = seg.Kind switch { ToolpathSegmentKind.Rapid => Colors.OrangeRed, ToolpathSegmentKind.Linear => Colors.LimeGreen, ToolpathSegmentKind.Arc => Colors.DeepSkyBlue, _ => Colors.White };
            return new LinesVisual3D { Color = color, Thickness = 2.0, Points = new Point3DCollection(seg.Points) };
        }

        private void ToolsList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ToolsList.SelectedItem is ToolViewModel tool) UpdateToolGeometry(tool, GetCurrentPosition()); }

        private void GCodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GCodeList.SelectedIndex < 0 || _currentParser == null) return;
            int selectedLine = GCodeList.SelectedIndex + 1;
            SyncToolWithState();
            if (ToolsList.SelectedItem is ToolViewModel tool) UpdateToolGeometry(tool, GetCurrentPosition());
            foreach (var entry in _lineVisualsMap)
            {
                bool isVisible = entry.Key <= selectedLine;
                foreach (var visual in entry.Value)
                {
                    if (isVisible && !Viewport.Children.Contains(visual)) Viewport.Children.Add(visual);
                    else if (!isVisible && Viewport.Children.Contains(visual)) Viewport.Children.Remove(visual);
                }
            }
            var stateParser = new GCodeParser();
            foreach (var cmd in _currentParser.Commands) { if (cmd.LineNumber <= selectedLine) CommandReplayer.ReplayCommand(stateParser, cmd); else break; }
            UpdateMachineStateUI(stateParser.State);
        }

        private void UpdateMachineStateUI(MachineState state)
        {
            ToolText.Text = $"Инструмент: {(state.ToolNumber?.ToString() ?? "-")}";
            CoordSystemText.Text = $"СК: {state.CurrentCoordinateSystem.Letter}{state.CurrentCoordinateSystem.Number}";
            RefSystemText.Text = $"Отсчет: {(state.IsAbsolute ? "ABS (G90)" : "INC (G91)")}";
            PositionText.Text = $"X: {state.X:F3} Y: {state.Y:F3} Z: {state.Z:F3}";
            CoolantText.Text = $"СОЖ: {(state.IsCoolantOn ? "ВКЛ" : "ВЫКЛ")}";
        }

        private static string BuildStatsText(string filePath, GCodeParser parser, List<ToolpathSegment> segments)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Path.GetFileName(filePath));
            sb.AppendLine($"Команд: {parser.Commands.Count}");
            sb.AppendLine($"Сегментов пути: {segments.Count}");
            sb.AppendLine($"Дуг (геометрия): {parser.Commands.Count(c => c.Arc != null)}");
            sb.AppendLine();
            sb.AppendLine("Цвета: КрасныйG0, ЗелёныйG1, ГолубойG2/G3");
            return sb.ToString();
        }

        private void ClearToolpath() { foreach (var v in _toolpathVisuals) Viewport.Children.Remove(v); _toolpathVisuals.Clear(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) OpenFile_Click(this, e);
            else if (e.Key == Key.F) Viewport.ZoomExtents();
            base.OnKeyDown(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _stockCutWorker?.Dispose();
            base.OnClosed(e);
        }
    }
}
