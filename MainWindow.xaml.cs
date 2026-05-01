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
using System.Windows.Threading;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using System.Windows.Controls.Primitives;
using CNCSS.Logic;
using CNCSS.Vis;
using CNCSS.Data;
using CNCSS.UI.ViewModels;
using CNCSS.Controller.Core;
using CNCSS.Machine.Core;
using CNCSS.Simulation.Bus;
using CNCSS.Simulation.Execution;
using CNCSS.UI.FanucPanel;
using CNCSS.UI.Dialogs;
using CNCSS.UI.Presenters;
using System.Diagnostics;

namespace CNCSS
{
    /// <summary>
    /// Главное окно: 3D-сцена (Helix), воксельная заготовка, панели FANUC и операторской станции,
    /// загрузка УП, воспроизведение и синхронизация с шиной <see cref="CNCSS.Simulation.Bus.ISimulationBus"/>.
    /// </summary>
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
        private bool _isStockUpdating = false;

        private VoxelStock? _stock;
        private StockCutWorker? _stockCutWorker;
        private readonly ModelVisual3D _stockVisual = new();
        private readonly GeometryModel3D _stockModel = new();

        // Визуализация инструмента
        private ModelVisual3D _toolVisual = new();
        private readonly GeometryModel3D _fluteModel = new();
        private readonly GeometryModel3D _shankModel = new();
        private readonly ISimulationBus _simulationBus;
        private readonly IControllerCore _controllerCore;
        private readonly IMachineCore _machineCore;
        private readonly IDisposable _machineStateSubscription;
        private readonly IDisposable _alarmSubscription;
        private readonly IDisposable _mdiModeSubscription;
        private readonly IDisposable _programLineSubscription;
        private readonly IDisposable _workOffsetsSubscription;
        private readonly ProgramExecutionService _programExecutionService;
        private readonly ProgramStateService _programStateService = new();
        private readonly PlaybackLoopService _playbackLoopService;
        private readonly UiRenderService _uiRenderService = new();
        private readonly StockRenderService _stockRenderService = new();
        private readonly ToolpathRenderService _toolpathRenderService = new();
        private static readonly bool EnableLegacyUiControls = false;
        private readonly TranslateTransform3D _toolTransform = new();
        private ToolViewModel? _cachedToolGeometryTool;
        private (double Diameter, double ShankDiameter, double FluteLength, double OverallLength, Color FluteColor) _cachedToolGeometryKey;
        private bool _hasCachedToolGeometry;
        private ToolSettingsWindow? _toolSettingsDialog;
        private readonly object _machineStateUiSync = new();
        private MachineStateChangedEvent? _pendingMachineStateEvent;
        private bool _machineStateUiUpdateQueued;
        private string _currentControllerMode = "MEM";
        private bool _isControllerRunning;
        private string _lastInterlockCode = "OK";
        private string _lastInterlockDetail = "-";
        private string _currentMdiMode = "G90";
        private string _currentCoordSystem = "G54";
        private string _systemUnitsText = "MM (G21)";
        private string _systemCoordModeText = "ABS (G90)";
        private string _systemPlaneText = "G17 (XY)";
        private string _systemMotionText = "G0";
        private string _systemCompText = "LEN G49 RAD G40";
        private double _currentOffsetX;
        private double _currentOffsetY;
        private double _currentOffsetZ;
        private readonly Dictionary<int, (double X, double Y, double Z)> _workOffsets = new();
        private int _selectedOffsetSystem = 54;
        private string _lastMdiCommand = "-";
        private string _lastMdiStatus = "-";
        private bool _isSingleBlockEnabled;
        private bool _isOptionalStopEnabled;
        private bool _isDryRunEnabled;
        private bool _suppressSelectionSideEffects;
        private string? _loadedNcProgramPath;

        private const double RES_HIGH = ProjectConstants.RES_HIGH;
        private const double RES_MEDIUM = ProjectConstants.RES_MEDIUM;
        private const double RES_COARSE = ProjectConstants.RES_COARSE;

        private double _selectedResolution = RES_MEDIUM;

        // FPS Counter fields
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _fpsSlowdownFactor = 1.0; // Коэффициент замедления при низком FPS
        private bool _isFanucDragging;
        private Point _fanucDragStart;
        private double _fanucStartLeft;
        private double _fanucStartTop;
        private bool _isFanucMinimized;
        private double _fanucExpandedHeight = double.NaN;
        private const double FanucPanelResizeMinWidth = 640;
        private const double FanucPanelResizeMinHeight = 440;
        private bool _isOperatorDragging;
        private Point _operatorDragStart;
        private double _operatorStartLeft;
        private double _operatorStartTop;
        private bool _isOperatorMinimized;
        private bool _suppressOperatorFeedOverrideSync;
        private double _operatorSpindleOverridePercent = 100.0;
        private double _operatorExpandedHeight = double.NaN;
        private const double OperatorPanelResizeMinWidth = 346;
        private const double OperatorPanelResizeMinHeight = 285;
        private readonly System.Windows.Threading.DispatcherTimer _fanucClockTimer = new();
        private readonly Stopwatch _runTimeStopwatch = new();
        private readonly Stopwatch _cycleTimeStopwatch = new();
        private TimeSpan _accumulatedRunTime = TimeSpan.Zero;
        private TimeSpan _accumulatedCycleTime = TimeSpan.Zero;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            CompositionTarget.Rendering += OnRendering;
            PreviewMouseDown += MainWindow_PreviewMouseDown;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            ToolsList.ItemsSource = _tools;

            _simulationBus = new SimulationBus();
            _controllerCore = new ControllerCore(_simulationBus);
            _machineCore = new MachineCore(_simulationBus);
            _programExecutionService = new ProgramExecutionService(_simulationBus);
            _playbackLoopService = new PlaybackLoopService(_programExecutionService);
            _machineStateSubscription = _simulationBus.Subscribe<MachineStateChangedEvent>(OnMachineStateChanged);
            _alarmSubscription = _simulationBus.Subscribe<AlarmRaisedEvent>(OnAlarmRaised);
            _mdiModeSubscription = _simulationBus.Subscribe<MdiModeChangedEvent>(OnMdiModeChanged);
            _programLineSubscription = _simulationBus.Subscribe<ProgramLineExecutedEvent>(OnProgramLineExecuted);
            _workOffsetsSubscription = _simulationBus.Subscribe<WorkOffsetsChangedEvent>(OnWorkOffsetsChanged);
            FanucPanel.CycleStartRequested += OnFanucCycleStartRequested;
            FanucPanel.FeedHoldRequested += OnFanucFeedHoldRequested;
            FanucPanel.ResetRequested += OnFanucResetRequested;
            FanucPanel.ModeChangedRequested += OnFanucModeChangedRequested;
            FanucPanel.JogRequested += OnFanucJogRequested;
            FanucPanel.MdiExecuteRequested += OnFanucMdiExecuteRequested;
            FanucPanel.ProgDirectoryCreateAndOpenRequested += OnFanucProgDirectoryCreateAndOpenRequested;
            FanucPanel.ProgOpenByNameRequested += OnFanucProgOpenByNameRequested;
            FanucPanel.ProgDeleteProgramByNameRequested += OnFanucProgDeleteProgramByNameRequested;
            FanucPanel.SingleBlockChangedRequested += OnFanucSingleBlockChangedRequested;
            FanucPanel.OptionalStopChangedRequested += OnFanucOptionalStopChangedRequested;
            FanucPanel.DryRunChangedRequested += OnFanucDryRunChangedRequested;
            FanucPanel.OffsetUpdateRequested += OnFanucOffsetUpdateRequested;
            FanucPanel.OffsetSystemSelectionChangedRequested += OnFanucOffsetSystemSelectionChangedRequested;
            FanucPanel.OffsetReadActiveToEditorRequested += OnFanucOffsetReadActiveToEditorRequested;

            OperatorPanel.ModeSelected += OnOperatorModeSelected;
            OperatorPanel.JogAxisDelta += OnOperatorJogAxisDelta;
            OperatorPanel.CycleStart += OperatorPanel_CycleStart;
            OperatorPanel.FeedHold += OperatorPanel_FeedHold;
            OperatorPanel.CycleStopRequested += OperatorPanel_CycleStop;
            OperatorPanel.EmergencyResetRequested += OperatorPanel_EmergencyReset;
            OperatorPanel.FeedWorkOverridePercentChanged += OnOperatorFeedWorkOverridePercentChanged;
            OperatorPanel.SpindleOverridePercentChanged += OnOperatorSpindleOverridePercentChanged;
            OperatorPanel.SingleBlockChanged += OperatorPanel_SingleBlockChanged;
            OperatorPanel.OptionalStopChanged += OperatorPanel_OptionalStopChanged;

            _animationTimer = new System.Windows.Threading.DispatcherTimer();
            _animationTimer.Tick += AnimationTimer_Tick;
            _fanucClockTimer.Interval = TimeSpan.FromSeconds(1);
            _fanucClockTimer.Tick += (_, _) => FanucPanel.UpdateStatusClock(DateTime.Now);
            _fanucClockTimer.Start();

            InitToolVisual();
            ConfigureControlMode();
            
            _stockModel.Material = MaterialHelper.CreateMaterial(Colors.LightGray);
            _stockModel.BackMaterial = _stockModel.Material;
            _stockVisual.Content = _stockModel;
            Viewport.Children.Add(_stockVisual);
            _playbackLoopService.Reset(_lastPosition);
            RefreshDiagnosticsPanel();
            FanucPanel.UpdateStatusClock(DateTime.Now);

            UpdateResButtons();

            if (OperatorFloatingHost != null)
            {
                OperatorFloatingHost.SizeChanged += OperatorFloatingHost_SizeChanged;
            }

            if (FanucFloatingHost != null)
            {
                FanucFloatingHost.SizeChanged += FanucFloatingHost_SizeChanged;
            }
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            Button? button = FindVisualParent<Button>(source);
            if (button != null)
            {
                string caption = GetInteractiveCaption(button);
                if (!string.IsNullOrWhiteSpace(caption))
                {
                    FanucPanel.LogUserAction($"PRESS: {caption}");
                }
                return;
            }

            CheckBox? checkBox = FindVisualParent<CheckBox>(source);
            if (checkBox != null)
            {
                string caption = GetInteractiveCaption(checkBox);
                if (!string.IsNullOrWhiteSpace(caption))
                {
                    FanucPanel.LogUserAction($"TOGGLE: {caption}");
                }
                return;
            }

            RadioButton? radio = FindVisualParent<RadioButton>(source);
            if (radio != null)
            {
                string caption = GetInteractiveCaption(radio);
                if (!string.IsNullOrWhiteSpace(caption))
                {
                    FanucPanel.LogUserAction($"SELECT: {caption}");
                }
                return;
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.None)
            {
                return;
            }

            string key = e.Key.ToString().ToUpperInvariant();
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                key = $"{Keyboard.Modifiers.ToString().ToUpperInvariant()}+{key}";
            }

            FanucPanel.LogUserAction($"KEY: {key}");
        }

        private static T? FindVisualParent<T>(DependencyObject? start) where T : DependencyObject
        {
            DependencyObject? current = start;
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }

                current = GetSafeParent(current);
            }

            return null;
        }

        private static DependencyObject? GetSafeParent(DependencyObject current)
        {
            if (current is Visual || current is Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }

            if (current is FrameworkContentElement fce)
            {
                return fce.Parent;
            }

            if (current is ContentElement ce)
            {
                return ContentOperations.GetParent(ce);
            }

            return null;
        }

        private static string GetInteractiveCaption(FrameworkElement element)
        {
            string? byName = element.Name;
            string? byText = (element as ContentControl)?.Content?.ToString();
            string caption = string.IsNullOrWhiteSpace(byText) ? byName ?? string.Empty : byText;
            return caption.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private RowDefinition? GetOperatorFloatingBodyRowDefinition()
        {
            return OperatorFloatingHost?.Child is Grid g && g.RowDefinitions.Count > 1
                ? g.RowDefinitions[1]
                : null;
        }

        /// <summary>
        /// Сжимает окно только до высоты шапки: учитываем измерение заголовка (кнопки, шрифт),
        /// а после layout дополнительно уточняем — у FANUC заголовок иногда давал больший клиент после первого кадра.
        /// </summary>
        private static void ApplyCollapsedFloatingHostSize(Border host, Border headerStrip, ScrollViewer? contentScroll)
        {
            contentScroll?.ClearValue(FrameworkElement.MaxHeightProperty);

            double interior = Math.Max(120,
                host.ActualWidth > 4
                    ? host.ActualWidth - host.BorderThickness.Left - host.BorderThickness.Right
                    : 640);

            headerStrip.Measure(new Size(interior, double.PositiveInfinity));
            double headerH = headerStrip.DesiredSize.Height;
            if (headerH <= 0.5 && !double.IsNaN(headerStrip.Height) && headerStrip.Height > 0)
            {
                headerH = headerStrip.Height;
            }
            if (headerH <= 0.5)
            {
                headerH = 34;
            }
            headerH = Math.Ceiling(headerH);

            double collapsedTotal = headerH + host.BorderThickness.Top + host.BorderThickness.Bottom;

            host.MinHeight = collapsedTotal;
            host.MaxHeight = collapsedTotal;
            host.Height = collapsedTotal;
        }

        private void OperatorFloatingHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
            UpdateOperatorScrollViewport();

        private void ConfigureControlMode()
        {
            if (EnableLegacyUiControls)
            {
                if (LegacyControlsPanel != null)
                {
                    LegacyControlsPanel.Visibility = Visibility.Visible;
                }
                return;
            }

            if (LegacyControlsPanel != null)
            {
                LegacyControlsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void MenuFanucWindowItem_Checked(object sender, RoutedEventArgs e)
        {
            if (FanucFloatingHost != null)
            {
                FanucFloatingHost.Visibility = Visibility.Visible;
            }
        }

        private void MenuFanucWindowItem_Unchecked(object sender, RoutedEventArgs e)
        {
            if (FanucFloatingHost != null)
            {
                FanucFloatingHost.Visibility = Visibility.Collapsed;
            }
        }

        private void MenuOperatorPanelItem_Checked(object sender, RoutedEventArgs e)
        {
            if (OperatorFloatingHost != null)
            {
                OperatorFloatingHost.Visibility = Visibility.Visible;
            }
        }

        private void MenuOperatorPanelItem_Unchecked(object sender, RoutedEventArgs e)
        {
            if (OperatorFloatingHost != null)
            {
                OperatorFloatingHost.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateOperatorScrollViewport()
        {
            if (OperatorContentScroll == null || OperatorFloatingHost == null || OperatorFloatingHeader == null || _isOperatorMinimized)
            {
                return;
            }

            double h = OperatorFloatingHost.ActualHeight
                - OperatorFloatingHeader.ActualHeight
                - OperatorFloatingHost.BorderThickness.Top
                - OperatorFloatingHost.BorderThickness.Bottom;
            OperatorContentScroll.MaxHeight = Math.Max(40, h);
        }

        private void OperatorMinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isOperatorMinimized)
            {
                _operatorExpandedHeight = OperatorFloatingHost.ActualHeight;
            }

            _isOperatorMinimized = !_isOperatorMinimized;
            OperatorFloatingContent.Visibility = _isOperatorMinimized ? Visibility.Collapsed : Visibility.Visible;
            OperatorResizeThumb.Visibility = _isOperatorMinimized ? Visibility.Collapsed : Visibility.Visible;

            RowDefinition? bodyRow = GetOperatorFloatingBodyRowDefinition();

            if (_isOperatorMinimized)
            {
                if (bodyRow != null)
                {
                    bodyRow.MinHeight = 0;
                    bodyRow.Height = new GridLength(0);
                }

                ApplyCollapsedFloatingHostSize(OperatorFloatingHost, OperatorFloatingHeader, OperatorContentScroll);
                Border opHostRef = OperatorFloatingHost;
                Border opHeaderRef = OperatorFloatingHeader;
                ScrollViewer? opScrollRef = OperatorContentScroll;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isOperatorMinimized)
                    {
                        return;
                    }

                    ApplyCollapsedFloatingHostSize(opHostRef, opHeaderRef, opScrollRef);
                }), DispatcherPriority.Loaded);
            }
            else
            {
                if (bodyRow != null)
                {
                    bodyRow.Height = new GridLength(1, GridUnitType.Star);
                }

                OperatorFloatingHost.ClearValue(FrameworkElement.MinHeightProperty);
                OperatorFloatingHost.ClearValue(FrameworkElement.MaxHeightProperty);
                OperatorFloatingHost.MinHeight = OperatorPanelResizeMinHeight;

                if (!double.IsNaN(_operatorExpandedHeight) && _operatorExpandedHeight >= OperatorPanelResizeMinHeight - 1)
                {
                    OperatorFloatingHost.Height = _operatorExpandedHeight;
                }
                else
                {
                    OperatorFloatingHost.ClearValue(FrameworkElement.HeightProperty);
                }
            }

            OperatorMinimizeButton.Content = _isOperatorMinimized ? "□" : "_";

            if (!_isOperatorMinimized)
            {
                Dispatcher.BeginInvoke(new Action(UpdateOperatorScrollViewport), DispatcherPriority.Loaded);
            }
        }

        private void EnsureOperatorFloatingExpanded()
        {
            if (!_isOperatorMinimized)
            {
                return;
            }

            _isOperatorMinimized = false;
            OperatorFloatingContent.Visibility = Visibility.Visible;
            OperatorResizeThumb.Visibility = Visibility.Visible;
            RowDefinition? bodyRow = GetOperatorFloatingBodyRowDefinition();
            if (bodyRow != null)
            {
                bodyRow.MinHeight = 0;
                bodyRow.Height = new GridLength(1, GridUnitType.Star);
            }

            OperatorFloatingHost.ClearValue(FrameworkElement.MinHeightProperty);
            OperatorFloatingHost.ClearValue(FrameworkElement.MaxHeightProperty);
            OperatorFloatingHost.MinHeight = OperatorPanelResizeMinHeight;
            OperatorMinimizeButton.Content = "_";
        }

        private static Size MeasureFloatingContentSize(Border headerStrip, FrameworkElement contentElement)
        {
            headerStrip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            contentElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var headerSize = headerStrip.DesiredSize;
            var contentSize = contentElement.DesiredSize;
            return new Size(
                Math.Max(headerSize.Width, contentSize.Width),
                headerSize.Height + contentSize.Height);
        }

        private static void ApplyFloatingHostSizeToFitContent(
            Border host,
            Border headerStrip,
            ScrollViewer contentScroll,
            FrameworkElement contentElement,
            double minWidth,
            double minHeight)
        {
            host.ClearValue(FrameworkElement.MaxHeightProperty);
            host.ClearValue(FrameworkElement.MaxWidthProperty);
            host.ClearValue(FrameworkElement.MinHeightProperty);
            host.ClearValue(FrameworkElement.MinWidthProperty);
            contentScroll.ClearValue(FrameworkElement.MaxHeightProperty);

            var desired = MeasureFloatingContentSize(headerStrip, contentElement);
            double width = Math.Max(minWidth, Math.Ceiling(desired.Width + host.BorderThickness.Left + host.BorderThickness.Right));
            double height = Math.Max(minHeight, Math.Ceiling(desired.Height + host.BorderThickness.Top + host.BorderThickness.Bottom));

            host.MinWidth = minWidth;
            host.MinHeight = minHeight;
            host.Width = width;
            host.Height = height;
        }

        private static double ResolveHostWidth(Border host, double fallbackMinWidth)
        {
            if (host.ActualWidth > 1)
            {
                return host.ActualWidth;
            }

            if (!double.IsNaN(host.Width) && host.Width > 1)
            {
                return host.Width;
            }

            return fallbackMinWidth;
        }

        private static double ResolveHostHeight(Border host, double fallbackMinHeight)
        {
            if (host.ActualHeight > 1)
            {
                return host.ActualHeight;
            }

            if (!double.IsNaN(host.Height) && host.Height > 1)
            {
                return host.Height;
            }

            return fallbackMinHeight;
        }

        private void ArrangeFloatingPanelsAtStartup()
        {
            if (FanucFloatingHost == null || OperatorFloatingHost == null || Viewport == null)
            {
                return;
            }

            const double topMargin = 24;
            const double rightMargin = 24;
            const double rowGap = 10;

            double viewportWidth = Viewport.ActualWidth > 1 ? Viewport.ActualWidth : ActualWidth;
            double viewportHeight = Viewport.ActualHeight > 1 ? Viewport.ActualHeight : ActualHeight;

            double fanucW = ResolveHostWidth(FanucFloatingHost, FanucPanelResizeMinWidth);
            double fanucH = ResolveHostHeight(FanucFloatingHost, FanucPanelResizeMinHeight);
            double operatorW = ResolveHostWidth(OperatorFloatingHost, OperatorPanelResizeMinWidth);
            double operatorH = ResolveHostHeight(OperatorFloatingHost, OperatorPanelResizeMinHeight);

            double fanucLeft = Math.Max(0, viewportWidth - rightMargin - fanucW);
            double fanucTop = Math.Max(0, topMargin);
            Canvas.SetLeft(FanucFloatingHost, fanucLeft);
            Canvas.SetTop(FanucFloatingHost, fanucTop);

            double operatorLeft = Math.Max(0, viewportWidth - rightMargin - operatorW);
            double operatorTop = fanucTop + fanucH + rowGap;
            double maxBottomTop = Math.Max(topMargin, viewportHeight - operatorH - 8);
            if (operatorTop > maxBottomTop)
            {
                operatorTop = maxBottomTop;
            }

            Canvas.SetLeft(OperatorFloatingHost, operatorLeft);
            Canvas.SetTop(OperatorFloatingHost, Math.Max(0, operatorTop));
        }

        private void OperatorResetSizeButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            EnsureOperatorFloatingExpanded();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyFloatingHostSizeToFitContent(
                    OperatorFloatingHost,
                    OperatorFloatingHeader,
                    OperatorContentScroll,
                    OperatorPanel,
                    OperatorPanelResizeMinWidth,
                    OperatorPanelResizeMinHeight);

                _operatorExpandedHeight = OperatorFloatingHost.Height;
                OperatorFloatingHost.UpdateLayout();
                UpdateOperatorScrollViewport();
            }), DispatcherPriority.Loaded);
        }

        private void OperatorResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (OperatorFloatingHost == null || _isOperatorMinimized)
            {
                return;
            }

            double w = OperatorFloatingHost.Width;
            if (double.IsNaN(w) || w <= 0)
            {
                w = OperatorFloatingHost.ActualWidth;
            }

            double h = OperatorFloatingHost.Height;
            if (double.IsNaN(h) || h <= 0)
            {
                h = OperatorFloatingHost.ActualHeight;
            }

            OperatorFloatingHost.Width = Math.Max(OperatorPanelResizeMinWidth, w + e.HorizontalChange);
            OperatorFloatingHost.Height = Math.Max(OperatorPanelResizeMinHeight, h + e.VerticalChange);
        }

        private void OperatorFloatingHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isOperatorDragging = true;
            _operatorDragStart = e.GetPosition(this);
            _operatorStartLeft = Canvas.GetLeft(OperatorFloatingHost);
            _operatorStartTop = Canvas.GetTop(OperatorFloatingHost);
            OperatorFloatingHeader.CaptureMouse();
        }

        private void OperatorFloatingHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isOperatorDragging)
            {
                return;
            }

            Point current = e.GetPosition(this);
            double dx = current.X - _operatorDragStart.X;
            double dy = current.Y - _operatorDragStart.Y;
            Canvas.SetLeft(OperatorFloatingHost, Math.Max(0, _operatorStartLeft + dx));
            Canvas.SetTop(OperatorFloatingHost, Math.Max(0, _operatorStartTop + dy));
        }

        private void OperatorFloatingHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isOperatorDragging)
            {
                return;
            }

            _isOperatorDragging = false;
            OperatorFloatingHeader.ReleaseMouseCapture();
        }

        private RowDefinition? GetFanucFloatingBodyRowDefinition()
        {
            return FanucFloatingHost?.Child is Grid g && g.RowDefinitions.Count > 1
                ? g.RowDefinitions[1]
                : null;
        }

        private void FanucFloatingHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
            UpdateFanucScrollViewport();

        private void UpdateFanucScrollViewport()
        {
            if (FanucContentScroll == null || FanucFloatingHost == null || FanucFloatingHeader == null || _isFanucMinimized)
            {
                return;
            }

            double h = FanucFloatingHost.ActualHeight
                - FanucFloatingHeader.ActualHeight
                - FanucFloatingHost.BorderThickness.Top
                - FanucFloatingHost.BorderThickness.Bottom;
            FanucContentScroll.MaxHeight = Math.Max(40, h);
        }

        private void FanucMinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isFanucMinimized)
            {
                _fanucExpandedHeight = FanucFloatingHost.ActualHeight;
            }

            _isFanucMinimized = !_isFanucMinimized;
            FanucFloatingContent.Visibility = _isFanucMinimized ? Visibility.Collapsed : Visibility.Visible;
            FanucResizeThumb.Visibility = _isFanucMinimized ? Visibility.Collapsed : Visibility.Visible;

            RowDefinition? bodyRow = GetFanucFloatingBodyRowDefinition();

            if (_isFanucMinimized)
            {
                if (bodyRow != null)
                {
                    bodyRow.MinHeight = 0;
                    bodyRow.Height = new GridLength(0);
                }

                ApplyCollapsedFloatingHostSize(FanucFloatingHost, FanucFloatingHeader, FanucContentScroll);
                Border fnHostRef = FanucFloatingHost;
                Border fnHeaderRef = FanucFloatingHeader;
                ScrollViewer? fnScrollRef = FanucContentScroll;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isFanucMinimized)
                    {
                        return;
                    }

                    ApplyCollapsedFloatingHostSize(fnHostRef, fnHeaderRef, fnScrollRef);
                }), DispatcherPriority.Loaded);
            }
            else
            {
                if (bodyRow != null)
                {
                    bodyRow.Height = new GridLength(1, GridUnitType.Star);
                }

                FanucFloatingHost.ClearValue(FrameworkElement.MinHeightProperty);
                FanucFloatingHost.ClearValue(FrameworkElement.MaxHeightProperty);
                FanucFloatingHost.MinHeight = FanucPanelResizeMinHeight;

                if (!double.IsNaN(_fanucExpandedHeight) && _fanucExpandedHeight >= FanucPanelResizeMinHeight - 1)
                {
                    FanucFloatingHost.Height = _fanucExpandedHeight;
                }
                else
                {
                    FanucFloatingHost.ClearValue(FrameworkElement.HeightProperty);
                }
            }

            FanucMinimizeButton.Content = _isFanucMinimized ? "□" : "_";

            if (!_isFanucMinimized)
            {
                Dispatcher.BeginInvoke(new Action(UpdateFanucScrollViewport), DispatcherPriority.Loaded);
            }
        }

        private void EnsureFanucFloatingExpanded()
        {
            if (!_isFanucMinimized)
            {
                return;
            }

            _isFanucMinimized = false;
            FanucFloatingContent.Visibility = Visibility.Visible;
            FanucResizeThumb.Visibility = Visibility.Visible;
            RowDefinition? bodyRow = GetFanucFloatingBodyRowDefinition();
            if (bodyRow != null)
            {
                bodyRow.MinHeight = 0;
                bodyRow.Height = new GridLength(1, GridUnitType.Star);
            }

            FanucFloatingHost.ClearValue(FrameworkElement.MinHeightProperty);
            FanucFloatingHost.ClearValue(FrameworkElement.MaxHeightProperty);
            FanucFloatingHost.MinHeight = FanucPanelResizeMinHeight;
            FanucMinimizeButton.Content = "_";
        }

        private void FanucResetSizeButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            EnsureFanucFloatingExpanded();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyFloatingHostSizeToFitContent(
                    FanucFloatingHost,
                    FanucFloatingHeader,
                    FanucContentScroll,
                    FanucPanel,
                    FanucPanelResizeMinWidth,
                    FanucPanelResizeMinHeight);

                _fanucExpandedHeight = FanucFloatingHost.Height;
                FanucFloatingHost.UpdateLayout();
                UpdateFanucScrollViewport();
            }), DispatcherPriority.Loaded);
        }

        private void FanucResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (FanucFloatingHost == null || _isFanucMinimized)
            {
                return;
            }

            double w = FanucFloatingHost.Width;
            if (double.IsNaN(w) || w <= 0)
            {
                w = FanucFloatingHost.ActualWidth;
            }

            double h = FanucFloatingHost.Height;
            if (double.IsNaN(h) || h <= 0)
            {
                h = FanucFloatingHost.ActualHeight;
            }

            FanucFloatingHost.Width = Math.Max(FanucPanelResizeMinWidth, w + e.HorizontalChange);
            FanucFloatingHost.Height = Math.Max(FanucPanelResizeMinHeight, h + e.VerticalChange);
        }

        private void FanucFloatingHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isFanucDragging = true;
            _fanucDragStart = e.GetPosition(this);
            _fanucStartLeft = Canvas.GetLeft(FanucFloatingHost);
            _fanucStartTop = Canvas.GetTop(FanucFloatingHost);
            FanucFloatingHeader.CaptureMouse();
        }

        private void FanucFloatingHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isFanucDragging)
            {
                return;
            }

            Point current = e.GetPosition(this);
            double dx = current.X - _fanucDragStart.X;
            double dy = current.Y - _fanucDragStart.Y;
            Canvas.SetLeft(FanucFloatingHost, Math.Max(0, _fanucStartLeft + dx));
            Canvas.SetTop(FanucFloatingHost, Math.Max(0, _fanucStartTop + dy));
        }

        private void FanucFloatingHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isFanucDragging)
            {
                return;
            }

            _isFanucDragging = false;
            FanucFloatingHeader.ReleaseMouseCapture();
        }

        private void MenuCycleStart_Click(object sender, RoutedEventArgs e) => OnFanucCycleStartRequested();
        private void MenuFeedHold_Click(object sender, RoutedEventArgs e) => OnFanucFeedHoldRequested();
        private void MenuResetStop_Click(object sender, RoutedEventArgs e) => OnFanucResetRequested();

        private void MenuProgramDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Owner = this,
                Title = "Симуляция - Программа",
                Width = 780,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });

            var list = new ListBox
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                ItemsSource = GCodeList.ItemsSource
            };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedIndex >= 0 && GCodeList.SelectedIndex != list.SelectedIndex)
                {
                    GCodeList.SelectedIndex = list.SelectedIndex;
                    GCodeList.ScrollIntoView(GCodeList.SelectedItem);
                }
            };
            list.SelectedIndex = GCodeList.SelectedIndex;
            Grid.SetRow(list, 0);
            root.Children.Add(list);

            var btns = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 10) };
            btns.Children.Add(new Button { Content = "Cycle Start", Margin = new Thickness(3) });
            btns.Children.Add(new Button { Content = "Feed Hold", Margin = new Thickness(3) });
            btns.Children.Add(new Button { Content = "Reset / Stop", Margin = new Thickness(3) });
            ((Button)btns.Children[0]).Click += (_, _) => OnFanucCycleStartRequested();
            ((Button)btns.Children[1]).Click += (_, _) => OnFanucFeedHoldRequested();
            ((Button)btns.Children[2]).Click += (_, _) => OnFanucResetRequested();
            Grid.SetRow(btns, 1);
            root.Children.Add(btns);

            var stats = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Background = Brushes.White,
                Text = StatsBox.Text
            };
            Grid.SetRow(stats, 2);
            root.Children.Add(stats);

            dialog.Content = root;
            dialog.ShowDialog();
        }

        private void MenuToolsDialog_Click(object sender, RoutedEventArgs e)
        {
            if (_toolSettingsDialog != null)
            {
                if (_toolSettingsDialog.IsLoaded)
                {
                    _toolSettingsDialog.Activate();
                    return;
                }

                _toolSettingsDialog = null;
            }

            var wnd = new ToolSettingsWindow(
                    _tools,
                    GetProgramReferencedToolNumbers(),
                    vm => vm.PropertyChanged += Tool_PropertyChanged,
                    vm => vm.PropertyChanged -= Tool_PropertyChanged,
                    OnToolSettingsAppliedFromDialog)
            {
                Owner = this
            };

            wnd.Closed += (_, _) =>
            {
                if (ReferenceEquals(_toolSettingsDialog, wnd))
                {
                    _toolSettingsDialog = null;
                }
            };

            _toolSettingsDialog = wnd;
            wnd.Show();
        }

        private void OnToolSettingsAppliedFromDialog()
        {
            InvalidateToolGeometryCache();
            if (ToolsList.SelectedItem is ToolViewModel active)
            {
                UpdateToolGeometry(active, GetCurrentPosition());
            }
            else
            {
                UpdateToolGeometry(null, GetCurrentPosition());
            }

            FanucPanel.LogUserAction("Инструмент: применены параметры (Симуляция → Инструмент)");
        }

        private IReadOnlyList<int> GetProgramReferencedToolNumbers()
        {
            if (_currentParser == null)
            {
                return Array.Empty<int>();
            }

            return _currentParser.Commands
                .Where(c => c.ToolNumber.HasValue)
                .Select(c => c.ToolNumber!.Value)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        private void MenuStockDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Owner = this,
                Title = "Симуляция - Заготовка",
                Width = 460,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var root = new StackPanel { Margin = new Thickness(12) };
            var visible = new CheckBox { Content = "Показывать заготовку", IsChecked = StockVisibleCheck.IsChecked, Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(visible);

            root.Children.Add(new TextBlock { Text = "Минимум (X, Y, Z)" });
            var min = new UniformGrid { Columns = 3, Margin = new Thickness(0, 3, 0, 8) };
            var minX = new TextBox { Text = StockMinX.Text, Margin = new Thickness(2) };
            var minY = new TextBox { Text = StockMinY.Text, Margin = new Thickness(2) };
            var minZ = new TextBox { Text = StockMinZ.Text, Margin = new Thickness(2) };
            min.Children.Add(minX); min.Children.Add(minY); min.Children.Add(minZ);
            root.Children.Add(min);

            root.Children.Add(new TextBlock { Text = "Максимум (X, Y, Z)" });
            var max = new UniformGrid { Columns = 3, Margin = new Thickness(0, 3, 0, 8) };
            var maxX = new TextBox { Text = StockMaxX.Text, Margin = new Thickness(2) };
            var maxY = new TextBox { Text = StockMaxY.Text, Margin = new Thickness(2) };
            var maxZ = new TextBox { Text = StockMaxZ.Text, Margin = new Thickness(2) };
            max.Children.Add(maxX); max.Children.Add(maxY); max.Children.Add(maxZ);
            root.Children.Add(max);

            root.Children.Add(new TextBlock { Text = "Разрешение (мм)" });
            var res = new ComboBox { Margin = new Thickness(0, 3, 0, 8) };
            res.Items.Add(new ComboBoxItem { Content = "Высокая (0.1)", Tag = "0.1" });
            res.Items.Add(new ComboBoxItem { Content = "Средняя (0.3)", Tag = "0.3" });
            res.Items.Add(new ComboBoxItem { Content = "Грубая (0.6)", Tag = "0.6" });
            if (Math.Abs(_selectedResolution - 0.1) < 0.0001) res.SelectedIndex = 0;
            else if (Math.Abs(_selectedResolution - 0.3) < 0.0001) res.SelectedIndex = 1;
            else res.SelectedIndex = 2;
            root.Children.Add(res);

            root.Children.Add(new TextBlock { Text = "Цвет" });
            var color = new ComboBox { Margin = new Thickness(0, 3, 0, 12) };
            foreach (ComboBoxItem item in StockColorCombo.Items)
            {
                color.Items.Add(new ComboBoxItem { Content = item.Content, Tag = item.Tag });
            }
            color.SelectedIndex = Math.Max(0, StockColorCombo.SelectedIndex);
            root.Children.Add(color);

            var apply = new Button { Content = "Применить", Width = 120, HorizontalAlignment = HorizontalAlignment.Right };
            apply.Click += (_, _) =>
            {
                StockVisibleCheck.IsChecked = visible.IsChecked;
                StockMinX.Text = minX.Text; StockMinY.Text = minY.Text; StockMinZ.Text = minZ.Text;
                StockMaxX.Text = maxX.Text; StockMaxY.Text = maxY.Text; StockMaxZ.Text = maxZ.Text;
                if (res.SelectedItem is ComboBoxItem r && double.TryParse(r.Tag?.ToString(), out double rv))
                {
                    _selectedResolution = rv;
                    UpdateResButtons();
                }
                if (color.SelectedItem is ComboBoxItem c)
                {
                    StockColorCombo.SelectedIndex = color.SelectedIndex;
                }

                StockVisible_Changed(this, new RoutedEventArgs());
                ApplyStock();
                dialog.DialogResult = true;
                dialog.Close();
            };
            root.Children.Add(apply);

            dialog.Content = root;
            dialog.ShowDialog();
        }

        private void MenuSpeedPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item &&
                double.TryParse(item.Tag?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double multiplier))
            {
                _simulationMultiplier = multiplier;
                StatusText.Text = $"Множитель скорости: x{multiplier}";
            }
        }

        private void MenuResolution_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item &&
                double.TryParse(item.Tag?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double res))
            {
                _selectedResolution = res;
                UpdateResButtons();
                if (IsLoaded)
                {
                    ApplyStock();
                }
            }
        }

        private void MenuStockVisible_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item)
            {
                StockVisibleCheck.IsChecked = item.IsChecked;
                StockVisible_Changed(item, e);
            }
        }

        private void OnMachineStateChanged(MachineStateChangedEvent evt)
        {
            lock (_machineStateUiSync)
            {
                _pendingMachineStateEvent = evt;
                if (_machineStateUiUpdateQueued)
                {
                    return;
                }

                _machineStateUiUpdateQueued = true;
            }

            Dispatcher.BeginInvoke(new Action(ApplyPendingMachineStateUpdate), DispatcherPriority.Render);
        }

        private void ApplyPendingMachineStateUpdate()
        {
            MachineStateChangedEvent? evt;
            lock (_machineStateUiSync)
            {
                evt = _pendingMachineStateEvent;
                _pendingMachineStateEvent = null;
                _machineStateUiUpdateQueued = false;
            }

            if (evt == null)
            {
                return;
            }

            _isControllerRunning = evt.IsRunning;
            StatusText.Text = evt.IsRunning ? "Controller: Cycle start" : "Controller: Feed hold";
            FanucPanel.UpdateRunState(evt.IsRunning);
            FanucPanel.UpdateMachinePosition(evt.X, evt.Y, evt.Z);
            PositionText.Text = $"X: {evt.X:F3} Y: {evt.Y:F3} Z: {evt.Z:F3}";
            string spindleCode = evt.IsSpindleOn ? (evt.IsSpindleCW ? "M3" : "M4") : "M5";
            string toolDisplay = evt.ToolNumber.HasValue ? $"T{evt.ToolNumber.Value}" : "-";
            FanucPanel.UpdatePosRuntime(evt.FeedRate, toolDisplay, evt.SpindleSpeed, spindleCode, evt.IsCoolantOn);
            _currentCoordSystem = evt.CoordinateSystem;
            _currentOffsetX = evt.OffsetX;
            _currentOffsetY = evt.OffsetY;
            _currentOffsetZ = evt.OffsetZ;
            FanucPanel.UpdateOffsets(_currentCoordSystem, _currentOffsetX, _currentOffsetY, _currentOffsetZ);

            var machinePos = new Point3D(evt.X, evt.Y, evt.Z);
            _lastPosition = machinePos;
            _uiRenderService.SyncToolVisual(
                evt.ToolNumber,
                _tools,
                ToolsList,
                Viewport,
                _toolVisual,
                machinePos,
                UpdateToolGeometry,
                tool => tool.PropertyChanged += Tool_PropertyChanged,
                _currentParser == null || GCodeList.SelectedIndex < 0);
        }

        private void OnAlarmRaised(AlarmRaisedEvent evt)
        {
            Dispatcher.Invoke(() =>
            {
                if (evt.Code.StartsWith("MDI", StringComparison.OrdinalIgnoreCase))
                {
                    _lastMdiStatus = "ERROR";
                }
                _lastInterlockCode = evt.Code;
                _lastInterlockDetail = evt.Message;
                StatusText.Text = $"Alarm {evt.Code}: {evt.Message}";
                FanucPanel.SetAlarm($"{evt.Code} {evt.Message}", true);
                RefreshDiagnosticsPanel();
            });
        }

        private void OnMdiModeChanged(MdiModeChangedEvent evt)
        {
            Dispatcher.Invoke(() =>
            {
                _currentMdiMode = evt.Mode;
                RefreshDiagnosticsPanel();
            });
        }

        private void OnProgramLineExecuted(ProgramLineExecutedEvent evt)
        {
            Dispatcher.Invoke(() => FanucPanel.UpdateProgramLine(evt.LineNumber));
        }

        private void OnWorkOffsetsChanged(WorkOffsetsChangedEvent evt)
        {
            Dispatcher.Invoke(() =>
            {
                _workOffsets.Clear();
                foreach (var offset in evt.Offsets)
                {
                    _workOffsets[offset.CoordinateSystemNumber] = (offset.X, offset.Y, offset.Z);
                }

                UpdateOffsetEditorBySystem(_selectedOffsetSystem);
            });
        }

        private void OnFanucCycleStartRequested()
        {
            if (_controllerCore.CycleStart())
            {
                _lastInterlockCode = "OK";
                _lastInterlockDetail = "Cycle start allowed";
                if (!_runTimeStopwatch.IsRunning) _runTimeStopwatch.Start();
                if (!_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Start();
                StartAnimationCycle();
                RefreshDiagnosticsPanel();
            }
        }

        private void OnFanucFeedHoldRequested()
        {
            if (_controllerCore.FeedHold())
            {
                _lastInterlockCode = "OK";
                _lastInterlockDetail = "Feed hold command accepted";
                if (_runTimeStopwatch.IsRunning) _runTimeStopwatch.Stop();
                if (_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Stop();
                _accumulatedRunTime = _runTimeStopwatch.Elapsed;
                _accumulatedCycleTime = _cycleTimeStopwatch.Elapsed;
                PauseAnimationCycle();
                RefreshDiagnosticsPanel();
            }
        }

        private void OnFanucResetRequested()
        {
            _controllerCore.Reset();
            _lastInterlockCode = "OK";
            _lastInterlockDetail = "Reset command accepted";
            _runTimeStopwatch.Reset();
            _cycleTimeStopwatch.Reset();
            _accumulatedRunTime = TimeSpan.Zero;
            _accumulatedCycleTime = TimeSpan.Zero;
            StopAnimationCycle();
            RefreshDiagnosticsPanel();
        }

        private void OnFanucModeChangedRequested(string mode)
        {
            string normalizedMode = mode == "ZERO_RETURN" ? "ZeroReturn" : mode;
            if (_controllerCore.SetMode(normalizedMode))
            {
                _currentControllerMode = mode;
                _lastInterlockCode = "OK";
                _lastInterlockDetail = $"Mode changed to {mode}";
                StatusText.Text = $"Controller mode: {mode}";
                FanucPanel.SetAlarm("NONE", false);
                FanucPanel.UpdateMode(mode);
                RefreshDiagnosticsPanel();
                OperatorPanel.HighlightMode(_currentControllerMode);
                return;
            }

            FanucPanel.UpdateMode(_currentControllerMode);
            OperatorPanel.HighlightMode(_currentControllerMode);
        }

        private void OnOperatorModeSelected(object? sender, string mode) => OnFanucModeChangedRequested(mode);

        private void OnOperatorJogAxisDelta(object? sender, (string Axis, double Delta) args) =>
            OnFanucJogRequested(args.Axis, args.Delta);

        private void OperatorPanel_CycleStart(object? sender, EventArgs e) => OnFanucCycleStartRequested();

        private void OperatorPanel_FeedHold(object? sender, EventArgs e) => OnFanucFeedHoldRequested();

        private void OperatorPanel_CycleStop(object? sender, EventArgs e) => OnFanucFeedHoldRequested();

        private void OperatorPanel_EmergencyReset(object? sender, EventArgs e) => OnFanucResetRequested();

        private void OnOperatorFeedWorkOverridePercentChanged(object? sender, double percent)
        {
            if (_suppressOperatorFeedOverrideSync || WorkOverrideSlider == null)
            {
                return;
            }

            _suppressOperatorFeedOverrideSync = true;
            try
            {
                double v = Math.Clamp(percent, WorkOverrideSlider.Minimum, WorkOverrideSlider.Maximum);
                WorkOverrideSlider.Value = v;
                UpdateOverrideTexts();
                RefreshDiagnosticsPanel();
            }
            finally
            {
                _suppressOperatorFeedOverrideSync = false;
            }
        }

        private void OnOperatorSpindleOverridePercentChanged(object? sender, double percent) =>
            _operatorSpindleOverridePercent = percent;

        private void OnFanucJogRequested(string axis, double delta)
        {
            if (_controllerCore.Jog(axis, delta))
            {
                _lastInterlockCode = "OK";
                _lastInterlockDetail = $"Jog {axis}{delta:+0.0;-0.0} accepted";
                StatusText.Text = $"Jog {axis}{delta:+0.0;-0.0}";
                RefreshDiagnosticsPanel();
            }
        }

        private void OnFanucMdiExecuteRequested(string command)
        {
            _lastMdiCommand = string.IsNullOrWhiteSpace(command) ? "-" : command.Trim();
            if (_controllerCore.ExecuteMdi(command))
            {
                _lastInterlockCode = "OK";
                _lastInterlockDetail = "MDI command accepted";
                _lastMdiStatus = "OK";
                StatusText.Text = $"MDI executed: {command}";
                RefreshDiagnosticsPanel();
            }
            else
            {
                _lastMdiStatus = "ERROR";
                RefreshDiagnosticsPanel();
            }
        }

        private void OnFanucProgDirectoryCreateAndOpenRequested(string normalizedOLine)
        {
            string fileName = normalizedOLine + ".nc";
            if (string.IsNullOrEmpty(normalizedOLine) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                fileName.Contains(Path.DirectorySeparatorChar) ||
                fileName.Contains(Path.AltDirectorySeparatorChar))
            {
                FanucPanel.SetAlarm("PROGRAM NAME INVALID", true);
                return;
            }

            try
            {
                string dir = FanucPanel.ResolveNcProgramsDirectory();
                string path = Path.Combine(dir, fileName);

                if (File.Exists(path))
                {
                    FanucPanel.SetAlarm($"PROGRAM FILE EXISTS ({fileName})", true);
                    return;
                }

                string fullPath = Path.GetFullPath(path);
                string? parentDir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(parentDir))
                    Directory.CreateDirectory(parentDir);

                File.WriteAllText(path, normalizedOLine + Environment.NewLine);

                LoadAndRender(path);
                FanucPanel.NavigateToProgMainScreen();
                FanucPanel.ClearMdiInputBuffer();
            }
            catch (Exception ex)
            {
                FanucPanel.SetAlarm($"NEW PROGRAM: {ex.Message}", true);
            }
        }

        private void OnFanucProgOpenByNameRequested(string normalizedOLine)
        {
            if (!FanucPanel.TryResolveNcProgramFile(normalizedOLine, out string? path) || !File.Exists(path))
            {
                return;
            }

            LoadAndRender(path);
            FanucPanel.NavigateToProgMainScreen();
        }

        private void OnFanucProgDeleteProgramByNameRequested(string normalizedOLine)
        {
            if (!FanucPanel.TryResolveNcProgramFile(normalizedOLine, out string? path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                FanucPanel.SetAlarm($"DELETE: {ex.Message}", true);
                return;
            }

            string deletedFull = Path.GetFullPath(path);
            if (_loadedNcProgramPath != null
                && string.Equals(deletedFull, _loadedNcProgramPath, StringComparison.OrdinalIgnoreCase))
            {
                ClearLoadedNcProgram();
            }

            FanucPanel.LogUserAction($"Удалён файл: {Path.GetFileName(path)}");
            FanucPanel.RefreshDirectoryListingFromDisk();
        }

        private void ClearLoadedNcProgram()
        {
            ClearToolpath();
            _lineVisualsMap.Clear();
            _tools.Clear();
            _currentLines = Array.Empty<string>();
            GCodeList.ItemsSource = _currentLines;
            _currentParser = null;
            _programExecutionService.LoadProgram(Array.Empty<ParsedCommand>());
            _programExecutionService.SetCurrentIndex(0);
            _loadedNcProgramPath = null;
            StatsBox.Text = string.Empty;
            FanucPanel.SetNcProgramSource(Array.Empty<string>(), null);
            FanucPanel.LogUserAction("Программа снята (очистка)");
        }

        private void OnFanucSingleBlockChangedRequested(bool enabled)
        {
            _isSingleBlockEnabled = enabled;
            _programExecutionService.SetSingleBlock(enabled);
            _lastInterlockCode = "SETTING";
            _lastInterlockDetail = enabled ? "Single block enabled" : "Single block disabled";
            RefreshDiagnosticsPanel();
            OperatorPanel.SyncMachiningToggles(_isSingleBlockEnabled, _isOptionalStopEnabled);
        }

        private void OnFanucOptionalStopChangedRequested(bool enabled)
        {
            _isOptionalStopEnabled = enabled;
            _programExecutionService.SetOptionalStop(enabled);
            _lastInterlockCode = "SETTING";
            _lastInterlockDetail = enabled ? "Optional stop enabled" : "Optional stop disabled";
            RefreshDiagnosticsPanel();
            OperatorPanel.SyncMachiningToggles(_isSingleBlockEnabled, _isOptionalStopEnabled);
        }

        private void OperatorPanel_SingleBlockChanged(object? sender, bool enabled) =>
            OnFanucSingleBlockChangedRequested(enabled);

        private void OperatorPanel_OptionalStopChanged(object? sender, bool enabled) =>
            OnFanucOptionalStopChangedRequested(enabled);

        private void OnFanucDryRunChangedRequested(bool enabled)
        {
            _isDryRunEnabled = enabled;
            _lastInterlockCode = "SETTING";
            _lastInterlockDetail = enabled ? "Dry run enabled" : "Dry run disabled";
            RefreshDiagnosticsPanel();
        }

        private void OnFanucOffsetUpdateRequested(int coordinateSystemNumber, double? x, double? y, double? z)
        {
            int pNumber = coordinateSystemNumber - 53;
            string mdi = $"G10 L2 P{pNumber}";
            if (x.HasValue) mdi += $" X{x.Value:0.###}";
            if (y.HasValue) mdi += $" Y{y.Value:0.###}";
            if (z.HasValue) mdi += $" Z{z.Value:0.###}";
            OnFanucMdiExecuteRequested(mdi);
        }

        private void OnFanucOffsetSystemSelectionChangedRequested(int coordinateSystemNumber)
        {
            _selectedOffsetSystem = coordinateSystemNumber;
            UpdateOffsetEditorBySystem(coordinateSystemNumber);
        }

        private void OnFanucOffsetReadActiveToEditorRequested()
        {
            if (_currentCoordSystem.Length == 3 &&
                _currentCoordSystem.StartsWith("G", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(_currentCoordSystem[1..], out int systemNumber))
            {
                _selectedOffsetSystem = systemNumber;
                UpdateOffsetEditorBySystem(systemNumber);
            }
        }

        private void RefreshDiagnosticsPanel()
        {
            var runTime = _runTimeStopwatch.IsRunning ? _runTimeStopwatch.Elapsed : _accumulatedRunTime;
            var cycleTime = _cycleTimeStopwatch.IsRunning ? _cycleTimeStopwatch.Elapsed : _accumulatedCycleTime;
            FanucPanel.UpdatePosTimers(runTime, cycleTime);
            FanucPanel.UpdateDiagnostics(
                _currentControllerMode,
                _currentMdiMode,
                _isControllerRunning,
                _lastInterlockCode,
                _lastInterlockDetail,
                "LIMITS X[-500;500] Y[-500;500] Z[-300;300]",
                _lastMdiCommand,
                _lastMdiStatus,
                _isSingleBlockEnabled,
                _isOptionalStopEnabled,
                _isDryRunEnabled);
            FanucPanel.UpdateOffsets(_currentCoordSystem, _currentOffsetX, _currentOffsetY, _currentOffsetZ);
            UpdateOffsetEditorBySystem(_selectedOffsetSystem);
            FanucPanel.UpdateSettings(
                WorkOverrideSlider?.Value ?? 100,
                RapidOverrideSlider?.Value ?? 100,
                _isSingleBlockEnabled,
                _isOptionalStopEnabled,
                _isDryRunEnabled);
            FanucPanel.UpdateSystemPage(
                _systemUnitsText,
                _systemCoordModeText,
                _systemPlaneText,
                _systemMotionText,
                _currentCoordSystem,
                _systemCompText);
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
            _toolVisual.Transform = _toolTransform;
        }

        private void UpdateToolGeometry(ToolViewModel? tool, Point3D position)
        {
            if (tool == null)
            {
                _fluteModel.Geometry = null;
                _shankModel.Geometry = null;
                _cachedToolGeometryTool = null;
                _hasCachedToolGeometry = false;
                return;
            }

            EnsureToolGeometry(tool);
            UpdateToolTransform(position);
        }

        private void EnsureToolGeometry(ToolViewModel tool)
        {
            var key = (tool.Diameter, tool.ShankDiameter, tool.FluteLength, tool.OverallLength, tool.FluteColor);
            if (_hasCachedToolGeometry && ReferenceEquals(_cachedToolGeometryTool, tool) && _cachedToolGeometryKey.Equals(key))
            {
                return;
            }

            double fluteRadius = tool.Diameter / 2.0;
            double shankRadius = tool.ShankDiameter / 2.0;
            double shankEnd = Math.Max(tool.FluteLength, tool.OverallLength);

            var fluteBuilder = new MeshBuilder();
            fluteBuilder.AddCylinder(new Point3D(0, 0, 0), new Point3D(0, 0, tool.FluteLength), fluteRadius, 20, true, true);
            _fluteModel.Geometry = fluteBuilder.ToMesh();

            Material fluteMat = MaterialHelper.CreateMaterial(tool.FluteColor);
            _fluteModel.Material = fluteMat;
            _fluteModel.BackMaterial = fluteMat;

            var shankBuilder = new MeshBuilder();
            shankBuilder.AddCylinder(new Point3D(0, 0, tool.FluteLength), new Point3D(0, 0, shankEnd), shankRadius, 20, true, true);
            _shankModel.Geometry = shankBuilder.ToMesh();

            _cachedToolGeometryTool = tool;
            _cachedToolGeometryKey = key;
            _hasCachedToolGeometry = true;
        }

        private void InvalidateToolGeometryCache()
        {
            _cachedToolGeometryTool = null;
            _hasCachedToolGeometry = false;
        }

        private void UpdateToolTransform(Point3D position)
        {
            _toolTransform.OffsetX = position.X;
            _toolTransform.OffsetY = position.Y;
            _toolTransform.OffsetZ = position.Z;
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

        private void ApplyRuntimeStatusForLine(int selectedLine)
        {
            if (_currentParser == null || selectedLine <= 0)
            {
                return;
            }

            var lastCmd = _currentParser.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);
            if (lastCmd == null)
            {
                return;
            }

            FanucPanel.UpdateProgramLine(lastCmd.LineNumber);
            var state = lastCmd.EndState;
            ApplyRuntimeStatus(state);
            if (_machineCore is MachineCore runtimeSyncMachineCore)
            {
                runtimeSyncMachineCore.SyncRuntimeFromState(state);
            }
        }

        private void UpdateProgramProgress()
        {
            if (MainProgressBar != null && _currentLines.Length > 0)
            {
                MainProgressBar.Value = (double)(GCodeList.SelectedIndex + 1) / _currentLines.Length * 100;
            }
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
            UpdateOverrideTexts();
            if (!_suppressOperatorFeedOverrideSync && ReferenceEquals(sender, WorkOverrideSlider))
            {
                OperatorPanel.SyncWorkOverrideSlider(WorkOverrideSlider.Value);
            }
        }

        private void UpdateOverrideTexts()
        {
            if (WorkOverrideText != null && WorkOverrideSlider != null)
                WorkOverrideText.Text = WorkOverrideSlider.Value.ToString("F0") + "%";
            
            if (RapidOverrideText != null && RapidOverrideSlider != null)
                RapidOverrideText.Text = RapidOverrideSlider.Value.ToString("F0") + "%";
        }

        private async void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            FanucPanel.UpdatePosTimers(
                _runTimeStopwatch.IsRunning ? _runTimeStopwatch.Elapsed : _accumulatedRunTime,
                _cycleTimeStopwatch.IsRunning ? _cycleTimeStopwatch.Elapsed : _accumulatedCycleTime);

            var machineCore = _machineCore as MachineCore;
            if (machineCore != null && !machineCore.State.IsRunning)
            {
                return;
            }

            if (_currentParser == null)
            {
                StopAnimationCycle();
                return;
            }

            var tick = _playbackLoopService.Tick(
                machineCore?.State.IsRunning ?? false,
                _lastPosition,
                GCodeList.SelectedIndex,
                state => GetPhysicalSpeed(state),
                _simulationMultiplier,
                _fpsSlowdownFactor);

            if (tick.Action == PlaybackLoopAction.StopProgram)
            {
                StopCycleForProgramEnd(tick.EndProgramMCode, tick.RewindToStart);
                return;
            }

            if (tick.HasIndexUpdate && GCodeList.SelectedIndex != tick.NewIndex)
            {
                _suppressSelectionSideEffects = true;
                GCodeList.SelectedIndex = tick.NewIndex;
                GCodeList.ScrollIntoView(GCodeList.SelectedItem);
                SyncToolWithState();
                ApplyRuntimeStatusForLine(tick.NewIndex + 1);
                UpdateProgramProgress();
                _suppressSelectionSideEffects = false;
            }

            _animationTimer.Interval = TimeSpan.FromMilliseconds(tick.TimerIntervalMs);
            _interpolationProgress = tick.Progress;
            Point3D currentPos = tick.CurrentPosition;

            if (tick.Action == PlaybackLoopAction.PauseForOptionalStop)
            {
                _suppressSelectionSideEffects = true;
                GCodeList.SelectedIndex = tick.NewIndex;
                GCodeList.ScrollIntoView(GCodeList.SelectedItem);
                SyncToolWithState();
                ApplyRuntimeStatusForLine(tick.NewIndex + 1);
                UpdateProgramProgress();
                _suppressSelectionSideEffects = false;

                if (_controllerCore.FeedHold())
                {
                    _lastInterlockCode = "OPTIONAL_STOP";
                    _lastInterlockDetail = "Program paused at M1";
                    PauseAnimationCycle();
                    RefreshDiagnosticsPanel();
                }
                return;
            }

            PositionText.Text = $"X: {currentPos.X:F3} Y: {currentPos.Y:F3} Z: {currentPos.Z:F3}";

            if (ToolsList.SelectedItem is ToolViewModel tool)
            {
                UpdateToolGeometry(tool, currentPos);
                _stockRenderService.ProcessCutStep(
                    _isDryRunEnabled,
                    _stock,
                    _stockCutWorker,
                    StockVisibleCheck,
                    tool,
                    _lastPosition,
                    currentPos);

                if (_stockRenderService.ShouldRefreshStock(_stock, _isStockUpdating, 250))
                {
                    _ = UpdateStockMeshAsync();
                }
            }
            _lastPosition = currentPos;
            _machineCore.UpdatePosition(currentPos.X, currentPos.Y, currentPos.Z);
            _machineCore.Tick(0.01);

            if (tick.Action == PlaybackLoopAction.PauseForSingleBlock)
            {
                if (_controllerCore.FeedHold())
                {
                    _lastInterlockCode = "SINGLE_BLOCK";
                    _lastInterlockDetail = "Program paused after block";
                    PauseAnimationCycle();
                    RefreshDiagnosticsPanel();
                }
            }
        }

        private async Task UpdateStockMeshAsync(bool showProgress = false)
        {
            if (_stock == null || _isStockUpdating) return;
            _isStockUpdating = true;
            
            try
            {
                await _stockRenderService.RefreshStockVisualAsync(_stock, _stockVisual, StockProgressPanel, showProgress);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stock update error: {ex.Message}");
            }
            finally 
            { 
                _isStockUpdating = false;
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_controllerCore.CycleStart())
            {
                StartAnimationCycle();
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_controllerCore.FeedHold())
            {
                PauseAnimationCycle();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _controllerCore.Reset();
            StopAnimationCycle();
        }

        private void StartAnimationCycle()
        {
            if (GCodeList.Items.Count == 0) return;
            if (GCodeList.SelectedIndex >= GCodeList.Items.Count - 1) GCodeList.SelectedIndex = 0;
            _programExecutionService.SetCurrentIndex(GCodeList.SelectedIndex);
            _programExecutionService.BeginCycle();
            _playbackLoopService.BeginSegmentFrom(_lastPosition);
            _animationTimer.Start();
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
        }

        private void PauseAnimationCycle()
        {
            _animationTimer.Stop();
            _programExecutionService.ClearSingleBlockStop();
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
        }

        private void StopCycleForProgramEnd(int? endProgramMCode, bool rewindToStart)
        {
            _animationTimer.Stop();
            _programExecutionService.ClearSingleBlockStop();
            if (_runTimeStopwatch.IsRunning) _runTimeStopwatch.Stop();
            if (_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Stop();
            _accumulatedRunTime = _runTimeStopwatch.Elapsed;
            _accumulatedCycleTime = _cycleTimeStopwatch.Elapsed;
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = false;

            // Конец программы всегда останавливает цикл.
            _controllerCore.FeedHold();

            if (rewindToStart && GCodeList.Items.Count > 0)
            {
                _suppressSelectionSideEffects = true;
                GCodeList.SelectedIndex = 0;
                GCodeList.ScrollIntoView(GCodeList.SelectedItem);
                _suppressSelectionSideEffects = false;

                _programExecutionService.SetCurrentIndex(0);
                _lastPosition = GetCurrentPosition();
                _interpolationProgress = 1.0;
                _playbackLoopService.BeginSegmentFrom(_lastPosition);
                ApplyRuntimeStatusForLine(1);
                UpdateProgramProgress();
            }
            else
            {
                _programExecutionService.SetCurrentIndex(Math.Max(0, GCodeList.SelectedIndex));
            }

            _lastInterlockCode = endProgramMCode == 30 ? "M30" : endProgramMCode == 2 ? "M2" : "PROGRAM_END";
            _lastInterlockDetail = endProgramMCode == 30
                ? "M30: cycle stop and rewind to start"
                : endProgramMCode == 2
                    ? "M2: cycle stop"
                    : "Program ended";
            RefreshDiagnosticsPanel();
        }

        private void StopAnimationCycle()
        {
            _animationTimer.Stop();
            _programExecutionService.ClearSingleBlockStop();
            if (_runTimeStopwatch.IsRunning) _runTimeStopwatch.Stop();
            if (_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Stop();
            _accumulatedRunTime = _runTimeStopwatch.Elapsed;
            _accumulatedCycleTime = _cycleTimeStopwatch.Elapsed;
            GCodeList.SelectedIndex = 0;
            _programExecutionService.SetCurrentIndex(0);
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            _lastPosition = new Point3D(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z);
            _interpolationProgress = 1.0;
            _playbackLoopService.Reset(_lastPosition);
            FanucPanel.UpdateProgramLine(null);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateResButtons();
            UpdateOverrideTexts();

            static bool TryNcPath(string fileName, out string resolved)
            {
                foreach (string root in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
                {
                    string p = Path.Combine(root, fileName);
                    if (File.Exists(p))
                    {
                        resolved = Path.GetFullPath(p);
                        return true;
                    }
                }

                resolved = string.Empty;
                return false;
            }

            string ncPath;
            if (!TryNcPath("O0001.nc", out ncPath))
            {
                if (!TryNcPath("program.nc", out ncPath))
                {
                    ncPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "program.nc");
                    CreateDemoNc(ncPath);
                }
            }

            LoadAndRender(ncPath);

            OperatorPanel.HighlightMode(_currentControllerMode);
            if (WorkOverrideSlider != null)
            {
                OperatorPanel.SyncWorkOverrideSlider(WorkOverrideSlider.Value);
            }

            OperatorPanel.SyncSpindleOverrideSlider(_operatorSpindleOverridePercent);
            OperatorPanel.SyncMachiningToggles(_isSingleBlockEnabled, _isOptionalStopEnabled);

            Dispatcher.BeginInvoke(new Action(ArrangeFloatingPanelsAtStartup), DispatcherPriority.Loaded);
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
            if (MenuStockVisibleItem != null)
            {
                MenuStockVisibleItem.IsChecked = StockVisibleCheck.IsChecked == true;
            }
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
                _toolSettingsDialog?.Close();
                ClearToolpath();
                _lineVisualsMap.Clear();
                _currentLines = File.ReadAllLines(filePath);
                GCodeList.ItemsSource = _currentLines;
                _currentParser = new GCodeParser();
                _currentParser.ProcessFile(filePath);
                _programExecutionService.LoadProgram(_currentParser.Commands);
                _programExecutionService.SetCurrentIndex(0);
                _playbackLoopService.Reset(new Point3D(MachineState.HOME_X, MachineState.HOME_Y, MachineState.HOME_Z));
                _tools.Clear();
                var toolNumbers = _currentParser.Commands.Where(c => c.ToolNumber.HasValue).Select(c => c.ToolNumber!.Value).Distinct().OrderBy(n => n);
                foreach (var t in toolNumbers)
                {
                    var tool = new ToolViewModel { Number = t };
                    tool.FluteColor = ToolPaletteSwatches.NextRandomDistinctFluteColor(_tools.Select(x => x.FluteColor));
                    tool.PropertyChanged += Tool_PropertyChanged;
                    _tools.Add(tool);
                }
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
                    _toolpathRenderService.TrackVisible(visual);
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
                FanucPanel.SetNcProgramSource(_currentLines ?? Array.Empty<string>(), filePath);
                _loadedNcProgramPath = Path.GetFullPath(filePath);
                FanucPanel.LogUserAction($"Загрузка программы: {Path.GetFileName(filePath)}");
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
            _programExecutionService.SetCurrentIndex(GCodeList.SelectedIndex);
            if (_suppressSelectionSideEffects)
            {
                return;
            }

            _lastPosition = GetCurrentPosition();
            _playbackLoopService.BeginSegmentFrom(_lastPosition);
            int selectedLine = GCodeList.SelectedIndex + 1;
            SyncToolWithState();
            if (ToolsList.SelectedItem is ToolViewModel tool) UpdateToolGeometry(tool, GetCurrentPosition());
            _toolpathRenderService.UpdateVisibilityByLine(_lineVisualsMap, Viewport, selectedLine);
            var stateParser = _programStateService.BuildStateAtLine(_currentParser, selectedLine);
            UpdateMachineStateUI(stateParser.State);
            UpdateProgramProgress();
        }

        private void UpdateMachineStateUI(MachineState state)
        {
            PositionText.Text = $"X: {state.X:F3} Y: {state.Y:F3} Z: {state.Z:F3}";
            FanucPanel.UpdateMachinePosition(state.X, state.Y, state.Z);
            ApplyRuntimeStatus(state);
        }

        private void ApplyRuntimeStatus(MachineState state)
        {
            _currentCoordSystem = $"{state.CurrentCoordinateSystem.Letter}{state.CurrentCoordinateSystem.Number}";
            _systemUnitsText = state.IsMetric ? "MM (G21)" : "INCH (G20)";
            _systemCoordModeText = state.IsAbsolute ? "ABS (G90)" : "INC (G91)";
            _systemPlaneText = state.CurrentPlane.Number switch
            {
                17 => "G17 (XY)",
                18 => "G18 (XZ)",
                19 => "G19 (YZ)",
                _ => $"G{state.CurrentPlane.Number}"
            };
            _systemMotionText = $"G{state.CurrentMotionMode.Number}";
            _systemCompText = $"LEN G{state.ToolLengthCompensation.Number} RAD G{state.CutterCompensation.Number}";
            var activeOffset = state.GetActiveWorkOffset();
            _currentOffsetX = activeOffset.X;
            _currentOffsetY = activeOffset.Y;
            _currentOffsetZ = activeOffset.Z;
            for (int i = MachineState.MinWorkOffsetNumber; i <= MachineState.MaxWorkOffsetNumber; i++)
            {
                var value = state.GetWorkOffset(i);
                _workOffsets[i] = (value.X, value.Y, value.Z);
            }
            FanucPanel.UpdateOffsets(_currentCoordSystem, _currentOffsetX, _currentOffsetY, _currentOffsetZ);
            UpdateOffsetEditorBySystem(_selectedOffsetSystem);
            _uiRenderService.ApplyRuntimeStatus(
                state,
                FanucPanel,
                ToolText,
                CoordSystemText,
                RefSystemText,
                FeedStatusText,
                SpindleStatusText,
                CoolantText);
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

        private void ClearToolpath()
        {
            foreach (var v in _toolpathVisuals)
            {
                Viewport.Children.Remove(v);
            }

            _toolpathVisuals.Clear();
            _toolpathRenderService.Reset();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (FanucPanel.TryHandleNcProgramKeys(e.Key))
            {
                e.Handled = true;
                base.OnPreviewKeyDown(e);
                return;
            }

            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenFile_Click(this, e);
            }

            bool mdiTyping = FanucPanel.IsMdiLineKeyboardFocused();
            if (!mdiTyping)
            {
                if (e.Key == Key.F)
                {
                    Viewport.ZoomExtents();
                }
                else if (e.Key == Key.PageUp || e.Key == Key.Oem4)
                {
                    if (FanucPanel.TryNavigateProgramDirectoryPage(-1))
                    {
                        e.Handled = true;
                    }
                    else
                    {
                        FanucPanel.NavigateSoftkeyPrev();
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.PageDown || e.Key == Key.Oem6)
                {
                    if (FanucPanel.TryNavigateProgramDirectoryPage(1))
                    {
                        e.Handled = true;
                    }
                    else
                    {
                        FanucPanel.NavigateSoftkeyNext();
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Left)
                {
                    FanucPanel.NavigateSoftkeyPrev();
                    e.Handled = true;
                }
                else if (e.Key == Key.Right)
                {
                    FanucPanel.NavigateSoftkeyNext();
                    e.Handled = true;
                }
            }

            base.OnPreviewKeyDown(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _stockCutWorker?.Dispose();
            _machineStateSubscription.Dispose();
            _alarmSubscription.Dispose();
            _mdiModeSubscription.Dispose();
            _programLineSubscription.Dispose();
            _workOffsetsSubscription.Dispose();
            FanucPanel.CycleStartRequested -= OnFanucCycleStartRequested;
            FanucPanel.FeedHoldRequested -= OnFanucFeedHoldRequested;
            FanucPanel.ResetRequested -= OnFanucResetRequested;
            FanucPanel.ModeChangedRequested -= OnFanucModeChangedRequested;
            FanucPanel.JogRequested -= OnFanucJogRequested;
            FanucPanel.MdiExecuteRequested -= OnFanucMdiExecuteRequested;
            FanucPanel.ProgDirectoryCreateAndOpenRequested -= OnFanucProgDirectoryCreateAndOpenRequested;
            FanucPanel.ProgOpenByNameRequested -= OnFanucProgOpenByNameRequested;
            FanucPanel.ProgDeleteProgramByNameRequested -= OnFanucProgDeleteProgramByNameRequested;
            FanucPanel.SingleBlockChangedRequested -= OnFanucSingleBlockChangedRequested;
            FanucPanel.OptionalStopChangedRequested -= OnFanucOptionalStopChangedRequested;
            FanucPanel.DryRunChangedRequested -= OnFanucDryRunChangedRequested;
            FanucPanel.OffsetUpdateRequested -= OnFanucOffsetUpdateRequested;
            FanucPanel.OffsetSystemSelectionChangedRequested -= OnFanucOffsetSystemSelectionChangedRequested;
            FanucPanel.OffsetReadActiveToEditorRequested -= OnFanucOffsetReadActiveToEditorRequested;
            OperatorPanel.ModeSelected -= OnOperatorModeSelected;
            OperatorPanel.JogAxisDelta -= OnOperatorJogAxisDelta;
            OperatorPanel.CycleStart -= OperatorPanel_CycleStart;
            OperatorPanel.FeedHold -= OperatorPanel_FeedHold;
            OperatorPanel.CycleStopRequested -= OperatorPanel_CycleStop;
            OperatorPanel.EmergencyResetRequested -= OperatorPanel_EmergencyReset;
            OperatorPanel.FeedWorkOverridePercentChanged -= OnOperatorFeedWorkOverridePercentChanged;
            OperatorPanel.SpindleOverridePercentChanged -= OnOperatorSpindleOverridePercentChanged;
            OperatorPanel.SingleBlockChanged -= OperatorPanel_SingleBlockChanged;
            OperatorPanel.OptionalStopChanged -= OperatorPanel_OptionalStopChanged;
            if (OperatorFloatingHost != null)
            {
                OperatorFloatingHost.SizeChanged -= OperatorFloatingHost_SizeChanged;
            }

            if (FanucFloatingHost != null)
            {
                FanucFloatingHost.SizeChanged -= FanucFloatingHost_SizeChanged;
            }

            if (_machineCore is IDisposable disposableMachineCore)
            {
                disposableMachineCore.Dispose();
            }
            base.OnClosed(e);
        }

        private void UpdateOffsetEditorBySystem(int coordinateSystemNumber)
        {
            if (!_workOffsets.TryGetValue(coordinateSystemNumber, out var values))
            {
                values = (0, 0, 0);
            }

            FanucPanel.UpdateOffsetEditor($"G{coordinateSystemNumber}", values.X, values.Y, values.Z);
        }

        private void FanucPanel_Loaded(object sender, RoutedEventArgs e)
        {
            OperatorPanel.HighlightMode(_currentControllerMode);
            if (WorkOverrideSlider != null)
            {
                OperatorPanel.SyncWorkOverrideSlider(WorkOverrideSlider.Value);
            }

            OperatorPanel.SyncMachiningToggles(_isSingleBlockEnabled, _isOptionalStopEnabled);
            UpdateOperatorScrollViewport();
            UpdateFanucScrollViewport();
        }
    }
}
