using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Machine.Model
{
    /// <summary>
    /// Координаты дочерних объектов <c>table.Root</c> (заготовка, контур): СК кинематического узла стола,
    /// без mesh и без сдвига MCS на корне сборки (MCS задаётся сдвигом корня сборки в симуляции).
    /// </summary>
    public static class TableSceneTransforms
    {
        /// <summary>Кинематика стола (table.Root.Transform) в MCS.</summary>
        public static Matrix3D BuildTableKinematicMatrix(
            MachineDefinition definition,
            double machineX,
            double machineY,
            double machineZ)
        {
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, machineX, machineY, machineZ);

            if (transforms.TryGetValue(MachineNodeIds.Table, out Transform3D? kinematic))
            {
                return ToMatrix(kinematic);
            }

            return Matrix3D.Identity;
        }

        /// <summary>TCP или другая точка в MCS → table.Root local.</summary>
        public static Point3D TcpMcsToTableRootLocal(
            MachineDefinition definition,
            double machineX,
            double machineY,
            double machineZ,
            Point3D pointInMcs)
        {
            Matrix3D kinematic = BuildTableKinematicMatrix(definition, machineX, machineY, machineZ);
            if (!kinematic.HasInverse)
            {
                return pointInMcs;
            }

            kinematic.Invert();
            return kinematic.Transform(pointInMcs);
        }

        /// <summary>Абсолютная сцена (после сдвига MCS на сборке) → table.Root local.</summary>
        public static Point3D SceneAbsoluteToTableRootLocal(
            MachineDefinition definition,
            double machineX,
            double machineY,
            double machineZ,
            Point3D scenePoint)
        {
            MachineGeometryPoint mcs = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            Point3D inMcs = new(
                scenePoint.X - mcs.X,
                scenePoint.Y - mcs.Y,
                scenePoint.Z - mcs.Z);
            return TcpMcsToTableRootLocal(definition, machineX, machineY, machineZ, inMcs);
        }

        /// <inheritdoc cref="SceneAbsoluteToTableRootLocal"/>
        public static Point3D SceneToTableLocal(
            MachineDefinition definition,
            double machineX,
            double machineY,
            double machineZ,
            Point3D scenePoint) =>
            SceneAbsoluteToTableRootLocal(definition, machineX, machineY, machineZ, scenePoint);

        /// <summary>table.Root local → абсолютная сцена viewport (обратное к <see cref="SceneToTableLocal"/>).</summary>
        public static Point3D TableRootLocalToSceneAbsolute(
            MachineDefinition definition,
            double machineX,
            double machineY,
            double machineZ,
            Point3D tableLocal)
        {
            Matrix3D kinematic = BuildTableKinematicMatrix(definition, machineX, machineY, machineZ);
            Point3D inMcs = kinematic.Transform(tableLocal);
            MachineGeometryPoint mcs = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            return new Point3D(
                inMcs.X + mcs.X,
                inMcs.Y + mcs.Y,
                inMcs.Z + mcs.Z);
        }

        public static Point3D SceneToTableLocal(
            MachineDefinition definition,
            MachineState state,
            Point3D scenePoint) =>
            SceneToTableLocal(definition, state.X, state.Y, state.Z, scenePoint);

        /// <summary>Устаревшее имя: только кинематика стола (без mesh/MCS).</summary>
        public static Matrix3D BuildTableToSceneMatrix(
            MachineDefinition definition,
            double machineX,
            double machineY,
            double machineZ) =>
            BuildTableKinematicMatrix(definition, machineX, machineY, machineZ);

        private static Matrix3D CreateTranslation(double x, double y, double z)
        {
            var matrix = Matrix3D.Identity;
            matrix.OffsetX = x;
            matrix.OffsetY = y;
            matrix.OffsetZ = z;
            return matrix;
        }

        private static Matrix3D ToMatrix(Transform3D transform)
        {
            if (transform is MatrixTransform3D matrixTransform)
            {
                return matrixTransform.Value;
            }

            if (transform is Transform3DGroup group)
            {
                Matrix3D combined = Matrix3D.Identity;
                foreach (Transform3D child in group.Children)
                {
                    combined *= ToMatrix(child);
                }

                return combined;
            }

            return Matrix3D.Identity;
        }
    }
}
