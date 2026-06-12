using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    /// <summary>Computes node transforms and TCP from machine-axis values.</summary>
    public static class KinematicChainSolver
    {
        public sealed record MachinePose(
            double X,
            double Y,
            double Z,
            Transform3D BaseTransform,
            Transform3D TableTransform,
            Transform3D SpindleTransform,
            Point3D ToolCenterPoint,
            IReadOnlyDictionary<string, Transform3D> NodeTransforms);

        public static MachinePose Solve(MachineDefinition definition, double x, double y, double z)
        {
            var transforms = SolveTransforms(definition, x, y, z);
            Point3D tcp = ComputeToolCenterPoint(definition, x, y, z);
            return new MachinePose(
                x,
                y,
                z,
                transforms[MachineNodeIds.Base],
                transforms[MachineNodeIds.Table],
                transforms[MachineNodeIds.Spindle],
                tcp,
                transforms);
        }

        public static IReadOnlyDictionary<string, Transform3D> SolveTransforms(MachineDefinition definition, double x, double y, double z)
        {
            var result = new Dictionary<string, Transform3D>(StringComparer.OrdinalIgnoreCase);

            Transform3D baseTransform = Transform3D.Identity;
            result[MachineNodeIds.Base] = baseTransform;

            Vector3D tableMotion = ResolveTableChainMotion(definition, definition.Table.MotionAxes, x, y, z);
            Matrix3D tableAttach = MachineNodeMeshTransforms.ComposeAttachmentMatrix(
                definition.Base,
                definition.Table,
                tableMotion.X,
                tableMotion.Y,
                tableMotion.Z);
            Transform3D tableTransform = new MatrixTransform3D(tableAttach);
            result[MachineNodeIds.Table] = tableTransform;

            Vector3D spindleMotion = ResolveMotionVector(definition, definition.Spindle.MotionAxes, x, y, z);
            Matrix3D spindleAttach = MachineNodeMeshTransforms.ComposeAttachmentMatrix(
                definition.Base,
                definition.Spindle,
                spindleMotion.X,
                spindleMotion.Y,
                spindleMotion.Z);
            Transform3D spindleTransform = new MatrixTransform3D(spindleAttach);
            result[MachineNodeIds.Spindle] = spindleTransform;

            foreach (MachineExtraNodeDefinition extra in TopologicalSortExtras(definition.ExtraNodes))
            {
                if (!result.TryGetValue(extra.ParentNodeId, out Transform3D? parentTransform))
                {
                    parentTransform = baseTransform;
                }

                Vector3D motion = extra.MotionLink switch
                {
                    MachineMotionLink.Table => ResolveTableChainMotion(definition, extra.MotionAxes, x, y, z),
                    MachineMotionLink.Spindle => ResolveMotionVector(definition, extra.MotionAxes, x, y, z),
                    _ => new Vector3D(0, 0, 0)
                };

                MachineNodeDefinition parentDef = ResolveParentDefinition(definition, extra.ParentNodeId);
                Matrix3D attach = MachineNodeMeshTransforms.ComposeAttachmentMatrix(
                    parentDef,
                    extra,
                    motion.X,
                    motion.Y,
                    motion.Z);
                result[extra.Id] = new MatrixTransform3D(attach * GetMatrix(parentTransform));
            }

            return result;
        }

        /// <summary>Режущая вершина (TCP УП) в MCS: плоскость крепления минус вылет вдоль -Z.</summary>
        public static Point3D ComputeToolCenterPoint(
            MachineDefinition definition,
            double physicalX,
            double physicalY,
            double physicalZ,
            double stickOutMm = 50)
        {
            Point3D holderPlane = ToolMountMcsHelper.ComputeTcpPhysical(definition, physicalX, physicalY, physicalZ);
            if (stickOutMm <= 1e-9)
            {
                return holderPlane;
            }

            return ToolHolderKinematics.ComputeToolTipFromHolder(holderPlane, stickOutMm);
        }

        /// <summary>World point where workpiece bottom sits (after optional fixture height).</summary>
        public static Point3D ComputeWorkpieceMountPoint(
            MachineDefinition definition,
            IReadOnlyDictionary<string, Transform3D> transforms,
            Rect3D? tableMeshBoundsLocal = null) =>
            WorkpieceMountPlacement.ComputeMountWorldWithFixture(definition, transforms, tableMeshBoundsLocal);

        private static MachineNodeDefinition ResolveParentDefinition(MachineDefinition definition, string parentNodeId)
        {
            if (string.Equals(parentNodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase))
            {
                return definition.Base;
            }

            if (string.Equals(parentNodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase))
            {
                return definition.Table;
            }

            if (string.Equals(parentNodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase))
            {
                return definition.Spindle;
            }

            MachineExtraNodeDefinition? extra = definition.ExtraNodes.FirstOrDefault(n =>
                string.Equals(n.Id, parentNodeId, StringComparison.OrdinalIgnoreCase));
            if (extra != null)
            {
                return new MachineNodeDefinition
                {
                    AttachOnParent = extra.AttachOnParent.Clone(),
                    AttachOnChild = extra.AttachOnChild.Clone()
                };
            }

            return definition.Base;
        }

        private static Vector3D ResolveMotionVector(MachineDefinition definition, MachineProgramAxisMask mask, double x, double y, double z)
        {
            // Profile attach/limits are in MCS; parser pose is physical = MCS + McsZeroOffset.
            var mcs = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            double mcsX = MachineMcsCoordinates.PhysicalAxisToMcs(x, mcs.X);
            double mcsY = MachineMcsCoordinates.PhysicalAxisToMcs(y, mcs.Y);
            double mcsZ = MachineMcsCoordinates.PhysicalAxisToMcs(z, mcs.Z);
            return new(
                mask.HasFlag(MachineProgramAxisMask.X) ? mcsX - definition.GetAxisHomeMcs("X") : 0,
                mask.HasFlag(MachineProgramAxisMask.Y) ? mcsY - definition.GetAxisHomeMcs("Y") : 0,
                mask.HasFlag(MachineProgramAxisMask.Z) ? mcsZ - definition.GetAxisHomeMcs("Z") : 0);
        }

        /// <summary>
        /// Стол XY: оси X/Y в кинематике противоположны направлению координат X/Y в УП/WCS на столе.
        /// </summary>
        private static Vector3D ResolveTableChainMotion(
            MachineDefinition definition,
            MachineProgramAxisMask mask,
            double x,
            double y,
            double z)
        {
            Vector3D motion = ResolveMotionVector(definition, mask, x, y, z);
            if (!MachineKinematics.UsesTableMountedWorkpiece(definition))
            {
                return motion;
            }

            return new Vector3D(
                mask.HasFlag(MachineProgramAxisMask.X) ? -motion.X : motion.X,
                mask.HasFlag(MachineProgramAxisMask.Y) ? -motion.Y : motion.Y,
                motion.Z);
        }

        private static Matrix3D GetMatrix(Transform3D transform) =>
            transform is MatrixTransform3D matrix ? matrix.Value : Matrix3D.Identity;

        private static List<MachineExtraNodeDefinition> TopologicalSortExtras(IReadOnlyList<MachineExtraNodeDefinition> extras)
        {
            var list = extras.ToList();
            var sorted = new List<MachineExtraNodeDefinition>();
            var remaining = new HashSet<string>(list.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
            int guard = list.Count * list.Count + 1;

            while (remaining.Count > 0 && guard-- > 0)
            {
                bool progressed = false;
                foreach (MachineExtraNodeDefinition extra in list)
                {
                    if (!remaining.Contains(extra.Id))
                    {
                        continue;
                    }

                    bool parentReady = MachineNodeIds.IsBuiltIn(extra.ParentNodeId)
                        || sorted.Any(s => string.Equals(s.Id, extra.ParentNodeId, StringComparison.OrdinalIgnoreCase));
                    if (!parentReady)
                    {
                        continue;
                    }

                    sorted.Add(extra);
                    remaining.Remove(extra.Id);
                    progressed = true;
                }

                if (!progressed)
                {
                    sorted.AddRange(list.Where(e => remaining.Contains(e.Id)));
                    break;
                }
            }

            return sorted;
        }
    }
}
