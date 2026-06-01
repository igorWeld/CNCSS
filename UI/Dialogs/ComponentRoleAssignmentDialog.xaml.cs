using System.Windows;
using System.Windows.Controls;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;
using HelixToolkit.Wpf;

namespace CNCSS.UI.Dialogs
{
    public partial class ComponentRoleAssignmentDialog : Window
    {
        private readonly ComponentRoleAssignmentViewModel _viewModel;
        private bool _suppressRoleChange;

        public ComponentRoleAssignmentDialog(MeshImportResult importResult)
        {
            InitializeComponent();
            _viewModel = new ComponentRoleAssignmentViewModel(importResult);
            DataContext = _viewModel;
        }

        public IReadOnlyList<ComponentRoleAssignment>? Assignments { get; private set; }

        private void RoleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRoleChange
                || sender is not ComboBox combo
                || combo.DataContext is not ComponentRoleRowViewModel row
                || e.AddedItems.Count == 0)
            {
                return;
            }

            if (combo.SelectedValue is not MachineComponentRole newRole)
            {
                return;
            }

            MachineComponentRole previous = row.Role;
            if (newRole == previous)
            {
                return;
            }

            if (_viewModel.TrySetRole(row, newRole, out string? warning))
            {
                return;
            }

            _suppressRoleChange = true;
            combo.SelectedValue = previous;
            _suppressRoleChange = false;
            MessageBox.Show(this, warning, "Назначение ролей", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanApply)
            {
                MessageBox.Show(
                    this,
                    "Назначьте ровно по одному компоненту на роли «Шпиндель», «Основание» и «Стол».",
                    "Назначение ролей",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Assignments = _viewModel.BuildAssignments();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ComponentPreviewViewport_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is HelixViewport3D viewport && viewport.Children.Count > 0)
            {
                viewport.ZoomExtents();
            }
        }
    }
}
