using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    /// <summary>TCP / точка крепления в машинной системе координат (MCS).</summary>
    public static class ToolMountMcsHelper
    {
        /// <summary>Физическая позиция TCP при заданной физической позе осей (мм).</summary>
        public static Point3D ComputeTcpPhysical(MachineDefinition definition, double physicalX, double physicalY, double physicalZ)
        {
            var mcsOffset = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            double mcsPoseX = physicalX - mcsOffset.X;
            double mcsPoseY = physicalY - mcsOffset.Y;
            double mcsPoseZ = physicalZ - mcsOffset.Z;

            double tcpMcsX = definition.ToolMount.X + (mcsPoseX - definition.GetAxisHomeMcs("X"));
            double tcpMcsY = definition.ToolMount.Y + (mcsPoseY - definition.GetAxisHomeMcs("Y"));
            double tcpMcsZ = definition.ToolMount.Z + (mcsPoseZ - definition.GetAxisHomeMcs("Z"));

            return new Point3D(
                tcpMcsX + mcsOffset.X,
                tcpMcsY + mcsOffset.Y,
                tcpMcsZ + mcsOffset.Z);
        }

        /// <summary>Вычисляет <see cref="MachineDefinition.ToolMount"/> (TCP в MCS) по мировой точке при текущей позе осей.</summary>
        public static MachineGeometryPoint InferToolMountMcsFromWorldTcp(
            MachineDefinition definition,
            Point3D worldTcp,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            var mcsOffset = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            double mcsPoseX = physicalX - mcsOffset.X;
            double mcsPoseY = physicalY - mcsOffset.Y;
            double mcsPoseZ = physicalZ - mcsOffset.Z;

            return new MachineGeometryPoint
            {
                X = worldTcp.X - mcsOffset.X - mcsPoseX + definition.GetAxisHomeMcs("X"),
                Y = worldTcp.Y - mcsOffset.Y - mcsPoseY + definition.GetAxisHomeMcs("Y"),
                Z = worldTcp.Z - mcsOffset.Z - mcsPoseZ + definition.GetAxisHomeMcs("Z")
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
            MachineGeometryPoint tm = definition.ToolMount;
            if (Math.Abs(tm.X) > 120 || Math.Abs(tm.Y) > 120 || Math.Abs(tm.Z) > 120)
            {
                definition.ToolMount = MachineGeometryPoint.Zero;
            }
        }
    }
}
