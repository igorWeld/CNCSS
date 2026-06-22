namespace CNCSS.UI.Hosts
{
    /// <summary>Выбранная строка УП без привязки к скрытому ListBox.</summary>
    public sealed class ProgramLineNavigator
    {
        private int _selectedIndex = -1;
        private bool _suppressSelectionChanged;

        public event EventHandler? SelectionChanged;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set => SetSelectedIndex(value, raiseChanged: true);
        }

        public int LineCount { get; private set; }

        public void BindLineCount(int count)
        {
            LineCount = Math.Max(0, count);
            if (_selectedIndex >= LineCount)
            {
                SetSelectedIndex(LineCount > 0 ? LineCount - 1 : -1, raiseChanged: true);
            }
        }

        public void SetSelectedIndexSilently(int index)
        {
            SetSelectedIndex(index, raiseChanged: false);
        }

        public int GetSelectedLineNumber1Based() => Math.Max(1, _selectedIndex + 1);

        private void SetSelectedIndex(int index, bool raiseChanged)
        {
            int clamped = LineCount > 0 ? Math.Clamp(index, 0, LineCount - 1) : (index < 0 ? -1 : 0);
            if (_selectedIndex == clamped)
            {
                return;
            }

            _selectedIndex = clamped;
            if (raiseChanged && !_suppressSelectionChanged)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public IDisposable SuppressSelectionChanged()
        {
            _suppressSelectionChanged = true;
            return new SuppressScope(this);
        }

        private sealed class SuppressScope : IDisposable
        {
            private readonly ProgramLineNavigator _owner;
            public SuppressScope(ProgramLineNavigator owner) => _owner = owner;
            public void Dispose() => _owner._suppressSelectionChanged = false;
        }
    }
}
