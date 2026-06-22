using System.Windows;
using HelixToolkit.Wpf;
using CNCSS.UI.ViewModels;

namespace CNCSS.UI.Dialogs
{
    /// <summary>Диалог «Конструктор заготовки» (меню Симуляция).</summary>
    public partial class StockConstructorWindow : Window
    {
        public StockConstructorWindow(StockConstructorViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            // ViewModel подключает превью заготовки и стола к HelixViewport3D.
            vm.AttachViewport(PreviewViewport);

            vm.RequestClose += (_, accepted) =>
            {
                DialogResult = accepted;
                Close();
            };
        }
    }
}

