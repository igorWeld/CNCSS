using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CNCSS.UI.ViewModels
{
    /// <summary>Базовый класс моделей представления с <see cref="INotifyPropertyChanged"/> и помощником <see cref="SetProperty{T}"/>.</summary>
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
