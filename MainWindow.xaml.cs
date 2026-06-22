using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
using CNCSS.Infrastructure.Composition;
using CNCSS.Logic;
using CNCSS.Logic.Voxel;
using CNCSS.Logic.ProgramLoading;
using CNCSS.Logic.NcPrograms;
using CNCSS.Vis;
using CNCSS.Data;
using CNCSS.Data.Config;
using CNCSS.Data.Tools;
using CNCSS.UI;
using CNCSS.UI.ViewModels;
using CNCSS.Controller.Core;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Core;
using CNCSS.Machine.Model;
using CNCSS.Simulation.Bus;
using CNCSS.Simulation.Execution;
using CNCSS.UI.FanucPanel;
using CNCSS.UI.Hosts;
using CNCSS.UI.Dialogs;
using CNCSS.UI.Presenters;
using CNCSS.UI.Configuration;
using System.Diagnostics;
using CNCSS.UI.Views;

namespace CNCSS
{
    /// <summary>
    /// Главное окно: 3D-сцена (Helix), параметрическая заготовка на столе, панели FANUC и операторской станции,
    /// загрузка УП, воспроизведение и синхронизация с шиной <see cref="CNCSS.Simulation.Bus.ISimulationBus"/>.
    /// Воксельная заготовка для резания создаётся только при Cycle Start; видимость в сцене — через <c>FilterShowStock</c>.
    /// </summary>
    public partial class MainWindow : Window, IMainView
    {
        private readonly record struct StockConfig(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);

        private readonly List<Visual3D> _toolpathVisuals = new();
        private readonly Dictionary<int, List<Visual3D>> _lineVisualsMap = new();
        private readonly LinesVisual3D _toolpathPlaybackCap = new() { Thickness = 2.5 };
        private readonly ObservableCollection<ToolViewModel> _tools = new();
        private GCodeParser? _currentParser => _programWorkspace.Parser;
        private string[] _currentLines => _programWorkspace.Lines;
        private string? _loadedNcProgramPath => _programWorkspace.LoadedPath;
        private System.Windows.Threading.DispatcherTimer _animationTimer;
        private Point3D _lastPosition = new Point3D(0, 0, 0);
        private double _interpolationProgress = 1.0;

        private readonly StockLifecycleCoordinator _stockLifecycle;
        private readonly ProgramLineNavigator _programLine = new();
        private ToolViewModel? _selectedTool;
        private string _programStatsText = string.Empty;
        private double _workFeedOverridePercent = 100.0;
        private double _rapidFeedOverridePercent = 100.0;
        private bool _cycleCanStart = true;
        private bool _cycleCanPause;
        private readonly ModelVisual3D _stockVisual = new();
        private readonly ModelVisual3D _toolpathRoot = new();
        private Matrix3D _stockTableToWorld = Matrix3D.Identity;
        private readonly GeometryModel3D _stockModel = new();
        private readonly Model3DGroup _stockDisplayRoot = new();

        // Визуализация инструмента
        private ModelVisual3D _toolVisual = new();
        private readonly GeometryModel3D _fluteModel = new();
        private readonly GeometryModel3D _shankModel = new();
        private TranslateTransform3D _sceneWorldShift = new();
        private readonly Dictionary<int, ModelVisual3D> _wcsMarkers = new();
        private bool _sceneFiltersReady;
        /// <summary>Синхронизация чекбокса/тулбара видимости заготовки без рекурсии обработчиков.</summary>
        private bool _syncingStockDisplay;
        private bool _filterShowTool = true;
        private bool _filterShowToolpath = true;
        private bool _filterShowMachine = true;
        private bool _filterShowAdaptiveProximity;
        private readonly ModelVisual3D _adaptiveProximityVisual = new();
        private readonly GeometryModel3D _adaptiveProximityModel = new();
        private readonly TranslateTransform3D _adaptiveProximityTransform = new();
        private ToolViewModel? _cachedAdaptiveProximityTool;
        private (double Diameter, double OverallLength) _cachedAdaptiveProximityKey;
        private bool _hasCachedAdaptiveProximityGeometry;
        private readonly ISimulationBus _simulationBus;
        private readonly IControllerCore _controllerCore;
        private readonly IMachineCore _machineCore;
        private readonly ProgramWorkspace _programWorkspace;
        private readonly StockSimulationCoordinator _stockCoordinator;
        private readonly ProgramPlaybackHost _programPlaybackHost;
        private readonly NcProgramCatalogService _ncProgramCatalogService;
        private readonly MainPresenter _mainPresenter;
        private readonly IDisposable _machineStateSubscription;
        private readonly IDisposable _alarmSubscription;
        private readonly IDisposable _mdiModeSubscription;
        private readonly IDisposable _programLineSubscription;
        private readonly IDisposable _workOffsetsSubscription;
        private readonly IDisposable _toolOffsetsSubscription;
        private readonly ProgramExecutionService _programExecutionService;
        private readonly UiRenderService _uiRenderService;
        private readonly StockRenderService _stockRenderService;
        private readonly SimulationLoopHost _simulationLoopHost;
        private readonly ToolpathRenderService _toolpathRenderService;
        private readonly MachineProfileService _machineProfileService;
        private readonly StlMeshLoader _stlMeshLoader;
        private readonly MachineVisualCoordinator _machineVisualCoordinator;
        private MachineSetupWindow? _machineSetupWindow;
        private readonly Process _selfProcess = Process.GetCurrentProcess();
        private double _cutBackpressureScale = 1.0;
        private DateTime _runtimeLoadLastAtUtc = DateTime.UtcNow;
        private TimeSpan _runtimeLoadLastCpu = Process.GetCurrentProcess().TotalProcessorTime;
        private double _runtimeCpuPercent;
        private readonly TranslateTransform3D _toolTransform = new();
        private ToolViewModel? _cachedToolGeometryTool;
        private (ToolType Kind, double Diameter, double ShankDiameter, double FluteLength, double OverallLength, Color FluteColor, double DrillPointAngle) _cachedToolGeometryKey;
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
        private int? _lastMessageToolNumber;

        // FPS Counter fields
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _fpsSlowdownFactor = 1.0; // Коэффициент замедления при низком FPS
        private Model3D? _voxelDisplayRootChild;
        private bool _lastVoxelShellDisplayed;
        private bool _isFanucDragging;
        private Point _fanucDragStart;
        private double _fanucStartLeft;
        private double _fanucStartTop;
        private readonly TranslateTransform _fanucDragTransform = new();
        private Window? _fanucPanelWindow;
        private bool _isFanucMinimized;
        private double _fanucExpandedHeight = double.NaN;
        private bool _isOperatorDragging;
        private Point _operatorDragStart;
        private double _operatorStartLeft;
        private double _operatorStartTop;
        private readonly TranslateTransform _operatorDragTransform = new();
        private Window? _operatorPanelWindow;
        private LoadingWindow? _voxelLoadingWindow;
        private int _voxelLoadingWindowDepth;
        private bool _isClosing;
        private bool _suppressPanelWindowMenuSync;
        private bool _isOperatorMinimized;
        private double _operatorSpindleOverridePercent = 100.0;
        private double _operatorExpandedHeight = double.NaN;
        private readonly System.Windows.Threading.DispatcherTimer _fanucClockTimer = new();
        private readonly Stopwatch _runTimeStopwatch = new();
        private readonly Stopwatch _cycleTimeStopwatch = new();
        private TimeSpan _accumulatedRunTime = TimeSpan.Zero;
        private TimeSpan _accumulatedCycleTime = TimeSpan.Zero;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            CompositionTarget.Rendering += OnRendering;
            PreviewMouseDown += MainWindow_PreviewMouseDown;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            var composition = new AppCompositionRoot();
            _stockLifecycle = composition.StockLifecycleCoordinator;
            _programLine.SelectionChanged += OnProgramLineSelectionChanged;
            _simulationBus = composition.SimulationBus;
            _controllerCore = composition.ControllerCore;
            _machineCore = composition.MachineCore;
            _programExecutionService = composition.ProgramExecutionService;
            _programWorkspace = composition.ProgramWorkspace;
            _stockCoordinator = composition.StockCoordinator;
            _programPlaybackHost = composition.ProgramPlaybackHost;
            _ncProgramCatalogService = composition.NcProgramCatalogService;
            _mainPresenter = composition.CreateMainPresenter(this);
            _uiRenderService = composition.UiRenderService;
            _stockRenderService = composition.StockRenderService;
            _simulationLoopHost = new SimulationLoopHost(_stockRenderService);
            _toolpathRenderService = composition.ToolpathRenderService;
            _machineProfileService = composition.MachineProfileService;
            _stlMeshLoader = composition.StlMeshLoader;
            _machineVisualCoordinator = composition.MachineVisualCoordinator;
            FanucPanel.ProgramCatalogService = _ncProgramCatalogService;
            _machineProfileService.ActiveProfileChanged += _ =>
                Dispatcher.InvokeAsync(() => ApplyActiveMachineProfileAsync(moveAxesToProfileHome: false));
            _machineStateSubscription = _simulationBus.Subscribe<MachineStateChangedEvent>(OnMachineStateChanged);
            _alarmSubscription = _simulationBus.Subscribe<AlarmRaisedEvent>(OnAlarmRaised);
            _mdiModeSubscription = _simulationBus.Subscribe<MdiModeChangedEvent>(OnMdiModeChanged);
            _programLineSubscription = _simulationBus.Subscribe<ProgramLineExecutedEvent>(OnProgramLineExecuted);
            _workOffsetsSubscription = _simulationBus.Subscribe<WorkOffsetsChangedEvent>(OnWorkOffsetsChanged);
            _toolOffsetsSubscription = _simulationBus.Subscribe<ToolOffsetsChangedEvent>(OnToolOffsetsChanged);
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
            FanucPanel.OffsetToolValueUpdateRequested += OnFanucOffsetToolValueUpdateRequested;

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
            OperatorPanel.MdiCommandRequested += OnOperatorMdiCommandRequested;

            _animationTimer = new System.Windows.Threading.DispatcherTimer();
            _animationTimer.Tick += AnimationTimer_Tick;
            _fanucClockTimer.Interval = TimeSpan.FromSeconds(1);
            _fanucClockTimer.Tick += (_, _) => FanucPanel.UpdateStatusClock(DateTime.Now);
            _fanucClockTimer.Start();

            InitToolVisual();
            InitAdaptiveProximityVisual();

            _stockModel.Material = MaterialHelper.CreateMaterial(Colors.LightGray);
            _stockModel.BackMaterial = _stockModel.Material;
            _stockVisual.Content = _stockModel;
            Viewport.Children.Add(_stockVisual);
            _programPlaybackHost.ResetToHome();
            RefreshDiagnosticsPanel();
            FanucPanel.UpdateStatusClock(DateTime.Now);

            // Now all scene services are initialized; allow filter handlers.
            _sceneFiltersReady = true;
            ApplySceneFilters();

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

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _isClosing = true;
            _stockLifecycle.ReleaseRuntime();
            _fanucPanelWindow?.Close();
            _operatorPanelWindow?.Close();
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

        private static void BeginFloatingDrag(Border host, TranslateTransform transform)
        {
            transform.X = 0;
            transform.Y = 0;
            host.RenderTransform = transform;
            host.CacheMode = new BitmapCache();
        }

        private static void UpdateFloatingDragTransform(
            TranslateTransform transform,
            double startLeft,
            double startTop,
            Point dragStart,
            Point current)
        {
            double targetLeft = Math.Max(0, startLeft + current.X - dragStart.X);
            double targetTop = Math.Max(0, startTop + current.Y - dragStart.Y);
            transform.X = targetLeft - startLeft;
            transform.Y = targetTop - startTop;
        }

        private static void CommitFloatingDrag(Border host, TranslateTransform transform, double startLeft, double startTop)
        {
            Canvas.SetLeft(host, Math.Max(0, startLeft + transform.X));
            Canvas.SetTop(host, Math.Max(0, startTop + transform.Y));
            transform.X = 0;
            transform.Y = 0;
            host.ClearValue(UIElement.CacheModeProperty);
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

        private void InitializeExternalPanelWindows()
        {
            if (_fanucPanelWindow != null || _operatorPanelWindow != null)
            {
                return;
            }

            DetachFloatingPanelHost(FanucFloatingHost);
            DetachFloatingPanelHost(OperatorFloatingHost);

            PromoteFloatingHostToWindowContent(
                FanucFloatingHost,
                FanucFloatingHeader,
                FanucFloatingContent,
                FanucResizeThumb,
                FanucContentScroll);
            PromoteFloatingHostToWindowContent(
                OperatorFloatingHost,
                OperatorFloatingHeader,
                OperatorFloatingContent,
                OperatorResizeThumb,
                OperatorContentScroll);

            _fanucPanelWindow = CreateOwnedPanelWindow(
                "FANUC 0i-MF Plus",
                FanucFloatingHost,
                UiLayoutConstants.FanucPanelResizeMinWidth,
                UiLayoutConstants.FanucPanelResizeMinHeight,
                Math.Max(UiLayoutConstants.FloatingPanelMargin, Left + Math.Max(0, ActualWidth - UiLayoutConstants.FanucPanelResizeMinWidth - 32)),
                Math.Max(UiLayoutConstants.FloatingPanelMargin, Top + 56),
                MenuFanucWindowItem);

            _operatorPanelWindow = CreateOwnedPanelWindow(
                "Управление станком",
                OperatorFloatingHost,
                UiLayoutConstants.OperatorPanelResizeMinWidth,
                UiLayoutConstants.OperatorPanelResizeMinHeight,
                Math.Max(UiLayoutConstants.FloatingPanelMargin, Left + Math.Max(0, ActualWidth - UiLayoutConstants.OperatorPanelResizeMinWidth - 32)),
                Math.Max(UiLayoutConstants.FloatingPanelMargin, Top + 56 + UiLayoutConstants.FanucPanelResizeMinHeight + UiLayoutConstants.FloatingPanelGap),
                MenuOperatorPanelItem);

            if (MenuFanucWindowItem?.IsChecked == true)
            {
                _fanucPanelWindow.Show();
            }

            if (MenuOperatorPanelItem?.IsChecked == true)
            {
                _operatorPanelWindow.Show();
            }
        }

        private static void DetachFloatingPanelHost(Border host)
        {
            if (VisualTreeHelper.GetParent(host) is Panel parent)
            {
                parent.Children.Remove(host);
            }

            host.ClearValue(Canvas.LeftProperty);
            host.ClearValue(Canvas.TopProperty);
            host.ClearValue(UIElement.RenderTransformProperty);
            host.ClearValue(UIElement.CacheModeProperty);
            host.HorizontalAlignment = HorizontalAlignment.Stretch;
            host.VerticalAlignment = VerticalAlignment.Stretch;
            host.Visibility = Visibility.Visible;
        }

        private static void PromoteFloatingHostToWindowContent(
            Border host,
            Border embeddedHeader,
            Border embeddedContent,
            Thumb resizeThumb,
            ScrollViewer contentScroll)
        {
            embeddedHeader.Visibility = Visibility.Collapsed;
            resizeThumb.Visibility = Visibility.Collapsed;
            contentScroll.ClearValue(FrameworkElement.MaxHeightProperty);

            if (host.Child is Grid grid && grid.RowDefinitions.Count >= 2)
            {
                grid.RowDefinitions[0].Height = new GridLength(0);
                grid.RowDefinitions[0].MinHeight = 0;
                grid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            }

            Grid.SetRow(embeddedContent, 0);
            Grid.SetRowSpan(embeddedContent, 2);
            host.BorderThickness = new Thickness(0);
            host.CornerRadius = new CornerRadius(0);
        }

        private Window CreateOwnedPanelWindow(
            string title,
            Border content,
            double width,
            double height,
            double left,
            double top,
            MenuItem? linkedMenuItem)
        {
            var window = new Window
            {
                Owner = this,
                Title = title,
                Content = content,
                Width = width,
                Height = height,
                MinWidth = width,
                MinHeight = height,
                Left = left,
                Top = top,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ResizeMode = ResizeMode.CanResize,
                WindowStyle = WindowStyle.SingleBorderWindow,
                SizeToContent = SizeToContent.WidthAndHeight,
                Background = Brushes.Transparent
            };

            window.Loaded += (_, _) =>
            {
                window.SizeToContent = SizeToContent.Manual;
            };

            window.Closing += (_, e) =>
            {
                if (_isClosing)
                {
                    return;
                }

                e.Cancel = true;
                window.Hide();
                if (linkedMenuItem != null)
                {
                    _suppressPanelWindowMenuSync = true;
                    linkedMenuItem.IsChecked = false;
                    _suppressPanelWindowMenuSync = false;
                }
            };

            return window;
        }

        private void MenuFanucWindowItem_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressPanelWindowMenuSync)
            {
                return;
            }

            if (_fanucPanelWindow != null)
            {
                _fanucPanelWindow.Show();
                _fanucPanelWindow.Activate();
            }
            else if (FanucFloatingHost != null)
            {
                FanucFloatingHost.Visibility = Visibility.Visible;
            }
        }

        private void MenuFanucWindowItem_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressPanelWindowMenuSync)
            {
                return;
            }

            if (_fanucPanelWindow != null)
            {
                _fanucPanelWindow.Hide();
            }
            else if (FanucFloatingHost != null)
            {
                FanucFloatingHost.Visibility = Visibility.Collapsed;
            }
        }

        private void MenuOperatorPanelItem_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressPanelWindowMenuSync)
            {
                return;
            }

            if (_operatorPanelWindow != null)
            {
                _operatorPanelWindow.Show();
                _operatorPanelWindow.Activate();
            }
            else if (OperatorFloatingHost != null)
            {
                OperatorFloatingHost.Visibility = Visibility.Visible;
            }
        }

        private void MenuOperatorPanelItem_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressPanelWindowMenuSync)
            {
                return;
            }

            if (_operatorPanelWindow != null)
            {
                _operatorPanelWindow.Hide();
            }
            else if (OperatorFloatingHost != null)
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
                OperatorFloatingHost.MinHeight = UiLayoutConstants.OperatorPanelResizeMinHeight;

                if (!double.IsNaN(_operatorExpandedHeight) && _operatorExpandedHeight >= UiLayoutConstants.OperatorPanelResizeMinHeight - 1)
                {
                    OperatorFloatingHost.Height = _operatorExpandedHeight;
                }
                else
                {
                    OperatorFloatingHost.ClearValue(FrameworkElement.HeightProperty);
                }
            }

            OperatorMinimizeButton.Content = _isOperatorMinimized ? "в–Ў" : "_";

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
            OperatorFloatingHost.MinHeight = UiLayoutConstants.OperatorPanelResizeMinHeight;
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

            double fanucW = ResolveHostWidth(FanucFloatingHost, UiLayoutConstants.FanucPanelResizeMinWidth);
            double fanucH = ResolveHostHeight(FanucFloatingHost, UiLayoutConstants.FanucPanelResizeMinHeight);
            double operatorW = ResolveHostWidth(OperatorFloatingHost, UiLayoutConstants.OperatorPanelResizeMinWidth);
            double operatorH = ResolveHostHeight(OperatorFloatingHost, UiLayoutConstants.OperatorPanelResizeMinHeight);

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
                    UiLayoutConstants.OperatorPanelResizeMinWidth,
                    UiLayoutConstants.OperatorPanelResizeMinHeight);

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

            OperatorFloatingHost.Width = Math.Max(UiLayoutConstants.OperatorPanelResizeMinWidth, w + e.HorizontalChange);
            OperatorFloatingHost.Height = Math.Max(UiLayoutConstants.OperatorPanelResizeMinHeight, h + e.VerticalChange);
        }

        private void OperatorFloatingHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_operatorPanelWindow != null)
            {
                return;
            }

            _isOperatorDragging = true;
            _operatorDragStart = e.GetPosition(this);
            _operatorStartLeft = Canvas.GetLeft(OperatorFloatingHost);
            _operatorStartTop = Canvas.GetTop(OperatorFloatingHost);
            BeginFloatingDrag(OperatorFloatingHost, _operatorDragTransform);
            OperatorFloatingHeader.CaptureMouse();
        }

        private void OperatorFloatingHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isOperatorDragging)
            {
                return;
            }

            Point current = e.GetPosition(this);
            UpdateFloatingDragTransform(_operatorDragTransform, _operatorStartLeft, _operatorStartTop, _operatorDragStart, current);
        }

        private void OperatorFloatingHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isOperatorDragging)
            {
                return;
            }

            _isOperatorDragging = false;
            CommitFloatingDrag(OperatorFloatingHost, _operatorDragTransform, _operatorStartLeft, _operatorStartTop);
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
                FanucFloatingHost.MinHeight = UiLayoutConstants.FanucPanelResizeMinHeight;

                if (!double.IsNaN(_fanucExpandedHeight) && _fanucExpandedHeight >= UiLayoutConstants.FanucPanelResizeMinHeight - 1)
                {
                    FanucFloatingHost.Height = _fanucExpandedHeight;
                }
                else
                {
                    FanucFloatingHost.ClearValue(FrameworkElement.HeightProperty);
                }
            }

            FanucMinimizeButton.Content = _isFanucMinimized ? "в–Ў" : "_";

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
            FanucFloatingHost.MinHeight = UiLayoutConstants.FanucPanelResizeMinHeight;
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
                    UiLayoutConstants.FanucPanelResizeMinWidth,
                    UiLayoutConstants.FanucPanelResizeMinHeight);

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

            FanucFloatingHost.Width = Math.Max(UiLayoutConstants.FanucPanelResizeMinWidth, w + e.HorizontalChange);
            FanucFloatingHost.Height = Math.Max(UiLayoutConstants.FanucPanelResizeMinHeight, h + e.VerticalChange);
        }

        private void FanucFloatingHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_fanucPanelWindow != null)
            {
                return;
            }

            _isFanucDragging = true;
            _fanucDragStart = e.GetPosition(this);
            _fanucStartLeft = Canvas.GetLeft(FanucFloatingHost);
            _fanucStartTop = Canvas.GetTop(FanucFloatingHost);
            BeginFloatingDrag(FanucFloatingHost, _fanucDragTransform);
            FanucFloatingHeader.CaptureMouse();
        }

        private void FanucFloatingHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isFanucDragging)
            {
                return;
            }

            Point current = e.GetPosition(this);
            UpdateFloatingDragTransform(_fanucDragTransform, _fanucStartLeft, _fanucStartTop, _fanucDragStart, current);
        }

        private void FanucFloatingHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isFanucDragging)
            {
                return;
            }

            _isFanucDragging = false;
            CommitFloatingDrag(FanucFloatingHost, _fanucDragTransform, _fanucStartLeft, _fanucStartTop);
            FanucFloatingHeader.ReleaseMouseCapture();
        }

        private void MenuCycleStart_Click(object sender, RoutedEventArgs e) => OnFanucCycleStartRequested();
        private void MenuFeedHold_Click(object sender, RoutedEventArgs e) => OnFanucFeedHoldRequested();

        private void ViewportCycleStart_Click(object sender, RoutedEventArgs e) => OnFanucCycleStartRequested();

        private void ViewportCycleStop_Click(object sender, RoutedEventArgs e) => OnFanucFeedHoldRequested();
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
                ItemsSource = _currentLines
            };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedIndex >= 0 && _programLine.SelectedIndex != list.SelectedIndex)
                {
                    _programLine.SelectedIndex = list.SelectedIndex;
                    
                }
            };
            list.SelectedIndex = _programLine.SelectedIndex;
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
                Text = _programStatsText
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
            SyncToolLibraryToOffsetTables(reparseProgramIfLoaded: true);
            if (_selectedTool is ToolViewModel active)
            {
                UpdateToolGeometry(active, GetToolHolderPosition(GetCurrentPosition()));
            }
            else
            {
                UpdateToolGeometry(null, GetToolHolderPosition(GetCurrentPosition()));
            }

            FanucPanel.LogUserAction("Инструмент: параметры перенесены в OFFSET (H/D) и применены к G43");
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

        private void MenuQuickMeasureTool_Click(object sender, RoutedEventArgs e)
        {
            if (_tools.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Сначала добавьте инструменты (меню «Симуляция → Инструмент…»).",
                    "Измерение инструмента",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int applied = _tools.Count(t => t.Number is >= 1 and <= 30);
            SyncToolLibraryToOffsetTables(reparseProgramIfLoaded: true);

            string summary = applied > 0
                ? $"Измерение: в OFFSET (H/D) перенесены параметры {applied} инстр. из библиотеки T"
                : "Измерение: ни один инструмент не попал в OFFSET (1…30)";
            var skipped = _tools.Where(t => t.Number < 1 || t.Number > 30).Select(t => $"T{t.Number}").ToList();
            if (skipped.Count > 0)
            {
                summary += $" (пропущены: {string.Join(", ", skipped)})";
            }

            StatusText.Text = summary;
            FanucPanel.LogUserAction(summary);
        }

        private void ApplyToolOffsetGeomValue(int row, FanucPanelControl.OffsetToolColumn column, double value)
        {
            FanucPanel.SetToolOffsetDisplayValue(row, column, value);
            PushToolOffsetToController(row, column, value);
        }

        private void PushToolOffsetToController(int row, FanucPanelControl.OffsetToolColumn column, double value, bool reparseProgramIfLoaded = true)
        {
            switch (column)
            {
                case FanucPanelControl.OffsetToolColumn.GeomH:
                    _machineCore.SetToolOffsetRow(row, geomH: value);
                    break;
                case FanucPanelControl.OffsetToolColumn.WearH:
                    _machineCore.SetToolOffsetRow(row, wearH: value);
                    break;
                case FanucPanelControl.OffsetToolColumn.GeomD:
                    _machineCore.SetToolOffsetRow(row, geomD: value);
                    break;
                case FanucPanelControl.OffsetToolColumn.WearD:
                    _machineCore.SetToolOffsetRow(row, wearD: value);
                    break;
            }

            SyncProgramParserToolOffsetRow(row);
            if (reparseProgramIfLoaded && _currentParser != null && _loadedNcProgramPath != null)
            {
                RebuildProgramAndToolpathForCurrentWorkOffsets();
            }
        }

        /// <summary>
        /// Строка OFFSET n = инструмент Tn: GEOM(H) и GEOM(D) из библиотеки инструментов (источник для G43/G41).
        /// </summary>
        private void SyncToolLibraryToOffsetTables(bool reparseProgramIfLoaded = false)
        {
            foreach (ToolViewModel tool in _tools)
            {
                int row = tool.Number;
                if (row < 1 || row > 30)
                {
                    continue;
                }

                _machineCore.SetToolOffsetRow(row, geomH: tool.OverallLength, geomD: tool.Diameter);
                FanucPanel.SetToolOffsetDisplayValue(row, FanucPanelControl.OffsetToolColumn.GeomH, tool.OverallLength);
                FanucPanel.SetToolOffsetDisplayValue(row, FanucPanelControl.OffsetToolColumn.GeomD, tool.Diameter);
                SyncProgramParserToolOffsetRow(row);
            }

            if (reparseProgramIfLoaded && _currentParser != null && _loadedNcProgramPath != null)
            {
                RebuildProgramAndToolpathForCurrentWorkOffsets();
            }
        }

        private void SyncProgramParserToolOffsetRow(int row)
        {
            if (_currentParser == null)
            {
                return;
            }

            var offsetState = _machineCore.GetOffsetTableState();
            if (offsetState.ToolLengthGeom.TryGetValue(row, out double geomH))
            {
                _currentParser.State.SetToolLengthOffsetValue(row, geom: geomH);
            }

            if (offsetState.ToolRadiusGeom.TryGetValue(row, out double geomD))
            {
                _currentParser.State.SetToolRadiusOffsetValue(row, geom: geomD);
            }
        }

        private void MenuQuickWcsZero_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetStockMachineBounds(out WorkpiecePlacement.StockBounds bounds))
            {
                MessageBox.Show(
                    this,
                    "Сначала задайте заготовку (меню «Симуляция → Конструктор заготовки…»).",
                    "Установить ноль",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dialog = new UI.Dialogs.WcsQuickZeroWindow(bounds, _selectedOffsetSystem)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true && dialog.DialogResultValue is { } r)
            {
                _selectedOffsetSystem = r.SystemNumber;
                WorkpieceMountPlacement.RegisterWcsOriginTableLocal(
                    r.SystemNumber,
                    new Point3D(r.X, r.Y, r.Z));
                MachineGeometryPoint originMcs = StockBoundsPointToWcsOffsetMcs(
                    new MachineGeometryPoint { X = r.X, Y = r.Y, Z = r.Z });
                ApplyWorkOffsetDirect(r.SystemNumber, originMcs.X, originMcs.Y, originMcs.Z);
                UpdateOffsetEditorBySystem(r.SystemNumber);
                StatusText.Text =
                    $"WCS G{r.SystemNumber} (MCS): X={originMcs.X:F3} Y={originMcs.Y:F3} Z={originMcs.Z:F3}";
            }
        }

        private void ApplyWorkOffsetDirect(int systemNumber, double x, double y, double z)
        {
            // Direct controller command so it works in any controller mode (no MDI lock).
            string payload =
                $"{systemNumber}:{x.ToString(CultureInfo.InvariantCulture)}:{y.ToString(CultureInfo.InvariantCulture)}:{z.ToString(CultureInfo.InvariantCulture)}";
            _simulationBus.Publish(new ControllerCommandEvent("WorkOffsetSet", payload, DateTime.UtcNow));
        }

        private bool TryGetStockMachineBounds(out WorkpiecePlacement.StockBounds bounds)
        {
            bounds = default;
            if (!TryReadStockConfig(out StockConfig cfg))
            {
                return false;
            }

            if (_stockLifecycle.AnchoredToTable && _stockLifecycle.BoundsAreTableLocal)
            {
                bounds = new WorkpiecePlacement.StockBounds(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
                return true;
            }

            if (_stockLifecycle.AnchoredToTable)
            {
                bounds = TryConvertStockBoundsToTableLocal(cfg, out WorkpiecePlacement.StockBounds tableLocal)
                    ? tableLocal
                    : new WorkpiecePlacement.StockBounds(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
                return true;
            }

            bounds = new WorkpiecePlacement.StockBounds(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
            return true;
        }

        /// <summary>Открывает конструктор заготовки (Симуляция → Конструктор заготовки…).</summary>
        private void MenuStockDialog_Click(object sender, RoutedEventArgs e)
        {
            var vm = new StockConstructorViewModel(_stockLifecycle.ConstructorConfig);
            if (_machineVisualCoordinator.TryGetNodeMeshModelClone(MachineNodeIds.Table, out Model3D? tableModel))
            {
                vm.SetTableModel(tableModel);
            }
            Rect3D? tableBoundsLocal = _machineVisualCoordinator.TryGetTableMeshBoundsLocal(out Rect3D bounds) && !bounds.IsEmpty
                ? bounds
                : null;
            (double mx, double my, double mz) = _machineVisualCoordinator.GetPreviewPhysicalPose();
            vm.SetPlacementContext(_machineProfileService.ActiveProfile, tableBoundsLocal, mx, my, mz);
            var dialog = new StockConstructorWindow(vm) { Owner = this };

            bool? accepted = dialog.ShowDialog();
            if (accepted != true || vm.Result == null)
            {
                return;
            }

            ApplyStockConstructor(vm.Result);
        }

        /// <summary>
        /// Применяет результат конструктора: параметрическая заготовка на Helix; воксели — в фоне.
        /// </summary>
        private void ApplyStockConstructor(StockConstructorConfig cfg)
        {
            _stockLifecycle.ConstructorConfig = cfg;
            _stockLifecycle.DeferVoxelUntilRun = true;
            SetStockDisplayVisible(true);

            _stockLifecycle.VoxelResolutionMm = VoxelConstants.VoxelResolutionMm;
            _stockLifecycle.AdaptiveDisplayGridMm = VoxelConstants.DisplayGridDefaultMm;
            UpdateResButtons();

            SetStockColorFromWpfColor(cfg.Color.ToWpfColor());

            if (!TryBuildStockBoundsTableLocal(cfg, out WorkpiecePlacement.StockBounds bounds))
            {
                MessageBox.Show(this, "Некорректные параметры заготовки.", "Заготовка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _stockLifecycle.AnchoredToTable = true;
            _stockLifecycle.BoundsAreTableLocal = true;
            _stockLifecycle.AutoAlignToMount = false;
            _stockLifecycle.Bounds = bounds;

            ReleaseVoxelStockRuntime();
            _stockModel.Geometry = _stockLifecycle.BuildParametricMesh(bounds);
            _stockModel.Material = MaterialHelper.CreateMaterial(cfg.Color.ToWpfColor());
            _stockModel.BackMaterial = _stockModel.Material;

            SyncStockVisualToTable();
            ApplyStockViewportVisibility(triggerMeshRefresh: false);
            BeginBackgroundVoxelRuntimePrep(cfg.Color.ToWpfColor());
        }

        private CancellationTokenSource? _voxelPrepCts;

        /// <summary>Фоновая подготовка bitmap/surface после конструктора (без модального окна).</summary>
        private void BeginBackgroundVoxelRuntimePrep(Color surfaceColor)
        {
            _voxelPrepCts?.Cancel();
            _voxelPrepCts?.Dispose();
            _voxelPrepCts = new CancellationTokenSource();
            CancellationToken token = _voxelPrepCts.Token;

            Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                bool ok = _stockLifecycle.TryEnsureVoxelRuntime(false, surfaceColor, PumpUiForLoadingOverlay);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                Dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (!ok)
                    {
                        StatusText.Text = "Не удалось подготовить воксельную заготовку";
                        return;
                    }

                    _stockLifecycle.DeferVoxelShellUntilApproach = true;
                    _stockLifecycle.DeferVoxelUntilRun = false;
                    StatusText.Text = "Воксельная заготовка готова";
                    UpdateStockStatsOverlay();
                });
            }, token);
        }

        private bool ShouldShowD3dStockOverlay() => false;

        private bool IsStockShownInViewport() => FilterShowStock?.IsChecked == true;

        /// <summary>Показать/скрыть заготовку в 3D (только визуал, не пересоздаёт воксели).</summary>
        private void SetStockDisplayVisible(bool visible)
        {
            if (_syncingStockDisplay)
            {
                return;
            }

            _syncingStockDisplay = true;
            try
            {
                if (FilterShowStock != null)
                {
                    FilterShowStock.IsChecked = visible;
                }

                ApplyStockViewportVisibility();
            }
            finally
            {
                _syncingStockDisplay = false;
            }
        }

        private void SetStockColorFromWpfColor(Color color)
        {
            _stockModel.Material = MaterialHelper.CreateMaterial(color);
            _stockModel.BackMaterial = _stockModel.Material;
        }

        private bool TryBuildStockBoundsTableLocal(StockConstructorConfig cfg, out WorkpiecePlacement.StockBounds bounds)
        {
            bounds = default;
            MachineDefinition profile = _machineProfileService.ActiveProfile;
            Rect3D? tableBoundsLocal = _machineVisualCoordinator.TryGetTableMeshBoundsLocal(out Rect3D b) && !b.IsEmpty ? b : null;
            (double mx, double my, double mz) = _machineVisualCoordinator.GetPreviewPhysicalPose();

            (double width, double depth, double height) dims = cfg.ShapeType switch
            {
                StockShapeType.Rectangular => (cfg.Param1Mm, cfg.Param2Mm, cfg.Param3Mm),
                // Hex: Param1=across flats (inscribed circle diameter), Param2=thickness (Z)
                StockShapeType.Hexagonal => (cfg.Param1Mm, cfg.Param1Mm, cfg.Param2Mm),
                StockShapeType.Round => (cfg.Param1Mm, cfg.Param1Mm, cfg.Param2Mm),
                StockShapeType.Tube => (cfg.Param1Mm, cfg.Param1Mm, cfg.Param3Mm),
                _ => default
            };

            if (dims.width <= 0 || dims.depth <= 0 || dims.height <= 0)
            {
                return false;
            }

            WorkpiecePlacement.StockBounds aligned = WorkpieceMountPlacement.AlignStockTableLocal(
                profile,
                tableBoundsLocal,
                dims.width,
                dims.depth,
                dims.height);

            var centerTable = new Point3D(
                (aligned.MinX + aligned.MaxX) * 0.5,
                (aligned.MinY + aligned.MaxY) * 0.5,
                (aligned.MinZ + aligned.MaxZ) * 0.5);

            Point3D centerMcs = WorkpieceMountPlacement.TableRootLocalToWcsMcs(profile, mx, my, mz, centerTable);
            var desiredCenterMcs = new Point3D(cfg.CenterXMcsMm, cfg.CenterYMcsMm, centerMcs.Z);
            Point3D desiredCenterTable = WorkpieceMountPlacement.WcsMcsToTableRootLocal(profile, mx, my, mz, desiredCenterMcs);

            Vector3D delta = desiredCenterTable - centerTable;
            bounds = new WorkpiecePlacement.StockBounds(
                aligned.MinX + delta.X,
                aligned.MaxX + delta.X,
                aligned.MinY + delta.Y,
                aligned.MaxY + delta.Y,
                aligned.MinZ,
                aligned.MaxZ);
            return true;
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

        /// <summary>Синхронизирует отображение воксельной или параметрической заготовки.</summary>
        private void RefreshVoxelStockDisplay(VoxelStockVolume stock, bool scheduleCutRebuild = false)
        {
            _ = scheduleCutRebuild;
            if (_stockVisual == null || !IsStockShownInViewport())
            {
                return;
            }

            bool useVoxel = stock.UseVoxelForDisplay;
            if (useVoxel)
            {
                bool showUnderlay = stock.ShowParametricUnderlay;
                bool needsRefresh = _stockDisplayRoot.Children.Count == 0;
                if (showUnderlay)
                {
                    needsRefresh = needsRefresh
                        || _stockDisplayRoot.Children.Count != 2
                        || !ReferenceEquals(_stockDisplayRoot.Children[0], _stockModel)
                        || !ReferenceEquals(_stockDisplayRoot.Children[1], stock.MainModel);
                }
                else
                {
                    needsRefresh = needsRefresh
                        || _stockDisplayRoot.Children.Count != 1
                        || !ReferenceEquals(_stockDisplayRoot.Children[0], stock.MainModel);
                }

                if (needsRefresh)
                {
                    _stockDisplayRoot.Children.Clear();
                    if (showUnderlay)
                    {
                        _stockDisplayRoot.Children.Add(_stockModel);
                    }

                    _stockDisplayRoot.Children.Add(stock.MainModel);
                }
            }
            else
            {
                if (_stockDisplayRoot.Children.Count != 1 || !ReferenceEquals(_stockDisplayRoot.Children[0], _stockModel))
                {
                    _stockDisplayRoot.Children.Clear();
                    _stockDisplayRoot.Children.Add(_stockModel);
                }
            }

            if (_stockVisual.Content != _stockDisplayRoot)
            {
                _stockVisual.Content = _stockDisplayRoot;
            }

            _voxelDisplayRootChild = useVoxel ? stock.MainModel : _stockModel;
            _lastVoxelShellDisplayed = useVoxel;
            UpdateResButtons();
            UpdateStockStatsOverlay();
        }

        /// <summary>Прогрев оболочки при подводе инструмента.</summary>
        private void TryBeginVoxelShellOnApproach(Point3D toolStockLocal)
        {
            if (_stockLifecycle.Stock is not VoxelStockVolume stock || stock.ShellDisplayed)
            {
                return;
            }

            if (_stockLifecycle.Bounds is not WorkpiecePlacement.StockBounds bounds)
            {
                return;
            }

            var volume = StockLifecycleCoordinator.ToVolumeConfig(bounds);
            double dist = StockApproachHelper.DistancePointToBoundsMm(
                toolStockLocal.X, toolStockLocal.Y, toolStockLocal.Z, volume);
            if (dist > VoxelConstants.VoxelWarmupApproachDistanceMm)
            {
                return;
            }

            stock.ActivateShellDisplay();
            stock.BeginDisplayWarmup();
            RefreshVoxelStockDisplay(stock);
        }

        /// <summary>Завершает отложенный прогрев оболочки.</summary>
        private bool TryCompleteDeferredVoxelShell(VoxelStockVolume stock)
        {
            if (stock.ShellDisplayed)
            {
                return true;
            }

            if (_stockLifecycle.DeferVoxelShellUntilApproach)
            {
                return false;
            }

            stock.ActivateShellDisplay();
            RefreshVoxelStockDisplay(stock);
            return stock.ShellDisplayed;
        }

        /// <summary>Освобождает воксельный runtime и worker заготовки.</summary>
        private void ReleaseVoxelStockRuntime()
        {
            _voxelPrepCts?.Cancel();
            _stockLifecycle.ReleaseRuntime();
            _voxelDisplayRootChild = null;
            _lastVoxelShellDisplayed = false;
            if (_stockVisual != null)
            {
                _stockDisplayRoot.Children.Clear();
            }
        }

        private void ApplyStockViewportVisibility(bool triggerMeshRefresh = true)
        {
            _ = triggerMeshRefresh;
            if (_stockVisual == null)
            {
                return;
            }

            if (!IsStockShownInViewport())
            {
                _stockVisual.Content = null;

                if (ResolutionStatusText != null)
                {
                    ResolutionStatusText.Visibility = Visibility.Collapsed;
                }

                return;
            }

            if (_stockLifecycle.Stock is VoxelStockVolume voxelStock)
            {
                if (!voxelStock.ShellDisplayed && !_stockLifecycle.DeferVoxelShellUntilApproach)
                {
                    voxelStock.ActivateShellDisplay();
                }

                RefreshVoxelStockDisplay(voxelStock);
            }
            else
            {
                _stockVisual.Content = _stockModel;
            }

            if (ResolutionStatusText != null)
            {
                ResolutionStatusText.Visibility = Visibility.Visible;
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
            UpdateFanucMachinePosition(evt);
            PositionText.Text = FormatMcsPosition(
                evt.X, evt.Y, evt.Z,
                evt.MachineZeroOffsetX, evt.MachineZeroOffsetY, evt.MachineZeroOffsetZ);
            string spindleCode = evt.IsSpindleOn ? (evt.IsSpindleCW ? "M3" : "M4") : "M5";
            string toolDisplay = evt.ToolNumber.HasValue ? $"T{evt.ToolNumber.Value}" : "-";
            FanucPanel.UpdatePosRuntime(evt.FeedRate, toolDisplay, evt.SpindleSpeed, spindleCode, evt.IsCoolantOn);
            _currentCoordSystem = evt.CoordinateSystem;
            _currentOffsetX = evt.OffsetX;
            _currentOffsetY = evt.OffsetY;
            _currentOffsetZ = evt.OffsetZ;
            FanucPanel.UpdateOffsets(_currentCoordSystem, _currentOffsetX, _currentOffsetY, _currentOffsetZ);
            _programPlaybackHost.SetCurrentPosition(new Point3D(evt.X, evt.Y, evt.Z));
            _machineVisualCoordinator.UpdatePose(evt.X, evt.Y, evt.Z);
            ApplyTableKinematicPose(evt.X, evt.Y, evt.Z);
            if (!MachineKinematics.UsesTableMountedWorkpiece(_machineProfileService.ActiveProfile) &&
                TryParseCoordinateSystemNumber(evt.CoordinateSystem, out int activeWcs))
            {
                SyncActiveWcsOriginMarker(activeWcs, evt.X, evt.Y, evt.Z, updateTableLocalPosition: true);
            }
            SyncCutTrackingFromMachinePosition(new Point3D(evt.X, evt.Y, evt.Z));
            bool showTool = _filterShowTool && evt.ToolNumber.HasValue;
            bool mountNodeTool = UsesMountNodeToolVisual();
            _uiRenderService.SyncToolVisual(
                showTool ? evt.ToolNumber : null,
                _tools,
                tool => _selectedTool = tool,
                Viewport,
                _toolVisual,
                GetToolHolderPosition(evt.X, evt.Y, evt.Z),
                UpdateToolGeometry,
                tool => tool.PropertyChanged += Tool_PropertyChanged,
                _currentParser == null || _programLine.SelectedIndex < 0,
                skipViewportAttach: mountNodeTool);
            SyncToolVisualMount(showTool);
        }

        private void SceneFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_sceneFiltersReady || _syncingStockDisplay)
            {
                return;
            }

            // Видимость заготовки — только отображение; воксели не пересчитываются.
            if (ReferenceEquals(sender, FilterShowStock))
            {
                SetStockDisplayVisible(FilterShowStock?.IsChecked == true);
                return;
            }

            _filterShowTool = FilterShowTool?.IsChecked == true;
            _filterShowToolpath = FilterShowToolpath?.IsChecked == true;
            _filterShowMachine = FilterShowMachine?.IsChecked == true;
            _filterShowAdaptiveProximity = FilterShowAdaptiveProximity?.IsChecked == true;
            if (_filterShowAdaptiveProximity && _selectedTool != null)
            {
                EnsureAdaptiveProximityGeometry(_selectedTool);
            }
            else if (!_filterShowAdaptiveProximity)
            {
                _adaptiveProximityModel.Geometry = null;
            }

            ApplySceneFilters();
        }

        private void ApplySceneFilters()
        {
            // Machine parts
            _machineVisualCoordinator.SetBaseVisible(_filterShowMachine);
            _machineVisualCoordinator.SetTableVisible(_filterShowMachine);
            _machineVisualCoordinator.SetSpindleVisible(_filterShowMachine);
            _machineVisualCoordinator.SetExtrasVisible(_filterShowMachine);

            // Toolpath
            if (!_filterShowToolpath)
            {
                foreach (var v in _toolpathVisuals)
                {
                    if (_toolpathRoot.Children.Contains(v))
                    {
                        _toolpathRoot.Children.Remove(v);
                    }
                }
                _toolpathRenderService.Reset();
            }
            else
            {
                // Re-apply visibility up to selected line (or show all if nothing selected).
                _toolpathRenderService.Reset();
                int selectedLine = _programLine.SelectedIndex >= 0
                    ? Math.Max(1, _programLine.SelectedIndex + 1)
                    : int.MaxValue;
                _toolpathRenderService.UpdateVisibilityByLine(_lineVisualsMap, _toolpathRoot, selectedLine);
            }

            // Tool
            if (!_filterShowTool)
            {
                SyncToolVisualMount(showTool: false);
                if (Viewport.Children.Contains(_toolVisual))
                {
                    Viewport.Children.Remove(_toolVisual);
                }
            }
            else if (IsToolVisualAttached())
            {
                SyncToolVisualMount(showTool: true);
            }

            SyncAdaptiveProximityVisual(ShouldShowAdaptiveProximityVisual());
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
            Dispatcher.BeginInvoke(() =>
            {
                _workOffsets.Clear();
                foreach (var offset in evt.Offsets)
                {
                    _workOffsets[offset.CoordinateSystemNumber] = (offset.X, offset.Y, offset.Z);
                }

                RefreshWcsOriginTableLocalCacheFromOffsets();

                FanucPanel.UpdateWorkOffsetsTable(_workOffsets);
                UpdateOffsetEditorBySystem(_selectedOffsetSystem);
                SyncActiveWcsOriginMarker();

                // If a program is loaded, rebuild toolpath scene using updated WCS offsets
                // so contours using the corresponding G54..G59 are redrawn with the shift.
                if (_currentParser != null && _loadedNcProgramPath != null)
                {
                    RebuildProgramAndToolpathForCurrentWorkOffsets();
                }
            });
        }

        private void OnToolOffsetsChanged(ToolOffsetsChangedEvent evt)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_currentParser != null && _loadedNcProgramPath != null)
                {
                    RebuildProgramAndToolpathForCurrentWorkOffsets();
                }
            });
        }

        private MachineState CreateProgramSeedState()
        {
            var seed = new MachineState();
            _machineProfileService.ActiveProfile.ApplyHomeToMachineState(seed);
            foreach (var pair in _workOffsets)
            {
                int sys = pair.Key;
                if (sys < MachineState.MinWorkOffsetNumber || sys > MachineState.MaxWorkOffsetNumber)
                {
                    continue;
                }

                seed.SetWorkOffset(sys, pair.Value.X, pair.Value.Y, pair.Value.Z);
            }

            _machineCore.CopyToolOffsetTablesTo(seed);
            return seed;
        }

        private bool TryReparseLoadedProgramWithActiveProfile(out ProgramLoadResult reprased)
        {
            reprased = null!;
            ProgramLoadResult? updated = _programWorkspace.ReparseWithSeed(CreateProgramSeedState());
            if (updated == null)
            {
                return false;
            }

            reprased = updated;
            return true;
        }

        private void RebuildProgramAndToolpathForCurrentWorkOffsets() =>
            _ = RebuildProgramAndToolpathForCurrentWorkOffsetsAsync();

        private async Task RebuildProgramAndToolpathForCurrentWorkOffsetsAsync()
        {
            if (_currentParser == null)
            {
                return;
            }

            try
            {
                MachineState seedState = CreateProgramSeedState();
                var profile = _machineProfileService.ActiveProfile;
                double stickOut = GetToolStickOutMm();
                int currentUiIndex = Math.Max(0, _programLine.SelectedIndex);
                string[]? lines = _programWorkspace.Lines.Length > 0 ? _programWorkspace.Lines.ToArray() : null;

                if (lines == null || lines.Length == 0)
                {
                    return;
                }

                string filePath = _programWorkspace.LoadedPath ?? string.Empty;
                string fullPath = filePath;
                PreparedProgramLoad prepared = await Task.Run(() =>
                    _mainPresenter.ProgramLoadOrchestrator.Reparse(
                        lines,
                        filePath,
                        fullPath,
                        seedState,
                        profile,
                        stickOut,
                        currentUiIndex));

                BindProgram(prepared.LoadResult);
                _programLine.SetSelectedIndexSilently(currentUiIndex);
                ApplyPreparedProgram(prepared);

                int selectedLine = Math.Max(1, _programLine.SelectedIndex + 1);
                _toolpathRenderService.UpdateVisibilityByLine(_lineVisualsMap, _toolpathRoot, selectedLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RebuildProgramAndToolpath failed: {ex}");
                MessageBox.Show(this, ex.Message, "Ошибка пересчёта траектории", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnFanucCycleStartRequested()
        {
            if (StartAnimationCycle())
            {
                FanucPanel.LogUserAction("CYCLE START");
                _lastInterlockCode = "OK";
                _lastInterlockDetail = "Cycle start allowed";
                if (!_runTimeStopwatch.IsRunning) _runTimeStopwatch.Start();
                if (!_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Start();
                RefreshDiagnosticsPanel();
            }
        }

        private void OnFanucFeedHoldRequested()
        {
            if (PauseAnimationCycle())
            {
                FanucPanel.LogUserAction("CYCLE STOP / FEED HOLD");
                _lastInterlockCode = "OK";
                _lastInterlockDetail = "Feed hold command accepted";
                if (_runTimeStopwatch.IsRunning) _runTimeStopwatch.Stop();
                if (_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Stop();
                _accumulatedRunTime = _runTimeStopwatch.Elapsed;
                _accumulatedCycleTime = _cycleTimeStopwatch.Elapsed;
                RefreshDiagnosticsPanel();
            }
        }

        private void OnFanucResetRequested()
        {
            FanucPanel.LogUserAction("RESET / CYCLE STOP");
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
            _workFeedOverridePercent = Math.Clamp(percent, 0, 120);
            RefreshDiagnosticsPanel();
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

        private void OnOperatorMdiCommandRequested(object? sender, string command) =>
            OnFanucMdiExecuteRequested(command);

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
            try
            {
                string path = _ncProgramCatalogService.CreateProgram(_loadedNcProgramPath, normalizedOLine);
                LoadAndRender(path);
                FanucPanel.NavigateToProgMainScreen();
                FanucPanel.ClearMdiInputBuffer();
            }
            catch (InvalidOperationException ex)
            {
                FanucPanel.SetAlarm(ex.Message, true);
            }
            catch (Exception ex)
            {
                FanucPanel.SetAlarm($"NEW PROGRAM: {ex.Message}", true);
            }
        }

        private void OnFanucProgOpenByNameRequested(string normalizedOLine)
        {
            if (!_ncProgramCatalogService.TryResolveProgramFile(_loadedNcProgramPath, normalizedOLine, out string? path) || !File.Exists(path))
            {
                return;
            }

            LoadAndRender(path);
            FanucPanel.NavigateToProgMainScreen();
        }

        private void OnFanucProgDeleteProgramByNameRequested(string normalizedOLine)
        {
            if (!_ncProgramCatalogService.TryResolveProgramFile(_loadedNcProgramPath, normalizedOLine, out string? path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                _ncProgramCatalogService.DeleteProgram(path);
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
            _programWorkspace.Clear();
            _programLine.BindLineCount(0);
            _programLine.SetSelectedIndexSilently(-1);
            _programExecutionService.LoadProgram(Array.Empty<ParsedCommand>());
            _programExecutionService.SetCurrentIndex(0);
            _programStatsText = string.Empty;
            FanucPanel.SetNcProgramSource(Array.Empty<string>(), null);
            FanucPanel.LogUserAction("Программа снята (очистка)");
        }

        public void BindProgram(ProgramLoadResult loadResult)
        {
            _programLine.BindLineCount(loadResult.Lines.Length);
            _programLine.SetSelectedIndexSilently(0);
            FanucPanel.SetNcProgramSource(loadResult.Lines, loadResult.FullPath);
        }

        public void ApplyPreparedProgram(PreparedProgramLoad prepared)
        {
            _programStatsText = prepared.StatsText;
            EnsureWcsTableLocalCacheForToolpath(CreateProgramSeedState());
            ClearToolpath();
            _lineVisualsMap.Clear();
            ApplyToolpathSegmentsToScene(prepared.SegmentsWithLines);
            SyncStockVisualToTable();
        }

        public void ClearProgramView()
        {
            ClearLoadedNcProgram();
        }

        public void SelectProgramLine(int index)
        {
            if (_programLine.LineCount == 0)
            {
                return;
            }

            _programLine.SelectedIndex = Math.Clamp(index, 0, _programLine.LineCount - 1);
            
        }

        public void ApplyLineState(MachineState state)
        {
            UpdateMachineStateUI(state);
        }

        public void SetCycleButtons(bool canStart, bool canPause)
        {
            _cycleCanStart = canStart;
            _cycleCanPause = canPause;
        }

        public void ShowError(string message)
        {
            MessageBox.Show(this, message, "CNCSS", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void SetStatus(string message)
        {
            StatusText.Text = message;
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

        private void OnFanucOffsetToolValueUpdateRequested(int toolRow, FanucPanelControl.OffsetToolColumn column, double value, bool isAdd)
        {
            PushToolOffsetToController(toolRow, column, value);
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
                _workFeedOverridePercent,
                _rapidFeedOverridePercent,
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
            if (_wcsMarkers.Count > 0)
            {
                BringWcsMarkersToViewportFront();
            }

            _frameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            if (elapsed >= 0.5)
            {
                double fps = _frameCount / elapsed;
                if (FpsText != null)
                {
                    FpsText.Text = fps.ToString("F0");
                    float minFps = VoxelConstants.ViewportMinFps;
                    if (fps < minFps) FpsText.Foreground = Brushes.Red;
                    else if (fps < minFps * 2) FpsText.Foreground = Brushes.Orange;
                    else FpsText.Foreground = Brushes.Lime;
                }

                if (ChunkStatsText != null && _stockLifecycle.Stock is VoxelStockVolume d3dHud)
                {
                    ChunkStatsText.Text =
                        $"Cut: {d3dHud.LastCutMs:F1} ms | Render: {d3dHud.LastRenderMs:F1} ms | Mesh: {d3dHud.DisplayMeshCount}";
                }
                else if (ChunkStatsText != null)
                {
                    ChunkStatsText.Text = "Cut: - | Render: - | Mesh: -";
                }

                double playbackFpsFloor = VoxelConstants.ViewportMinFps;
                double targetSlowdown = fps < playbackFpsFloor
                    ? Math.Max(0.55, fps / playbackFpsFloor)
                    : 1.0;
                // Плавно подводим slowdown к целевому значению, чтобы скорость не "прыгала".
                _fpsSlowdownFactor = Math.Clamp(
                    _fpsSlowdownFactor + (targetSlowdown - _fpsSlowdownFactor) * 0.30,
                    0.55,
                    1.0);

                _frameCount = 0;
                _lastFpsUpdate = now;
                UpdateRuntimeLoadStatusText();
                UpdateStockStatsOverlay();
            }
        }

        private static string FormatVoxelCount(long count)
        {
            if (count >= 1_000_000)
            {
                return $"{count / 1_000_000.0:F1}M";
            }

            if (count >= 1_000)
            {
                return $"{count / 1_000.0:F1}K";
            }

            return count.ToString();
        }

        private void UpdateStockStatsOverlay()
        {
            if (StockStatsPanel == null)
            {
                return;
            }

            if (_stockLifecycle.Stock is not VoxelStockVolume stock)
            {
                StockStatsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            StockVoxelDisplayStats stats = stock.GetDisplayStats();
            StockStatsPanel.Visibility = Visibility.Visible;

            if (StockStatsSizeText != null)
            {
                StockStatsSizeText.Text =
                    $"Размер: {stats.SizeXmm:F0} × {stats.SizeYmm:F0} × {stats.SizeZmm:F0} мм";
            }

            if (StockStatsBricksText != null)
            {
                StockStatsBricksText.Text =
                    $"Чанки: {stats.ChunkCount} | surface {stats.SurfaceVoxelCount}";
            }

            if (StockStatsVoxelsText != null)
            {
                StockStatsVoxelsText.Text =
                    $"Поверхность: {FormatVoxelCount(stats.SurfaceVoxelCount)} вокселей";
            }

            if (StockStatsPrecisionText != null)
            {
                string mode = stats.ShellDisplayed ? "adaptive voxel" : "param";
                StockStatsPrecisionText.Text =
                    $"Точность: {stats.VoxelResolutionMm:F2} мм ({mode})";
            }
        }

        private void UpdateResButtons()
        {
            double simStep = _stockLifecycle.VoxelResolutionMm;
            string text = $"Воксели: адаптивные (вкл., шаг {simStep:F2} мм)";
            if (_stockLifecycle.Stock is VoxelStockVolume voxelStock)
            {
                text += voxelStock.FormatLodStatusSuffix();
            }
            else
            {
                double grid = VoxelConstants.NormalizeDisplayGridMm(_stockLifecycle.AdaptiveDisplayGridMm);
                text += $" | grid {grid:F2} мм ({VoxelConstants.GetDisplayGridLabel(grid)})";
            }
            if (VoxelResolutionStatusText != null)
            {
                VoxelResolutionStatusText.Text = text;
            }

            if (ResolutionStatusText != null)
            {
                ResolutionStatusText.Text = text;
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
            SyncToolVisualMount(showTool: false);
        }

        private void InitAdaptiveProximityVisual()
        {
            _adaptiveProximityVisual.Content = _adaptiveProximityModel;
            _adaptiveProximityVisual.Transform = _adaptiveProximityTransform;
        }

        private bool ShouldShowAdaptiveProximityVisual() =>
            _filterShowAdaptiveProximity && _selectedTool != null;

        private void EnsureAdaptiveProximityGeometry(ToolViewModel tool)
        {
            if (!_filterShowAdaptiveProximity)
            {
                return;
            }

            var key = (tool.Diameter, tool.OverallLength);
            if (_hasCachedAdaptiveProximityGeometry &&
                ReferenceEquals(_cachedAdaptiveProximityTool, tool) &&
                _cachedAdaptiveProximityKey.Equals(key))
            {
                return;
            }

            double toolRadius = tool.Diameter / 2.0;
            _adaptiveProximityModel.Geometry = AdaptiveProximityVisualBuilder.BuildMesh(toolRadius, tool.OverallLength);
            Material material = AdaptiveProximityVisualBuilder.CreateMaterial();
            _adaptiveProximityModel.Material = material;
            _adaptiveProximityModel.BackMaterial = material;

            _cachedAdaptiveProximityTool = tool;
            _cachedAdaptiveProximityKey = key;
            _hasCachedAdaptiveProximityGeometry = true;
        }

        private void ClearAdaptiveProximityGeometry()
        {
            _adaptiveProximityModel.Geometry = null;
            _cachedAdaptiveProximityTool = null;
            _hasCachedAdaptiveProximityGeometry = false;
        }

        private void SyncAdaptiveProximityVisual(bool show)
        {
            if (!show)
            {
                if (UsesMountNodeToolVisual())
                {
                    _machineVisualCoordinator.SetMountNodeAdaptiveProximity(null);
                }
                else if (Viewport.Children.Contains(_adaptiveProximityVisual))
                {
                    Viewport.Children.Remove(_adaptiveProximityVisual);
                }

                return;
            }

            if (UsesMountNodeToolVisual())
            {
                RemoveVisualFromViewport(_adaptiveProximityVisual);
                _machineVisualCoordinator.SetMountNodeAdaptiveProximity(_adaptiveProximityVisual);
                Point3D local = _machineVisualCoordinator.GetToolHolderTranslateInMountNode();
                _adaptiveProximityTransform.OffsetX = local.X;
                _adaptiveProximityTransform.OffsetY = local.Y;
                _adaptiveProximityTransform.OffsetZ = local.Z;
                return;
            }

            if (!Viewport.Children.Contains(_adaptiveProximityVisual))
            {
                Viewport.Children.Add(_adaptiveProximityVisual);
            }
        }

        /// <param name="tool">Активный инструмент или null для скрытия геометрии.</param>
        /// <param name="holderPosition">Точка крепления на шпинделе (торец хвостовика, tool local Z=0).</param>
        private void UpdateToolGeometry(ToolViewModel? tool, Point3D holderPosition)
        {
            if (tool == null)
            {
                _fluteModel.Geometry = null;
                _shankModel.Geometry = null;
                _cachedToolGeometryTool = null;
                _hasCachedToolGeometry = false;
                ClearAdaptiveProximityGeometry();
                SyncAdaptiveProximityVisual(show: false);
                return;
            }

            EnsureToolGeometry(tool);
            if (_filterShowAdaptiveProximity)
            {
                EnsureAdaptiveProximityGeometry(tool);
                SyncAdaptiveProximityVisual(show: true);
            }

            UpdateToolTransform(holderPosition);
        }

        private void EnsureToolGeometry(ToolViewModel tool)
        {
            var key = (tool.SelectedType, tool.Diameter, tool.ShankDiameter, tool.FluteLength, tool.OverallLength, tool.FluteColor, tool.SelectedType == ToolType.Drill ? tool.PointAngle : 0.0);
            if (_hasCachedToolGeometry && ReferenceEquals(_cachedToolGeometryTool, tool) && _cachedToolGeometryKey.Equals(key))
            {
                return;
            }

            (MeshGeometry3D fluteGeom, MeshGeometry3D shankGeom) = ToolGeometryBuilder.Build(
                tool.SelectedType,
                tool.Diameter,
                tool.ShankDiameter,
                tool.FluteLength,
                tool.OverallLength,
                tool.PointAngle);

            _fluteModel.Geometry = fluteGeom;

            Material fluteMat = MaterialHelper.CreateMaterial(tool.FluteColor);
            _fluteModel.Material = fluteMat;
            _fluteModel.BackMaterial = fluteMat;

            _shankModel.Geometry = shankGeom;

            _cachedToolGeometryTool = tool;
            _cachedToolGeometryKey = key;
            _hasCachedToolGeometry = true;
        }

        private void InvalidateToolGeometryCache()
        {
            _cachedToolGeometryTool = null;
            _hasCachedToolGeometry = false;
            _cachedAdaptiveProximityTool = null;
            _hasCachedAdaptiveProximityGeometry = false;
        }

        private void UpdateToolTransform(Point3D position)
        {
            if (UsesMountNodeToolVisual())
            {
                Point3D local = _machineVisualCoordinator.GetToolHolderTranslateInMountNode();
                _toolTransform.OffsetX = local.X;
                _toolTransform.OffsetY = local.Y;
                _toolTransform.OffsetZ = local.Z;
            }
            else
            {
                _toolTransform.OffsetX = position.X;
                _toolTransform.OffsetY = position.Y;
                _toolTransform.OffsetZ = position.Z;
            }

            SyncAdaptiveProximityTransform();
        }

        private void SyncAdaptiveProximityTransform()
        {
            _adaptiveProximityTransform.OffsetX = _toolTransform.OffsetX;
            _adaptiveProximityTransform.OffsetY = _toolTransform.OffsetY;
            _adaptiveProximityTransform.OffsetZ = _toolTransform.OffsetZ;
        }

        private bool UsesMountNodeToolVisual() =>
            MachineKinematics.UsesTableMountedWorkpiece(_machineProfileService.ActiveProfile);

        private void SyncToolVisualMount(bool showTool)
        {
            if (!UsesMountNodeToolVisual())
            {
                if (!showTool)
                {
                    _machineVisualCoordinator.SetMountNodeTool(null);
                }

                return;
            }

            if (!showTool)
            {
                _machineVisualCoordinator.SetMountNodeTool(null);
                return;
            }

            RemoveVisualFromViewport(_toolVisual);
            _machineVisualCoordinator.SetMountNodeTool(_toolVisual);
            Point3D local = _machineVisualCoordinator.GetToolHolderTranslateInMountNode();
            _toolTransform.OffsetX = local.X;
            _toolTransform.OffsetY = local.Y;
            _toolTransform.OffsetZ = local.Z;
        }

        private bool IsToolVisualAttached() =>
            Viewport.Children.Contains(_toolVisual) ||
            _machineVisualCoordinator.IsVisualOnToolMountNode(_toolVisual);

        private void Tool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ToolViewModel tool)
            {
                return;
            }

            if (_selectedTool == tool)
            {
                UpdateToolGeometry(tool, GetToolHolderPosition(GetCurrentPosition()));
            }

            if (e.PropertyName is nameof(ToolViewModel.Diameter) or nameof(ToolViewModel.OverallLength))
            {
                _hasCachedAdaptiveProximityGeometry = false;
                if (_filterShowAdaptiveProximity && _selectedTool == tool)
                {
                    EnsureAdaptiveProximityGeometry(tool);
                }
            }
        }

        private Point3D GetCurrentPosition()
        {
            return GetCurrentPositionForLine(_programLine.SelectedIndex + 1);
        }

        private Point3D GetCurrentPositionForLine(int selectedLine)
        {
            var pos = _programWorkspace.GetPositionAtUiLine(selectedLine);
            return new Point3D(pos.X, pos.Y, pos.Z);
        }

        private void SyncToolWithState()
        {
            if (_currentParser == null || _programLine.SelectedIndex < 0) return;
            int selectedLine = _programLine.SelectedIndex + 1;
            var lastCmd = _currentParser.Commands.LastOrDefault(c => c.LineNumber <= selectedLine);
            bool showTool = lastCmd?.EndState.ToolNumber != null && _filterShowTool;
            if (showTool)
            {
                int toolNum = lastCmd!.EndState.ToolNumber!.Value;
                var toolVM = _tools.FirstOrDefault(t => t.Number == toolNum);
                if (toolVM != null && _selectedTool != toolVM)
                {
                    _selectedTool = toolVM;
                }
            }

            if (UsesMountNodeToolVisual())
            {
                SyncToolVisualMount(showTool);
            }
            else if (showTool && !Viewport.Children.Contains(_toolVisual))
            {
                Viewport.Children.Add(_toolVisual);
            }
            else if (!showTool && Viewport.Children.Contains(_toolVisual))
            {
                Viewport.Children.Remove(_toolVisual);
            }
        }

        private double GetPhysicalSpeed(MachineState state)
        {
            if (state.CurrentMotionMode.Number == 0)
            {
                double overrideVal = _rapidFeedOverridePercent;
                return (MachineState.RAPID_FEED / 60.0) * (overrideVal / 100.0);
            }

            double workOverride = _workFeedOverridePercent;
            return (state.FeedRate / 60.0) * (workOverride / 100.0);
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
            _machineCore.SyncRuntimeFromState(state);
        }

        private void UpdateProgramProgress()
        {
            if (MainProgressBar != null && _currentLines.Length > 0)
            {
                MainProgressBar.Value = (double)(_programLine.SelectedIndex + 1) / _currentLines.Length * 100;
            }
        }

        private double _simulationMultiplier = 2.0;

        private async void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            FanucPanel.UpdatePosTimers(
                _runTimeStopwatch.IsRunning ? _runTimeStopwatch.Elapsed : _accumulatedRunTime,
                _cycleTimeStopwatch.IsRunning ? _cycleTimeStopwatch.Elapsed : _accumulatedCycleTime);

            if (!_machineCore.State.IsRunning)
            {
                return;
            }

            if (_currentParser == null)
            {
                StopAnimationCycle();
                return;
            }

            var playbackStep = _programPlaybackHost.Tick(
                _programLine.SelectedIndex,
                state => GetPhysicalSpeed(state),
                _simulationMultiplier,
                _fpsSlowdownFactor * _cutBackpressureScale);
            var tick = playbackStep.LoopResult;

            if (tick.Action == PlaybackLoopAction.StopProgram)
            {
                StopCycleForProgramEnd(tick.EndProgramMCode, tick.RewindToStart);
                return;
            }

            if (tick.HasIndexUpdate && _programLine.SelectedIndex != tick.NewIndex)
            {
                SelectProgramLineForRuntime(tick.NewIndex);
            }

            _animationTimer.Interval = TimeSpan.FromMilliseconds(tick.TimerIntervalMs);
            _interpolationProgress = _programPlaybackHost.InterpolationProgress;
            Point3D currentPos = playbackStep.CurrentPosition;
            _machineVisualCoordinator.UpdatePose(currentPos.X, currentPos.Y, currentPos.Z);
            ApplyTableKinematicPose(currentPos.X, currentPos.Y, currentPos.Z);
            UpdateToolpathPlaybackVisual(currentPos);

            if (tick.Action == PlaybackLoopAction.PauseForOptionalStop)
            {
                SelectProgramLineForRuntime(tick.NewIndex);

                if (PauseAnimationCycle())
                {
                    _lastInterlockCode = "OPTIONAL_STOP";
                    _lastInterlockDetail = "Program paused at M1";
                    RefreshDiagnosticsPanel();
                }
                return;
            }

            ParsedCommand? currentCommand = _programExecutionService.GetCurrentCommand();
            MachineState playbackState = currentCommand?.StartState ?? _currentParser.State;
            var activeOffset = playbackState.GetActiveWorkOffset();
            PositionText.Text = FormatMcsPosition(
                currentPos.X,
                currentPos.Y,
                currentPos.Z,
                playbackState.MachineZeroOffsetX,
                playbackState.MachineZeroOffsetY,
                playbackState.MachineZeroOffsetZ);
            playbackState.MachineTipToProgram(
                currentPos.X,
                currentPos.Y,
                currentPos.Z,
                out double fanucTipX,
                out double fanucTipY,
                out double fanucTipZ);
            FanucPanel.UpdateMachinePosition(
                currentPos.X,
                currentPos.Y,
                currentPos.Z,
                playbackState.MachineZeroOffsetX,
                playbackState.MachineZeroOffsetY,
                playbackState.MachineZeroOffsetZ,
                activeOffset.X,
                activeOffset.Y,
                activeOffset.Z,
                fanucTipX,
                fanucTipY,
                fanucTipZ);

            // Контур/рез — по кончику в WCS; шпиндель — по машинным осям (ниже при G43).
            Point3D tcpPos = MapPhysicalToToolpathLocal(
                new Point3D(currentPos.X, currentPos.Y, currentPos.Z),
                playbackState);

            ToolViewModel? activeTool = _selectedTool as ToolViewModel
                ?? _cachedToolGeometryTool
                ?? (playbackStep.ActiveToolNumber.HasValue
                    ? _tools.FirstOrDefault(t => t.Number == playbackStep.ActiveToolNumber.Value)
                    : null);

            if (activeTool != null)
            {
                UpdateToolGeometry(activeTool, GetToolHolderPosition(currentPos.X, currentPos.Y, currentPos.Z));
            }

            Point3D cutFrom = ProgramPointToStockLocal(_lastPosition);
            Point3D cutTo = ProgramPointToStockLocal(tcpPos);
            TryBeginVoxelShellOnApproach(cutTo);
            // Шаг playback = хорда между соседними позами интерполяции (G01 и G02/G03).
            // Полная ArcGeometry блока здесь некорректна — native сэмплировал бы всю дугу заново.
            CutMotionDescriptor cutMotion = CutMotionDescriptor.Linear(cutFrom, cutTo);
            bool canRemoveMaterial = playbackStep.BlockKind is MotionBlockKind.Linear or MotionBlockKind.Arc;
            SimulationFrameResult frameResult = _simulationLoopHost.ProcessStockCutStep(
                _isDryRunEnabled,
                _stockLifecycle.Stock,
                activeTool,
                canRemoveMaterial,
                cutMotion,
                (float)tick.TimerIntervalMs);
            _cutBackpressureScale = frameResult.PlaybackScale;

            if (_stockLifecycle.Stock is VoxelStockVolume d3dStock)
            {
                TryCompleteDeferredVoxelShell(d3dStock);

                if (d3dStock.ShellDisplayed && !_lastVoxelShellDisplayed)
                {
                    RefreshVoxelStockDisplay(d3dStock);
                }
            }

            _lastPosition = tcpPos;

            if (tick.Action == PlaybackLoopAction.PauseForSingleBlock)
            {
                if (PauseAnimationCycle())
                {
                    _lastInterlockCode = "SINGLE_BLOCK";
                    _lastInterlockDetail = "Program paused after block";
                    RefreshDiagnosticsPanel();
                }
            }
        }

        private void SelectProgramLineForRuntime(int lineIndex)
        {
            _suppressSelectionSideEffects = true;
            _programLine.SelectedIndex = lineIndex;
            
            SyncToolWithState();
            ApplyRuntimeStatusForLine(lineIndex + 1);
            UpdateProgramProgress();
            _suppressSelectionSideEffects = false;
        }

        private void ShowVoxelLoadingWindow(string message)
        {
            _voxelLoadingWindowDepth++;
            if (_voxelLoadingWindow != null)
            {
                _voxelLoadingWindow.UpdateMessage(message);
                return;
            }
            _voxelLoadingWindow = new LoadingWindow(this, message);
            _voxelLoadingWindow.Closed += (_, _) => _voxelLoadingWindow = null;
            _voxelLoadingWindow.Show();
            PumpUiForLoadingOverlay();
        }

        private void PumpUiForLoadingOverlay()
        {
            var frame = new DispatcherFrame();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }), null);
            Dispatcher.PushFrame(frame);
        }

        /// <summary>Ждёт завершения фоновой задачи, прокачивая UI (без Dispatcher.Invoke с фонового потока).</summary>
        private void WaitBackgroundTaskWithUiPump(Task task)
        {
            while (!task.IsCompleted)
            {
                PumpUiForLoadingOverlay();
                Thread.Sleep(1);
            }

            task.GetAwaiter().GetResult();
        }

        private void UpdateVoxelLoadingMessage(string message)
        {
            if (_voxelLoadingWindow != null)
            {
                _voxelLoadingWindow.UpdateMessage(message);
            }
        }

        private void HideVoxelLoadingWindow()
        {
            _voxelLoadingWindowDepth = Math.Max(0, _voxelLoadingWindowDepth - 1);
            if (_voxelLoadingWindowDepth > 0)
            {
                return;
            }

            LoadingWindow? loading = _voxelLoadingWindow;
            _voxelLoadingWindow = null;
            loading?.RestoreOwner();
            loading?.Close();
        }

        private bool StartAnimationCycle()
        {
            if (_programPlaybackHost.TryStart(
                    _programLine.SelectedIndex,
                    _programLine.LineCount,
                    EnsureVoxelStockForRun,
                    out int normalizedLineIndex))
            {
                SetCycleButtons(canStart: false, canPause: true);

                if (_programLine.SelectedIndex != normalizedLineIndex)
                {
                    _programLine.SelectedIndex = normalizedLineIndex;
                }

                SyncCutTrackingFromMachinePosition(_programPlaybackHost.CurrentPosition);
                _animationTimer.Start();
                return true;
            }

            return false;
        }

        private bool PauseAnimationCycle()
        {
            if (!_programPlaybackHost.TryPause())
            {
                return false;
            }

            SetCycleButtons(canStart: true, canPause: false);
            _animationTimer.Stop();

            return true;
        }

        private void StopCycleForProgramEnd(int? endProgramMCode, bool rewindToStart)
        {
            _animationTimer.Stop();
            if (_runTimeStopwatch.IsRunning) _runTimeStopwatch.Stop();
            if (_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Stop();
            _accumulatedRunTime = _runTimeStopwatch.Elapsed;
            _accumulatedCycleTime = _cycleTimeStopwatch.Elapsed;
            SetCycleButtons(canStart: true, canPause: false);
            Point3D rewindPosition = rewindToStart && _programLine.LineCount > 0
                ? GetCurrentPositionForLine(1)
                : _programPlaybackHost.CurrentPosition;
            ProgramEndState endState = _programPlaybackHost.StopForProgramEnd(
                _programLine.SelectedIndex,
                _programLine.LineCount,
                endProgramMCode,
                rewindToStart,
                rewindPosition);
            SyncCutTrackingFromMachinePosition(endState.CurrentPosition);
            _interpolationProgress = _programPlaybackHost.InterpolationProgress;

            if (endState.RewindToStart && _programLine.LineCount > 0)
            {
                _suppressSelectionSideEffects = true;
                _programLine.SelectedIndex = endState.SelectedLineIndex;
                
                _suppressSelectionSideEffects = false;
                ApplyRuntimeStatusForLine(1);
                UpdateProgramProgress();
            }

            _lastInterlockCode = endProgramMCode == 30 ? "M30" : endProgramMCode == 2 ? "M2" : "PROGRAM_END";
            _lastInterlockDetail = endProgramMCode == 30
                ? "M30: cycle stop and rewind to start"
                : endProgramMCode == 2
                    ? "M2: cycle stop"
                    : "Program ended";
            FanucPanel.LogUserAction($"{_lastInterlockCode}: CYCLE STOP");
            _stockLifecycle.Stock?.FreezeModel();
            RefreshDiagnosticsPanel();
        }

        private void UpdateRuntimeLoadStatusText()
        {
            if (RuntimeLoadText == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            TimeSpan cpuNow = _selfProcess.TotalProcessorTime;
            double elapsedMs = (now - _runtimeLoadLastAtUtc).TotalMilliseconds;
            if (elapsedMs >= 150)
            {
                double cpuUsedMs = (cpuNow - _runtimeLoadLastCpu).TotalMilliseconds;
                double cpuPercent = cpuUsedMs / (elapsedMs * Math.Max(1, Environment.ProcessorCount)) * 100.0;
                _runtimeCpuPercent = Math.Clamp(cpuPercent, 0.0, 100.0);
                _runtimeLoadLastAtUtc = now;
                _runtimeLoadLastCpu = cpuNow;
            }

            string stockState = _stockLifecycle.Stock is VoxelStockVolume voxel
                ? $"Adaptive ({voxel.AdaptiveStock.Octree.EnumerateAllChunks().Count} chunks, cut {voxel.LastRemovedVoxels})"
                : "нет";
            RuntimeLoadText.Text = $"CPU: {_runtimeCpuPercent:F0}% | Заготовка: {stockState}";
        }

        private void StopAnimationCycle()
        {
            _animationTimer.Stop();
            if (_runTimeStopwatch.IsRunning) _runTimeStopwatch.Stop();
            if (_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Stop();
            _accumulatedRunTime = _runTimeStopwatch.Elapsed;
            _accumulatedCycleTime = _cycleTimeStopwatch.Elapsed;
            _programPlaybackHost.ResetToHome();
            SyncCutTrackingFromMachinePosition(_programPlaybackHost.CurrentPosition);
            _interpolationProgress = _programPlaybackHost.InterpolationProgress;
            _mainPresenter.Reset(_programPlaybackHost.CurrentPosition);
            FanucPanel.UpdateProgramLine(null);
            HideToolpathPlaybackCap();

            if (_lineVisualsMap.Count > 0 && _programLine.SelectedIndex >= 0)
            {
                _toolpathRenderService.UpdateVisibilityByLine(
                    _lineVisualsMap,
                    _toolpathRoot,
                    _programLine.SelectedIndex + 1);
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateResButtons();

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

            ShowVoxelLoadingWindow("Запуск приложения...");
            try
            {
                await Dispatcher.Yield(DispatcherPriority.Render);
                _machineVisualCoordinator.Attach(Viewport);
                _machineVisualCoordinator.SetMachineConstructorOverlaysVisible(false);
                UpdateVoxelLoadingMessage("Загрузка станка и профиля...");
                await ApplyActiveMachineProfileAsync();
                UpdateVoxelLoadingMessage("Загрузка программы и заготовки...");
                await LoadAndRenderAsync(ncPath);
            }
            finally
            {
                HideVoxelLoadingWindow();
            }

            OperatorPanel.HighlightMode(_currentControllerMode);
            OperatorPanel.SyncWorkOverrideSlider(_workFeedOverridePercent);
            OperatorPanel.SyncSpindleOverrideSlider(_operatorSpindleOverridePercent);
            OperatorPanel.SyncMachiningToggles(_isSingleBlockEnabled, _isOptionalStopEnabled);

            _ = Dispatcher.BeginInvoke(new Action(InitializeExternalPanelWindows), DispatcherPriority.Loaded);
            _ = Dispatcher.BeginInvoke(
                () => ViewportCameraHelper.SetDefaultMainSceneView(Viewport),
                DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Создаёт воксельную заготовку по текущим габаритам и <see cref="_stockLifecycle.ConstructorConfig"/>
        /// (форма: прямоугольник, шестигранник, круг, труба). Маска занятости — в <see cref="StockSimulationCoordinator"/>.
        /// </summary>
        /// <param name="reuseRunningSimulation">
        /// Если симуляция уже идёт с несъёмным объёмом — не пересоздаём воксели (перед Cycle Start).
        /// </param>
        /// <param name="warnOnInvalidStockConfig">При неверных размерах показывать окно (перед запуском цикла).</param>
        private bool TryEnsureVoxelStock(bool reuseRunningSimulation, bool warnOnInvalidStockConfig)
        {
            if (!TryReadStockConfig(out _))
            {
                if (warnOnInvalidStockConfig)
                {
                    MessageBox.Show(this, "Некорректные параметры заготовки.", "Заготовка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return false;
            }

            try
            {
                if (!_stockLifecycle.TryEnsureVoxelRuntime(
                        reuseRunningSimulation,
                        GetSelectedStockColor()))
                {
                    if (warnOnInvalidStockConfig)
                    {
                        MessageBox.Show(this, "Некорректные параметры заготовки.", "Заготовка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    return false;
                }
            }
            catch (Exception ex) when (warnOnInvalidStockConfig)
            {
                MessageBox.Show(this, ex.Message, "Заготовка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>Cycle Start: переиспользует заготовку из конструктора без тяжёлого прогрева; пересборка только после FreezeModel.</summary>
        private bool EnsureVoxelStockForRun()
        {
            if (_stockLifecycle.Stock is VoxelStockVolume existing && !existing.IsCutsFrozen)
            {
                _stockLifecycle.DeferVoxelShellUntilApproach = true;
                _stockLifecycle.DeferVoxelUntilRun = false;
                ApplyStockViewportVisibility(triggerMeshRefresh: false);
                return true;
            }

            if (_stockLifecycle.Stock is VoxelStockVolume { IsCutsFrozen: true })
            {
                return BuildAndWarmVoxelStock(
                    "Сброс заготовки для нового цикла…",
                    reuseRunningSimulation: false,
                    showLoadingModal: true);
            }

            if (_stockLifecycle.Stock is VoxelStockVolume { ShellDisplayed: true, IsCutsFrozen: false })
            {
                _stockLifecycle.DeferVoxelShellUntilApproach = false;
                _stockLifecycle.DeferVoxelUntilRun = false;
                ApplyStockViewportVisibility(triggerMeshRefresh: false);
                return true;
            }

            if (_stockLifecycle.Stock == null)
            {
                if (_stockLifecycle.Bounds == null || _stockLifecycle.ConstructorConfig == null)
                {
                    MessageBox.Show(
                        this,
                        "Создайте заготовку в меню «Симуляция → Конструктор заготовки…».",
                        "Заготовка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                if (!BuildAndWarmVoxelStock(
                        "Подготовка воксельной заготовки…",
                        reuseRunningSimulation: false,
                        showLoadingModal: true))
                {
                    return false;
                }
            }

            _stockLifecycle.DeferVoxelShellUntilApproach = true;
            _stockLifecycle.DeferVoxelUntilRun = true;
            ApplyStockViewportVisibility(triggerMeshRefresh: false);
            return true;
        }

        /// <summary>Создаёт воксельный runtime и прогревает оболочку.</summary>
        private bool BuildAndWarmVoxelStock(string loadingMessage, bool reuseRunningSimulation, bool showLoadingModal)
        {
            if (showLoadingModal)
            {
                ShowVoxelLoadingWindow(loadingMessage);
            }

            try
            {
                if (!TryReadStockConfig(out _))
                {
                    MessageBox.Show(this, "Некорректные параметры заготовки.", "Заготовка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (reuseRunningSimulation &&
                    _stockLifecycle.Stock is VoxelStockVolume { IsCutsFrozen: false, ShellDisplayed: true })
                {
                    _stockLifecycle.DeferVoxelShellUntilApproach = false;
                    _stockLifecycle.DeferVoxelUntilRun = false;
                    ApplyStockViewportVisibility(triggerMeshRefresh: false);
                    return true;
                }

                if (_stockLifecycle.Stock == null)
                {
                    if (!_stockLifecycle.TryEnsureVoxelRuntime(false, GetSelectedStockColor(), PumpUiForLoadingOverlay))
                    {
                        MessageBox.Show(
                            this,
                            "Не удалось подготовить заготовку. Проверьте параметры в конструкторе.",
                            "Заготовка",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return false;
                    }
                }

                _stockLifecycle.DeferVoxelShellUntilApproach = true;
                _stockLifecycle.DeferVoxelUntilRun = false;
                ApplyStockViewportVisibility(triggerMeshRefresh: false);
                return true;
            }
            finally
            {
                if (showLoadingModal)
                {
                    HideVoxelLoadingWindow();
                }
            }
        }

        /// <summary>Подготовка заготовки к отображению (параметрическая модель).</summary>
        private bool PrepareVoxelStockVisual(string loadingMessage, bool showLoadingModal)
        {
            bool ownsModal = showLoadingModal && _voxelLoadingWindow == null;
            if (ownsModal)
            {
                ShowVoxelLoadingWindow(loadingMessage);
            }
            else if (showLoadingModal)
            {
                UpdateVoxelLoadingMessage(loadingMessage);
            }

            try
            {
                if (!TryReadStockConfig(out _))
                {
                    MessageBox.Show(this, "Некорректные параметры заготовки.", "Заготовка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                _stockLifecycle.DeferVoxelUntilRun = false;
                if (_stockLifecycle.Stock == null &&
                    !_stockLifecycle.TryEnsureVoxelRuntime(false, GetSelectedStockColor()))
                {
                    return false;
                }

                ApplyStockViewportVisibility(triggerMeshRefresh: false);
                if (_stockLifecycle.Stock is VoxelStockVolume d3dReady)
                {
                    RefreshVoxelStockDisplay(d3dReady);
                }

                return true;
            }
            finally
            {
                if (ownsModal)
                {
                    HideVoxelLoadingWindow();
                }
            }
        }

        private void ApplySettingsTop_Click(object sender, RoutedEventArgs e)
        {
            // Поля находятся внутри DataTemplate, поэтому ищем их через родительский StackPanel кнопки.
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

        /// <summary>
        /// Обновляет параметрический меш заготовки (ручные Min/Max, смена разрешения).
        /// Сбрасывает воксельный runtime; после конструктора заготовки используйте <see cref="ApplyStockConstructor"/>.
        /// </summary>
        private void ApplyStock()
        {
            try
            {
                if (!TryReadStockConfig(out StockConfig cfg))
                {
                    return;
                }

                if (_stockLifecycle.AnchoredToTable)
                {
                    if (_stockLifecycle.AutoAlignToMount)
                    {
                        RealignStockCenteredOnMount(
                            _machineProfileService.ActiveProfile,
                            cfg.MaxX - cfg.MinX,
                            cfg.MaxY - cfg.MinY,
                            cfg.MaxZ - cfg.MinZ);
                        if (!TryReadStockConfig(out cfg))
                        {
                            return;
                        }
                    }
                }

                _stockLifecycle.Bounds = ToStockBounds(cfg);
                ReleaseVoxelStockRuntime();

                var bounds = ToStockBounds(cfg);
                _stockModel.Geometry = _stockLifecycle.BuildParametricMesh(bounds);
                Color color = GetSelectedStockColor();
                _stockModel.Material = MaterialHelper.CreateMaterial(color);
                _stockModel.BackMaterial = _stockModel.Material;

                SyncStockVisualToTable(syncWcsMarker: false);
                ApplyStockViewportVisibility();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyStock failed: {ex}");
            }
        }

        private void LoadAndRender(string filePath) => _ = LoadAndRenderAsync(filePath);

        private async Task LoadAndRenderAsync(string filePath)
        {
            bool ownsLoadingOverlay = _voxelLoadingWindow == null;
            if (ownsLoadingOverlay)
            {
                ShowVoxelLoadingWindow("Загрузка программы и расчёт траектории...");
            }
            else
            {
                UpdateVoxelLoadingMessage("Загрузка программы и расчёт траектории...");
            }

            await Dispatcher.Yield(DispatcherPriority.Background);

            try
            {
                _toolSettingsDialog?.Close();
                ClearToolpath();
                _lineVisualsMap.Clear();

                MachineState toolpathSeed = CreateProgramSeedState();
                var profile = _machineProfileService.ActiveProfile;
                double stickOut = GetToolStickOutMm();

                PreparedProgramLoad prepared = await Task.Run(() =>
                    _mainPresenter.ProgramLoadOrchestrator.Load(filePath, toolpathSeed, profile, stickOut));

                ProgramLoadResult loadResult = prepared.LoadResult;
                BindProgram(loadResult);
                SetStatus($"Program loaded: {Path.GetFileName(loadResult.FullPath)}");
                ApplyPreparedProgram(prepared);

                _lastMessageToolNumber = null;
                SyncCutTrackingFromMachinePosition(_programPlaybackHost.CurrentPosition);
                _interpolationProgress = _programPlaybackHost.InterpolationProgress;
                _tools.Clear();
                foreach (var t in loadResult.ToolNumbers)
                {
                    var tool = new ToolViewModel { Number = t };
                    tool.FluteColor = ToolPaletteSwatches.NextRandomDistinctFluteColor(_tools.Select(x => x.FluteColor));
                    tool.PropertyChanged += Tool_PropertyChanged;
                    _tools.Add(tool);
                }

                SelectFirstTool();
                UpdateMachineStateUI(loadResult.Parser.State);

                // Автоматический подбор габаритов заготовки по контуру УП больше не используется:
                // параметры заготовки задаются только через конструктор заготовки.

                if (_selectedTool is ToolViewModel selectedTool)
                {
                    UpdateToolGeometry(selectedTool, GetToolHolderPosition(GetCurrentPosition()));
                }

                FanucPanel.LogUserAction($"Загрузка программы: {Path.GetFileName(loadResult.FullPath)}");

                // Воксельная заготовка строится только при старте выполнения УП (Cycle Start).

                await Dispatcher.InvokeAsync(() => Viewport.ZoomExtents(), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (ownsLoadingOverlay)
                {
                    HideVoxelLoadingWindow();
                }
            }
        }

        private void ApplyToolpathSegmentsToScene(IReadOnlyList<ToolpathSegmentWithLine> segmentsWithLines)
        {
            foreach (var item in segmentsWithLines)
            {
                var visual = CreateVisualForSegment(item.Segment);
                _toolpathRoot.Children.Add(visual);
                _toolpathRenderService.TrackVisible(visual);
                _toolpathVisuals.Add(visual);
                if (!_lineVisualsMap.ContainsKey(item.LineNumber))
                {
                    _lineVisualsMap[item.LineNumber] = new List<Visual3D>();
                }

                _lineVisualsMap[item.LineNumber].Add(visual);
            }

            int selectedLine = Math.Max(1, _programLine.SelectedIndex + 1);
            _toolpathRenderService.UpdateVisibilityByLine(_lineVisualsMap, _toolpathRoot, selectedLine);
        }

        private Visual3D CreateVisualForSegment(ToolpathSegment seg)
        {
            var color = seg.Kind switch { ToolpathSegmentKind.Rapid => Colors.OrangeRed, ToolpathSegmentKind.Linear => Colors.LimeGreen, ToolpathSegmentKind.Arc => Colors.DeepSkyBlue, _ => Colors.White };
            // Points in table-local CS (WCS-based); stock and toolpath are parented under the table node.
            return new LinesVisual3D { Color = color, Thickness = 2.0, Points = new Point3DCollection(seg.Points) };
        }

        private void SelectFirstTool()
        {
            _selectedTool = _tools.Count > 0 ? _tools[0] : null;
            if (_selectedTool != null)
            {
                UpdateToolGeometry(_selectedTool, GetToolHolderPosition(GetCurrentPosition()));
            }
        }

        private void OnProgramLineSelectionChanged(object? sender, EventArgs e)
        {
            if (_programLine.SelectedIndex < 0 || _currentParser == null) return;
            _programExecutionService.SetCurrentIndex(_programLine.SelectedIndex);
            if (_suppressSelectionSideEffects)
            {
                return;
            }

            Point3D machinePos = GetCurrentPosition();
            SyncCutTrackingFromMachinePosition(machinePos);
            _programPlaybackHost.SetCurrentPosition(machinePos);
            _programPlaybackHost.BeginSegmentFromCurrentPosition();
            int selectedLine = _programLine.SelectedIndex + 1;
            SyncToolWithState();
            if (_selectedTool is ToolViewModel tool)
            {
                UpdateToolGeometry(tool, GetToolHolderPosition(GetCurrentPosition()));
            }

            HideToolpathPlaybackCap();
            _toolpathRenderService.UpdateVisibilityByLine(_lineVisualsMap, _toolpathRoot, selectedLine);
            _mainPresenter.OnLineSelected(_programLine.SelectedIndex);
            UpdateProgramProgress();
        }

        private void UpdateMachineStateUI(MachineState state)
        {
            PositionText.Text = FormatMcsPosition(state);
            UpdateFanucMachinePosition(state);
            ApplyRuntimeStatus(state);
        }

        private static void UpdateFanucMachinePosition(FanucPanelControl fanucPanel, MachineState state)
        {
            var activeOffset = state.GetActiveWorkOffset();
            state.MachineTipToProgram(state.X, state.Y, state.Z, out double tipX, out double tipY, out double tipZ);
            fanucPanel.UpdateMachinePosition(
                state.X,
                state.Y,
                state.Z,
                state.MachineZeroOffsetX,
                state.MachineZeroOffsetY,
                state.MachineZeroOffsetZ,
                activeOffset.X,
                activeOffset.Y,
                activeOffset.Z,
                tipX,
                tipY,
                tipZ);
        }

        private void UpdateFanucMachinePosition(MachineState state) =>
            UpdateFanucMachinePosition(FanucPanel, state);

        private void UpdateFanucMachinePosition(MachineStateChangedEvent evt)
        {
            if (_currentParser?.State is MachineState state)
            {
                state.MachineTipToProgram(evt.X, evt.Y, evt.Z, out double tipX, out double tipY, out double tipZ);
                FanucPanel.UpdateMachinePosition(
                    evt.X,
                    evt.Y,
                    evt.Z,
                    evt.MachineZeroOffsetX,
                    evt.MachineZeroOffsetY,
                    evt.MachineZeroOffsetZ,
                    evt.OffsetX,
                    evt.OffsetY,
                    evt.OffsetZ,
                    tipX,
                    tipY,
                    tipZ);
                return;
            }

            FanucPanel.UpdateMachinePosition(
                evt.X,
                evt.Y,
                evt.Z,
                evt.MachineZeroOffsetX,
                evt.MachineZeroOffsetY,
                evt.MachineZeroOffsetZ,
                evt.OffsetX,
                evt.OffsetY,
                evt.OffsetZ);
        }

        private static string FormatMcsPosition(MachineState state) =>
            FormatMcsPosition(
                state.X,
                state.Y,
                state.Z,
                state.MachineZeroOffsetX,
                state.MachineZeroOffsetY,
                state.MachineZeroOffsetZ);

        private static string FormatMcsPosition(
            double physicalX,
            double physicalY,
            double physicalZ,
            double machineZeroOffsetX,
            double machineZeroOffsetY,
            double machineZeroOffsetZ)
        {
            double mcsX = physicalX - machineZeroOffsetX;
            double mcsY = physicalY - machineZeroOffsetY;
            double mcsZ = physicalZ - machineZeroOffsetZ;
            return $"X: {mcsX:F3} Y: {mcsY:F3} Z: {mcsZ:F3}";
        }

        private void ApplyRuntimeStatus(MachineState state)
        {
            if (state.ToolNumber.HasValue && state.ToolNumber != _lastMessageToolNumber)
            {
                _lastMessageToolNumber = state.ToolNumber;
                FanucPanel.LogUserAction($"TOOL CALL T{state.ToolNumber.Value}");
            }

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
            SyncActiveWcsOriginMarker(
                state.CurrentCoordinateSystem.Number,
                state.X,
                state.Y,
                state.Z,
                updateTableLocalPosition: true);
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

        private void ClearToolpath()
        {
            foreach (var v in _toolpathVisuals)
            {
                if (_toolpathRoot.Children.Contains(v))
                {
                    _toolpathRoot.Children.Remove(v);
                }
            }

            _toolpathVisuals.Clear();
            HideToolpathPlaybackCap();
            _toolpathRenderService.Reset();
        }

        private void HideToolpathPlaybackCap()
        {
            if (_toolpathRoot.Children.Contains(_toolpathPlaybackCap))
            {
                _toolpathRoot.Children.Remove(_toolpathPlaybackCap);
            }
        }

        private void UpdateToolpathPlaybackVisual(Point3D currentPhysical)
        {
            if (!_filterShowToolpath || _lineVisualsMap.Count == 0)
            {
                return;
            }

            if (!_machineCore.State.IsRunning)
            {
                return;
            }

            var cmd = _programExecutionService.GetCurrentCommand();
            int currentLine = cmd?.LineNumber ?? Math.Max(1, _programLine.SelectedIndex + 1);
            bool inSegment = _programPlaybackHost.IsSegmentInProgress;
            int throughLine = inSegment ? currentLine - 1 : currentLine;
            if (throughLine < 0)
            {
                throughLine = 0;
            }

            if (inSegment && cmd != null)
            {
                MachineState segmentState = cmd.StartState;
                Point3D capFrom = MapPhysicalToToolpathLocal(_programPlaybackHost.SegmentStart, segmentState);
                Point3D capTo = MapPhysicalToToolpathLocal(currentPhysical, segmentState);
                _toolpathRenderService.UpdatePlaybackProgress(
                    _lineVisualsMap,
                    _toolpathRoot,
                    throughLine,
                    _toolpathPlaybackCap,
                    showCap: true,
                    capFrom,
                    capTo,
                    GetToolpathColorForCommand(cmd));
            }
            else
            {
                _toolpathRenderService.UpdatePlaybackProgress(
                    _lineVisualsMap,
                    _toolpathRoot,
                    throughLine,
                    _toolpathPlaybackCap,
                    showCap: false,
                    default,
                    default,
                    Colors.White);
            }
        }

        private static bool IsActiveCutMotion(Point3D from, Point3D to)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            return dx * dx + dy * dy + dz * dz >=
                   VoxelConstants.CutMotionEpsilonMm * VoxelConstants.CutMotionEpsilonMm;
        }

        private Point3D MapPhysicalToToolpathLocal(Point3D physical, MachineState state)
        {
            MachineDefinition profile = _machineProfileService.ActiveProfile;
            if (MachineKinematics.UsesTableMountedWorkpiece(profile))
            {
                return WorkpieceMountPlacement.PhysicalProgramToTableLocal(
                    profile,
                    state,
                    physical.X,
                    physical.Y,
                    physical.Z);
            }

            state.MachineAxisToWorkpieceTip(physical.X, physical.Y, physical.Z, out double programX, out double programY, out double programZ);
            MachineState.WorkOffset wcs = state.GetActiveWorkOffset();
            Point3D wcsScene = MachineAttachmentService.GetWcsOriginScene(
                profile,
                new MachineGeometryPoint { X = wcs.X, Y = wcs.Y, Z = wcs.Z });
            return new Point3D(
                wcsScene.X + programX,
                wcsScene.Y + programY,
                wcsScene.Z + programZ);
        }

        private static Color GetToolpathColorForCommand(ParsedCommand cmd)
        {
            if (cmd.Arc != null)
            {
                return Colors.DeepSkyBlue;
            }

            return cmd.StartState.CurrentMotionMode.Number == 0
                ? Colors.OrangeRed
                : Colors.LimeGreen;
        }

        private StockVolumeConfig BuildStockVolumeConfig(StockConfig cfg)
        {
            if (!_stockLifecycle.AnchoredToTable)
            {
                return new StockVolumeConfig(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
            }

            if (_stockLifecycle.BoundsAreTableLocal)
            {
                return new StockVolumeConfig(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
            }

            if (TryConvertStockBoundsToTableLocal(cfg, out WorkpiecePlacement.StockBounds tableLocal))
            {
                return new StockVolumeConfig(
                    tableLocal.MinX,
                    tableLocal.MaxX,
                    tableLocal.MinY,
                    tableLocal.MaxY,
                    tableLocal.MinZ,
                    tableLocal.MaxZ);
            }

            return new StockVolumeConfig(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
        }

        private bool TryConvertStockBoundsToTableLocal(StockConfig cfg, out WorkpiecePlacement.StockBounds tableLocal)
        {
            tableLocal = default;
            Transform3D tableToWorld = _machineVisualCoordinator.GetNodeToWorldTransform(MachineNodeIds.Table);
            if (tableToWorld is not MatrixTransform3D matrixTransform)
            {
                return false;
            }

            Matrix3D worldToTable = matrixTransform.Value;
            if (!worldToTable.HasInverse)
            {
                return false;
            }

            worldToTable.Invert();
            var world = new WorkpiecePlacement.StockBounds(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
            tableLocal = WorkpiecePlacement.TransformBounds(worldToTable, world);
            return true;
        }

        /// <summary>Точка в СК границ заготовки (table.Root local или сцена) → смещение WCS в MCS.</summary>
        private MachineGeometryPoint StockBoundsPointToWcsOffsetMcs(MachineGeometryPoint pointInStockBoundsSpace)
        {
            if (_stockLifecycle.AnchoredToTable && _stockLifecycle.BoundsAreTableLocal)
            {
                return TableLocalStockPointToMcs(pointInStockBoundsSpace);
            }

            var mcsZero = _machineProfileService.ActiveProfile.McsZeroOffset ?? MachineGeometryPoint.Zero;
            return MachineMcsCoordinates.SceneToMcs(pointInStockBoundsSpace, mcsZero);
        }

        private MachineGeometryPoint TableLocalStockPointToMcs(MachineGeometryPoint tableRootLocal)
        {
            (double mx, double my, double mz) = _machineVisualCoordinator.GetPreviewPhysicalPose();
            MachineDefinition profile = _machineProfileService.ActiveProfile;
            Point3D inMcs = WorkpieceMountPlacement.TableRootLocalToWcsMcs(
                profile,
                mx,
                my,
                mz,
                new Point3D(tableRootLocal.X, tableRootLocal.Y, tableRootLocal.Z));
            return new MachineGeometryPoint { X = inMcs.X, Y = inMcs.Y, Z = inMcs.Z };
        }

        private void RefreshWcsOriginTableLocalCacheFromOffsets()
        {
            var profile = _machineProfileService.ActiveProfile;
            if (!MachineKinematics.UsesTableMountedWorkpiece(profile))
            {
                WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
                return;
            }

            (double mx, double my, double mz) = _machineVisualCoordinator.GetPreviewPhysicalPose();
            WorkpieceMountPlacement.SyncWcsOriginTableLocalFromMcs(profile, _workOffsets, mx, my, mz);
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
            _stockLifecycle.ReleaseRuntime();
            _machineStateSubscription.Dispose();
            _alarmSubscription.Dispose();
            _mdiModeSubscription.Dispose();
            _programLineSubscription.Dispose();
            _workOffsetsSubscription.Dispose();
            _toolOffsetsSubscription.Dispose();
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
            OperatorPanel.MdiCommandRequested -= OnOperatorMdiCommandRequested;
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
            OperatorPanel.SyncWorkOverrideSlider(_workFeedOverridePercent);

            OperatorPanel.SyncMachiningToggles(_isSingleBlockEnabled, _isOptionalStopEnabled);
            UpdateOperatorScrollViewport();
            UpdateFanucScrollViewport();
        }
    }
}


