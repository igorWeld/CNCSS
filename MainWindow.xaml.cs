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
using CNCSS.Logic.ProgramLoading;
using CNCSS.Logic.NcPrograms;
using CNCSS.Vis;
using CNCSS.Vis.Configuration;
using CNCSS.Data;
using CNCSS.Data.Tools;
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
using CNCSS.GpuVerification;
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
        private readonly SemaphoreSlim _stockMeshGate = new(1, 1);

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
        private readonly ToolpathRenderService _toolpathRenderService;
        private readonly MachineProfileService _machineProfileService;
        private readonly StlMeshLoader _stlMeshLoader;
        private readonly MachineVisualCoordinator _machineVisualCoordinator;
        private MachineSetupWindow? _machineSetupWindow;
        private readonly VoxelPerformanceMonitor _voxelPerformanceMonitor = new();
        private readonly Process _selfProcess = Process.GetCurrentProcess();
        private DateTime _stockLoadLastAtUtc;
        private TimeSpan _stockLoadLastCpu;
        private double _stockLoadCpuPercent;
        private double _stockLoadGpuMemoryPercent;
        private string _stockLoadGpuMode = "GPU ожидание";
        private DateTime _runtimeLoadLastAtUtc = DateTime.UtcNow;
        private TimeSpan _runtimeLoadLastCpu = Process.GetCurrentProcess().TotalProcessorTime;
        private double _runtimeCpuPercent;
        private DateTime _lastCameraDrivenStockRefreshUtc = DateTime.MinValue;
        private bool _suppressCameraDrivenStockRefresh;
        private int _stockMeshRefreshWorkerRunning;
        private int _stockMeshRefreshPending;
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

        /// <summary>Настройки модуля GPU-верификации (persist JSON).</summary>
        private GpuVerificationUserSettings _gpuVerificationSettings = GpuVerificationSettingsStore.Load();


        // FPS Counter fields
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _fpsSlowdownFactor = 1.0; // Коэффициент замедления при низком FPS
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
            _toolpathRenderService = composition.ToolpathRenderService;
            _machineProfileService = composition.MachineProfileService;
            _stlMeshLoader = composition.StlMeshLoader;
            _machineVisualCoordinator = composition.MachineVisualCoordinator;
            FanucPanel.ProgramCatalogService = _ncProgramCatalogService;
            _machineProfileService.ActiveProfileChanged += _ =>
                Dispatcher.InvokeAsync(() => ApplyActiveMachineProfileAsync());
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
            
            _stockModel.Material = MaterialHelper.CreateMaterial(Colors.LightGray);
            _stockModel.BackMaterial = _stockModel.Material;
            _stockVisual.Content = _stockModel;
            Viewport.Children.Add(_stockVisual);
            Viewport.Children.Add(_toolpathRoot);
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

        private void MenuStockWcsZero_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string tag } || !TryParseStockWcsZeroTag(tag, out StockWorkOriginXY xy, out StockWorkOriginZ z))
            {
                return;
            }

            ApplyWorkOffsetFromStock(xy, z);
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
                    "Нулевая точка детали",
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

        private static bool TryParseStockWcsZeroTag(string tag, out StockWorkOriginXY xy, out StockWorkOriginZ z)
        {
            xy = StockWorkOriginXY.Center;
            z = StockWorkOriginZ.Top;
            string[] parts = tag.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            xy = parts[0] switch
            {
                "Center" => StockWorkOriginXY.Center,
                "MinMin" => StockWorkOriginXY.CornerMinXMinY,
                "MaxMin" => StockWorkOriginXY.CornerMaxXMinY,
                "MinMax" => StockWorkOriginXY.CornerMinXMaxY,
                "MaxMax" => StockWorkOriginXY.CornerMaxXMaxY,
                _ => StockWorkOriginXY.Center
            };

            z = parts[1] switch
            {
                "Top" => StockWorkOriginZ.Top,
                "Bottom" => StockWorkOriginZ.Bottom,
                _ => StockWorkOriginZ.Top
            };

            return true;
        }

        private void ApplyWorkOffsetFromStock(StockWorkOriginXY xy, StockWorkOriginZ z)
        {
            if (!TryGetStockMachineBounds(out WorkpiecePlacement.StockBounds bounds))
            {
                MessageBox.Show(
                    this,
                    "Сначала задайте заготовку (меню «Симуляция → Конструктор заготовки…»).",
                    "Нулевая точка детали",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MachineGeometryPoint originOnStock = StockWorkOrigin.Compute(bounds, xy, z);
            int system = _selectedOffsetSystem;
            WorkpieceMountPlacement.RegisterWcsOriginTableLocal(
                system,
                new Point3D(originOnStock.X, originOnStock.Y, originOnStock.Z));
            MachineGeometryPoint originMcs = StockBoundsPointToWcsOffsetMcs(originOnStock);
            ApplyWorkOffsetDirect(system, originMcs.X, originMcs.Y, originMcs.Z);

            string xyLabel = xy switch
            {
                StockWorkOriginXY.Center => "центр XY",
                StockWorkOriginXY.CornerMinXMinY => "угол Min X, Min Y",
                StockWorkOriginXY.CornerMaxXMinY => "угол Max X, Min Y",
                StockWorkOriginXY.CornerMinXMaxY => "угол Min X, Max Y",
                StockWorkOriginXY.CornerMaxXMaxY => "угол Max X, Max Y",
                _ => "центр XY"
            };
            string zLabel = z == StockWorkOriginZ.Top ? "верх Z" : "низ Z";
            StatusText.Text =
                $"WCS G{system} (MCS): X={originMcs.X:F3} Y={originMcs.Y:F3} Z={originMcs.Z:F3} ({xyLabel}, {zLabel})";
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
        /// Применяет результат конструктора: параметрический меш на столе, цвет и габариты;
        /// воксельный runtime откладывается до Cycle Start (<see cref="_stockLifecycle.DeferVoxelUntilRun"/>).
        /// </summary>
        private void ApplyStockConstructor(StockConstructorConfig cfg)
        {
            _stockLifecycle.ConstructorConfig = cfg;

            _stockLifecycle.DeferVoxelUntilRun = true;
            SetStockDisplayVisible(true);

            _stockLifecycle.VoxelResolutionMm = cfg.VerificationResolutionMm;
            UpdateResButtons();

            SetStockColorFromWpfColor(cfg.Color);

            if (!TryBuildStockBoundsTableLocal(cfg, out WorkpiecePlacement.StockBounds bounds))
            {
                MessageBox.Show(this, "Некорректные параметры заготовки.", "Заготовка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _stockLifecycle.AnchoredToTable = true;
            _stockLifecycle.BoundsAreTableLocal = true;
            _stockLifecycle.AutoAlignToMount = false;
            _stockLifecycle.Bounds = bounds;

            ApplyStock();
            SyncStockVisualToTable();
        }

        private bool IsStockShownInViewport() => FilterShowStock?.IsChecked == true;

        /// <summary>Показать/скрыть заготовку в 3D (не влияет на расчёт вокселей при Cycle Start).</summary>
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

        /// <summary>Освобождает воксельный runtime, worker и GPU-сессию заготовки.</summary>
        private void ReleaseVoxelStockRuntime()
        {
            Interlocked.Exchange(ref _stockMeshRefreshPending, 0);
            _stockLifecycle.ReleaseRuntime();
            if (_stockVisual != null)
            {
                _stockVisual.Content = null;
            }
        }

        /// <summary>Подставляет в viewport параметрический или воксельный меш в зависимости от наличия runtime.</summary>
        private void ApplyStockViewportVisibility()
        {
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

            _stockVisual.Content = GetStockViewportContent();
            if (_stockLifecycle.Stock != null)
            {
                _ = UpdateStockMeshAsync(meshRefreshNonBlockingGate: false);
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
            _lastPosition = GetToolTcpPosition(evt.X, evt.Y, evt.Z);
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
            if (_stockLifecycle.Stock is VoxelStock voxelStock && !_suppressCameraDrivenStockRefresh)
            {
                // Камера может вращаться при паузе/стопе цикла: если есть грязные чанки,
                // дотягиваем пересборку даже без движения инструмента, чтобы не было "пустот".
                if (voxelStock.IsDirty)
                {
                    DateTime nowCam = DateTime.UtcNow;
                    if ((nowCam - _lastCameraDrivenStockRefreshUtc).TotalMilliseconds >= UiLayoutConstants.CameraDrivenStockRefreshMs)
                    {
                        _lastCameraDrivenStockRefreshUtc = nowCam;
                        RequestStockMeshRefresh();
                    }
                }
            }

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
                if (fps < UiLayoutConstants.TargetFps)
                {
                    // Плавное снижение коэффициента (минимум 0.1)
                    _fpsSlowdownFactor = Math.Max(0.1, fps / UiLayoutConstants.TargetFps);
                }
                else
                {
                    _fpsSlowdownFactor = 1.0;
                }

                _frameCount = 0;
                _lastFpsUpdate = now;
                UpdateRuntimeLoadStatusText();
            }
        }

        private void UpdateResButtons()
        {
            string resName = _stockLifecycle.VoxelResolutionMm <= ProjectConstants.RES_HIGH + 1e-9 ? "Высокая" :
                _stockLifecycle.VoxelResolutionMm <= ProjectConstants.RES_MEDIUM + 1e-9 ? "Средняя" :
                _stockLifecycle.VoxelResolutionMm <= ProjectConstants.RES_COARSE + 1e-9 ? "Грубое" : "Пользовательское";
            string text = $"Воксели: {resName} ({_stockLifecycle.VoxelResolutionMm:F2} мм)";
            if (VoxelResolutionStatusText != null)
            {
                VoxelResolutionStatusText.Text = text;
            }

            if (ResolutionStatusText != null)
            {
                ResolutionStatusText.Text = text;
            }
        }

        private void MenuVoxelResolution_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item &&
                double.TryParse(item.Tag?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double res))
            {
                _stockLifecycle.VoxelResolutionMm = res;
                UpdateResButtons();
                if (IsLoaded)
                {
                    ApplyStock();
                }
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
                return;
            }

            EnsureToolGeometry(tool);
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
        }

        private void UpdateToolTransform(Point3D position)
        {
            if (UsesMountNodeToolVisual())
            {
                Point3D local = _machineVisualCoordinator.GetToolHolderTranslateInMountNode();
                _toolTransform.OffsetX = local.X;
                _toolTransform.OffsetY = local.Y;
                _toolTransform.OffsetZ = local.Z;
                return;
            }

            _toolTransform.OffsetX = position.X;
            _toolTransform.OffsetY = position.Y;
            _toolTransform.OffsetZ = position.Z;
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
                _fpsSlowdownFactor);
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

            MachineState playbackState = _currentParser.State;
            var activeOffset = playbackState.GetActiveWorkOffset();
            PositionText.Text = FormatMcsPosition(
                currentPos.X,
                currentPos.Y,
                currentPos.Z,
                playbackState.MachineZeroOffsetX,
                playbackState.MachineZeroOffsetY,
                playbackState.MachineZeroOffsetZ);
            // FANUC: машинные оси (шпиндель), без обратной коррекции G43/G44 на длину.
            FanucPanel.UpdateMachinePosition(
                currentPos.X,
                currentPos.Y,
                currentPos.Z,
                playbackState.MachineZeroOffsetX,
                playbackState.MachineZeroOffsetY,
                playbackState.MachineZeroOffsetZ,
                activeOffset.X,
                activeOffset.Y,
                activeOffset.Z);

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

            Point3D cutFrom = ToStockLocalPoint(_lastPosition);
            Point3D cutTo = ToStockLocalPoint(tcpPos);
            _stockRenderService.ProcessCutStep(
                _isDryRunEnabled,
                _stockLifecycle.Stock,
                _stockLifecycle.CutWorker,
                activeTool,
                cutFrom,
                cutTo,
                _stockLifecycle.GpuSession);

            // Визуализация должна следовать за движением инструмента: после реза сразу
            // пробуем обновить грязные чанки. Gate внутри UpdateStockMeshAsync не даст
            // накопить параллельные пересборки.
            if (_stockLifecycle.Stock?.IsDirty == true)
            {
                RequestStockMeshRefresh();
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

        private async Task UpdateStockMeshAsync(bool showProgress = false, bool meshRefreshNonBlockingGate = true)
        {
            IStockVolume? stock = _stockLifecycle.Stock;
            if (stock == null)
            {
                return;
            }

            if (stock is VoxelStock voxelStock && Viewport?.Camera is ProjectionCamera camera)
            {
                // Адаптивная дорисовка под текущий ракурс: приоритетно пересобираем видимую область.
                voxelStock.SetCameraFocus(camera.Position, camera.LookDirection);
            }

            if (meshRefreshNonBlockingGate)
            {
                // Пока идёт пересборка, не копим await на SemaphoreSlim — следующий кадр снова проверит ShouldRefreshStock.
                if (!await _stockMeshGate.WaitAsync(0))
                {
                    return;
                }
            }
            else
            {
                await _stockMeshGate.WaitAsync();
            }

            try
            {
                // Заготовку могли сбросить, пока ждали gate (смена УП, выкл. вокселей).
                if (!ReferenceEquals(stock, _stockLifecycle.Stock))
                {
                    return;
                }

                var profile = VoxelSimulationProfile.ForResolution(_stockLifecycle.VoxelResolutionMm);
                using (_voxelPerformanceMonitor.MeasureMeshRefresh(profile.Name, dirtyChunkCount: -1))
                {
                    await _stockRenderService.RefreshStockVisualAsync(
                        stock,
                        _stockVisual,
                        StockProgressPanel,
                        showProgress,
                        stockShownInViewport: IsStockShownInViewport());

                    if (!ReferenceEquals(stock, _stockLifecycle.Stock))
                    {
                        _stockVisual.Content = null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stock update error: {ex.Message}");
            }
            finally
            {
                _stockMeshGate.Release();
            }
        }

        private void RequestStockMeshRefresh()
        {
            Interlocked.Exchange(ref _stockMeshRefreshPending, 1);
            if (Interlocked.CompareExchange(ref _stockMeshRefreshWorkerRunning, 1, 0) != 0)
            {
                return;
            }

            _ = RunStockMeshRefreshWorkerAsync();
        }

        private async Task RunStockMeshRefreshWorkerAsync()
        {
            try
            {
                while (Interlocked.Exchange(ref _stockMeshRefreshPending, 0) == 1)
                {
                    // Внутренний воркер должен делать реальную работу, а не крутиться в busy-loop,
                    // если gate временно занят. Поэтому здесь всегда blocking-wait.
                    await UpdateStockMeshAsync(showProgress: false, meshRefreshNonBlockingGate: false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _stockMeshRefreshWorkerRunning, 0);
                if (Interlocked.CompareExchange(ref _stockMeshRefreshPending, 0, 0) == 1 &&
                    Interlocked.CompareExchange(ref _stockMeshRefreshWorkerRunning, 1, 0) == 0)
                {
                    _ = RunStockMeshRefreshWorkerAsync();
                }
            }
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
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }), null);
            Dispatcher.PushFrame(frame);
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

            _voxelLoadingWindow?.Close();
            _voxelLoadingWindow = null;
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
            _lastPosition = endState.CurrentPosition;
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

        private async Task RebuildFinalStockAfterProgramEndAsync()
        {
            if (_currentParser == null || _isDryRunEnabled)
            {
                _stockLifecycle.Stock?.FreezeModel();
                return;
            }

            if (!TryReadStockConfig(out StockConfig cfg))
            {
                _stockLifecycle.Stock?.FreezeModel();
                return;
            }

            try
            {
                if (StockProgressPanel != null)
                {
                    StockProgressPanel.Visibility = Visibility.Visible;
                }
                InitializeStockLoadTelemetry(cfg);
                int estimatedChunks = EstimateFinalStockChunkCount(cfg);
                SetStockProgress(0, $"0 / {estimatedChunks} чанков");
                await Dispatcher.Yield(DispatcherPriority.Render);

                _cycleCanStart = false;
                _cycleCanPause = false;

                _stockLifecycle.CutWorker?.Dispose();
                _stockLifecycle.CutWorker = null;
                _stockLifecycle.GpuSession?.Dispose();
                _stockLifecycle.GpuSession = null;

                var toolsSnapshot = _tools.ToDictionary(t => t.Number, t => new FinalStockTool(
                    Diameter: Math.Max(0.001, t.Diameter),
                    FluteLength: Math.Max(FinalStockSettings.ResolutionMm, t.FluteLength),
                    FluteColor: t.FluteColor));
                var commandsSnapshot = _currentParser.Commands.ToArray();

                var finalBuildProgress = new Progress<(int Done, int Total)>(p =>
                {
                    double percent = p.Total <= 0 ? 0 : p.Done * 100.0 / p.Total;
                    SetFinalStockDisplayProgress(estimatedChunks, 0.0, 0.5, percent / 100.0);
                });

                VoxelStock finalStock = await TryBuildFinalStockAsync(
                    cfg,
                    commandsSnapshot,
                    toolsSnapshot,
                    finalBuildProgress);
                await Dispatcher.Yield(DispatcherPriority.Render);

                await _stockMeshGate.WaitAsync();
                try
                {
                    _stockLifecycle.Stock = finalStock;
                    _stockLifecycle.Bounds = ToStockBounds(cfg);
                    ApplyStockViewportVisibility();

                    await UpdateFinalStockMeshWithProgressAsync(finalStock, estimatedChunks, phaseStart: 0.5);
                    finalStock.FreezeModel();
                }
                finally
                {
                    _stockMeshGate.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Final stock rebuild failed: {ex.Message}");
                _stockLifecycle.Stock?.FreezeModel();
            }
            finally
            {
                if (StockLoadText != null)
                {
                    StockLoadText.Text = string.Empty;
                }
                if (StockProgressPanel != null)
                {
                    StockProgressPanel.Visibility = Visibility.Collapsed;
                }

                _cycleCanStart = true;
                _cycleCanPause = false;
            }
        }

        private sealed record FinalStockTool(double Diameter, double FluteLength, Color FluteColor);

        private void SetStockProgress(double percent, string? text = null)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (StockProgressBar != null)
            {
                StockProgressBar.IsIndeterminate = true;
                StockProgressBar.Value = percent;
            }

            if (StockProgressText != null)
            {
                StockProgressText.Text = text ?? $"{percent:F0}%";
            }

            UpdateStockLoadTelemetryText();
        }

        private void InitializeStockLoadTelemetry(StockConfig cfg)
        {
            _stockLoadLastAtUtc = DateTime.UtcNow;
            _stockLoadLastCpu = _selfProcess.TotalProcessorTime;
            _stockLoadCpuPercent = 0;
            _stockLoadGpuMemoryPercent = 0;
            _stockLoadGpuMode = "CPU";

            GpuVerificationProbeResult probe = GpuVerificationEngine.Probe();
            if (!_gpuVerificationSettings.Enabled || !probe.IsAvailable)
            {
                _stockLoadGpuMode = "CPU fallback";
                return;
            }

            int dimX = Math.Max(1, (int)((cfg.MaxX - cfg.MinX) / FinalStockSettings.ResolutionMm));
            int dimY = Math.Max(1, (int)((cfg.MaxY - cfg.MinY) / FinalStockSettings.ResolutionMm));
            int dimZ = Math.Max(1, (int)((cfg.MaxZ - cfg.MinZ) / FinalStockSettings.ResolutionMm));
            _stockLoadGpuMemoryPercent = GpuVerificationEngine.EstimateOccupancyMemoryLoadPercent(dimX, dimY, dimZ);
            _stockLoadGpuMode = "GPU+CPU параллельно";
        }

        private void UpdateStockLoadTelemetryText()
        {
            if (StockLoadText == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            TimeSpan cpuNow = _selfProcess.TotalProcessorTime;
            double elapsedMs = (now - _stockLoadLastAtUtc).TotalMilliseconds;
            if (elapsedMs >= 200)
            {
                double cpuUsedMs = (cpuNow - _stockLoadLastCpu).TotalMilliseconds;
                double cpuPercent = cpuUsedMs / (elapsedMs * Math.Max(1, Environment.ProcessorCount)) * 100.0;
                _stockLoadCpuPercent = Math.Clamp(cpuPercent, 0.0, 100.0);
                _stockLoadLastAtUtc = now;
                _stockLoadLastCpu = cpuNow;
            }

            StockLoadText.Text = $"CPU: {_stockLoadCpuPercent:F0}% | GPU: {_stockLoadGpuMemoryPercent:F0}% ({_stockLoadGpuMode})";
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

            double gpuPercent = Math.Clamp(GpuVerificationEngine.LastEstimatedGpuLoadPercent, 0.0, 100.0);
            string voxelInfo = _stockLifecycle.Stock switch
            {
                VoxelStock vs => vs.TotalVoxelCount.ToString("N0", CultureInfo.CurrentCulture),
                _ => "-",
            };
            RuntimeLoadText.Text = $"CPU: {_runtimeCpuPercent:F0}% | GPU: {gpuPercent:F0}% | Voxels: {voxelInfo} | Виз: воксельная 3D";
        }

        private static int EstimateFinalStockChunkCount(StockConfig cfg)
        {
            static int AxisChunks(double length)
            {
                int voxels = Math.Max(1, (int)(length / FinalStockSettings.ResolutionMm));
                return (voxels + VoxelChunk.Size - 1) / VoxelChunk.Size;
            }

            return AxisChunks(cfg.MaxX - cfg.MinX) *
                   AxisChunks(cfg.MaxY - cfg.MinY) *
                   AxisChunks(cfg.MaxZ - cfg.MinZ);
        }

        private async Task UpdateFinalStockMeshWithProgressAsync(VoxelStock finalStock, int displayTotalChunks, double phaseStart)
        {
            // Для верифицированной итоговой заготовки важнее полнота кадра, чем поэтапная дорисовка:
            // пересобираем все грязные чанки за один проход, чтобы геометрия сразу была целиком
            // и не зависела от текущего положения камеры.
            SetFinalStockDisplayProgress(displayTotalChunks, phaseStart, 1.0, 0.0);
            StockProgressPanel?.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Render);

            await finalStock.UpdateAllVisualsAsync();
            SetStockProgress(100, $"{displayTotalChunks} / {displayTotalChunks} чанков");
        }

        private void SetFinalStockDisplayProgress(int totalChunks, double phaseStart, double phaseEnd, double phaseFraction)
        {
            totalChunks = Math.Max(1, totalChunks);
            phaseFraction = Math.Clamp(phaseFraction, 0.0, 1.0);
            double normalized = phaseStart + (phaseEnd - phaseStart) * phaseFraction;
            int doneChunks = Math.Clamp((int)Math.Round(totalChunks * normalized), 0, totalChunks);
            SetStockProgress(normalized * 100.0, $"{doneChunks} / {totalChunks} чанков");
        }

        /// <summary>Финальная сборка заготовки: при включённом GPU и допустимом размере сетки — compute, иначе CPU.</summary>
        private async Task<VoxelStock> TryBuildFinalStockAsync(
            StockConfig cfg,
            ParsedCommand[] commands,
            Dictionary<int, FinalStockTool> tools,
            Progress<(int Done, int Total)> finalBuildProgress)
        {
            return await Task.Run(() =>
            {
                List<VoxelStock.CylinderCut> cuts = CollectFinalStockCuts(cfg, commands, tools, finalBuildProgress);
                cuts = OptimizeFinalCutsLinearRuns(cuts);

                if (_gpuVerificationSettings.Enabled &&
                    TryBuildFinalStockOnGpu(cfg, cuts, finalBuildProgress, commands.Length, out VoxelStock? gpuStock))
                {
                    return gpuStock ?? BuildFinalStockCpuFromCuts(cfg, commands.Length, cuts, finalBuildProgress);
                }

                return BuildFinalStockCpuFromCuts(cfg, commands.Length, cuts, finalBuildProgress);
            }).ConfigureAwait(false);
        }

        private bool TryBuildFinalStockOnGpu(
            StockConfig cfg,
            List<VoxelStock.CylinderCut> cuts,
            IProgress<(int Done, int Total)> progress,
            int commandsCount,
            out VoxelStock? stock)
        {
            stock = null;
            if (cuts.Count == 0)
            {
                return false;
            }

            GpuVerificationProbeResult probe = GpuVerificationEngine.Probe();
            if (!probe.IsAvailable)
            {
                return false;
            }

            try
            {
                var gb = new GpuStockBounds(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
                var gpuCuts = new GpuCylinderCut[cuts.Count];
                if (cuts.Count >= 2048)
                {
                    var convertParallel = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
                    };
                    Parallel.For(0, cuts.Count, convertParallel, i =>
                    {
                        var c = cuts[i];
                        gpuCuts[i] = new GpuCylinderCut(
                            c.Start.X,
                            c.Start.Y,
                            c.Start.Z,
                            c.End.X,
                            c.End.Y,
                            c.End.Z,
                            c.Radius,
                            c.FluteLength);
                    });
                }
                else
                {
                    for (int i = 0; i < cuts.Count; i++)
                    {
                        var c = cuts[i];
                        gpuCuts[i] = new GpuCylinderCut(
                            c.Start.X,
                            c.Start.Y,
                            c.Start.Z,
                            c.End.X,
                            c.End.Y,
                            c.End.Z,
                            c.Radius,
                            c.FluteLength);
                    }
                }

                var cutProgress = new Progress<(int Done, int Total)>(p =>
                {
                    int overallDone = commandsCount + p.Done;
                    int overallTotal = commandsCount + Math.Max(1, p.Total);
                    progress.Report((overallDone, overallTotal));
                });

                if (!GpuVerificationEngine.TryComputeOccupancy(
                        gb,
                        FinalStockSettings.ResolutionMm,
                        0,
                        gpuCuts,
                        cutProgress,
                        out uint[]? occ,
                        out _,
                        out _,
                        out _,
                        out string? _))
                {
                    return false;
                }

                if (occ == null)
                {
                    return false;
                }

                stock = VoxelStock.FromGpuOccupancyMask(
                    cfg.MaxX - cfg.MinX,
                    cfg.MaxY - cfg.MinY,
                    cfg.MaxZ - cfg.MinZ,
                    FinalStockSettings.ResolutionMm,
                    new Point3D((cfg.MinX + cfg.MaxX) / 2, (cfg.MinY + cfg.MaxY) / 2, 0),
                    cfg.MaxZ,
                    occ);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPU final stock failed, CPU fallback: {ex.Message}");
                stock = null;
                return false;
            }
        }

        private static VoxelStock BuildFinalStockCpuFromCuts(
            StockConfig cfg,
            int commandsCount,
            List<VoxelStock.CylinderCut> cuts,
            IProgress<(int Done, int Total)>? progress)
        {
            var stock = new VoxelStock(
                cfg.MaxX - cfg.MinX,
                cfg.MaxY - cfg.MinY,
                cfg.MaxZ - cfg.MinZ,
                FinalStockSettings.ResolutionMm,
                new Point3D((cfg.MinX + cfg.MaxX) / 2, (cfg.MinY + cfg.MaxY) / 2, 0),
                cfg.MaxZ);

            stock.CutCylinders(cuts, (done, total) =>
            {
                int overallDone = commandsCount + done;
                int overallTotal = commandsCount + Math.Max(1, total);
                progress?.Report((overallDone, overallTotal));
            });

            return stock;
        }

        private static List<VoxelStock.CylinderCut> CollectFinalStockCuts(
            StockConfig cfg,
            IReadOnlyList<ParsedCommand> commands,
            IReadOnlyDictionary<int, FinalStockTool> tools,
            IProgress<(int Done, int Total)>? progress = null)
        {
            var replay = new GCodeParser();
            var cuts = new List<VoxelStock.CylinderCut>(commands.Count);
            int reportEvery = Math.Max(1, commands.Count / 100);

            for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
            {
                ParsedCommand cmd = commands[commandIndex];
                double x0 = replay.State.X;
                double y0 = replay.State.Y;
                double z0 = replay.State.Z;

                CommandReplayer.ReplayCommand(replay, cmd);

                double x1 = replay.State.X;
                double y1 = replay.State.Y;
                double z1 = replay.State.Z;

                if (!tools.TryGetValue(replay.State.ToolNumber ?? 0, out FinalStockTool? tool))
                {
                    continue;
                }

                if (cmd.GCodes.Any(g => g.Number == 28) || cmd.MCodes.Any(m => m.Number == 6))
                {
                    continue;
                }

                if (!IsCuttingMotion(replay.State.CurrentMotionMode.Number))
                {
                    continue;
                }

                if (cmd.Arc != null)
                {
                    AddArcFinalStockCuts(cuts, cmd.Arc, tool, cfg);
                }
                else if (HasPositionDelta(x0, y0, z0, x1, y1, z1))
                {
                    AddSegmentFinalStockCut(cuts, new Point3D(x0, y0, z0), new Point3D(x1, y1, z1), tool, cfg);
                }

                if ((commandIndex + 1) % reportEvery == 0 || commandIndex + 1 == commands.Count)
                {
                    progress?.Report((commandIndex + 1, commands.Count * 2));
                }
            }

            return cuts;
        }

        private static bool IsCuttingMotion(int motionNumber) => motionNumber is 1 or 2 or 3;

        private static bool HasPositionDelta(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return Math.Abs(x1 - x0) > ProjectConstants.EPSILON ||
                   Math.Abs(y1 - y0) > ProjectConstants.EPSILON ||
                   Math.Abs(z1 - z0) > ProjectConstants.EPSILON;
        }

        private static void AddArcFinalStockCuts(List<VoxelStock.CylinderCut> cuts, ArcGeometry arc, FinalStockTool tool, StockConfig cfg)
        {
            int samples = GetFinalArcSampleCount(arc);
            Point3D previous = PointOnArc(arc, 0);
            for (int i = 1; i <= samples; i++)
            {
                Point3D next = PointOnArc(arc, i / (double)samples);
                AddSegmentFinalStockCut(cuts, previous, next, tool, cfg);
                previous = next;
            }
        }

        private static int GetFinalArcSampleCount(ArcGeometry arc)
        {
            double chordTolerance = FinalStockSettings.ResolutionMm * 0.5;
            double angleByChordTolerance = arc.Radius <= ProjectConstants.EPSILON
                ? Math.Abs(arc.SweepAngleRad)
                : 2.0 * Math.Acos(Math.Clamp(1.0 - chordTolerance / arc.Radius, -1.0, 1.0));
            double maxAngleStep = Math.Clamp(angleByChordTolerance, Math.PI / 720.0, Math.PI / 36.0);
            return Math.Clamp((int)Math.Ceiling(Math.Abs(arc.SweepAngleRad) / maxAngleStep), 1, 20000);
        }

        private static Point3D PointOnArc(ArcGeometry arc, double t)
        {
            double ang = arc.StartAngleRad + t * arc.SweepAngleRad;
            double u = arc.CenterU + arc.Radius * Math.Cos(ang);
            double v = arc.CenterV + arc.Radius * Math.Sin(ang);
            double x0 = arc.StartX + t * (arc.EndX - arc.StartX);
            double y0 = arc.StartY + t * (arc.EndY - arc.StartY);
            double z0 = arc.StartZ + t * (arc.EndZ - arc.StartZ);

            return arc.Plane switch
            {
                18 => new Point3D(u, y0, v),
                19 => new Point3D(x0, u, v),
                _ => new Point3D(u, v, z0)
            };
        }

        private static void AddSegmentFinalStockCut(List<VoxelStock.CylinderCut> cuts, Point3D start, Point3D end, FinalStockTool tool, StockConfig cfg)
        {
            Vector3D move = end - start;
            if (move.Length <= ProjectConstants.EPSILON)
            {
                return;
            }

            double radius = tool.Diameter * 0.5;
            if (!SegmentCanTouchStock(start, end, radius, tool.FluteLength, cfg))
            {
                return;
            }

            if (TryMergeWithPreviousFinalCut(cuts, start, end, radius, tool))
            {
                return;
            }

            cuts.Add(new VoxelStock.CylinderCut(start, end, radius, tool.FluteLength, tool.FluteColor));
        }

        private static bool TryMergeWithPreviousFinalCut(
            List<VoxelStock.CylinderCut> cuts,
            Point3D start,
            Point3D end,
            double radius,
            FinalStockTool tool)
        {
            if (cuts.Count == 0)
            {
                return false;
            }

            var last = cuts[^1];
            if (Math.Abs(last.Radius - radius) > ProjectConstants.EPSILON ||
                Math.Abs(last.FluteLength - tool.FluteLength) > ProjectConstants.EPSILON ||
                !last.ToolColor.Equals(tool.FluteColor) ||
                (last.End - start).Length > FinalStockSettings.ResolutionMm)
            {
                return false;
            }

            Vector3D a = last.End - last.Start;
            Vector3D b = end - start;
            if (a.Length <= ProjectConstants.EPSILON || b.Length <= ProjectConstants.EPSILON)
            {
                return false;
            }

            a.Normalize();
            b.Normalize();
            if (Vector3D.DotProduct(a, b) < 0.9995 ||
                Vector3D.CrossProduct(a, b).Length > 0.001)
            {
                return false;
            }

            cuts[^1] = last with { End = end };
            return true;
        }

        private static bool SegmentCanTouchStock(Point3D start, Point3D end, double radius, double fluteLength, StockConfig cfg)
        {
            double minX = Math.Min(start.X, end.X) - radius;
            double maxX = Math.Max(start.X, end.X) + radius;
            double minY = Math.Min(start.Y, end.Y) - radius;
            double maxY = Math.Max(start.Y, end.Y) + radius;
            double minZ = Math.Min(start.Z, end.Z);
            double maxZ = Math.Max(start.Z, end.Z) + fluteLength;

            return maxX >= cfg.MinX && minX <= cfg.MaxX &&
                   maxY >= cfg.MinY && minY <= cfg.MaxY &&
                   maxZ >= cfg.MinZ && minZ <= cfg.MaxZ;
        }

        private void StopAnimationCycle()
        {
            _animationTimer.Stop();
            if (_runTimeStopwatch.IsRunning) _runTimeStopwatch.Stop();
            if (_cycleTimeStopwatch.IsRunning) _cycleTimeStopwatch.Stop();
            _accumulatedRunTime = _runTimeStopwatch.Elapsed;
            _accumulatedCycleTime = _cycleTimeStopwatch.Elapsed;
            _programPlaybackHost.ResetToHome();
            _lastPosition = _programPlaybackHost.CurrentPosition;
            _interpolationProgress = _programPlaybackHost.InterpolationProgress;
            _mainPresenter.Reset(_lastPosition);
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

                if (MenuGpuVerificationItem != null)
                {
                    MenuGpuVerificationItem.IsChecked = _gpuVerificationSettings.Enabled;
                }

                GpuVerificationProbeAndUpdateStatus();
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
            // After LoadAndRender's ZoomExtents and machine rebuild — lock startup camera to the default 3/4 view.
            _ = Dispatcher.BeginInvoke(
                () => ViewportCameraHelper.SetDefaultMainSceneView(Viewport),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private async Task ApplyActiveMachineProfileAsync()
        {
            var profile = _machineProfileService.ActiveProfile;
            _machineCore.UpdateKinematics(profile.ToKinematicsModel());
            ApplySceneOriginFromMcs(profile.McsZeroOffset);

            var physicalHome = profile.GetPhysicalHomePosition();
            // Do not publish MachineZeroSetTo here: it compensates axis pose and leaves the machine
            // between physical 0 and profile HOME (Fanuc MACHINE shows ~2× McsZeroOffset).
            _machineCore.ApplyProfileHome(profile);
            if (_currentParser != null)
            {
                profile.ApplyHomeToMachineState(_currentParser.State);
            }

            // G28 / M6: физическая HOME из профиля (ось HOME в MCS + McsZeroOffset).
            _programExecutionService.SetHomeTarget(physicalHome.X, physicalHome.Y, physicalHome.Z);

            double targetX = physicalHome.X;
            double targetY = physicalHome.Y;
            double targetZ = physicalHome.Z;
            _machineCore.HomeTo(targetX, targetY, targetZ);
            _programPlaybackHost.SetCurrentPosition(new Point3D(targetX, targetY, targetZ));
            _lastPosition = GetToolTcpPosition(targetX, targetY, targetZ);

            await _machineVisualCoordinator.RebuildAsync();
            _machineVisualCoordinator.UpdatePose(targetX, targetY, targetZ);
            ApplyStockPlacementFromProfile(profile, targetX, targetY, targetZ);
            bool showTool = _filterShowTool && _selectedTool is ToolViewModel;
            SyncToolVisualMount(showTool);
            if (_selectedTool is ToolViewModel tool)
            {
                UpdateToolGeometry(tool, GetToolHolderPosition(targetX, targetY, targetZ));
            }

            SyncActiveWcsOriginMarker();

            // If MCS changed (profile applied/saved), toolpath must be rebuilt with the new seed.
            // Otherwise the existing contour (built with old MCS seed) will be shifted by _sceneWorldShift and appear in a wrong corner.
            if (_currentParser != null && _loadedNcProgramPath != null)
            {
                RebuildProgramAndToolpathForCurrentWorkOffsets();
            }
        }

        private void ApplySceneOriginFromMcs(MachineGeometryPoint mcsZeroOffset)
        {
            // Kinematic attach/limits are in MCS; assembly root maps MCS → absolute scene.
            _sceneWorldShift = new TranslateTransform3D(mcsZeroOffset.X, mcsZeroOffset.Y, mcsZeroOffset.Z);
            _machineVisualCoordinator.SetWorldTransform(_sceneWorldShift);

            SyncStockVisualToTable();

            _toolVisual.Transform = _toolTransform;
            SyncToolVisualMount(_filterShowTool && IsToolVisualAttached());

            // Table kinematic transform is in MCS; assembly root applies +McsZeroOffset.
            if (_stockLifecycle.AnchoredToTable)
            {
                SyncStockVisualToTable();
            }
        }

        private int GetActiveWorkOffsetSystemNumber()
        {
            if (_currentParser != null)
            {
                return _currentParser.State.CurrentCoordinateSystem.Number;
            }

            return TryParseCoordinateSystemNumber(_currentCoordSystem, out int parsed)
                ? parsed
                : MachineState.MinWorkOffsetNumber;
        }

        private static bool TryParseCoordinateSystemNumber(string text, out int systemNumber)
        {
            systemNumber = MachineState.MinWorkOffsetNumber;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (text.StartsWith('(') && text.EndsWith(')'))
            {
                text = text[1..^1].Trim();
            }

            if (text.Length < 2 || (text[0] != 'G' && text[0] != 'g'))
            {
                return false;
            }

            if (!int.TryParse(text.AsSpan(1), out systemNumber))
            {
                return false;
            }

            return systemNumber is >= MachineState.MinWorkOffsetNumber and <= MachineState.MaxWorkOffsetNumber;
        }

        private void SyncActiveWcsOriginMarker(bool updateTableLocalPosition = true)
        {
            (double X, double Y, double Z) pose = _machineVisualCoordinator.GetPreviewPhysicalPose();
            SyncActiveWcsOriginMarker(
                GetActiveWorkOffsetSystemNumber(),
                pose.X,
                pose.Y,
                pose.Z,
                updateTableLocalPosition);
        }

        private void SyncActiveWcsOriginMarker(
            int activeSystemNumber,
            double machineX,
            double machineY,
            double machineZ,
            bool updateTableLocalPosition = true)
        {
            const double sphereRadiusMm = 2.8;
            const double axisLenMm = 18.0;
            MachineDefinition profile = _machineProfileService.ActiveProfile;
            bool onTable = MachineKinematics.UsesTableMountedWorkpiece(profile);
            ModelVisual3D? tableMountedMarker = null;
            Point3D? activeWcsTableLocal = null;

            for (int sys = MachineState.MinWorkOffsetNumber; sys <= MachineState.MaxWorkOffsetNumber; sys++)
            {
                var marker = WcsMarkerVisualBuilder.Build($"G{sys}", sphereRadiusMm, axisLenMm);
                _wcsMarkers[sys] = marker;

                _machineVisualCoordinator.DetachFromMachineHierarchy(marker);
                if (Viewport.Children.Contains(marker))
                {
                    Viewport.Children.Remove(marker);
                }

                if (sys != activeSystemNumber)
                {
                    continue;
                }

                if (!_workOffsets.TryGetValue(sys, out (double X, double Y, double Z) v))
                {
                    v = (0, 0, 0);
                }

                if (onTable)
                {
                    tableMountedMarker = marker;
                    if (updateTableLocalPosition)
                    {
                        activeWcsTableLocal = WorkpieceMountPlacement.ResolveFixedWcsOriginTableLocal(
                            profile,
                            sys,
                            v.X,
                            v.Y,
                            v.Z,
                            machineX,
                            machineY,
                            machineZ,
                            captureIfMissing: true);
                    }
                    else if (marker.Transform is TranslateTransform3D existing)
                    {
                        activeWcsTableLocal = new Point3D(existing.OffsetX, existing.OffsetY, existing.OffsetZ);
                    }
                    else if (WorkpieceMountPlacement.TryGetRegisteredWcsOriginTableLocal(sys, out Point3D registered))
                    {
                        activeWcsTableLocal = registered;
                    }
                }
                else if (updateTableLocalPosition)
                {
                    Point3D wcsInAssemblyMcs = WorkpieceMountPlacement.GetWcsOriginAssemblyMcs(
                        profile,
                        GetMachineStateForWcsVisual(),
                        machineX,
                        machineY,
                        machineZ);
                    _machineVisualCoordinator.AssemblyRoot.Children.Add(marker);
                    marker.Transform = new TranslateTransform3D(
                        wcsInAssemblyMcs.X,
                        wcsInAssemblyMcs.Y,
                        wcsInAssemblyMcs.Z);
                }
            }

            _machineVisualCoordinator.SetTableMountedWcsMarker(
                onTable ? tableMountedMarker : null,
                activeWcsTableLocal);
        }

        private MachineState GetMachineStateForWcsVisual()
        {
            MachineState state = _currentParser != null
                ? _currentParser.State.Clone()
                : CreateProgramSeedState();
            foreach (KeyValuePair<int, (double X, double Y, double Z)> pair in _workOffsets)
            {
                if (pair.Key is >= MachineState.MinWorkOffsetNumber and <= MachineState.MaxWorkOffsetNumber)
                {
                    state.SetWorkOffset(pair.Key, pair.Value.X, pair.Value.Y, pair.Value.Z);
                }
            }

            return state;
        }

        private double GetToolStickOutMm()
        {
            if (_selectedTool is ToolViewModel selected)
            {
                return selected.OverallLength;
            }

            if (_cachedToolGeometryTool != null)
            {
                return _cachedToolGeometryTool.OverallLength;
            }

            return _machineProfileService.ActiveProfile.DefaultToolStickOutMm;
        }

        private Point3D GetToolTcpPosition(double machineX, double machineY, double machineZ) =>
            _machineVisualCoordinator.GetToolCenterPoint(machineX, machineY, machineZ, GetToolStickOutMm());

        private Point3D GetToolHolderPosition(double machineX, double machineY, double machineZ) =>
            _machineVisualCoordinator.GetToolHolderPoint(machineX, machineY, machineZ);

        private Point3D GetToolHolderPosition(Point3D machineAxisPosition) =>
            GetToolHolderPosition(machineAxisPosition.X, machineAxisPosition.Y, machineAxisPosition.Z);

        private void ApplyStockPlacementFromProfile(MachineDefinition profile, double machineX, double machineY, double machineZ)
        {
            if (!TryReadStockConfig(out StockConfig cfg))
            {
                return;
            }

            RealignStockCenteredOnMount(profile, cfg.MaxX - cfg.MinX, cfg.MaxY - cfg.MinY, cfg.MaxZ - cfg.MinZ);
        }

        private void RealignStockCenteredOnMount(MachineDefinition profile, double width, double depth, double height)
        {
            width = Math.Max(0.001, width);
            depth = Math.Max(0.001, depth);
            height = Math.Max(0.001, height);

            Rect3D? tableBounds = _machineVisualCoordinator.TryGetTableMeshBoundsLocal(out Rect3D bounds) && !bounds.IsEmpty
                ? bounds
                : null;
            WorkpiecePlacement.StockBounds aligned = WorkpieceMountPlacement.AlignStockTableLocal(
                profile,
                tableBounds,
                width,
                depth,
                height);

            _stockLifecycle.AnchoredToTable = true;
            _stockLifecycle.BoundsAreTableLocal = true;
            _stockLifecycle.AutoAlignToMount = true;
            _stockLifecycle.Bounds = aligned;
            SyncStockVisualToTable();
        }

        private bool TryGetStockTableLocalBounds(out WorkpiecePlacement.StockBounds bounds)
        {
            bounds = default;
            if (!_stockLifecycle.AnchoredToTable || !_stockLifecycle.BoundsAreTableLocal || !TryReadStockConfig(out StockConfig cfg))
            {
                return false;
            }

            bounds = new WorkpiecePlacement.StockBounds(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
            return true;
        }

        /// <summary>Только кинематика стола: контур/WCS/заготовка едут с table.Root без пересчёта по TCP.</summary>
        private void ApplyTableKinematicPose(double machineX, double machineY, double machineZ)
        {
            var profile = _machineProfileService.ActiveProfile;
            if (!MachineKinematics.UsesTableMountedWorkpiece(profile))
            {
                return;
            }

            _machineVisualCoordinator.EnsureTableMountedWcsMarkerAttached();
            if (_stockLifecycle.AnchoredToTable)
            {
                _stockTableToWorld = TableSceneTransforms.BuildTableToSceneMatrix(profile, machineX, machineY, machineZ);
            }
        }

        private void EnsureWcsTableLocalCacheForToolpath(MachineState seedState)
        {
            var profile = _machineProfileService.ActiveProfile;
            if (!MachineKinematics.UsesTableMountedWorkpiece(profile))
            {
                return;
            }

            WorkpieceMountPlacement.SyncWcsOriginTableLocalFromMcs(
                profile,
                _workOffsets,
                seedState.X,
                seedState.Y,
                seedState.Z);
        }

        /// <summary>Привязывает заготовку и контур УП к узлу стола (table-local) для кинематики стола.</summary>
        private void SyncStockVisualToTable(bool syncWcsMarker = true)
        {
            var profile = _machineProfileService.ActiveProfile;
            bool tableMachine = MachineKinematics.UsesTableMountedWorkpiece(profile);
            bool stockOnTable = _stockLifecycle.AnchoredToTable && tableMachine;

            if (!tableMachine)
            {
                _machineVisualCoordinator.SetTableMountedVisuals(null, null);
                _machineVisualCoordinator.SetTableMountedWcsMarker(null);
                EnsureVisualOnViewport(_stockVisual);
                EnsureVisualOnViewport(_toolpathRoot);
                _stockVisual.Transform = Transform3D.Identity;
                _toolpathRoot.Transform = Transform3D.Identity;
                if (syncWcsMarker)
                {
                    SyncActiveWcsOriginMarker();
                }

                return;
            }

            // Контур — всегда дочерний элемент table.Root (table-local); движется только кинематикой стола.
            RemoveVisualFromViewport(_toolpathRoot);
            _toolpathRoot.Transform = Transform3D.Identity;

            if (stockOnTable)
            {
                RemoveVisualFromViewport(_stockVisual);
                _stockVisual.Transform = Transform3D.Identity;
            }
            else
            {
                EnsureVisualOnViewport(_stockVisual);
            }

            _machineVisualCoordinator.SetTableMountedVisuals(
                stockOnTable ? _stockVisual : null,
                _toolpathRoot);
            if (syncWcsMarker)
            {
                SyncActiveWcsOriginMarker();
            }
            else
            {
                _machineVisualCoordinator.EnsureTableMountedWcsMarkerAttached();
            }

            SyncToolVisualMount(_filterShowTool && IsToolVisualAttached());
            (double mx, double my, double mz) = _machineVisualCoordinator.GetPreviewPhysicalPose();
            ApplyTableKinematicPose(mx, my, mz);
        }

        private void EnsureVisualOnViewport(ModelVisual3D visual)
        {
            if (!Viewport.Children.Contains(visual))
            {
                Viewport.Children.Add(visual);
            }
        }

        private void RemoveVisualFromViewport(ModelVisual3D visual)
        {
            if (Viewport.Children.Contains(visual))
            {
                Viewport.Children.Remove(visual);
            }
        }

        private Point3D ToStockLocalPoint(Point3D world)
        {
            if (!_stockLifecycle.AnchoredToTable ||
                !MachineKinematics.UsesTableMountedWorkpiece(_machineProfileService.ActiveProfile))
            {
                return world;
            }

            Point3D pose = GetCurrentPosition();
            return TableSceneTransforms.SceneToTableLocal(
                _machineProfileService.ActiveProfile,
                pose.X,
                pose.Y,
                pose.Z,
                world);
        }

        private void MenuMachineSetup_Click(object sender, RoutedEventArgs e)
        {
            if (_machineSetupWindow != null)
            {
                if (_machineSetupWindow.IsLoaded)
                {
                    _machineSetupWindow.Activate();
                    return;
                }

                _machineSetupWindow = null;
            }

            var wnd = new MachineSetupWindow(_machineProfileService, _stlMeshLoader)
            {
                Owner = this
            };
            wnd.Closed += (_, _) => _machineSetupWindow = null;
            _machineSetupWindow = wnd;
            if (wnd.ShowDialog() == true || wnd.WasSaved)
            {
                _ = ApplyActiveMachineProfileAsync();
            }
        }

        private void MenuMachineProfile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new MachineProfilePickerWindow(_machineProfileService, () => MenuMachineSetup_Click(sender, e))
            {
                Owner = this
            };
            if (picker.ShowDialog() == true)
            {
                // Profile switch should move axes to profile Home.
                _ = ApplyActiveMachineProfileAsync();
            }
        }

        private void MenuMachineSaveFactoryDefault_Click(object sender, RoutedEventArgs e)
        {
            string name = _machineProfileService.ActiveProfile.DisplayName;
            if (MessageBox.Show(
                    this,
                    $"Сохранить текущий профиль «{name}» как заводские настройки?\n\n"
                    + "Пункт «Сбросить заводской профиль» будет восстанавливать именно этот снимок.",
                    "Станок",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _machineProfileService.SaveActiveProfileAsFactoryDefault();
                MessageBox.Show(
                    this,
                    "Заводские настройки сохранены.",
                    "Станок",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Станок", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MenuMachineFactoryReset_Click(object sender, RoutedEventArgs e)
        {
            string prompt = _machineProfileService.HasUserFactorySnapshot()
                ? "Восстановить заводской профиль станка из сохранённого снимка?"
                : "Восстановить заводской профиль станка из поставки (STEP)?";
            if (MessageBox.Show(this, prompt, "Станок", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _machineProfileService.ResetToFactoryDefault(_stlMeshLoader);
            _ = ApplyActiveMachineProfileAsync();
        }

        // MCS zero is managed per profile in Machine Setup.

        private void MenuGpuVerificationItem_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi)
            {
                return;
            }

            _gpuVerificationSettings.Enabled = mi.IsChecked == true;
            if (!_gpuVerificationSettings.Enabled)
            {
                _stockLifecycle.GpuSession?.Dispose();
                _stockLifecycle.GpuSession = null;
            }

            GpuVerificationSettingsStore.Save(_gpuVerificationSettings);
            GpuVerificationProbeAndUpdateStatus();
        }

        /// <summary>Обновляет подсказку меню после пробы DXGI/D3D11.</summary>
        private void GpuVerificationProbeAndUpdateStatus()
        {
            GpuVerificationProbeResult probe = GpuVerificationEngine.Probe();
            int effectiveAxisCap = GpuVerificationEngine.ComputeAxisCapByVram(0);
            static string FormatGiB(ulong bytes) => bytes > 0 ? $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GiB" : "н/д";
            ulong totalGpuMemoryBytes = probe.DedicatedVideoMemoryBytes + probe.SharedSystemMemoryBytes;
            if (MenuGpuVerificationItem != null)
            {
                MenuGpuVerificationItem.ToolTip =
                    $"Вся УП верифицируется на GPU сеткой симуляции; финальный блок — отдельно при лимите сетки. " +
                    $"Лимит по оси: {effectiveAxisCap} (авто, бюджет до 90% GPU memory)." +
                    $" Dedicated: {FormatGiB(probe.DedicatedVideoMemoryBytes)}, Shared: {FormatGiB(probe.SharedSystemMemoryBytes)}, Total: {FormatGiB(totalGpuMemoryBytes)}" +
                    $" ({GpuVerificationSettingsStore.GetDefaultFilePath()}).\n" +
                    (probe.IsAvailable
                        ? "GPU compute: доступен."
                        : $"GPU compute: недоступен для сессии — рантайм только CPU; причина: {probe.Message}");
            }

            System.Diagnostics.Debug.WriteLine(
                probe.IsAvailable ? "GPU верификация: готово" : $"GPU верификация: {probe.Message}");
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

        /// <summary>До Cycle Start — параметрический <see cref="_stockModel"/>; после — воксельный <c>MainModel</c>.</summary>
        private Model3D? GetStockViewportContent()
        {
            return _stockLifecycle.Stock != null ? _stockLifecycle.Stock.MainModel : _stockModel;
        }

        private Color GetSelectedStockColor() =>
            _stockLifecycle.ResolveStockColor(
                _stockLifecycle.ConstructorConfig?.Color,
                Colors.LightGray);

        private static MeshGeometry3D BuildStockBoxGeometry(StockConfig cfg)
        {
            double sx = Math.Max(0.001, cfg.MaxX - cfg.MinX);
            double sy = Math.Max(0.001, cfg.MaxY - cfg.MinY);
            double sz = Math.Max(0.001, cfg.MaxZ - cfg.MinZ);
            var center = new Point3D((cfg.MinX + cfg.MaxX) * 0.5, (cfg.MinY + cfg.MaxY) * 0.5, (cfg.MinZ + cfg.MaxZ) * 0.5);
            var builder = new MeshBuilder(false, false);
            builder.AddBox(center, sx, sy, sz);
            var mesh = builder.ToMesh();
            mesh.Freeze();
            return mesh;
        }

        private static StockConfig ToStockConfig(WorkpiecePlacement.StockBounds bounds) =>
            new(bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY, bounds.MinZ, bounds.MaxZ);

        private static WorkpiecePlacement.StockBounds ToStockBounds(StockConfig cfg) =>
            new(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);

        private bool TryReadStockConfig(out StockConfig cfg)
        {
            cfg = default;
            if (_stockLifecycle.Bounds is not WorkpiecePlacement.StockBounds bounds)
            {
                return false;
            }

            cfg = ToStockConfig(bounds);
            return cfg.MaxX > cfg.MinX && cfg.MaxY > cfg.MinY && cfg.MaxZ > cfg.MinZ;
        }

        /// <summary>
        /// Оптимизация финальной воксельной симуляции:
        /// объединяет подряд идущие коллинеарные линейные проходы одинаковым инструментом
        /// в один удлинённый "макро-проход" без изменения габаритов съёма.
        /// </summary>
        private static List<VoxelStock.CylinderCut> OptimizeFinalCutsLinearRuns(List<VoxelStock.CylinderCut> source)
        {
            if (source.Count < 2)
            {
                return source;
            }

            var optimized = new List<VoxelStock.CylinderCut>(source.Count);
            VoxelStock.CylinderCut current = source[0];

            for (int i = 1; i < source.Count; i++)
            {
                VoxelStock.CylinderCut next = source[i];
                if (CanMergeLinearRuns(current, next))
                {
                    current = current with { End = next.End };
                    continue;
                }

                optimized.Add(current);
                current = next;
            }

            optimized.Add(current);
            return optimized;
        }

        private static bool CanMergeLinearRuns(VoxelStock.CylinderCut a, VoxelStock.CylinderCut b)
        {
            if (Math.Abs(a.Radius - b.Radius) > ProjectConstants.EPSILON ||
                Math.Abs(a.FluteLength - b.FluteLength) > ProjectConstants.EPSILON ||
                !a.ToolColor.Equals(b.ToolColor))
            {
                return false;
            }

            // Проходы должны стыковаться в пределах шага финальной сетки.
            if ((a.End - b.Start).Length > FinalStockSettings.ResolutionMm)
            {
                return false;
            }

            Vector3D da = a.End - a.Start;
            Vector3D db = b.End - b.Start;
            if (da.Length <= ProjectConstants.EPSILON || db.Length <= ProjectConstants.EPSILON)
            {
                return false;
            }

            da.Normalize();
            db.Normalize();
            if (Vector3D.DotProduct(da, db) < 0.9997 ||
                Vector3D.CrossProduct(da, db).Length > 0.001)
            {
                return false;
            }

            // Проверяем, что конечная точка второго прохода остаётся на той же прямой.
            Vector3D fromAStartToBEnd = b.End - a.Start;
            Vector3D offLine = Vector3D.CrossProduct(fromAStartToBEnd, da);
            return offLine.Length <= FinalStockSettings.ResolutionMm;
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

            if (!_stockLifecycle.TryEnsureVoxelRuntime(
                    reuseRunningSimulation,
                    _gpuVerificationSettings.Enabled,
                    GetSelectedStockColor(),
                    out string? gpuError))
            {
                if (warnOnInvalidStockConfig)
                {
                    MessageBox.Show(this, "Некорректные параметры заготовки.", "Заготовка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return false;
            }

            if (gpuError != null)
            {
                System.Diagnostics.Debug.WriteLine($"GPU-сессия УП недоступна: {gpuError}");
            }

            ApplyStockViewportVisibility();
            return true;
        }

        /// <summary>Cycle Start: модальное окно, построение вокселей, замена параметрического меша, синхронная прогревка меша.</summary>
        private bool EnsureVoxelStockForRun()
        {
            bool showModal = _stockLifecycle.DeferVoxelUntilRun || _stockLifecycle.Stock == null;
            if (showModal)
            {
                ShowVoxelLoadingWindow("Расчет воксельной модели...");
            }

            try
            {
                if (!TryEnsureVoxelStock(reuseRunningSimulation: true, warnOnInvalidStockConfig: true))
                {
                    return false;
                }

                UpdateVoxelLoadingMessage("Расчёт и загрузка вокселей заготовки...");
                // Цельная модель заменяется воксельной: GetStockViewportContent() отдаёт _stockLifecycle.Stock.MainModel.
                ApplyStockViewportVisibility();

                if (_stockLifecycle.Stock is VoxelStock voxelStock)
                {
                    WarmUpStockVisualFullySync(voxelStock);
                }

                return true;
            }
            finally
            {
                if (showModal)
                {
                    HideVoxelLoadingWindow();
                }
            }
        }

        /// <summary>Синхронно дорисовывает все грязные чанки перед стартом цикла (UI не блокируется навсегда — PumpUi).</summary>
        private void WarmUpStockVisualFullySync(VoxelStock voxelStock)
        {
            var profile = VoxelSimulationProfile.ForResolution(_stockLifecycle.VoxelResolutionMm);
            int chunkBudget = Math.Max(8, profile.DirtyChunkBudget);
            _suppressCameraDrivenStockRefresh = true;
            _stockMeshGate.Wait();
            try
            {
                while (voxelStock.IsDirty)
                {
                    voxelStock.UpdateVisualsBatchAsync(chunkBudget).GetAwaiter().GetResult();
                    if (IsStockShownInViewport() && _stockVisual.Content != voxelStock.MainModel)
                    {
                        _stockVisual.Content = voxelStock.MainModel;
                    }

                    PumpUiForLoadingOverlay();
                }
            }
            finally
            {
                _stockMeshGate.Release();
                _suppressCameraDrivenStockRefresh = false;
            }
        }

        /// <summary>Дорисовывает воксельный меш заготовки чанками, отдавая кадры UI (без отдельного модального окна).</summary>
        private async Task WarmUpStockVisualFullyAsync()
        {
            if (_stockLifecycle.Stock is not VoxelStock voxelStock)
            {
                return;
            }

            var profile = VoxelSimulationProfile.ForResolution(_stockLifecycle.VoxelResolutionMm);
            int chunkBudget = Math.Max(8, profile.DirtyChunkBudget);
            _suppressCameraDrivenStockRefresh = true;
            await _stockMeshGate.WaitAsync();
            try
            {
                while (voxelStock.IsDirty)
                {
                    await voxelStock.UpdateVisualsBatchAsync(chunkBudget);
                    if (IsStockShownInViewport() && _stockVisual.Content != voxelStock.MainModel)
                    {
                        _stockVisual.Content = voxelStock.MainModel;
                    }

                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
            finally
            {
                _stockMeshGate.Release();
                _suppressCameraDrivenStockRefresh = false;
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
        /// Обновляет параметрический меш заготовки (форма из конструктора или бокс по полям Min/Max).
        /// Воксельный runtime сбрасывается; пересоздание — только через <see cref="TryEnsureVoxelStock"/>.
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
                _lastPosition = _programPlaybackHost.CurrentPosition;
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

            _lastPosition = GetCurrentPosition();
            _programPlaybackHost.SetCurrentPosition(_lastPosition);
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
            fanucPanel.UpdateMachinePosition(
                state.X,
                state.Y,
                state.Z,
                state.MachineZeroOffsetX,
                state.MachineZeroOffsetY,
                state.MachineZeroOffsetZ,
                activeOffset.X,
                activeOffset.Y,
                activeOffset.Z);
        }

        private void UpdateFanucMachinePosition(MachineState state) =>
            UpdateFanucMachinePosition(FanucPanel, state);

        private void UpdateFanucMachinePosition(MachineStateChangedEvent evt)
        {
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
            _stockLifecycle.CutWorker?.Dispose();
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

            _stockMeshGate.Dispose();
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


