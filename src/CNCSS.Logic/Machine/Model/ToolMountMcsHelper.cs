using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    /// <summary>TCP / точка крепления инструмента на выбранном узле станка.</summary>
    public static class ToolMountMcsHelper
    {
        /// <summary>Физическая позиция TCP при заданной физической позе осей (мм).</summary>
        public static Point3D ComputeTcpPhysical(MachineDefinition definition, double physicalX, double physicalY, double physicalZ)
        {
            var mcsOffset = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            Transform3D nodeTransform = ResolveToolMountNodeTransform(definition, physicalX, physicalY, physicalZ);
            Point3D tcpAssembly = nodeTransform.Transform(new Point3D(
                definition.ToolMount.X,
                definition.ToolMount.Y,
                definition.ToolMount.Z));

            return new Point3D(
                tcpAssembly.X + mcsOffset.X,
                tcpAssembly.Y + mcsOffset.Y,
                tcpAssembly.Z + mcsOffset.Z);
        }

        /// <summary>Вычисляет <see cref="MachineDefinition.ToolMount"/> в локальной СК узла крепления по мировой точке.</summary>
        public static MachineGeometryPoint InferToolMountMcsFromWorldTcp(
            MachineDefinition definition,
            Point3D worldTcp,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            var mcsOffset = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            Transform3D nodeTransform = ResolveToolMountNodeTransform(definition, physicalX, physicalY, physicalZ);
            Matrix3D node = nodeTransform.Value;
            if (!node.HasInverse)
            {
                return definition.ToolMount.Clone();
            }

            node.Invert();
            Point3D assemblyTcp = new(
                worldTcp.X - mcsOffset.X,
                worldTcp.Y - mcsOffset.Y,
                worldTcp.Z - mcsOffset.Z);
            Point3D localTcp = node.Transform(assemblyTcp);

            return new MachineGeometryPoint
            {
                X = localTcp.X,
                Y = localTcp.Y,
                Z = localTcp.Z
            };
        }

        public static MachineGeometryPoint SetTcpMcsZFromWorldPick(
            MachineDefinition definition,
            Point3D worldTcp,
            double physicalX,
            double physicalY,
            double physicalZ,
            MachineGeometryPoint currentTcpMcs)
        {
            MachineGeometryPoint inferred = InferToolMountMcsFromWorldTcp(definition, worldTcp, physicalX, physicalY, physicalZ);
            return new MachineGeometryPoint
            {
                X = currentTcpMcs.X,
                Y = currentTcpMcs.Y,
                Z = inferred.Z
            };
        }

        /// <summary>Старые профили хранили смещение от центра меша; сбрасываем в ноль MCS.</summary>
        public static void NormalizeLegacyMeshOffsetToolMount(MachineDefinition definition)
        {
            if (definition.ToolMountIsNodeLocal)
            {
                return;
            }

            MachineGeometryPoint tm = definition.ToolMount;
            if (Math.Abs(tm.X) > 120 || Math.Abs(tm.Y) > 120 || Math.Abs(tm.Z) > 120)
            {
                definition.ToolMount = MachineGeometryPoint.Zero;
            }
        }

        public static Transform3D ResolveToolMountNodeTransform(
            MachineDefinition definition,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            string nodeId = string.IsNullOrWhiteSpace(definition.ToolMountNodeId)
                ? MachineNodeIds.Spindle
                : definition.ToolMountNodeId;
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            return transforms.TryGetValue(nodeId, out Transform3D? transform)
                ? transform
                : Transform3D.Identity;
        }
    }
}
