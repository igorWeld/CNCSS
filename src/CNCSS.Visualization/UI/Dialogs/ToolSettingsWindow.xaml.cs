using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CNCSS.UI.ViewModels;

namespace CNCSS.UI.Dialogs
{
    /// <summary>
    /// Окно библиотеки инструментов в стиле панели стойки: строки <see cref="Rows"/>, «Применить», предупреждения по T из УП.
    /// </summary>
    public partial class ToolSettingsWindow : Window
    {
        private readonly ObservableCollection<ToolViewModel> _toolsBacking;
        private readonly HashSet<int> _programToolNumbers;
        private readonly Action<ToolViewModel> _registerTool;
        private readonly Action<ToolViewModel> _unregisterTool;
        private readonly Action _onApplied;
        private bool _isInitializing;

        public static readonly DependencyProperty IsDirtyProperty =
            DependencyProperty.Register(
                nameof(IsDirty),
                typeof(bool),
                typeof(ToolSettingsWindow),
                new PropertyMetadata(false));

        public ObservableCollection<ToolEditRowViewModel> Rows { get; } = new();

        public bool IsDirty
        {
            get => (bool)GetValue(IsDirtyProperty);
            private set => SetValue(IsDirtyProperty, value);
        }

        public ToolSettingsWindow(
            ObservableCollection<ToolViewModel> tools,
            IReadOnlyCollection<int> programReferencedToolNumbers,
            Action<ToolViewModel> registerToolPropertyChanged,
            Action<ToolViewModel> unregisterToolPropertyChanged,
            Action onApplied)
        {
            InitializeComponent();
            _toolsBacking = tools;
            _programToolNumbers = new HashSet<int>(programReferencedToolNumbers);
            _registerTool = registerToolPropertyChanged;
            _unregisterTool = unregisterToolPropertyChanged;
            _onApplied = onApplied;
            DataContext = this;
            Rows.CollectionChanged += Rows_CollectionChanged;

            _isInitializing = true;
            foreach (ToolViewModel vm in tools.OrderBy(t => t.Number))
            {
                AddRow(new ToolEditRowViewModel(vm), markDirty: false);
            }
            _isInitializing = false;

            Loaded += (_, _) => UpdateEmptyState();
            UpdateEmptyState();
        }

        private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (ToolEditRowViewModel row in e.OldItems)
                {
                    row.PropertyChanged -= Row_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (ToolEditRowViewModel row in e.NewItems)
                {
                    row.PropertyChanged += Row_PropertyChanged;
                }
            }
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_isInitializing && e.PropertyName != nameof(ToolEditRowViewModel.PreviewModel))
            {
                IsDirty = true;
            }
        }

        private void AddRow(ToolEditRowViewModel row, bool markDirty)
        {
            Rows.Add(row);
            if (markDirty)
            {
                IsDirty = true;
            }
        }

        private void UpdateEmptyState()
        {
            if (_programToolNumbers.Count == 0)
            {
                EmptyHint.Text = "В текущей УП нет вызовов T. При необходимости добавьте инструмент через «Новый инструмент» и задайте параметры перед «Применить».";
            }
            else
            {
                string list = string.Join(", ", _programToolNumbers.OrderBy(n => n).Select(n => $"T{n}"));
                EmptyHint.Text = $"Список пуст.\nПосле добавления строк учтите номера из программы: {list}";
            }

            EmptyHint.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void NewToolButton_Click(object sender, RoutedEventArgs e)
        {
            int nextNum = NextAvailableToolNumber();
            var vm = new ToolViewModel { Number = nextNum };
            vm.FluteColor = ToolPaletteSwatches.NextRandomDistinctFluteColor(_toolsBacking.Select(t => t.FluteColor));
            _registerTool.Invoke(vm);
            _toolsBacking.Add(vm);
            AddRow(new ToolEditRowViewModel(vm), markDirty: true);
            UpdateEmptyState();
        }

        private int NextAvailableToolNumber()
        {
            int n = 1;
            while (_toolsBacking.Any(t => t.Number == n))
            {
                n++;
            }

            return n;
        }

        private void RemoveRowButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not ToolEditRowViewModel row)
            {
                return;
            }

            _unregisterTool.Invoke(row.Source);
            _toolsBacking.Remove(row.Source);
            row.PropertyChanged -= Row_PropertyChanged;
            Rows.Remove(row);
            IsDirty = true;
            UpdateEmptyState();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            var plannedNumbers = new HashSet<int>();
            foreach (ToolEditRowViewModel row in Rows)
            {
                if (!row.TryPeekToolNumber(out int toolNum, out string peekErr))
                {
                    MessageBox.Show(this, peekErr, "Инструменты", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!plannedNumbers.Add(toolNum))
                {
                    MessageBox.Show(
                        this,
                        $"Номер T{toolNum} указан несколько раз — номера должны быть уникальными.",
                        "Инструменты",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            var missingNumbers = _programToolNumbers.Where(tn => !plannedNumbers.Contains(tn)).OrderBy(n => n).ToList();
            if (missingNumbers.Count > 0)
            {
                string tnList = string.Join(", ", missingNumbers.Select(n => $"T{n}"));
                string body =
                    $"Предупреждение: в программе указаны номера инструмента ({tnList}), но в текущей библиотеке нет строк с такими номерами. Цвета и геометрия будут использоваться только для тех T, строки которых здесь есть.\n\n" +
                    $"Всё равно применить изменения ко всем инструментам из списка?";

                MessageBoxResult answer = MessageBox.Show(
                    this,
                    body,
                    "Предупреждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            foreach (ToolEditRowViewModel row in Rows)
            {
                if (!row.TryApply(out string err))
                {
                    MessageBox.Show(this, err, "Инструменты", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _onApplied.Invoke();
            IsDirty = false;
        }
    }
}
