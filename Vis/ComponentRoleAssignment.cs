namespace CNCSS.Vis
{
    public sealed class ComponentRoleAssignment
    {
        public required MeshComponent Component { get; init; }

        public required MachineComponentRole Role { get; init; }
    }
}
