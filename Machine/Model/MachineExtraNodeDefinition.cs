namespace CNCSS.Machine.Model
{
    public sealed class MachineExtraNodeDefinition
    {
        /// <summary>Optional mesh color in packed ARGB (0 means "use default color").</summary>
        public uint MeshColorArgb { get; set; }

        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string DisplayName { get; set; } = "Доп. узел";

        public string ParentNodeId { get; set; } = MachineNodeIds.Table;

        public MachineMotionLink MotionLink { get; set; } = MachineMotionLink.Fixed;

        public MachineProgramAxisMask MotionAxes { get; set; } = MachineProgramAxisMask.None;

        public string StlFileName { get; set; } = string.Empty;

        public double MeshScale { get; set; } = 1.0;

        public double StlSourceMaxExtent { get; set; }

        public double TargetMaxExtentMm { get; set; }

        /// <summary>Смещение STL (мм) относительно MCS.</summary>
        public MachineGeometryPoint MeshOffset { get; set; } = MachineGeometryPoint.Zero;

        public MachineGeometryPoint MeshRotationDegrees { get; set; } = MachineGeometryPoint.Zero;

        /// <summary>Точка крепления на родителе (мм, MCS).</summary>
        public MachineGeometryPoint AttachOnParent { get; set; } = MachineGeometryPoint.Zero;

        /// <summary>Точка крепления на узле (мм, локально узла, MCS).</summary>
        public MachineGeometryPoint AttachOnChild { get; set; } = MachineGeometryPoint.Zero;

        public MachineExtraNodeDefinition Clone() => new()
        {
            Id = Id,
            DisplayName = DisplayName,
            ParentNodeId = ParentNodeId,
            MotionLink = MotionLink,
            MotionAxes = MotionAxes,
            MeshColorArgb = MeshColorArgb,
            StlFileName = StlFileName,
            MeshScale = MeshScale,
            StlSourceMaxExtent = StlSourceMaxExtent,
            TargetMaxExtentMm = TargetMaxExtentMm,
            MeshOffset = MeshOffset.Clone(),
            MeshRotationDegrees = MeshRotationDegrees.Clone(),
            AttachOnParent = AttachOnParent.Clone(),
            AttachOnChild = AttachOnChild.Clone()
        };

        public void SyncMeshScaleFromTarget()
        {
            if (StlSourceMaxExtent > 1e-9 && TargetMaxExtentMm > 1e-9)
            {
                MeshScale = TargetMaxExtentMm / StlSourceMaxExtent;
            }
        }

        public static MachineExtraNodeDefinition CreateNew(string displayName, string parentNodeId)
        {
            return new MachineExtraNodeDefinition
            {
                DisplayName = displayName,
                ParentNodeId = parentNodeId
            };
        }
    }
}
