using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CNCSS.Logic.NcPrograms;
using CNCSS.Machine.Model;

namespace CNCSS.UI.FanucPanel
{
    /// <summary>Визуальный блок пульта FANUC: страницы POS/PROG, аварии, офсеты, программное редактирование и привязка к событиям станка.</summary>
    public partial class FanucPanelControl : UserControl
    {
        public enum OffsetToolColumn
        {
            GeomH = 1,
            WearH = 2,
            GeomD = 3,
            WearD = 4
        }
        private enum FanucPage
        {
            Pos,
            Prog,
            Alarm,
            CustomGrph,
            Offset,
            System
        }
        private enum ProgSubPage
        {
            ProgramMain,
            ProgramDirectory
        }

        /// <summary>Подрежим экрана MESSAGE: текущее сообщение / лог действий / история ошибок за сессию.</summary>
        private enum MessageSubPage
        {
            CurrentMessage,
            UserActionHistory,
            SessionAlarmHistory
        }

        /// <summary>Подэкраны раздела OFFSET/SETTING (кнопка OFS SET).</summary>
        private enum OffsetSubPage
        {
            ToolOffset,
            Setting,
            Work
        }

        public event Action? CycleStartRequested;
        public event Action? FeedHoldRequested;
        public event Action? ResetRequested;
        public event Action<string>? ModeChangedRequested;
        public event Action<string, double>? JogRequested;
        public event Action<string>? MdiExecuteRequested;
        /// <summary>Режим PROG → DIR: ввод имени программы (O0999 и т.п.) Enter/INPUT создаёт пустой .nc только с строкой имени.</summary>
        public event Action<string>? ProgDirectoryCreateAndOpenRequested;
        /// <summary>Открыть существующую программу по O№ из каталога NC (без аларма, если файла нет).</summary>
        public event Action<string>? ProgOpenByNameRequested;
        /// <summary>Удалить файл программы с именем как в DIR (по MDI после парсера O).</summary>
        public event Action<string>? ProgDeleteProgramByNameRequested;
        public event Action<bool>? SingleBlockChangedRequested;
        public event Action<bool>? OptionalStopChangedRequested;
        public event Action<bool>? DryRunChangedRequested;
        public event Action<int, double?, double?, double?>? OffsetUpdateRequested;
        public event Action<int>? OffsetSystemSelectionChangedRequested;
        public event Action? OffsetReadActiveToEditorRequested;
        public event Action<int, OffsetToolColumn, double, bool>? OffsetToolValueUpdateRequested;
        private FanucPage _currentPage = FanucPage.Pos;
        private OffsetSubPage _offsetSubPage = OffsetSubPage.ToolOffset;
        private bool _offsetOprtMenuActive;
        // Per reference: OFFSET/WORK use OPRT menus; typing does not auto-switch softkeys here.
        private int _offsetToolSelectedRow = 1; // 1..8
        private int _offsetToolSelectedCol = 1; // 1..4 (GEOM(H), WEAR(H), GEOM(D), WEAR(D))
        private int _offsetToolPageStartRow = 1; // 1, 9, 17, 25
        private const int OffsetToolTotalRows = 30;
        private const int OffsetToolRowsPerPage = 8;
        private string _offsetInputBuffer = string.Empty;
        private Border[,]? _offsetToolCellBorders;
        private TextBlock[,]? _offsetToolCellTexts;
        private TextBlock[]? _offsetToolRowLabels;
        private readonly double[,] _offsetToolValues = new double[OffsetToolTotalRows + 1, 5]; // [1..30, 1..4]
        private int _offsetWorkSelectedSystem; // 0..3 => EXT,G54,G55,G56
        private int _offsetWorkSelectedAxis;   // 0..2 => X,Y,Z
        private Border[]? _offsetWorkCells;    // system*3 + axis
        private double _jogStep = 1.0;
        private MessageSubPage _messageSubPage = MessageSubPage.CurrentMessage;
        private int _messageSoftKeyCarouselIndex;
        private List<(string Label, Action Action)>[] _messageSoftKeyPages = [];
        private string _currentAlarmText = "NONE";
        private bool _currentAlarmIsError;
        private readonly List<string> _sessionAlarmLines = new();
        private readonly List<string> _userActionLines = new();
        private const int MaxSessionAlarmLines = 400;
        private const int MaxUserActionLines = 500;
        private bool _suppressModeChangedEvent;
        private bool _suppressOffsetSelectionEvent;
        private bool _isMdiShiftActive;
        private bool _isMdiInsertMode = true;
        private int _softKeyStartIndex;
        /// <summary>Страница софт-клавиш FANUC для PROG+MDI (&lt;-&gt;: BG-EDT… / READ…).</summary>
        private int _progMdiSoftKeyPageIndex;
        private List<(string Label, Action Action)>[] _progMdiSoftKeyPages = [];
        private bool _mdiProgSoftkeysContextActive;
        private readonly Dictionary<FanucPage, List<(string Label, Action Action)>> _softKeyByPage = new();
        private ProgSubPage _currentProgSubPage = ProgSubPage.ProgramMain;
        private string _progOHeader = "00001";
        private string _progNHeader = "N00000";
        private string _modeStatusAbbrev = "EDIT";
        private double _feedOverridePercent = 100;
        private double _lastSpindleRpm;
        private bool _hasNcProgramLoaded;
        /// <summary>Полный путь к загруженной NC‑программе (для окна DIR: размер файла и дата).</summary>
        private string? _ncProgramSourcePath;
        private double _loadedOffsetX;
        private double _loadedOffsetY;
        private double _loadedOffsetZ;
        private double _lastPhysicalX;
        private double _lastPhysicalY;
        private double _lastPhysicalZ;
        private double _machineZeroOffsetX;
        private double _machineZeroOffsetY;
        private double _machineZeroOffsetZ;
        private double _activeWorkOffsetX;
        private double _activeWorkOffsetY;
        private double _activeWorkOffsetZ;

        private static readonly SolidColorBrush SoftLabelIdleBorderBrush = CreateFrozenBrush(Color.FromRgb(83, 88, 92));
        private static readonly SolidColorBrush SoftLabelActiveBorderBrush = CreateFrozenBrush(Color.FromRgb(45, 52, 60));
        private static readonly SolidColorBrush ProgNcCaretCharHighlight = CreateFrozenBrush(Color.FromRgb(255, 255, 0));
        private static readonly SolidColorBrush ProgDirectoryTextBrush = CreateFrozenBrush(Color.FromRgb(16, 16, 16));

        private static readonly FontFamily FanucNcMonospaceFont = new("Consolas");

        /// <summary>Буквы MDI-панели: без Shift первая буква, с Shift та же клавиша выдаёт второй символ как на FANUC SHIFT.</summary>
        private static readonly Dictionary<Key, (string Plain, string Shifted)> PhysicalMdiDualKeyMap =
            new()
            {
                [Key.P] = ("P", "O"),
                [Key.Q] = ("Q", "N"),
                [Key.R] = ("R", "G"),
                [Key.A] = ("A", "7"),
                [Key.B] = ("B", "8"),
                [Key.C] = ("C", "9"),
                [Key.U] = ("U", "X"),
                [Key.V] = ("V", "Y"),
                [Key.W] = ("W", "Z"),
                [Key.I] = ("I", "M"),
                [Key.J] = ("J", "S"),
                [Key.K] = ("K", "T"),
                [Key.L] = ("L", "F"),
                [Key.D] = ("D", "H"),
                [Key.E] = ("E", ";"),
            };

        private List<string>? _progCachedNcLines;
        private int _progNcFocusLineIndex = -1;
        private int _progNcCaretColumn;

        private enum PosSubPage
        {
            Abs,
            Rel,
            All
        }

        private PosSubPage _posSubPage = PosSubPage.Abs;
        private bool _posOprtMenuActive;

        private bool _progOprtMenuActive;
        private int _progOprtMenuPageIndex;

        /// <summary>Кэш всех строк каталога (без шапки) для постраничного вывода.</summary>
        private List<(string a, string b, string c, string d)> _directoryDataCache = new();
        private int _directoryProgramListPageIndex;
        private const int DirectoryDataRowsPerPage = 12;

        public NcProgramCatalogService ProgramCatalogService { get; set; } = new();

        public FanucPanelControl()
        {
            InitializeComponent();
            Loaded += (_, _) => Dispatcher.BeginInvoke(() => Keyboard.Focus(this), DispatcherPriority.Input);

            _progMdiSoftKeyPages = BuildProgMdiEditSoftKeyPages();
            _messageSoftKeyPages = BuildMessageSoftKeyPages();
            InitializeSoftKeys();
            UpdatePageVisibility();
            ValidateOffsetEditorInputs();
            UpdateMdiEditModeButtons();
            EnsureOffsetToolCellCache();
            SnapOffsetToolPageToSelection();
            RefreshOffsetToolPage();
            ApplyOffsetToolSelectionVisual();
            RefreshOffsetInputLine();
        }

        private void EnsureOffsetToolCellCache()
        {
            if (_offsetToolCellBorders != null)
            {
                return;
            }

            // [row 1..8, col 1..4] → Border in XAML.
            _offsetToolCellBorders = new Border[9, 5];
            _offsetToolCellTexts = new TextBlock[9, 5];
            _offsetToolRowLabels = new TextBlock[9];
            for (int r = 1; r <= 8; r++)
            {
                if (FindName($"OffsetToolRowLabel_{r}") is TextBlock lbl)
                {
                    _offsetToolRowLabels[r] = lbl;
                }

                for (int c = 1; c <= 4; c++)
                {
                    if (FindName($"OffsetToolCell_{r}_{c}") is Border b)
                    {
                        _offsetToolCellBorders[r, c] = b;
                    }

                    if (FindName($"OffsetToolCellText_{r}_{c}") is TextBlock t)
                    {
                        _offsetToolCellTexts[r, c] = t;
                    }
                }
            }
        }

        private void RefreshOffsetToolPage()
        {
            EnsureOffsetToolCellCache();
            if (_offsetToolRowLabels == null)
            {
                return;
            }

            for (int visibleRow = 1; visibleRow <= OffsetToolRowsPerPage; visibleRow++)
            {
                int absoluteRow = _offsetToolPageStartRow + (visibleRow - 1);
                bool exists = absoluteRow <= OffsetToolTotalRows;
                TextBlock label = _offsetToolRowLabels[visibleRow];
                if (label != null)
                {
                    label.Text = exists ? absoluteRow.ToString("000", CultureInfo.InvariantCulture) : string.Empty;
                    label.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
                }

                if (_offsetToolCellTexts != null)
                {
                    for (int c = 1; c <= 4; c++)
                    {
                        TextBlock t = _offsetToolCellTexts[visibleRow, c];
                        if (t != null)
                        {
                            t.Text = exists
                                ? _offsetToolValues[absoluteRow, c].ToString("0.000", CultureInfo.InvariantCulture)
                                : string.Empty;
                        }
                    }
                }

                if (_offsetToolCellBorders == null)
                {
                    continue;
                }

                for (int c = 1; c <= 4; c++)
                {
                    Border b = _offsetToolCellBorders[visibleRow, c];
                    if (b != null)
                    {
                        b.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
        }

        private void ApplyOffsetToolSelectionVisual()
        {
            EnsureOffsetToolCellCache();
            if (_offsetToolCellBorders == null)
            {
                return;
            }

            for (int visibleRow = 1; visibleRow <= OffsetToolRowsPerPage; visibleRow++)
            {
                for (int c = 1; c <= 4; c++)
                {
                    Border b = _offsetToolCellBorders[visibleRow, c];
                    if (b == null)
                    {
                        continue;
                    }

                    int absoluteRow = _offsetToolPageStartRow + (visibleRow - 1);
                    b.Background = (absoluteRow == _offsetToolSelectedRow && c == _offsetToolSelectedCol)
                        ? new SolidColorBrush(Color.FromRgb(0xE8, 0xD4, 0x00))
                        : Brushes.Transparent;
                }
            }
        }

        private void SnapOffsetToolPageToSelection()
        {
            int clamped = Math.Max(1, Math.Min(OffsetToolTotalRows, _offsetToolSelectedRow));
            _offsetToolSelectedRow = clamped;
            _offsetToolPageStartRow = ((_offsetToolSelectedRow - 1) / OffsetToolRowsPerPage) * OffsetToolRowsPerPage + 1;
        }

        private void EnsureOffsetWorkCellCache()
        {
            if (_offsetWorkCells != null)
            {
                return;
            }

            // Index = system*3 + axis: system 0 EXT, 1 G54, 2 G55, 3 G56, 4 G57, 5 G58, 6 G59; axis 0 X,1 Y,2 Z.
            _offsetWorkCells = new Border[21];
            _offsetWorkCells[0] = FindName("OffsetWorkCell_EXT_X") as Border ?? new Border();
            _offsetWorkCells[1] = FindName("OffsetWorkCell_EXT_Y") as Border ?? new Border();
            _offsetWorkCells[2] = FindName("OffsetWorkCell_EXT_Z") as Border ?? new Border();
            _offsetWorkCells[3] = FindName("OffsetWorkCell_G54_X") as Border ?? new Border();
            _offsetWorkCells[4] = FindName("OffsetWorkCell_G54_Y") as Border ?? new Border();
            _offsetWorkCells[5] = FindName("OffsetWorkCell_G54_Z") as Border ?? new Border();
            _offsetWorkCells[6] = FindName("OffsetWorkCell_G55_X") as Border ?? new Border();
            _offsetWorkCells[7] = FindName("OffsetWorkCell_G55_Y") as Border ?? new Border();
            _offsetWorkCells[8] = FindName("OffsetWorkCell_G55_Z") as Border ?? new Border();
            _offsetWorkCells[9] = FindName("OffsetWorkCell_G56_X") as Border ?? new Border();
            _offsetWorkCells[10] = FindName("OffsetWorkCell_G56_Y") as Border ?? new Border();
            _offsetWorkCells[11] = FindName("OffsetWorkCell_G56_Z") as Border ?? new Border();
            _offsetWorkCells[12] = FindName("OffsetWorkCell_G57_X") as Border ?? new Border();
            _offsetWorkCells[13] = FindName("OffsetWorkCell_G57_Y") as Border ?? new Border();
            _offsetWorkCells[14] = FindName("OffsetWorkCell_G57_Z") as Border ?? new Border();
            _offsetWorkCells[15] = FindName("OffsetWorkCell_G58_X") as Border ?? new Border();
            _offsetWorkCells[16] = FindName("OffsetWorkCell_G58_Y") as Border ?? new Border();
            _offsetWorkCells[17] = FindName("OffsetWorkCell_G58_Z") as Border ?? new Border();
            _offsetWorkCells[18] = FindName("OffsetWorkCell_G59_X") as Border ?? new Border();
            _offsetWorkCells[19] = FindName("OffsetWorkCell_G59_Y") as Border ?? new Border();
            _offsetWorkCells[20] = FindName("OffsetWorkCell_G59_Z") as Border ?? new Border();
        }

        public void UpdateWorkOffsetsTable(IReadOnlyDictionary<int, (double X, double Y, double Z)> offsets)
        {
            string FormatOffset(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

            void Apply(int systemNumber, TextBlock x, TextBlock y, TextBlock z)
            {
                if (!offsets.TryGetValue(systemNumber, out var v))
                {
                    v = (0, 0, 0);
                }
                x.Text = FormatOffset(v.X);
                y.Text = FormatOffset(v.Y);
                z.Text = FormatOffset(v.Z);
            }

            Apply(0, OffsetWorkExtXText, OffsetWorkExtYText, OffsetWorkExtZText);
            Apply(54, OffsetWorkG54XText, OffsetWorkG54YText, OffsetWorkG54ZText);
            Apply(55, OffsetWorkG55XText, OffsetWorkG55YText, OffsetWorkG55ZText);
            Apply(56, OffsetWorkG56XText, OffsetWorkG56YText, OffsetWorkG56ZText);
            Apply(57, OffsetWorkG57XText, OffsetWorkG57YText, OffsetWorkG57ZText);
            Apply(58, OffsetWorkG58XText, OffsetWorkG58YText, OffsetWorkG58ZText);
            Apply(59, OffsetWorkG59XText, OffsetWorkG59YText, OffsetWorkG59ZText);
        }

        private void ApplyOffsetWorkSelectionVisual()
        {
            EnsureOffsetWorkCellCache();
            if (_offsetWorkCells == null)
            {
                return;
            }

            int selected = (_offsetWorkSelectedSystem * 3) + _offsetWorkSelectedAxis;
            for (int i = 0; i < _offsetWorkCells.Length; i++)
            {
                Border? b = _offsetWorkCells[i];
                if (b == null)
                {
                    continue;
                }

                b.Background = i == selected
                    ? new SolidColorBrush(Color.FromRgb(0xE8, 0xD4, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0xB8, 0xBE, 0xC6));
            }
        }

        private void RefreshOffsetInputLine()
        {
            if (OffsetInputTextBox == null)
            {
                return;
            }

            OffsetInputTextBox.Text = string.IsNullOrEmpty(_offsetInputBuffer) ? "_" : _offsetInputBuffer;
        }

        private static char? TryMapOffsetInputChar(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                return (char)('A' + (key - Key.A));
            }

            if (key >= Key.D0 && key <= Key.D9)
            {
                return (char)('0' + (key - Key.D0));
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return (char)('0' + (key - Key.NumPad0));
            }

            return key switch
            {
                Key.OemMinus => '-',
                Key.Subtract => '-',
                Key.OemPlus => '+',
                Key.Add => '+',
                Key.OemPeriod => '.',
                Key.Decimal => '.',
                Key.Space => ' ',
                _ => null
            };
        }

        private void FanucPanelControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_currentPage != FanucPage.Offset)
            {
                return;
            }

            if (_offsetSubPage == OffsetSubPage.Work)
            {
                switch (key)
                {
                    case Key.Left:
                        _offsetWorkSelectedSystem = (_offsetWorkSelectedSystem <= 1) ? _offsetWorkSelectedSystem : _offsetWorkSelectedSystem - 2;
                        ApplyOffsetWorkSelectionVisual();
                        e.Handled = true;
                        return;
                    case Key.Right:
                        _offsetWorkSelectedSystem = (_offsetWorkSelectedSystem >= 2) ? _offsetWorkSelectedSystem : _offsetWorkSelectedSystem + 2;
                        ApplyOffsetWorkSelectionVisual();
                        e.Handled = true;
                        return;
                    case Key.Up:
                        if (_offsetWorkSelectedAxis > 0)
                        {
                            _offsetWorkSelectedAxis--;
                        }
                        else
                        {
                            if (_offsetWorkSelectedSystem == 1) _offsetWorkSelectedSystem = 0;
                            else if (_offsetWorkSelectedSystem == 3) _offsetWorkSelectedSystem = 2;
                        }
                        ApplyOffsetWorkSelectionVisual();
                        e.Handled = true;
                        return;
                    case Key.Down:
                        if (_offsetWorkSelectedAxis < 2)
                        {
                            _offsetWorkSelectedAxis++;
                        }
                        else
                        {
                            if (_offsetWorkSelectedSystem == 0) _offsetWorkSelectedSystem = 1;
                            else if (_offsetWorkSelectedSystem == 2) _offsetWorkSelectedSystem = 3;
                        }
                        ApplyOffsetWorkSelectionVisual();
                        e.Handled = true;
                        return;
                }
            }

            if (_offsetSubPage == OffsetSubPage.ToolOffset)
            {
                switch (key)
                {
                    case Key.Left:
                        _offsetToolSelectedCol = Math.Max(1, _offsetToolSelectedCol - 1);
                        ApplyOffsetToolSelectionVisual();
                        e.Handled = true;
                        return;
                    case Key.Right:
                        _offsetToolSelectedCol = Math.Min(4, _offsetToolSelectedCol + 1);
                        ApplyOffsetToolSelectionVisual();
                        e.Handled = true;
                        return;
                    case Key.Up:
                        _offsetToolSelectedRow = Math.Max(1, _offsetToolSelectedRow - 1);
                        SnapOffsetToolPageToSelection();
                        RefreshOffsetToolPage();
                        ApplyOffsetToolSelectionVisual();
                        e.Handled = true;
                        return;
                    case Key.Down:
                        _offsetToolSelectedRow = Math.Min(OffsetToolTotalRows, _offsetToolSelectedRow + 1);
                        SnapOffsetToolPageToSelection();
                        RefreshOffsetToolPage();
                        ApplyOffsetToolSelectionVisual();
                        e.Handled = true;
                        return;
                }
            }

            if (key == Key.Escape)
            {
                _offsetInputBuffer = string.Empty;
                RefreshOffsetInputLine();
                e.Handled = true;
                return;
            }

            if (key == Key.Back)
            {
                if (!string.IsNullOrEmpty(_offsetInputBuffer))
                {
                    _offsetInputBuffer = _offsetInputBuffer.Length == 1 ? string.Empty : _offsetInputBuffer[..^1];
                    RefreshOffsetInputLine();
                }
                e.Handled = true;
                return;
            }

            char? ch = TryMapOffsetInputChar(key);
            if (ch.HasValue)
            {
                if (_offsetInputBuffer.Length < 22)
                {
                    _offsetInputBuffer += ch.Value;
                }
                RefreshOffsetInputLine();
                e.Handled = true;
            }
        }

        private static SolidColorBrush CreateFrozenBrush(Color c)
        {
            var brush = new SolidColorBrush(c);
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }

        /// <summary>Рамка вокруг ячейки PRGRM в режиме PROG экран программы.</summary>
        private void ApplySoftMenuLabelHighlight()
        {
            var slots = new[] { SoftLblBorderSlot1, SoftLblBorderSlot2, SoftLblBorderSlot3, SoftLblBorderSlot4, SoftLblBorderSlot5, SoftLblBorderSlot6 };
            foreach (Border b in slots)
            {
                b.BorderThickness = new Thickness(1);
                b.BorderBrush = SoftLabelIdleBorderBrush;
            }

            if (_currentPage == FanucPage.Pos)
            {
                // Per reference: highlight ABS/REL/ALL selection; in OPRT submenu don't highlight.
                if (_posOprtMenuActive)
                {
                    return;
                }

                Border? active = _posSubPage switch
                {
                    PosSubPage.Abs => SoftLblBorderSlot1,
                    PosSubPage.Rel => SoftLblBorderSlot2,
                    PosSubPage.All => SoftLblBorderSlot3,
                    _ => null
                };

                if (active != null)
                {
                    active.BorderThickness = new Thickness(2);
                    active.BorderBrush = SoftLabelActiveBorderBrush;
                }

                return;
            }

            if (_currentPage == FanucPage.Alarm)
            {
                ApplySoftMenuLabelHighlightMessageActive();
                return;
            }

            if (IsProgMdiProgramEditSoftkeysActive())
            {
                return;
            }

            if (_currentPage == FanucPage.Offset)
            {
                ApplySoftMenuLabelHighlightOffsetActive();
                return;
            }

            if (_currentPage == FanucPage.System)
            {
                // Only PARA exists; keep highlighted.
                SoftLblBorderSlot1.BorderThickness = new Thickness(2);
                SoftLblBorderSlot1.BorderBrush = SoftLabelActiveBorderBrush;
                return;
            }

            if (_currentPage == FanucPage.CustomGrph)
            {
                // Only PARAM exists; keep highlighted.
                SoftLblBorderSlot1.BorderThickness = new Thickness(2);
                SoftLblBorderSlot1.BorderBrush = SoftLabelActiveBorderBrush;
                return;
            }

            if (_currentPage != FanucPage.Prog)
            {
                return;
            }

            if (_currentProgSubPage == ProgSubPage.ProgramDirectory)
            {
                SoftLblBorderSlot3.BorderThickness = new Thickness(2);
                SoftLblBorderSlot3.BorderBrush = SoftLabelActiveBorderBrush;
            }
            else
            {
                SoftLblBorderSlot2.BorderThickness = new Thickness(2);
                SoftLblBorderSlot2.BorderBrush = SoftLabelActiveBorderBrush;
            }
        }

        private void ApplySoftMenuLabelHighlightOffsetActive()
        {
            // Для OFFSET меню ввода/(OPRT) не подсвечиваем ни одну софт-клавишу:
            // возврат выполняется софт-клавишей "<" внутри OPRT.
            if (_offsetOprtMenuActive)
            {
                return;
            }

            Border? active = _offsetSubPage switch
            {
                OffsetSubPage.ToolOffset => SoftLblBorderSlot1,
                OffsetSubPage.Setting => SoftLblBorderSlot2,
                OffsetSubPage.Work => SoftLblBorderSlot3,
                _ => null
            };

            if (active != null)
            {
                active.BorderThickness = new Thickness(2);
                active.BorderBrush = SoftLabelActiveBorderBrush;
            }
        }

        private void InitializeSoftKeys()
        {
            _softKeyByPage.Clear();
            _softKeyByPage[FanucPage.Pos] = new List<(string Label, Action Action)>
            {
                ("ABS", () =>
                {
                    _posOprtMenuActive = false;
                    _posSubPage = PosSubPage.Abs;
                    RefreshPosScreen();
                    ApplyScreenHeaderForPage();
                    UpdateSoftKeyBar();
                }),
                ("REL", () =>
                {
                    _posOprtMenuActive = false;
                    _posSubPage = PosSubPage.Rel;
                    RefreshPosScreen();
                    ApplyScreenHeaderForPage();
                    UpdateSoftKeyBar();
                }),
                ("ALL", () =>
                {
                    _posOprtMenuActive = false;
                    _posSubPage = PosSubPage.All;
                    RefreshPosScreen();
                    ApplyScreenHeaderForPage();
                    UpdateSoftKeyBar();
                }),
                ("", () => { }),
                ("OPRT", () =>
                {
                    _posOprtMenuActive = true;
                    UpdateSoftKeyBar();
                }),
                ("", () => { })
            };

            _softKeyByPage[FanucPage.Prog] = new List<(string Label, Action Action)>
            {
                ("", () => { }),
                ("PRGRM", () =>
                {
                    _currentProgSubPage = ProgSubPage.ProgramMain;
                    LogUserAction("PROG → PRGRM");
                    UpdateProgScreen();
                }),
                ("DIR", () =>
                {
                    _currentProgSubPage = ProgSubPage.ProgramDirectory;
                    LogUserAction("PROG → DIR");
                    UpdateProgScreen();
                }),
                ("", () => { }),
                ("", () => { }),
                ("(OPRT)", () =>
                {
                    _progOprtMenuActive = true;
                    _progOprtMenuPageIndex = 0;
                    LogUserAction("PROG → (OPRT)");
                    UpdateSoftKeyBar();
                })
            };

            _softKeyByPage[FanucPage.System] = new List<(string Label, Action Action)>
            {
                ("PARA", () => SetAlarm("SYSTEM PARA", false)),
                ("", () => { }),
                ("", () => { }),
                ("", () => { }),
                ("", () => { }),
                ("", () => { })
            };

            _softKeyByPage[FanucPage.CustomGrph] = new List<(string Label, Action Action)>
            {
                ("PARAM", () => SetAlarm("CUSTOM GRPH PARAM", false)),
                ("", () => { }),
                ("", () => { }),
                ("", () => { }),
                ("", () => { }),
                ("", () => { })
            };
            _softKeyStartIndex = 0;
            UpdateSoftKeyBar();
        }

        private List<(string Label, Action Action)>[] BuildProgMdiEditSoftKeyPages()
        {
            static string Dn() => "\u2193";
            static string Up() => "\u2191";

            var pageEdit = new List<(string Label, Action Action)>
            {
                ("BG-EDT", () => InvokeBackgroundEditOpenLoadedProgram()),
                ("O.SRH", () => InvokeOpenProgramByMdiONameIfExists()),
                ("SRH" + Dn(), () => SearchInOpenedProgramFromMdi(searchDown: true)),
                ("SRH" + Up(), () => SearchInOpenedProgramFromMdi(searchDown: false)),
                ("REWIND", () => RewindNcProgramToStart()),
                ("", () => { })
            };

            var pageExtio = new List<(string Label, Action Action)>
            {
                ("F.SRH", () => SetAlarm("FILE SEARCH", false)),
                ("READ", () => SetAlarm("READ", false)),
                ("PUNCH", () => SetAlarm("PUNCH", false)),
                ("DELETE", () => InvokeDeleteProgramByMdiOName()),
                ("EX-EDT", () => SetAlarm("EXTENDED EDIT", false)),
                ("", () => { })
            };

            return [pageEdit, pageExtio];
        }

        /// <summary>BG-EDT: экран редактирования уже загруженной программы (PRGRM).</summary>
        private void InvokeBackgroundEditOpenLoadedProgram()
        {
            if (!_hasNcProgramLoaded)
            {
                return;
            }

            LogUserAction("BG-EDT: экран программы");
            NavigateToProgMainScreen();
        }

        private void InvokeOpenProgramByMdiONameIfExists()
        {
            string mdi = (MdiInputTextBox.Text ?? string.Empty).Trim();
            if (!TryParseDirectoryNewProgramToken(mdi, out string normalizedO))
            {
                return;
            }

            LogUserAction($"O.SRH → {normalizedO}");
            ProgOpenByNameRequested?.Invoke(normalizedO);
        }

        private void InvokeDeleteProgramByMdiOName()
        {
            string mdi = (MdiInputTextBox.Text ?? string.Empty).Trim();
            if (!TryParseDirectoryNewProgramToken(mdi, out string normalizedO))
            {
                return;
            }

            LogUserAction($"DELETE файл программы → {normalizedO}");
            ProgDeleteProgramByNameRequested?.Invoke(normalizedO);
        }

        private void SearchInOpenedProgramFromMdi(bool searchDown)
        {
            if (!IsProgNcNavigationActive())
            {
                return;
            }

            string needle = (MdiInputTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(needle))
            {
                return;
            }

            LogUserAction($"{(searchDown ? "SRH↓" : "SRH↑")} поиск «{needle}»");

            var lines = _progCachedNcLines!;
            int n = lines.Count;

            bool LineMatches(int idx) =>
                lines[idx].Contains(needle, StringComparison.OrdinalIgnoreCase);

            int IdxOfMatch(int idx) =>
                lines[idx].IndexOf(needle, StringComparison.OrdinalIgnoreCase);

            int cur = Math.Clamp(_progNcFocusLineIndex < 0 ? 0 : _progNcFocusLineIndex, 0, n - 1);

            if (searchDown)
            {
                for (int line = cur + 1; line < n; line++)
                {
                    if (LineMatches(line))
                    {
                        SetNcProgramCaret(line, IdxOfMatch(line));
                        return;
                    }
                }

                for (int line = 0; line <= cur; line++)
                {
                    if (LineMatches(line))
                    {
                        SetNcProgramCaret(line, IdxOfMatch(line));
                        return;
                    }
                }
            }
            else
            {
                for (int line = cur - 1; line >= 0; line--)
                {
                    if (LineMatches(line))
                    {
                        SetNcProgramCaret(line, IdxOfMatch(line));
                        return;
                    }
                }

                for (int line = n - 1; line > cur; line--)
                {
                    if (LineMatches(line))
                    {
                        SetNcProgramCaret(line, IdxOfMatch(line));
                        return;
                    }
                }
            }
        }

        private void RewindNcProgramToStart()
        {
            if (!IsProgNcNavigationActive())
            {
                return;
            }

            LogUserAction("REWIND: к началу программы");
            SetNcProgramCaret(0, 0);
        }

        private bool IsProgMdiProgramEditSoftkeysActive() =>
            false;

        /// <summary>Обновляет набор нижних софт-клавиш после ввод/фокуса MDI или смены экрана.</summary>
        private void RefreshProgMdiTypingSoftkeys()
        {
            bool active = IsProgMdiProgramEditSoftkeysActive();
            if (_mdiProgSoftkeysContextActive != active)
            {
                _progMdiSoftKeyPageIndex = 0;
                _mdiProgSoftkeysContextActive = active;
                _softKeyStartIndex = 0;
            }

            UpdateSoftKeyBar();
        }

        /// <returns>True если обработано (стрелки листают только страницу FANUC-софтов в PROG+MDI).</returns>
        private bool TryProgMdiNavigateSoftkeyPage(int pageDelta)
        {
            if (!IsProgMdiProgramEditSoftkeysActive() || _progMdiSoftKeyPages.Length == 0)
            {
                return false;
            }

            int n = _progMdiSoftKeyPages.Length;
            _progMdiSoftKeyPageIndex = (_progMdiSoftKeyPageIndex + pageDelta + n) % n;
            _softKeyStartIndex = 0;
            UpdateSoftKeyBar();
            return true;
        }

        private bool TryMessageSoftkeyCarousel(int pageDelta)
        {
            if (_currentPage != FanucPage.Alarm || _messageSoftKeyPages.Length == 0)
            {
                return false;
            }

            int n = _messageSoftKeyPages.Length;
            _messageSoftKeyCarouselIndex = (_messageSoftKeyCarouselIndex + pageDelta + n) % n;
            _softKeyStartIndex = 0;
            UpdateSoftKeyBar();
            return true;
        }

        private List<(string Label, Action Action)>[] BuildMessageSoftKeyPages()
        {
            var pageA = new List<(string Label, Action Action)>
            {
                ("MSG", () => SwitchMessageSubPage(MessageSubPage.CurrentMessage)),
                ("HISTRY", () => SwitchMessageSubPage(MessageSubPage.UserActionHistory)),
                ("", () => { }),
                ("", () => { }),
                ("", () => { }),
                ("→", () => SwitchMessageSoftKeyPage(1))
            };

            var pageB = new List<(string Label, Action Action)>
            {
                ("MSGHIS", () => SwitchMessageSubPage(MessageSubPage.SessionAlarmHistory)),
                ("", () => { }),
                ("", () => { }),
                ("", () => { }),
                ("", () => { }),
                ("→", () => SwitchMessageSoftKeyPage(0))
            };

            return [pageA, pageB];
        }

        private void SwitchMessageSoftKeyPage(int pageIndex)
        {
            if (_messageSoftKeyPages.Length == 0)
            {
                return;
            }

            _messageSoftKeyCarouselIndex = Math.Clamp(pageIndex, 0, _messageSoftKeyPages.Length - 1);
            _softKeyStartIndex = 0;
            UpdateSoftKeyBar();
        }

        private void SwitchMessageSubPage(MessageSubPage subPage)
        {
            _messageSubPage = subPage;
            RefreshMessageScreenFull();
        }

        private void RefreshMessageScreenFull()
        {
            if (_currentPage != FanucPage.Alarm)
            {
                return;
            }

            UpdateMessageHeaderTitle();
            UpdateMessageBodyVisual();
            ApplySoftMenuLabelHighlight();
        }

        private void UpdateMessageHeaderTitle()
        {
            if (_currentPage != FanucPage.Alarm)
            {
                return;
            }

            ScreenModeText.Text = _messageSubPage switch
            {
                MessageSubPage.CurrentMessage => "ALARM MESSAGE",
                MessageSubPage.UserActionHistory => "MESSAGE",
                MessageSubPage.SessionAlarmHistory => "MESSAGE HISTORY",
                _ => "MESSAGE"
            };
            MessagePanelTitleText.Text = _messageSubPage switch
            {
                MessageSubPage.CurrentMessage => "MESSAGE",
                MessageSubPage.UserActionHistory => "HISTORY",
                MessageSubPage.SessionAlarmHistory => "MESSAGE HISTORY",
                _ => "MESSAGE"
            };
        }

        private void UpdateMessageBodyVisual()
        {
            if (_currentPage != FanucPage.Alarm || ScreenMessageBodyText == null)
            {
                return;
            }

            switch (_messageSubPage)
            {
                case MessageSubPage.CurrentMessage:
                    bool none = string.Equals(_currentAlarmText, "NONE", StringComparison.OrdinalIgnoreCase);
                    if (none)
                    {
                        ScreenMessageBodyText.Text = "(Нет активного аварийного сообщения)";
                        ScreenMessageBodyText.Foreground = Brushes.Black;
                        return;
                    }

                    ScreenMessageBodyText.Text = _currentAlarmText;
                    ScreenMessageBodyText.Foreground = _currentAlarmIsError
                        ? Brushes.OrangeRed
                        : Brushes.Black;
                    break;
                case MessageSubPage.UserActionHistory:
                    ScreenMessageBodyText.Text = FormatLineListBody(_userActionLines, emptyHint: "(Пока нет действий пользователя)");
                    ScreenMessageBodyText.Foreground = Brushes.Black;
                    break;
                case MessageSubPage.SessionAlarmHistory:
                    ScreenMessageBodyText.Text = FormatLineListBody(_sessionAlarmLines, emptyHint: "(За сеанс ошибок не было)");
                    ScreenMessageBodyText.Foreground = Brushes.Black;
                    break;
            }
        }

        private static string FormatLineListBody(List<string> lines, string emptyHint)
        {
            if (lines == null || lines.Count == 0)
            {
                return emptyHint;
            }

            return string.Join('\n', lines);
        }

        /// <summary>Вызывается из <see cref="ApplySoftMenuLabelHighlight"/> после сброса всех слотов.</summary>
        private void ApplySoftMenuLabelHighlightMessageActive()
        {
            if (_messageSoftKeyCarouselIndex == 0)
            {
                if (_messageSubPage == MessageSubPage.CurrentMessage)
                {
                    SoftLblBorderSlot1.BorderThickness = new Thickness(2);
                    SoftLblBorderSlot1.BorderBrush = SoftLabelActiveBorderBrush;
                }
                else if (_messageSubPage == MessageSubPage.UserActionHistory)
                {
                    SoftLblBorderSlot2.BorderThickness = new Thickness(2);
                    SoftLblBorderSlot2.BorderBrush = SoftLabelActiveBorderBrush;
                }
            }
            else if (_messageSoftKeyCarouselIndex == 1 && _messageSubPage == MessageSubPage.SessionAlarmHistory)
            {
                SoftLblBorderSlot1.BorderThickness = new Thickness(2);
                SoftLblBorderSlot1.BorderBrush = SoftLabelActiveBorderBrush;
            }
        }

        private static string FormatMessageLogTimestamp() =>
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>Строки для HISTRY на экране MESSAGE.</summary>
        public void LogUserAction(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string line = $"{FormatMessageLogTimestamp()} {message.Trim()}";
            _userActionLines.Insert(0, line);
            while (_userActionLines.Count > MaxUserActionLines)
            {
                _userActionLines.RemoveAt(_userActionLines.Count - 1);
            }

            if (_currentPage == FanucPage.Alarm && _messageSubPage == MessageSubPage.UserActionHistory)
            {
                UpdateMessageBodyVisual();
            }
        }

        private void UpdateSoftKeyBar()
        {
            var source = GetSoftKeyItemsForCurrentPage();
            var buttons = new[] { SoftKeyButton1, SoftKeyButton2, SoftKeyButton3, SoftKeyButton4, SoftKeyButton5, SoftKeyButton6 };
            var labels = new[] { SoftKeyLabel1, SoftKeyLabel2, SoftKeyLabel3, SoftKeyLabel4, SoftKeyLabel5, SoftKeyLabel6 };
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                int itemIndex = _softKeyStartIndex + i;
                if (itemIndex >= 0 && itemIndex < source.Count)
                {
                    button.Content = string.Empty;
                    button.Tag = itemIndex;
                    button.IsEnabled = true;
                    labels[i].Text = source[itemIndex].Label;
                }
                else
                {
                    button.Content = string.Empty;
                    button.Tag = -1;
                    button.IsEnabled = false;
                    labels[i].Text = string.Empty;
                }
            }

            ApplySoftMenuLabelHighlight();
        }

        public void UpdateMachinePosition(
            double physicalX,
            double physicalY,
            double physicalZ,
            double machineZeroOffsetX,
            double machineZeroOffsetY,
            double machineZeroOffsetZ,
            double workOffsetX,
            double workOffsetY,
            double workOffsetZ)
        {
            _lastPhysicalX = physicalX;
            _lastPhysicalY = physicalY;
            _lastPhysicalZ = physicalZ;
            _machineZeroOffsetX = machineZeroOffsetX;
            _machineZeroOffsetY = machineZeroOffsetY;
            _machineZeroOffsetZ = machineZeroOffsetZ;
            _activeWorkOffsetX = workOffsetX;
            _activeWorkOffsetY = workOffsetY;
            _activeWorkOffsetZ = workOffsetZ;
            RefreshPosScreen();

            var (_, _, _, absX, absY, absZ) = ToDisplayCoordinates();
            OffsetActualPositionLineText.Text =
                $"X        {absX:+0.000;-0.000;+0.000}  Y        {absY:+0.000;-0.000;+0.000}  Z        {absZ:+0.000;-0.000;+0.000}";
        }

        private (double mcsX, double mcsY, double mcsZ, double absX, double absY, double absZ) ToDisplayCoordinates()
        {
            double mcsX = _lastPhysicalX - _machineZeroOffsetX;
            double mcsY = _lastPhysicalY - _machineZeroOffsetY;
            double mcsZ = _lastPhysicalZ - _machineZeroOffsetZ;
            double absX = mcsX - _activeWorkOffsetX;
            double absY = mcsY - _activeWorkOffsetY;
            double absZ = mcsZ - _activeWorkOffsetZ;
            return (mcsX, mcsY, mcsZ, absX, absY, absZ);
        }

        private void RefreshPosScreen()
        {
            string Format(double value) => value.ToString("+0.000;-0.000;+0.000", CultureInfo.InvariantCulture);
            var (mcsX, mcsY, mcsZ, absX, absY, absZ) = ToDisplayCoordinates();

            if (_posSubPage == PosSubPage.All)
            {
                PosSingleGrid.Visibility = Visibility.Collapsed;
                PosAllGrid.Visibility = Visibility.Visible;

                PosAllAbsXText.Text = Format(absX);
                PosAllAbsYText.Text = Format(absY);
                PosAllAbsZText.Text = Format(absZ);
                PosAllRelXText.Text = Format(absX);
                PosAllRelYText.Text = Format(absY);
                PosAllRelZText.Text = Format(absZ);
                PosAllMachXText.Text = Format(mcsX);
                PosAllMachYText.Text = Format(mcsY);
                PosAllMachZText.Text = Format(mcsZ);
                PosAllDistXText.Text = "0.000";
                PosAllDistYText.Text = "0.000";
                PosAllDistZText.Text = "0.000";
                return;
            }

            PosSingleGrid.Visibility = Visibility.Visible;
            PosAllGrid.Visibility = Visibility.Collapsed;
            PosSingleXValueText.Text = Format(absX);
            PosSingleYValueText.Text = Format(absY);
            PosSingleZValueText.Text = Format(absZ);
        }

        public void UpdateProgramLine(int? lineNumber)
        {
            _progNHeader = lineNumber.HasValue ? $"N{lineNumber.Value:00000}" : "N00000";
            RefreshProgramBannerAndHeader();
        }

        public void UpdateFeedAndSpindle(double feed, double spindle, string spindleCode)
        {
            ScreenFeedText.Text = $"FEED: F{feed:F0}";
            _lastSpindleRpm = spindle;
            RefreshProgSpindleMetricLine();
        }

        public void UpdatePosRuntime(double feed, string toolText, double spindle, string spindleCode, bool coolantOn)
        {
            ScreenFeedText.Text = $"{feed:F0} MM/MIN";
            ScreenPosToolText.Text = $"TOOL: {toolText}";
            _lastSpindleRpm = spindle;
            RefreshProgSpindleMetricLine();
        }

        public void UpdatePosTimers(TimeSpan runTime, TimeSpan cycleTime)
        {
            ScreenRunTimeText.Text = $"{(int)runTime.TotalHours}H  {runTime.Minutes}M";
            ScreenCycleTimeText.Text = $"{(int)cycleTime.TotalHours}H  {cycleTime.Minutes}M  {cycleTime.Seconds}S";
        }

        public void UpdateToolAndCoordinate(string toolText, string coordinateText)
        {
            ScreenToolText.Text = $"TOOL: {toolText}";
            ScreenCoordText.Text = $"WCS: {coordinateText}";
        }

        public void UpdateCoolantAndReference(bool coolantOn, string referenceText)
        {
            ScreenCoolantText.Text = $"COOLANT: {(coolantOn ? "ON" : "OFF")}";
            ScreenRefText.Text = $"REF: {referenceText}";
        }

        public void UpdateRunState(bool isRunning)
        {
            RefreshProgramBannerAndHeader();
        }

        public void UpdateStatusClock(DateTime localTime)
        {
            string clock = localTime.ToString("HH:mm:ss");
            ScreenStatusClockText.Text = clock;
            OffsetStatusClockText.Text = clock;
        }

        public void UpdateDiagnostics(
            string mode,
            string mdiMode,
            bool isCycleRunning,
            string interlockCode,
            string interlockDetail,
            string limitsText,
            string lastMdiCommand,
            string mdiStatus,
            bool isSingleBlockEnabled,
            bool isOptionalStopEnabled,
            bool isDryRunEnabled)
        {
            ScreenDiagnModeText.Text = "DRAW MODE";
            ScreenDiagnMdiModeText.Text = string.Equals(mode, "MEM", StringComparison.OrdinalIgnoreCase) ? "PATH" : "CHECK";
            ScreenDiagnCycleText.Text = "SCALE";
            ScreenDiagnLastMdiText.Text = isCycleRunning ? "AUTO" : "MANUAL";
            ScreenDiagnMdiStatusText.Text = "PLANE";
            ScreenDiagnInterlockCodeText.Text = "XY";
            ScreenDiagnInterlockDetailText.Text = "TRACE";
            ScreenDiagnSingleBlockText.Text = string.Equals(interlockCode, "OK", StringComparison.OrdinalIgnoreCase) ? "ON" : "OFF";
            ScreenDiagnOptionalStopText.Text = "DRY RUN";
            ScreenDiagnDryRunText.Text = isDryRunEnabled ? "ON" : "OFF";
            ScreenDiagnLimitsText.Text = limitsText;
        }

        public void UpdateOffsets(string coordinateSystem, double offsetX, double offsetY, double offsetZ)
        {
            string wcs = string.IsNullOrWhiteSpace(coordinateSystem) ? "G54" : coordinateSystem.Trim();
            if (!wcs.StartsWith('('))
            {
                wcs = $"({wcs})";
            }

            OffsetWorkActiveSystemText.Text = wcs;

            string FormatOffset(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
            OffsetWorkG54XText.Text = FormatOffset(offsetX);
            OffsetWorkG54YText.Text = FormatOffset(offsetY);
            OffsetWorkG54ZText.Text = FormatOffset(offsetZ);
        }

        public void UpdateOffsetEditor(string coordinateSystem, double offsetX, double offsetY, double offsetZ)
        {
            _suppressOffsetSelectionEvent = true;
            try
            {
                foreach (var item in OffsetSystemComboBox.Items.OfType<ComboBoxItem>())
                {
                    if (item.Content is string content &&
                        string.Equals(content, coordinateSystem, StringComparison.OrdinalIgnoreCase))
                    {
                        OffsetSystemComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _suppressOffsetSelectionEvent = false;
            }

            OffsetXTextBox.Text = offsetX.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            OffsetYTextBox.Text = offsetY.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            OffsetZTextBox.Text = offsetZ.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            _loadedOffsetX = offsetX;
            _loadedOffsetY = offsetY;
            _loadedOffsetZ = offsetZ;
            ValidateOffsetEditorInputs();
        }

        public void UpdateSettings(double workOverridePercent, double rapidOverridePercent, bool singleBlock, bool optionalStop, bool dryRun)
        {
            _feedOverridePercent = workOverridePercent;
            RefreshProgSpindleMetricLine();
        }

        public void SetNcProgramSource(IReadOnlyList<string>? sourceLines, string? sourceFilePath = null)
        {
            if (sourceLines == null || sourceLines.Count == 0)
            {
                _progNcFocusLineIndex = -1;
                _progNcCaretColumn = 0;
                _hasNcProgramLoaded = false;
                _progOHeader = "00001";
                _progCachedNcLines = null;
                _ncProgramSourcePath = null;
                ProgGCodeLineHost.Children.Clear();
                ProgGCodeLineHost.Visibility = Visibility.Collapsed;
                ScreenProgNoProgramText.Visibility = Visibility.Visible;
            }
            else
            {
                _hasNcProgramLoaded = true;
                _progCachedNcLines = new List<string>(sourceLines.Count);
                foreach (var line in sourceLines)
                {
                    _progCachedNcLines.Add(line ?? string.Empty);
                }

                _ncProgramSourcePath = string.IsNullOrWhiteSpace(sourceFilePath)
                    ? null
                    : Path.GetFullPath(sourceFilePath);

                ScreenProgNoProgramText.Visibility = Visibility.Collapsed;
                _progOHeader = ExtractOProgramNumber5(string.Join('\n', sourceLines));
                RebuildNcProgramLineVisuals();
            }

            RefreshProgramBannerAndHeader();
            if (_currentPage == FanucPage.Prog)
            {
                UpdateProgScreen();
            }
        }

        public void UpdateSystemPage(string units, string coordinateMode, string plane, string motion, string activeWcs, string compensation)
        {
            ScreenSystemUnitsText.Text = "UNITS";
            ScreenSystemUnitsDataText.Text = units;
            ScreenSystemCoordModeText.Text = "COORD MODE";
            ScreenSystemCoordModeDataText.Text = coordinateMode;
            ScreenSystemPlaneText.Text = "PLANE";
            ScreenSystemPlaneDataText.Text = plane;
            ScreenSystemMotionText.Text = "MOTION";
            ScreenSystemMotionDataText.Text = motion;
            ScreenSystemWcsText.Text = "ACTIVE WCS";
            ScreenSystemWcsDataText.Text = activeWcs;
            ScreenSystemCompText.Text = "COMPENSATION";
            ScreenSystemCompDataText.Text = compensation;
        }

        public void UpdateMode(string mode)
        {
            SetModeSelection(mode);
            _modeStatusAbbrev = AbbreviateControllerMode(mode);
            ScreenEditStatusText.Text = $"{_modeStatusAbbrev}**** *** ***";
            RefreshOffsetStatusFooter();
            ApplyScreenHeaderForPage();
        }

        public void SetAlarm(string alarmText, bool isError)
        {
            _currentAlarmText = string.IsNullOrEmpty(alarmText) ? "NONE" : alarmText;
            _currentAlarmIsError = isError;

            bool isSignificantAlarm = isError
                && !string.Equals(_currentAlarmText, "NONE", StringComparison.OrdinalIgnoreCase);

            if (isSignificantAlarm)
            {
                string line = $"{FormatMessageLogTimestamp()} {_currentAlarmText}";
                _sessionAlarmLines.Insert(0, line);
                while (_sessionAlarmLines.Count > MaxSessionAlarmLines)
                {
                    _sessionAlarmLines.RemoveAt(_sessionAlarmLines.Count - 1);
                }
            }

            if (_currentPage == FanucPage.Alarm
                && (_messageSubPage == MessageSubPage.CurrentMessage
                    || (_messageSubPage == MessageSubPage.SessionAlarmHistory && isSignificantAlarm)))
            {
                UpdateMessageBodyVisual();
            }
        }

        private void CycleStart_Click(object sender, RoutedEventArgs e)
        {
            CycleStartRequested?.Invoke();
        }

        private void FeedHold_Click(object sender, RoutedEventArgs e)
        {
            FeedHoldRequested?.Invoke();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetRequested?.Invoke();
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressModeChangedEvent)
            {
                return;
            }

            if (ModeComboBox.SelectedItem is ComboBoxItem item && item.Content is string modeText)
            {
                ModeChangedRequested?.Invoke(modeText);
            }
        }

        private void SetModeSelection(string mode)
        {
            _suppressModeChangedEvent = true;
            try
            {
                string normalizedMode = mode.Equals("ZeroReturn", StringComparison.OrdinalIgnoreCase)
                    ? "ZERO_RETURN"
                    : mode.ToUpperInvariant();

                foreach (var item in ModeComboBox.Items.OfType<ComboBoxItem>())
                {
                    if (item.Content is string content &&
                        string.Equals(content, normalizedMode, StringComparison.OrdinalIgnoreCase))
                    {
                        ModeComboBox.SelectedItem = item;
                        return;
                    }
                }
            }
            finally
            {
                _suppressModeChangedEvent = false;
            }
        }

        private void PosPage_Click(object sender, RoutedEventArgs e)
        {
            SetPage(FanucPage.Pos);
        }

        private void ProgPage_Click(object sender, RoutedEventArgs e)
        {
            SetPage(FanucPage.Prog);
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                () =>
                {
                    if (_currentPage == FanucPage.Prog)
                    {
                        MdiInputTextBox.Focus();
                    }
                });
        }

        private void AlarmPage_Click(object sender, RoutedEventArgs e)
        {
            LogUserAction("Открыто окно MESSAGE");
            SetPage(FanucPage.Alarm);
        }

        private void DiagnPage_Click(object sender, RoutedEventArgs e)
        {
            SetPage(FanucPage.CustomGrph);
        }

        private void OffsetPage_Click(object sender, RoutedEventArgs e)
        {
            _offsetSubPage = OffsetSubPage.ToolOffset;
            _offsetOprtMenuActive = false;
            SetPage(FanucPage.Offset);
        }

        private void SetOffsetSubPage(OffsetSubPage subPage)
        {
            _offsetOprtMenuActive = false;
            _offsetSubPage = subPage;
            _offsetInputBuffer = string.Empty;
            RefreshOffsetInputLine();
            LogUserAction(subPage switch
            {
                OffsetSubPage.ToolOffset => "OFFSET → OFFSET",
                OffsetSubPage.Setting => "OFFSET → SETTING",
                OffsetSubPage.Work => "OFFSET → WORK",
                _ => "OFFSET"
            });
            UpdateOffsetScreen();
            UpdateSoftKeyBar();
        }

        private void EnterOffsetOprtMenu()
        {
            _offsetOprtMenuActive = true;
            LogUserAction("OFFSET → (OPRT)");
            UpdateSoftKeyBar();
        }

        private void ExitOffsetOprtMenu()
        {
            _offsetOprtMenuActive = false;
            LogUserAction("OFFSET ← (OPRT)");
            UpdateSoftKeyBar();
        }

        private void OffsetToolApplyInputToSelectedCell(bool add)
        {
            if (_offsetSubPage != OffsetSubPage.ToolOffset)
            {
                return;
            }

            string s = (_offsetInputBuffer ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(s))
            {
                return;
            }

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                SetAlarm("OFFSET VALUE ERROR", true);
                return;
            }

            int row = _offsetToolSelectedRow;
            int col = _offsetToolSelectedCol;
            if (row < 1 || row > OffsetToolTotalRows || col < 1 || col > 4)
            {
                return;
            }

            double next = add ? _offsetToolValues[row, col] + value : value;
            _offsetToolValues[row, col] = next;
            RefreshOffsetToolPage();
            ApplyOffsetToolSelectionVisual();

            OffsetToolValueUpdateRequested?.Invoke(
                row,
                (OffsetToolColumn)col,
                next,
                add);
        }

        private List<(string Label, Action Action)> GetOffsetSoftKeyItems()
        {
            if (_offsetOprtMenuActive)
            {
                string measureLabel = _offsetSubPage == OffsetSubPage.Work ? "MEASURE" : "C.INPUT";

                return
                [
                    ("<", ExitOffsetOprtMenu),
                    ("No.SRH", () => LogUserAction("OFFSET OPRT No.SRH")),
                    (measureLabel, () => LogUserAction($"OFFSET OPRT {measureLabel}")),
                    ("+INPUT", () =>
                    {
                        LogUserAction("OFFSET OPRT +INPUT");
                        OffsetToolApplyInputToSelectedCell(add: true);
                        _offsetInputBuffer = string.Empty;
                        RefreshOffsetInputLine();
                    }),
                    ("INPUT", () =>
                    {
                        LogUserAction("OFFSET OPRT INPUT");
                        OffsetToolApplyInputToSelectedCell(add: false);
                        _offsetInputBuffer = string.Empty;
                        RefreshOffsetInputLine();
                    }),
                    ("", () => { })
                ];
            }

            return
            [
                ("OFFSET", () => SetOffsetSubPage(OffsetSubPage.ToolOffset)),
                ("SETTING", () => SetOffsetSubPage(OffsetSubPage.Setting)),
                ("WORK", () => SetOffsetSubPage(OffsetSubPage.Work)),
                ("", () => { }),
                ("(OPRT)", EnterOffsetOprtMenu),
                ("", () => { })
            ];
        }

        private void UpdateOffsetScreen()
        {
            OffsetToolSubPanel.Visibility = _offsetSubPage == OffsetSubPage.ToolOffset
                ? Visibility.Visible
                : Visibility.Collapsed;
            OffsetSettingSubPanel.Visibility = _offsetSubPage == OffsetSubPage.Setting
                ? Visibility.Visible
                : Visibility.Collapsed;
            OffsetWorkSubPanel.Visibility = _offsetSubPage == OffsetSubPage.Work
                ? Visibility.Visible
                : Visibility.Collapsed;
            SnapOffsetToolPageToSelection();
            RefreshOffsetToolPage();
            ApplyOffsetToolSelectionVisual();
            ApplyOffsetWorkSelectionVisual();
            RefreshOffsetInputLine();
            ApplyScreenHeaderForPage();
            Dispatcher.BeginInvoke(() => Keyboard.Focus(this), DispatcherPriority.Input);
        }

        private void SystemPage_Click(object sender, RoutedEventArgs e)
        {
            SetPage(FanucPage.System);
        }

        private void SoftkeyPrev_Click(object sender, RoutedEventArgs e)
        {
            if (TryProgMdiNavigateSoftkeyPage(-1))
            {
                return;
            }

            if (TryMessageSoftkeyCarousel(-1))
            {
                return;
            }

            // Per PDF: physical arrows only page softkey menus, never switch modes.
            if (_currentPage == FanucPage.Prog && _progOprtMenuActive)
            {
                // Exit OPRT by physical ◀ as a convenience (soft "<" also exists).
                _progOprtMenuActive = false;
                _progOprtMenuPageIndex = 0;
                UpdateSoftKeyBar();
                return;
            }

            if (_currentPage == FanucPage.Pos && _posOprtMenuActive)
            {
                _posOprtMenuActive = false;
                UpdateSoftKeyBar();
                return;
            }

            // Otherwise no-op (no mode cycling).
        }

        private void SoftkeyNext_Click(object sender, RoutedEventArgs e)
        {
            if (TryProgMdiNavigateSoftkeyPage(1))
            {
                return;
            }

            if (TryMessageSoftkeyCarousel(1))
            {
                return;
            }

            if (_currentPage == FanucPage.Prog && _progOprtMenuActive)
            {
                _progOprtMenuPageIndex = (_progOprtMenuPageIndex + 1) % 3;
                UpdateSoftKeyBar();
                return;
            }

            SoftkeyNextAdvanceSlideWindowOnly();
        }

        /// <summary>Сдвиг «окна» из 6 клавиш, если пунктов меню больше шести (другие экраны).</summary>
        private void SoftkeyNextAdvanceSlideWindowOnly()
        {
            var source = GetSoftKeyItemsForCurrentPage();
            if (source.Count == 0)
            {
                return;
            }

            if (source.Count <= 6)
            {
                _softKeyStartIndex = 0;
            }
            else
            {
                _softKeyStartIndex = (_softKeyStartIndex + 1) % source.Count;
            }

            UpdateSoftKeyBar();
        }

        // NavigateSoftkeyPrev/Next were used to cycle pages in an earlier prototype.
        // Per reference PDF we must not change modes from physical softkey arrows.
        public void NavigateSoftkeyPrev() => SoftkeyPrev_Click(this, new RoutedEventArgs());

        public void NavigateSoftkeyNext() => SoftkeyNext_Click(this, new RoutedEventArgs());

        private void SoftKeyDynamic_Click(object sender, RoutedEventArgs e)
        {
            var source = GetSoftKeyItemsForCurrentPage();
            if (sender is not Button button || button.Tag is not int itemIndex)
            {
                return;
            }

            if (itemIndex < 0 || itemIndex >= source.Count)
            {
                return;
            }

            source[itemIndex].Action();
        }

        private List<(string Label, Action Action)> GetSoftKeyItemsForCurrentPage()
        {
            if (_currentPage == FanucPage.Alarm && _messageSoftKeyPages.Length > 0)
            {
                return _messageSoftKeyPages[_messageSoftKeyCarouselIndex % _messageSoftKeyPages.Length];
            }

            if (_currentPage == FanucPage.Offset)
            {
                return GetOffsetSoftKeyItems();
            }

            if (_currentPage == FanucPage.Pos && _posOprtMenuActive)
            {
                Action exit = () =>
                {
                    _posOprtMenuActive = false;
                    UpdateSoftKeyBar();
                };

                // Per reference screenshots: POS OPRT differs by subpage.
                if (_posSubPage == PosSubPage.Rel)
                {
                    return
                    [
                        ("<", exit),
                        ("PRESET", () => LogUserAction("POS OPRT PRESET")),
                        ("ORIGIN", () => LogUserAction("POS OPRT ORIGIN")),
                        ("", () => { }),
                        ("COM:0", () => LogUserAction("POS OPRT COM:0")),
                        ("RUN:0", () => LogUserAction("POS OPRT RUN:0")),
                    ];
                }

                return
                [
                    ("<", exit),
                    ("", () => { }),
                    ("", () => { }),
                    ("PTSPRE", () => LogUserAction("POS OPRT PTSPRE")),
                    ("RUNPRE", () => LogUserAction("POS OPRT RUNPRE")),
                    ("", () => { }),
                ];
            }

            if (_currentPage == FanucPage.Prog && _progOprtMenuActive)
            {
                Action exit = () =>
                {
                    _progOprtMenuActive = false;
                    _progOprtMenuPageIndex = 0;
                    LogUserAction("PROG ← (OPRT)");
                    UpdateSoftKeyBar();
                };

                Action nextPage = () =>
                {
                    _progOprtMenuPageIndex = (_progOprtMenuPageIndex + 1) % 3;
                    UpdateSoftKeyBar();
                };

                return _progOprtMenuPageIndex switch
                {
                    0 =>
                    [
                        ("<", exit),
                        ("BG-EDT", () => InvokeBackgroundEditOpenLoadedProgram()),
                        ("O.SRH", () => InvokeOpenProgramByMdiONameIfExists()),
                        ("SRH↓", () => SearchInOpenedProgramFromMdi(searchDown: true)),
                        ("SRH↑", () => SearchInOpenedProgramFromMdi(searchDown: false)),
                        ("→", nextPage),
                    ],
                    1 =>
                    [
                        ("<", exit),
                        ("REWIND", () => RewindNcProgramToStart()),
                        ("F.SRH", () => SetAlarm("FILE SEARCH", false)),
                        ("READ", () => SetAlarm("READ", false)),
                        ("PUNCH", () => LogUserAction("PROG OPRT PUNCH")),
                        ("→", nextPage),
                    ],
                    _ =>
                    [
                        ("<", exit),
                        ("DELETE", () => InvokeDeleteProgramByMdiOName()),
                        ("INSERT", () => LogUserAction("PROG OPRT INSERT")),
                        ("COPY", () => LogUserAction("PROG OPRT COPY")),
                        ("EX-EDT", () => SetAlarm("EXTENDED EDIT", false)),
                        ("→", nextPage),
                    ]
                };
            }

            return _softKeyByPage.TryGetValue(_currentPage, out var list)
                ? list
                : [];
        }

        private void SetPage(FanucPage page)
        {
            if (_currentPage == FanucPage.Prog && page != FanucPage.Prog)
            {
                _mdiProgSoftkeysContextActive = false;
                _progMdiSoftKeyPageIndex = 0;
            }

            _currentPage = page;
            _softKeyStartIndex = 0;
            if (page == FanucPage.Prog)
            {
                _currentProgSubPage = ProgSubPage.ProgramMain;
                _progOprtMenuActive = false;
                _progOprtMenuPageIndex = 0;
            }

            if (page == FanucPage.Alarm)
            {
                _messageSubPage = MessageSubPage.CurrentMessage;
                _messageSoftKeyCarouselIndex = 0;
            }

            if (page == FanucPage.Offset)
            {
                _offsetSubPage = OffsetSubPage.ToolOffset;
                _offsetOprtMenuActive = false;
                _offsetInputBuffer = string.Empty;
                RefreshOffsetInputLine();
                Dispatcher.BeginInvoke(() => Keyboard.Focus(this), DispatcherPriority.Input);
            }

            if (page == FanucPage.Pos)
            {
                _posOprtMenuActive = false;
            }

            UpdatePageVisibility();
            UpdateSoftKeyBar();
            if (page == FanucPage.Prog)
            {
                UpdateProgScreen();
            }
            else if (page == FanucPage.Alarm)
            {
                RefreshMessageScreenFull();
            }
        }

        private void UpdateProgScreen()
        {
            switch (_currentProgSubPage)
            {
                case ProgSubPage.ProgramMain:
                    ProgMainScroll.Visibility = Visibility.Visible;
                    ProgDirectoryScroll.Visibility = Visibility.Collapsed;
                    ScreenProgFgTopStripeText.Text = "(FG:EDIT)";
                    EnsureNcLinesPanelMatchesCache();
                    RefreshProgramBannerAndHeader();
                    break;
                case ProgSubPage.ProgramDirectory:
                    ProgMainScroll.Visibility = Visibility.Collapsed;
                    ProgDirectoryScroll.Visibility = Visibility.Visible;
                    ScreenProgFgTopStripeText.Text = "DIR";
                    RebuildProgramDirectoryTable();
                    break;
            }

            RefreshProgStripeOIContext();
            RefreshProgMdiTypingSoftkeys();
        }

        private void UpdatePageVisibility()
        {
            bool isPos = _currentPage == FanucPage.Pos;
            bool isProg = _currentPage == FanucPage.Prog;
            bool isOffset = _currentPage == FanucPage.Offset;

            PosPagePanel.Visibility = isPos ? Visibility.Visible : Visibility.Collapsed;
            PosCoordinateBorder.Visibility = isPos ? Visibility.Visible : Visibility.Collapsed;
            PosMetricsGrid.Visibility = isPos ? Visibility.Visible : Visibility.Collapsed;
            ProgPageRoot.Visibility = isProg ? Visibility.Visible : Visibility.Collapsed;
            ScreenDefaultStatusPanel.Visibility = isOffset ? Visibility.Collapsed : Visibility.Visible;
            ScreenOffsetStatusPanel.Visibility = isOffset ? Visibility.Visible : Visibility.Collapsed;
            if (isPos)
            {
                RefreshPosScreen();
            }

            AlarmPagePanel.Visibility = _currentPage == FanucPage.Alarm ? Visibility.Visible : Visibility.Collapsed;
            DiagnPagePanel.Visibility = _currentPage == FanucPage.CustomGrph ? Visibility.Visible : Visibility.Collapsed;
            OffsetPageRoot.Visibility = _currentPage == FanucPage.Offset ? Visibility.Visible : Visibility.Collapsed;
            SystemPagePanel.Visibility = _currentPage == FanucPage.System ? Visibility.Visible : Visibility.Collapsed;
            if (_currentPage == FanucPage.Offset)
            {
                UpdateOffsetScreen();
                RefreshOffsetStatusFooter();
            }
            if (MdiInputRowPanel != null)
            {
                MdiInputRowPanel.Visibility = _currentPage is FanucPage.Pos or FanucPage.Offset
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            ApplyScreenHeaderForPage();
            RefreshProgramBannerAndHeader();
        }

        private void ApplyScreenHeaderForPage()
        {
            switch (_currentPage)
            {
                case FanucPage.Pos:
                    ScreenModeText.Text = _posSubPage switch
                    {
                        PosSubPage.Rel => "ACTUAL POSITION(RELATIVE)",
                        PosSubPage.All => "ACTUAL POSITION(ALL)",
                        _ => "ACTUAL POSITION(ABSOLUTE)"
                    };
                    break;
                case FanucPage.Prog:
                    ScreenModeText.Text = _currentProgSubPage == ProgSubPage.ProgramDirectory
                        ? "PROGRAM DIRECTORY"
                        : "PROGRAM";
                    break;
                case FanucPage.Offset:
                    ScreenModeText.Text = _offsetSubPage switch
                    {
                        OffsetSubPage.ToolOffset => "OFFSET",
                        OffsetSubPage.Setting => "SETTING (HANDY)",
                        OffsetSubPage.Work => "WORK",
                        _ => "OFFSET"
                    };
                    break;
                case FanucPage.Alarm:
                    UpdateMessageHeaderTitle();
                    break;
                case FanucPage.CustomGrph:
                    ScreenModeText.Text = "GRAPHIC";
                    break;
                case FanucPage.System:
                    ScreenModeText.Text = "PARAMETER";
                    break;
                default:
                    ScreenModeText.Text = "PROGRAM";
                    break;
            }
        }

        private void RefreshProgramBannerAndHeader()
        {
            ScreenRunStateText.Text = FormatTopStripProgramBanner();
            RefreshProgStripeOIContext();
        }

        /// <summary>Левая надпись Oxxxx на бирюзовой полосе только при загруженной программе на экране PRGRM.</summary>
        private void RefreshProgStripeOIContext()
        {
            bool progMainUi =
                _currentPage == FanucPage.Prog
                && _currentProgSubPage == ProgSubPage.ProgramMain;

            if (!progMainUi)
            {
                ScreenProgStripeOLeft.Visibility = Visibility.Collapsed;
                return;
            }

            ScreenProgStripeOLeft.Visibility = _hasNcProgramLoaded
                ? Visibility.Visible
                : Visibility.Collapsed;

            ScreenProgStripeOLeft.Text = FormatProgOOnlyDisplay(_progOHeader);
            ScreenProgNoProgramText.Visibility = !_hasNcProgramLoaded
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void EnsureNcLinesPanelMatchesCache()
        {
            if (!_hasNcProgramLoaded || _progCachedNcLines == null || _progCachedNcLines.Count == 0)
            {
                _progNcFocusLineIndex = -1;
                _progNcCaretColumn = 0;
                ProgGCodeLineHost.Children.Clear();
                ProgGCodeLineHost.Visibility = Visibility.Collapsed;
                return;
            }

            if (ProgGCodeLineHost.Children.Count == _progCachedNcLines.Count)
            {
                ProgGCodeLineHost.Visibility = Visibility.Visible;
                SyncNcProgFocusIndicesToCache();
                for (int i = 0; i < _progCachedNcLines.Count; i++)
                {
                    RefreshNcLineVisual(i);
                }

                if (_progNcFocusLineIndex >= 0
                    && _progNcFocusLineIndex < ProgGCodeLineHost.Children.Count
                    && ProgGCodeLineHost.Children[_progNcFocusLineIndex] is FrameworkElement focusLine)
                {
                    focusLine.BringIntoView();
                }

                return;
            }

            RebuildNcProgramLineVisuals();
        }

        private void RebuildNcProgramLineVisuals()
        {
            ProgGCodeLineHost.Children.Clear();
            _progNcFocusLineIndex = -1;
            _progNcCaretColumn = 0;

            if (!_hasNcProgramLoaded || _progCachedNcLines == null)
            {
                ProgGCodeLineHost.Visibility = Visibility.Collapsed;
                return;
            }

            ProgGCodeLineHost.Visibility = Visibility.Visible;

            for (int i = 0; i < _progCachedNcLines.Count; i++)
            {
                var lineBorder = CreateNcProgramLineBorder(_progCachedNcLines[i]);
                ProgGCodeLineHost.Children.Add(lineBorder);
            }

            if (_progCachedNcLines.Count > 0)
            {
                SetNcProgramCaret(0, 0);
            }
        }

        private Border CreateNcProgramLineBorder(string lineText)
        {
            var tb = new TextBlock
            {
                Text = lineText,
                FontFamily = FanucNcMonospaceFont,
                FontSize = 13.5,
                FontWeight = FontWeights.Normal,
                Foreground = Brushes.Black,
                LineHeight = 19,
                TextWrapping = TextWrapping.NoWrap
            };
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);

            return new Border
            {
                Padding = new Thickness(8, 0, 4, 1),
                Background = Brushes.Transparent,
                Child = tb
            };
        }

        private bool IsProgNcNavigationActive()
        {
            return _currentPage == FanucPage.Prog
                && _currentProgSubPage == ProgSubPage.ProgramMain
                && _hasNcProgramLoaded
                && _progCachedNcLines is { Count: > 0 };
        }

        public bool IsMdiLineKeyboardFocused() =>
            MdiInputTextBox.IsKeyboardFocused || MdiInputTextBox.IsKeyboardFocusWithin;

        /// <summary>Каталог, из которого строится DIR и куда сохраняются новые программы.</summary>
        public string ResolveNcProgramsDirectory()
        {
            return ProgramCatalogService.ResolveProgramsDirectory(_ncProgramSourcePath);
        }

        /// <summary>Найти файл программы по имени O0999 и т.п. (расширения .nc, .ngc, .tap).</summary>
        public bool TryResolveNcProgramFile(string normalizedOLine, out string? fullPath)
        {
            return ProgramCatalogService.TryResolveProgramFile(_ncProgramSourcePath, normalizedOLine, out fullPath);
        }

        /// <summary>PROG DIR: постраничное перелистывание списка (используется клавишами PAGE и кнопками).</summary>
        public bool TryNavigateProgramDirectoryPage(int pageDelta)
        {
            if (_currentPage != FanucPage.Prog || _currentProgSubPage != ProgSubPage.ProgramDirectory)
            {
                return false;
            }

            _directoryDataCache = BuildProgramDirectoryDataOnly();
            int pageCount = GetDirectoryPageCount();
            pageCount = Math.Max(1, pageCount);

            _directoryProgramListPageIndex =
                (_directoryProgramListPageIndex + pageDelta + pageCount) % pageCount;

            LogUserAction($"DIR: страница {_directoryProgramListPageIndex + 1}/{pageCount}");
            RenderDirectoryPage();
            return true;
        }

        /// <summary>Перечитать каталог программ после удаления файла на диске.</summary>
        public void RefreshDirectoryListingFromDisk()
        {
            if (_currentPage != FanucPage.Prog || _currentProgSubPage != ProgSubPage.ProgramDirectory)
            {
                return;
            }

            _directoryDataCache = BuildProgramDirectoryDataOnly();
            int pageCount = Math.Max(1, GetDirectoryPageCount());
            _directoryProgramListPageIndex = Math.Clamp(_directoryProgramListPageIndex, 0, pageCount - 1);
            RenderDirectoryPage();
        }

        /// <summary>После успешного создания программы из DIR — очистить буфер ввода.</summary>
        public void ClearMdiInputBuffer()
        {
            MdiInputTextBox.Text = string.Empty;
            MdiInputTextBox.CaretIndex = 0;
            RefreshProgMdiTypingSoftkeys();
        }

        public void NavigateToProgMainScreen()
        {
            if (_currentPage != FanucPage.Prog)
            {
                return;
            }

            _currentProgSubPage = ProgSubPage.ProgramMain;
            UpdateProgScreen();
        }

        private static bool IsProgDirectoryPageActive(FanucPage page, ProgSubPage progSubPage) =>
            page == FanucPage.Prog && progSubPage == ProgSubPage.ProgramDirectory;

        /// <summary>Строка должна описывать только имя программы O№ без посторонних слов.</summary>
        private static bool TryParseDirectoryNewProgramToken(string mdi, out string normalizedOLine)
        {
            normalizedOLine = string.Empty;
            if (string.IsNullOrWhiteSpace(mdi))
            {
                return false;
            }

            string token = mdi.Trim();
            foreach (char c in token)
            {
                if (char.IsWhiteSpace(c))
                {
                    return false;
                }
            }

            if (token.EndsWith(".nc", StringComparison.OrdinalIgnoreCase))
            {
                token = token[..^3];
                if (token.Length == 0)
                {
                    return false;
                }
            }

            if (token.StartsWith('o') || token.StartsWith('O'))
            {
                token = token[1..];
            }

            token = token.Trim();
            if (token.Length < 1 || token.Length > 8)
            {
                return false;
            }

            for (int i = 0; i < token.Length; i++)
            {
                if (!char.IsAsciiDigit(token[i]))
                {
                    return false;
                }
            }

            normalizedOLine = "O" + token;
            return true;
        }

        public bool TryHandleNcProgramKeys(Key key)
        {
            if (IsMdiLineKeyboardFocused())
            {
                return false;
            }

            if (!IsProgNcNavigationActive())
            {
                return false;
            }

            var mods = Keyboard.Modifiers;
            if ((mods & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
            {
                return false;
            }

            return key switch
            {
                Key.Up => MoveNcProgramLineDelta(-1),
                Key.Down => MoveNcProgramLineDelta(1),
                Key.Left => MoveNcProgramColumnDelta(-1),
                Key.Right => MoveNcProgramColumnDelta(1),
                Key.PageUp => MoveNcProgramLineDelta(-EstimateNcPageLineCount()),
                Key.PageDown => MoveNcProgramLineDelta(EstimateNcPageLineCount()),
                _ => false
            };
        }

        private int EstimateNcPageLineCount()
        {
            double vh = ProgMainScroll.ViewportHeight;
            if (vh < 1 || double.IsNaN(vh) || double.IsInfinity(vh))
            {
                return 12;
            }

            const double lineApprox = 21;
            return Math.Max(1, (int)Math.Floor(vh / lineApprox));
        }

        private void SyncNcProgFocusIndicesToCache()
        {
            if (_progCachedNcLines == null || _progCachedNcLines.Count == 0)
            {
                _progNcFocusLineIndex = -1;
                _progNcCaretColumn = 0;
                return;
            }

            int n = _progCachedNcLines.Count;
            if (_progNcFocusLineIndex < 0)
            {
                _progNcFocusLineIndex = 0;
            }
            else if (_progNcFocusLineIndex >= n)
            {
                _progNcFocusLineIndex = n - 1;
            }

            _progNcCaretColumn = Math.Clamp(
                _progNcCaretColumn,
                0,
                _progCachedNcLines[_progNcFocusLineIndex].Length);
        }

        private void SetNcProgramCaret(int targetLineIndex, int? caretColumn)
        {
            if (_progCachedNcLines == null
                || ProgGCodeLineHost.Children.Count != _progCachedNcLines.Count)
            {
                return;
            }

            int n = _progCachedNcLines.Count;
            if (n == 0)
            {
                return;
            }

            int nf = Math.Clamp(targetLineIndex, 0, n - 1);
            int prev = _progNcFocusLineIndex;

            if (caretColumn.HasValue)
            {
                _progNcCaretColumn = caretColumn.Value;
            }
            else if (nf != prev)
            {
                _progNcCaretColumn = Math.Min(_progNcCaretColumn, _progCachedNcLines[nf].Length);
            }

            _progNcCaretColumn = Math.Clamp(_progNcCaretColumn, 0, _progCachedNcLines[nf].Length);
            _progNcFocusLineIndex = nf;

            if (prev >= 0 && prev != nf && prev < ProgGCodeLineHost.Children.Count)
            {
                RefreshNcLineVisual(prev);
            }

            RefreshNcLineVisual(nf);

            if (nf < ProgGCodeLineHost.Children.Count
                && ProgGCodeLineHost.Children[nf] is FrameworkElement focusEl)
            {
                focusEl.BringIntoView();
            }
        }

        private bool MoveNcProgramLineDelta(int delta)
        {
            if (!IsProgNcNavigationActive())
            {
                return false;
            }

            var lines = _progCachedNcLines!;
            int n = lines.Count;
            int cur = _progNcFocusLineIndex < 0 ? 0 : _progNcFocusLineIndex;
            int nf = Math.Clamp(cur + delta, 0, n - 1);
            SetNcProgramCaret(nf, null);
            return true;
        }

        private bool MoveNcProgramColumnDelta(int delta)
        {
            if (!IsProgNcNavigationActive())
            {
                return false;
            }

            var lines = _progCachedNcLines!;
            if (_progNcFocusLineIndex < 0)
            {
                SetNcProgramCaret(0, 0);
            }

            int line = _progNcFocusLineIndex;
            string txt = lines[line];
            int nc = Math.Clamp(_progNcCaretColumn + delta, 0, txt.Length);
            if (nc == _progNcCaretColumn)
            {
                return true;
            }

            _progNcCaretColumn = nc;
            RefreshNcLineVisual(line);
            return true;
        }

        private void RefreshNcLineVisual(int lineIndex)
        {
            if (_progCachedNcLines == null
                || lineIndex < 0
                || lineIndex >= _progCachedNcLines.Count
                || lineIndex >= ProgGCodeLineHost.Children.Count)
            {
                return;
            }

            if (ProgGCodeLineHost.Children[lineIndex] is not Border border || border.Child is not TextBlock tb)
            {
                return;
            }

            string raw = _progCachedNcLines[lineIndex];
            bool isFocus = _progNcFocusLineIndex == lineIndex;

            if (!isFocus)
            {
                tb.Inlines.Clear();
                tb.Text = raw;
                border.Background = Brushes.Transparent;
                return;
            }

            border.Background = Brushes.Transparent;
            tb.Text = string.Empty;
            tb.Inlines.Clear();

            int col = Math.Clamp(_progNcCaretColumn, 0, raw.Length);
            string before = raw[..col];
            string atGlyph = col < raw.Length ? raw[col].ToString() : "\u00a0";
            string after = col < raw.Length ? raw[(col + 1)..] : string.Empty;

            tb.Inlines.Add(new Run(before));
            tb.Inlines.Add(new Run(atGlyph)
            {
                FontWeight = FontWeights.Bold,
                Background = ProgNcCaretCharHighlight,
                Foreground = Brushes.Black
            });
            tb.Inlines.Add(new Run(after));
        }

        private static string FormatProgOOnlyDisplay(string oDigitsPaddedStorage)
        {
            if (!int.TryParse(oDigitsPaddedStorage, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oNum))
            {
                return "O0001";
            }

            return $"O{oNum:D4}";
        }

        /// <summary>Правый блок верхней бирюзовой полосы: всегда как на PROG — «Oxxxx Nxxxxx».</summary>
        private string FormatTopStripProgramBanner()
        {
            return FormatFanucProgIdWithO(_progOHeader, _progNHeader);
        }

        /// <returns>Строка вида «O0001 N00012».</returns>
        private static string FormatFanucProgIdWithO(string oDigitsPaddedStorage, string nToken)
        {
            if (!int.TryParse(oDigitsPaddedStorage, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oNum))
            {
                return $"O0001 {nToken}";
            }

            return $"O{oNum:D4} {nToken}";
        }

        private static string AbbreviateControllerMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return "MEM";
            }

            string u = mode.Trim().ToUpperInvariant();
            return u switch
            {
                "EDIT" => "EDIT",
                "MEM" => "MEM",
                "MDI" => "MDI",
                "JOG" => "JOG",
                "HANDLE" => "HNDL",
                "ZERO_RETURN" => "ZRN",
                _ => u.Length <= 4 ? u : u[..4]
            };
        }

        private static string ExtractOProgramNumber5(string nc)
        {
            if (string.IsNullOrWhiteSpace(nc))
            {
                return "00001";
            }

            Match m = Regex.Match(nc, @"(?m)^\s*O(\d+)", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                return "00001";
            }

            string digits = m.Groups[1].Value;
            const int Pad = 5;
            if (digits.Length <= Pad)
            {
                return digits.PadLeft(Pad, '0');
            }

            return digits.Substring(digits.Length - Pad, Pad);
        }

        private void RebuildProgramDirectoryTable()
        {
            _directoryProgramListPageIndex = 0;
            _directoryDataCache = BuildProgramDirectoryDataOnly();
            RenderDirectoryPage();
        }

        private int GetDirectoryPageCount()
        {
            int n = _directoryDataCache.Count;
            if (n == 0)
            {
                return 1;
            }

            return (int)Math.Ceiling((double)n / DirectoryDataRowsPerPage);
        }

        private void RenderDirectoryPage()
        {
            ProgDirectoryRowsPanel.Children.Clear();
            ProgDirectoryRowsPanel.Children.Add(CreateProgramDirectoryGridRow("NO.", "NAME", "BYTES", "DATA", isHeaderRow: true));

            int total = _directoryDataCache.Count;
            if (total == 0)
            {
                ProgDirectoryRowsPanel.Children.Add(CreateProgramDirectoryGridRow("0000", "—", "—", "—", isHeaderRow: false));
                return;
            }

            int pageCount = Math.Max(1, GetDirectoryPageCount());
            _directoryProgramListPageIndex = Math.Clamp(_directoryProgramListPageIndex, 0, pageCount - 1);
            int skip = _directoryProgramListPageIndex * DirectoryDataRowsPerPage;
            int take = Math.Min(DirectoryDataRowsPerPage, total - skip);

            for (int i = 0; i < take; i++)
            {
                var (a, b, c, d) = _directoryDataCache[skip + i];
                ProgDirectoryRowsPanel.Children.Add(CreateProgramDirectoryGridRow(a, b, c, d, isHeaderRow: false));
            }
        }

        /// <summary>Только данные строк каталога (без строки заголовка «NO.|NAME…»).</summary>
        private List<(string a, string b, string c, string d)> BuildProgramDirectoryDataOnly()
        {
            var rows = new List<(string, string, string, string)>();

            if (string.IsNullOrEmpty(_ncProgramSourcePath))
            {
                if (!_hasNcProgramLoaded)
                {
                    rows.Add(("0000", "—", "—", "—"));
                    return rows;
                }

                string oMem = $"O{(int.TryParse(_progOHeader, out int om) ? om : 1):D4}";
                rows.Add(("0001", oMem, "—", "—"));
                return rows;
            }

            try
            {
                string anchor = _ncProgramSourcePath;
                string? dir = Path.GetDirectoryName(anchor);
                IOrderedEnumerable<string> orderedFiles;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string pattern in new[] { "*.nc", "*.ngc", "*.tap" })
                    {
                        foreach (string f in Directory.EnumerateFiles(dir, pattern))
                        {
                            set.Add(f);
                        }
                    }

                    orderedFiles = set.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    orderedFiles = new[] { anchor }.OrderBy(static p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
                }

                const int MaxRows = 500;
                int no = 1;
                foreach (string path in orderedFiles.Take(MaxRows))
                {
                    rows.Add(BuildDirectoryEntryCells(path, no++));
                }

                return rows;
            }
            catch (Exception)
            {
                try
                {
                    rows.Add(BuildDirectoryEntryCells(_ncProgramSourcePath, 1));
                    return rows;
                }
                catch (Exception)
                {
                    string oFall = $"O{(int.TryParse(_progOHeader, out int of) ? of : 1):D4}";
                    rows.Add(("0001", oFall, "—", "—"));
                    return rows;
                }
            }
        }

        private static Grid CreateProgramDirectoryGridRow(string col0, string col1, string col2, string col3, bool isHeaderRow)
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
            double pad = isHeaderRow ? 6 : 4;
            for (int i = 0; i < 4; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            void AddCell(int column, string text)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontFamily = FanucNcMonospaceFont,
                    FontSize = 14,
                    Foreground = ProgDirectoryTextBrush,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(pad, 2, pad, 2),
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                if (isHeaderRow)
                {
                    tb.FontWeight = FontWeights.SemiBold;
                }

                Grid.SetColumn(tb, column);
                grid.Children.Add(tb);
            }

            AddCell(0, col0);
            AddCell(1, col1);
            AddCell(2, col2);
            AddCell(3, col3);
            return grid;
        }

        /// <summary>Столбец DATA: последняя запись на диск.</summary>
        private static (string, string, string, string) BuildDirectoryEntryCells(string filePath, int index)
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists)
            {
                return ($"{index:D4}", "—", "—", "—");
            }

            string onum5 = PeekOProgramNumberPrefixFromFile(filePath);
            string nameCell = $"O{(int.TryParse(onum5, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ov) ? ov : 1):D4}";
            string bytesCell = fi.Length.ToString(CultureInfo.InvariantCulture);
            string dateCell = fi.LastWriteTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return ($"{index:D4}", nameCell, bytesCell, dateCell);
        }

        private static string PeekOProgramNumberPrefixFromFile(string filePath)
        {
            try
            {
                using var sr = new StreamReader(filePath);
                var head = new StringBuilder();
                int n = 0;
                while (n < 32 && sr.ReadLine() is { } line)
                {
                    head.AppendLine(line);
                    n++;
                }

                return ExtractOProgramNumber5(head.ToString());
            }
            catch (Exception)
            {
                return "00001";
            }
        }

        private void RefreshProgSpindleMetricLine()
        {
            string sLine = $"S  {_lastSpindleRpm:F0}";
            string lLine = $"L{_feedOverridePercent:F0}%";
            ProgSpindleRpmMirrorText.Text = sLine;
            ProgSpindleLoadMirrorText.Text = lLine;
            ScreenSpindleText.Text = sLine;
            ScreenPosCoolantText.Text = lLine;
            RefreshOffsetStatusFooter();
        }

        private void RefreshOffsetStatusFooter()
        {
            OffsetStatusModeText.Text = _modeStatusAbbrev;
            OffsetStatusSpindleText.Text = $"S  {_lastSpindleRpm:F0}  L{_feedOverridePercent:F0}%";
        }

        private void JogStepComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (JogStepComboBox.SelectedItem is ComboBoxItem item &&
                item.Content is string text &&
                double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var step))
            {
                _jogStep = step;
                JogLabelText.Text = $"JOG (step {_jogStep:0.###} mm)";
            }
        }

        private void ExecuteMdiFromBuffer()
        {
            string mdi = MdiInputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(mdi))
            {
                LogUserAction("INPUT: пустой буфер MDI");
                SetAlarm("MDI BUFFER EMPTY", true);
                return;
            }

            if (IsProgDirectoryPageActive(_currentPage, _currentProgSubPage) &&
                TryParseDirectoryNewProgramToken(mdi, out string normalizedO))
            {
                LogUserAction($"INPUT: создание программы {normalizedO}");
                ProgDirectoryCreateAndOpenRequested?.Invoke(normalizedO);
                return;
            }

            LogUserAction($"INPUT (MDI): {mdi}");
            MdiExecuteRequested?.Invoke(mdi);
        }

        private void MdiExec_Click(object sender, RoutedEventArgs e) => ExecuteMdiFromBuffer();

        private void MdiExecLegacy_Click(object sender, RoutedEventArgs e)
        {
            SetAlarm("USE INPUT FOR MDI", false);
        }

        private void InsertMdiAtCaret(string valueToAppend)
        {
            if (string.IsNullOrEmpty(valueToAppend))
            {
                return;
            }

            int caret = MdiInputTextBox.CaretIndex;
            caret = Math.Clamp(caret, 0, MdiInputTextBox.Text.Length);
            string text = MdiInputTextBox.Text ?? string.Empty;
            if (_isMdiInsertMode || caret >= text.Length)
            {
                text = text.Insert(caret, valueToAppend);
            }
            else
            {
                int replaceLen = Math.Min(valueToAppend.Length, text.Length - caret);
                text = text.Remove(caret, replaceLen).Insert(caret, valueToAppend);
            }

            MdiInputTextBox.Text = text;
            MdiInputTextBox.CaretIndex = Math.Min(caret + valueToAppend.Length, text.Length);
            MdiInputTextBox.Focus();
        }

        private void MdiInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Per PDF: PRGRM/DIR and typing opens OPRT menu.
            if (_currentPage == FanucPage.Prog
                && (_currentProgSubPage is ProgSubPage.ProgramMain or ProgSubPage.ProgramDirectory)
                && !string.IsNullOrWhiteSpace(MdiInputTextBox.Text))
            {
                _progOprtMenuActive = true;
                _progOprtMenuPageIndex = 0;
            }

            RefreshProgMdiTypingSoftkeys();
            UpdateSoftKeyBar();
        }

        private void MdiInputTextBox_FocusChangedHandler(object sender, RoutedEventArgs e) =>
            Dispatcher.BeginInvoke(RefreshProgMdiTypingSoftkeys, DispatcherPriority.Input);

        private void MdiInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;

            if (key == Key.Insert && (mods & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
            {
                string mdiTrim = (MdiInputTextBox.Text ?? string.Empty).Trim();
                if (IsProgDirectoryPageActive(_currentPage, _currentProgSubPage)
                    && TryParseDirectoryNewProgramToken(mdiTrim, out _))
                {
                    ExecuteMdiFromBuffer();
                    e.Handled = true;
                    return;
                }
            }

            if (key == Key.Return || key == Key.Enter)
            {
                if ((mods & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
                {
                    ExecuteMdiFromBuffer();
                    e.Handled = true;
                }

                return;
            }

            if (key == Key.LeftShift || key == Key.RightShift)
            {
                return;
            }

            if ((mods & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
            {
                return;
            }

            if (!PhysicalMdiDualKeyMap.TryGetValue(key, out var pair))
            {
                return;
            }

            bool shiftHeld = (mods & ModifierKeys.Shift) != 0;
            InsertMdiAtCaret(shiftHeld ? pair.Shifted : pair.Plain);
            e.Handled = true;
        }

        private void MdiKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string tag || string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            string[] parts = tag.Split('|');
            bool dual = parts.Length == 2;
            bool useShift = dual &&
                (_isMdiShiftActive || (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            string valueToAppend = dual ? (useShift ? parts[1] : parts[0]) : tag;

            InsertMdiAtCaret(valueToAppend);

            if (_isMdiShiftActive && dual)
            {
                SetMdiShift(false);
            }
        }

        private void MdiCan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MdiInputTextBox.Text))
            {
                return;
            }

            int caret = MdiInputTextBox.CaretIndex;
            if (caret <= 0)
            {
                MdiInputTextBox.Text = string.Empty;
                MdiInputTextBox.CaretIndex = 0;
                return;
            }

            MdiInputTextBox.Text = MdiInputTextBox.Text.Remove(caret - 1, 1);
            MdiInputTextBox.CaretIndex = caret - 1;
            MdiInputTextBox.Focus();
        }

        private void MdiShift_Click(object sender, RoutedEventArgs e)
        {
            SetMdiShift(!_isMdiShiftActive);
        }

        private void MdiAlter_Click(object sender, RoutedEventArgs e)
        {
            _isMdiInsertMode = false;
            UpdateMdiEditModeButtons();
            MdiInputTextBox.Focus();
        }

        private void MdiInsert_Click(object sender, RoutedEventArgs e)
        {
            string mdiTrim = (MdiInputTextBox.Text ?? string.Empty).Trim();
            if (IsProgDirectoryPageActive(_currentPage, _currentProgSubPage)
                && TryParseDirectoryNewProgramToken(mdiTrim, out _))
            {
                ExecuteMdiFromBuffer();
                return;
            }

            _isMdiInsertMode = true;
            UpdateMdiEditModeButtons();
            MdiInputTextBox.Focus();
        }

        private void MdiDelete_Click(object sender, RoutedEventArgs e)
        {
            int caret = MdiInputTextBox.CaretIndex;
            if (caret >= MdiInputTextBox.Text.Length || MdiInputTextBox.Text.Length == 0)
            {
                return;
            }

            MdiInputTextBox.Text = MdiInputTextBox.Text.Remove(caret, 1);
            MdiInputTextBox.CaretIndex = Math.Min(caret, MdiInputTextBox.Text.Length);
            MdiInputTextBox.Focus();
        }

        private void MdiCursorLeft_Click(object sender, RoutedEventArgs e)
        {
            if (TryHandleNcProgramKeys(Key.Left))
            {
                return;
            }

            MdiInputTextBox.CaretIndex = Math.Max(0, MdiInputTextBox.CaretIndex - 1);
            MdiInputTextBox.Focus();
        }

        private void MdiCursorRight_Click(object sender, RoutedEventArgs e)
        {
            if (TryHandleNcProgramKeys(Key.Right))
            {
                return;
            }

            MdiInputTextBox.CaretIndex = Math.Min(MdiInputTextBox.Text.Length, MdiInputTextBox.CaretIndex + 1);
            MdiInputTextBox.Focus();
        }

        private void MdiCursorUp_Click(object sender, RoutedEventArgs e)
        {
            if (TryHandleNcProgramKeys(Key.Up))
            {
                return;
            }

            MdiInputTextBox.CaretIndex = 0;
            MdiInputTextBox.Focus();
        }

        private void MdiCursorDown_Click(object sender, RoutedEventArgs e)
        {
            if (TryHandleNcProgramKeys(Key.Down))
            {
                return;
            }

            MdiInputTextBox.CaretIndex = MdiInputTextBox.Text.Length;
            MdiInputTextBox.Focus();
        }

        private void MdiPageUp_Click(object sender, RoutedEventArgs e)
        {
            if (TryNavigateProgramDirectoryPage(-1))
            {
                return;
            }

            if (TryHandleNcProgramKeys(Key.PageUp))
            {
                return;
            }

            MdiInputTextBox.CaretIndex = Math.Max(0, MdiInputTextBox.CaretIndex - 10);
            MdiInputTextBox.Focus();
        }

        private void MdiPageDown_Click(object sender, RoutedEventArgs e)
        {
            if (TryNavigateProgramDirectoryPage(1))
            {
                return;
            }

            if (TryHandleNcProgramKeys(Key.PageDown))
            {
                return;
            }

            MdiInputTextBox.CaretIndex = Math.Min(MdiInputTextBox.Text.Length, MdiInputTextBox.CaretIndex + 10);
            MdiInputTextBox.Focus();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            SetAlarm("HELP: MDI EDIT ACTIVE", false);
        }

        private void SetMdiShift(bool enabled)
        {
            _isMdiShiftActive = enabled;
            if (MdiShiftButton != null)
            {
                MdiShiftButton.FontWeight = enabled ? FontWeights.Bold : FontWeights.Normal;
                MdiShiftButton.Background = enabled
                    ? System.Windows.Media.Brushes.LightSkyBlue
                    : System.Windows.Media.Brushes.LightGray;
            }
        }

        private void UpdateMdiEditModeButtons()
        {
            var canStyleBrush = TryFindResource("KeyBeigeBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.LightGray;

            if (MdiInsertButton != null)
            {
                MdiInsertButton.FontWeight = _isMdiInsertMode ? FontWeights.Bold : FontWeights.Normal;
                MdiInsertButton.Background = canStyleBrush;
            }

            if (MdiAlterButton != null)
            {
                MdiAlterButton.FontWeight = !_isMdiInsertMode ? FontWeights.Bold : FontWeights.Normal;
                MdiAlterButton.Background = !_isMdiInsertMode
                    ? System.Windows.Media.Brushes.Goldenrod
                    : System.Windows.Media.Brushes.LightGray;
            }
        }

        private void SingleBlockCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SingleBlockChangedRequested?.Invoke(SingleBlockCheckBox.IsChecked == true);
        }

        private void OptionalStopCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            OptionalStopChangedRequested?.Invoke(OptionalStopCheckBox.IsChecked == true);
        }

        private void DryRunCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            DryRunChangedRequested?.Invoke(DryRunCheckBox.IsChecked == true);
        }

        private void OffsetApply_Click(object sender, RoutedEventArgs e)
        {
            if (OffsetSystemComboBox.SelectedItem is not ComboBoxItem item ||
                item.Content is not string codeText ||
                codeText.Length != 3 ||
                !codeText.StartsWith("G", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(codeText[1..], out var codeNumber))
            {
                SetAlarm("OFFSET INPUT ERROR", true);
                return;
            }

            var style = System.Globalization.NumberStyles.Float;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(OffsetXTextBox.Text, style, culture, out var x) ||
                !double.TryParse(OffsetYTextBox.Text, style, culture, out var y) ||
                !double.TryParse(OffsetZTextBox.Text, style, culture, out var z))
            {
                SetAlarm("OFFSET VALUE ERROR", true);
                return;
            }

            OffsetUpdateRequested?.Invoke(codeNumber, x, y, z);
        }

        private void OffsetReadActive_Click(object sender, RoutedEventArgs e)
        {
            OffsetReadActiveToEditorRequested?.Invoke();
        }

        private void OffsetSystemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ValidateOffsetEditorInputs();

            if (_suppressOffsetSelectionEvent)
            {
                return;
            }

            if (OffsetSystemComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string codeText)
            {
                return;
            }

            if (codeText.Length == 3 &&
                codeText.StartsWith("G", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(codeText[1..], out int codeNumber))
            {
                OffsetSystemSelectionChangedRequested?.Invoke(codeNumber);
            }
        }

        private void OffsetEditorField_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateOffsetEditorInputs();
        }

        private void ValidateOffsetEditorInputs()
        {
            if (OffsetXTextBox == null || OffsetYTextBox == null || OffsetZTextBox == null)
            {
                return;
            }

            bool xValid = TryParseOffset(OffsetXTextBox.Text, out var xValue);
            bool yValid = TryParseOffset(OffsetYTextBox.Text, out var yValue);
            bool zValid = TryParseOffset(OffsetZTextBox.Text, out var zValue);

            PaintOffsetField(OffsetXTextBox, xValid, xValue, _loadedOffsetX);
            PaintOffsetField(OffsetYTextBox, yValid, yValue, _loadedOffsetY);
            PaintOffsetField(OffsetZTextBox, zValid, zValue, _loadedOffsetZ);

            if (OffsetApplyButton != null)
            {
                OffsetApplyButton.IsEnabled = xValid && yValid && zValid;
            }
        }

        private static bool TryParseOffset(string text, out double value)
        {
            return double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private static void PaintOffsetField(TextBox textBox, bool isValid, double value, double loadedValue)
        {
            if (!isValid)
            {
                textBox.Background = System.Windows.Media.Brushes.MistyRose;
                return;
            }

            bool changed = Math.Abs(value - loadedValue) > 0.0005;
            textBox.Background = changed
                ? System.Windows.Media.Brushes.LemonChiffon
                : System.Windows.Media.Brushes.White;
        }

        private void JogXPlus_Click(object sender, RoutedEventArgs e) => JogRequested?.Invoke("X", +_jogStep);
        private void JogYPlus_Click(object sender, RoutedEventArgs e) => JogRequested?.Invoke("Y", +_jogStep);
        private void JogZPlus_Click(object sender, RoutedEventArgs e) => JogRequested?.Invoke("Z", +_jogStep);
        private void JogXMinus_Click(object sender, RoutedEventArgs e) => JogRequested?.Invoke("X", -_jogStep);
        private void JogYMinus_Click(object sender, RoutedEventArgs e) => JogRequested?.Invoke("Y", -_jogStep);
        private void JogZMinus_Click(object sender, RoutedEventArgs e) => JogRequested?.Invoke("Z", -_jogStep);
    }
}
