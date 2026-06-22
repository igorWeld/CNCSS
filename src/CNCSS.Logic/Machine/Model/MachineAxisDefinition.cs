namespace CNCSS.Machine.Model
{
    public sealed class MachineAxisDefinition
    {
        public string Name { get; set; } = "X";

        /// <summary>Нижний предел перемещения по оси (мм, MCS).</summary>
        public double Min { get; set; }

        /// <summary>Верхний предел перемещения по оси (мм, MCS).</summary>
        public double Max { get; set; } = 500;

        /// <summary>HOME по оси (мм, MCS).</summary>
        public double Home { get; set; }

        public MachineNodeKind DrivenBy { get; set; } = MachineNodeKind.Table;

        public string DriverNodeId { get; set; } = MachineNodeIds.Table;

        public string ResolveDriverNodeId()
        {
            if (!string.IsNullOrWhiteSpace(DriverNodeId))
            {
                return DriverNodeId.Trim();
            }

            return DrivenBy switch
            {
                MachineNodeKind.Base => MachineNodeIds.Base,
                MachineNodeKind.Spindle => MachineNodeIds.Spindle,
                _ => MachineNodeIds.Table
            };
        }

        public MachineAxisDefinition Clone() => new()
        {
            Name = Name,
            Min = Min,
            Max = Max,
            Home = Home,
            DrivenBy = DrivenBy,
            DriverNodeId = DriverNodeId
        };

        public static MachineAxisDefinition CreateDefault(string name, string driverNodeId, double min, double max, double home)
        {
            MachineNodeKind kind = driverNodeId switch
            {
                MachineNodeIds.Base => MachineNodeKind.Base,
                MachineNodeIds.Spindle => MachineNodeKind.Spindle,
                _ => MachineNodeKind.Table
            };

            return new MachineAxisDefinition
            {
                Name = name,
                Min = min,
                Max = max,
                Home = home,
                DrivenBy = kind,
                DriverNodeId = driverNodeId
            };
        }
    }
}
