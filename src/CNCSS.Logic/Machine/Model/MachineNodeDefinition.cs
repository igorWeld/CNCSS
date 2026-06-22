namespace CNCSS.Machine.Model
{
    public sealed class MachineNodeDefinition
    {
        public MachineNodeKind Kind { get; set; }

        /// <summary>
        /// Optional mesh color in packed ARGB (0 means "use default color").
        /// Stored in profile JSON and applied to the node mesh in preview/simulation.
        /// </summary>
        public uint MeshColorArgb { get; set; }

        /// <summary>Relative path to STL inside the profile folder, or empty for placeholder.</summary>
        public string StlFileName { get; set; } = string.Empty;

        public double MeshScale { get; set; } = 1.0;

        /// <summary>Longest side of STL in file units (scale = 1).</summary>
        public double StlSourceMaxExtent { get; set; }

        /// <summary>Desired longest side in simulation millimeters.</summary>
        public double TargetMaxExtentMm { get; set; }

        /// <summary>Смещение STL (мм) относительно MCS.</summary>
        public MachineGeometryPoint MeshOffset { get; set; } = MachineGeometryPoint.Zero;

        public MachineGeometryPoint MeshRotationDegrees { get; set; } = MachineGeometryPoint.Zero;

        /// <summary>Точка крепления на родителе (мм, MCS).</summary>
        public MachineGeometryPoint AttachOnParent { get; set; } = MachineGeometryPoint.Zero;

        /// <summary>
        /// Точка отсчёта / привязки в локальной СК узла (мм, внутри STL после mesh offset).
        /// Следует за перемещениями осей; шарнир в MCS задаётся через кинематическую цепочку.
        /// </summary>
        public MachineGeometryPoint AttachOnChild { get; set; } = MachineGeometryPoint.Zero;

        /// <summary>Tool center point on spindle node (mm, spindle local).</summary>
        public MachineGeometryPoint ToolMount { get; set; } = MachineGeometryPoint.Zero;

        /// <summary>Which program axes (X/Y/Z) move this node during machining.</summary>
        public MachineProgramAxisMask MotionAxes { get; set; } = MachineProgramAxisMask.None;

        public MachineNodeDefinition Clone()
        {
            return new MachineNodeDefinition
            {
                Kind = Kind,
                MeshColorArgb = MeshColorArgb,
                StlFileName = StlFileName,
                MeshScale = MeshScale,
                StlSourceMaxExtent = StlSourceMaxExtent,
                TargetMaxExtentMm = TargetMaxExtentMm,
                MeshOffset = MeshOffset.Clone(),
                MeshRotationDegrees = MeshRotationDegrees.Clone(),
                AttachOnParent = AttachOnParent.Clone(),
                AttachOnChild = AttachOnChild.Clone(),
                ToolMount = ToolMount.Clone(),
                MotionAxes = MotionAxes
            };
        }

        public static MachineNodeDefinition CreateDefault(MachineNodeKind kind)
        {
            var node = new MachineNodeDefinition
            {
                Kind = kind,
                MeshScale = 1.0,
                MeshColorArgb = MachineNodeColors.ForKind(kind)
            };
            switch (kind)
            {
                case MachineNodeKind.Table:
                    node.AttachOnParent = new MachineGeometryPoint { X = 250, Y = 250, Z = 0 };
                    node.MotionAxes = MachineProgramAxisMask.Xy;
                    break;
                case MachineNodeKind.Spindle:
                    node.AttachOnParent = new MachineGeometryPoint { X = 250, Y = 250, Z = 400 };
                    node.ToolMount = new MachineGeometryPoint { X = 0, Y = 0, Z = 0 };
                    node.MotionAxes = MachineProgramAxisMask.Z;
                    break;
            }

            return node;
        }

        public void SyncMeshScaleFromTarget()
        {
            if (StlSourceMaxExtent > 1e-9 && TargetMaxExtentMm > 1e-9)
            {
                MeshScale = TargetMaxExtentMm / StlSourceMaxExtent;
            }
        }
    }
}
