using System.Windows.Media.Media3D;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class ComponentRoleAssignmentViewModelTests
{
    [Fact]
    public void CanApply_false_until_three_key_roles_assigned()
    {
        var result = MeshImportResult.Ok(
            "asm.stp",
            [
                CreateComponent(0, "A"),
                CreateComponent(1, "B"),
                CreateComponent(2, "C")
            ]);

        var vm = new ComponentRoleAssignmentViewModel(result);
        Assert.False(vm.CanApply);

        Assert.True(vm.TrySetRole(vm.Rows[0], MachineComponentRole.Base, out _));
        Assert.True(vm.TrySetRole(vm.Rows[1], MachineComponentRole.Table, out _));
        Assert.False(vm.CanApply);

        Assert.True(vm.TrySetRole(vm.Rows[2], MachineComponentRole.Spindle, out _));
        Assert.True(vm.CanApply);
    }

    [Fact]
    public void TrySetRole_blocks_duplicate_key_role()
    {
        var result = MeshImportResult.Ok(
            "asm.stp",
            [CreateComponent(0, "A"), CreateComponent(1, "B")]);
        var vm = new ComponentRoleAssignmentViewModel(result);

        Assert.True(vm.TrySetRole(vm.Rows[0], MachineComponentRole.Spindle, out _));
        Assert.False(vm.TrySetRole(vm.Rows[1], MachineComponentRole.Spindle, out string? warning));
        Assert.Contains("Шпиндель", warning, StringComparison.Ordinal);
        Assert.Equal(MachineComponentRole.Unassigned, vm.Rows[1].Role);
    }

    private static MeshComponent CreateComponent(int index, string name) =>
        new()
        {
            Index = index,
            DisplayName = name,
            Model = new GeometryModel3D(),
            TriangleCount = 12
        };
}
