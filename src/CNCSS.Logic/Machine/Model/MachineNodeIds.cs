namespace CNCSS.Machine.Model
{
    public static class MachineNodeIds
    {
        public const string Base = "base";
        public const string Table = "table";
        public const string Spindle = "spindle";

        public static bool IsBuiltIn(string nodeId) =>
            string.Equals(nodeId, Base, StringComparison.OrdinalIgnoreCase)
            || string.Equals(nodeId, Table, StringComparison.OrdinalIgnoreCase)
            || string.Equals(nodeId, Spindle, StringComparison.OrdinalIgnoreCase);

        public static MachineNodeKind? ToBuiltInKind(string nodeId)
        {
            if (string.Equals(nodeId, Base, StringComparison.OrdinalIgnoreCase))
            {
                return MachineNodeKind.Base;
            }

            if (string.Equals(nodeId, Table, StringComparison.OrdinalIgnoreCase))
            {
                return MachineNodeKind.Table;
            }

            if (string.Equals(nodeId, Spindle, StringComparison.OrdinalIgnoreCase))
            {
                return MachineNodeKind.Spindle;
            }

            return null;
        }
    }
}
