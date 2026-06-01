using System.Collections.ObjectModel;
using System.Windows.Media.Media3D;
using CNCSS.Vis;

namespace CNCSS.UI.ViewModels
{
    public sealed class ComponentRoleAssignmentViewModel : BaseViewModel
    {
        public ComponentRoleAssignmentViewModel(MeshImportResult importResult)
        {
            ComponentCount = importResult.Components.Count;
            foreach (MeshComponent component in importResult.Components)
            {
                Rows.Add(new ComponentRoleRowViewModel(component));
            }

            UpdateRoleStatus();
        }

        public int ComponentCount { get; }

        public ObservableCollection<ComponentRoleRowViewModel> Rows { get; } = new();

        public static IReadOnlyList<ComponentRoleOption> RoleOptions { get; } =
        [
            new(MachineComponentRole.Unassigned, "(не назначено)"),
            new(MachineComponentRole.Spindle, "Шпиндель"),
            new(MachineComponentRole.Base, "Основание (станина)"),
            new(MachineComponentRole.Table, "Стол (рабочий стол)"),
            new(MachineComponentRole.Other, "Остальной узел")
        ];

        private bool _canApply;

        public bool CanApply
        {
            get => _canApply;
            private set => SetProperty(ref _canApply, value);
        }

        private string _roleStatus = string.Empty;

        public string RoleStatus
        {
            get => _roleStatus;
            private set => SetProperty(ref _roleStatus, value);
        }

        public bool TrySetRole(ComponentRoleRowViewModel row, MachineComponentRole role, out string? warning)
        {
            warning = null;
            if (role is MachineComponentRole.Spindle or MachineComponentRole.Base or MachineComponentRole.Table)
            {
                ComponentRoleRowViewModel? existing = Rows.FirstOrDefault(
                    r => !ReferenceEquals(r, row) && r.Role == role);
                if (existing != null)
                {
                    warning =
                        $"Роль «{RoleOptions.First(o => o.Value == role).Title}» уже назначена компоненту «{existing.DisplayName}». Сначала снимите назначение.";
                    return false;
                }
            }

            row.Role = role;
            UpdateRoleStatus();
            return true;
        }

        public IReadOnlyList<ComponentRoleAssignment> BuildAssignments() =>
            Rows
                .Where(r => r.Role != MachineComponentRole.Unassigned)
                .Select(r => new ComponentRoleAssignment
                {
                    Component = r.Component,
                    Role = r.Role
                })
                .ToList();

        private void UpdateRoleStatus()
        {
            bool spindle = Rows.Any(r => r.Role == MachineComponentRole.Spindle);
            bool table = Rows.Any(r => r.Role == MachineComponentRole.Table);
            bool baseNode = Rows.Any(r => r.Role == MachineComponentRole.Base);

            RoleStatus =
                $"Шпиндель: {(spindle ? "✓" : "—")}   Стол: {(table ? "✓" : "—")}   Основание: {(baseNode ? "✓" : "—")}";

            CanApply = spindle && table && baseNode
                       && Rows.Count(r => r.Role == MachineComponentRole.Spindle) == 1
                       && Rows.Count(r => r.Role == MachineComponentRole.Table) == 1
                       && Rows.Count(r => r.Role == MachineComponentRole.Base) == 1;
        }
    }

    public sealed class ComponentRoleRowViewModel : BaseViewModel
    {
        public ComponentRoleRowViewModel(MeshComponent component)
        {
            Component = component;
            DisplayName = component.DisplayName;
            PreviewModel = component.Model;
            TriangleSummary = component.TriangleCount > 0
                ? $"{component.TriangleCount:N0} треуг."
                : string.Empty;
        }

        public MeshComponent Component { get; }

        public string DisplayName { get; }

        public Model3D? PreviewModel { get; }

        public string TriangleSummary { get; }

        private MachineComponentRole _role = MachineComponentRole.Unassigned;

        public MachineComponentRole Role
        {
            get => _role;
            set => SetProperty(ref _role, value);
        }
    }

    public sealed class ComponentRoleOption(MachineComponentRole value, string title)
    {
        public MachineComponentRole Value { get; } = value;

        public string Title { get; } = title;
    }
}
