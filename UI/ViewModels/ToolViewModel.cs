using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CNCSS.Data.Tools;

namespace CNCSS.UI.ViewModels
{
    public class ToolViewModel : INotifyPropertyChanged
    {
        private int _number;
        private double _diameter = 10.0;
        private double _shankDiameter = 10.0;
        private double _fluteLength = 30.0;
        private double _overallLength = 75.0;
        private int _flutes = 2;
        private double _pointAngle = 118.0;
        private ToolType _selectedType = ToolType.EndMill;

        public int Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public string DisplayName => $"Инструмент T{Number}";

        public double Diameter
        {
            get => _diameter;
            set { _diameter = value; OnPropertyChanged(); }
        }

        public double ShankDiameter
        {
            get => _shankDiameter;
            set { _shankDiameter = value; OnPropertyChanged(); }
        }

        public double FluteLength
        {
            get => _fluteLength;
            set { _fluteLength = value; OnPropertyChanged(); }
        }

        public double OverallLength
        {
            get => _overallLength;
            set { _overallLength = value; OnPropertyChanged(); }
        }

        public int Flutes
        {
            get => _flutes;
            set { _flutes = value; OnPropertyChanged(); }
        }

        public double PointAngle
        {
            get => _pointAngle;
            set { _pointAngle = value; OnPropertyChanged(); }
        }

        public bool IsDrill => SelectedType == ToolType.Drill;

        public ToolType SelectedType
        {
            get => _selectedType;
            set 
            { 
                _selectedType = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsDrill)); 
            }
        }

        public IEnumerable<ToolType> ToolTypes => Enum.GetValues(typeof(ToolType)).Cast<ToolType>();
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
