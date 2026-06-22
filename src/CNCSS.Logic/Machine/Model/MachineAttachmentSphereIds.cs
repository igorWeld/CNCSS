namespace CNCSS.Machine.Model
{
    /// <summary>Идентификаторы сфер точек привязки в превью конструктора.</summary>
    public static class MachineAttachmentSphereIds
    {
        public const string Mcs = "__MCS__";

        public static readonly string[] SetupNodes =
        [
            MachineNodeIds.Base,
            MachineNodeIds.Table,
            MachineNodeIds.Spindle
        ];

        public static bool IsMcs(string id) =>
            string.Equals(id, Mcs, StringComparison.OrdinalIgnoreCase);

        public static bool IsSetupNode(string id) =>
            SetupNodes.Any(n => string.Equals(n, id, StringComparison.OrdinalIgnoreCase));
    }
}
