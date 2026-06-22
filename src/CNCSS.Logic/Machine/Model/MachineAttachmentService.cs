using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    /// <summary>Точки привязки и перенос MCS в превью конструктора.</summary>
    public static class MachineAttachmentService
    {
        /// <summary>Мировые координаты нуля MCS: после <see cref="PlaceMcsOriginAtScene"/> всегда (0,0,0).</summary>
        public static Point3D GetMcsOriginScene(MachineDefinition definition)
        {
            _ = definition;
            return new Point3D(0, 0, 0);
        }

        public static Point3D GetWcsOriginScene(MachineDefinition definition, MachineGeometryPoint wcsOffsetMcs)
        {
            var mcs = MachineMcsCoordinates.GetMcsZeroOffset(definition);
            return new Point3D(
                mcs.X + wcsOffsetMcs.X,
                mcs.Y + wcsOffsetMcs.Y,
                mcs.Z + wcsOffsetMcs.Z);
        }

        public static Point3D SceneToAssembly(Point3D scene, MachineDefinition definition)
        {
            var mcs = MachineMcsCoordinates.GetMcsZeroOffset(definition);
            return new Point3D(scene.X - mcs.X, scene.Y - mcs.Y, scene.Z - mcs.Z);
        }

        public static Point3D GetSphereScenePosition(
            MachineDefinition definition,
            string sphereId,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            if (MachineAttachmentSphereIds.IsMcs(sphereId))
            {
                return GetMcsOriginScene(definition);
            }

            return MachineKinematics.GetAttachmentPointScene(
                definition,
                sphereId,
                physicalX,
                physicalY,
                physicalZ);
        }

        public static bool TryApplyAttachmentSceneTarget(
            MachineDefinition definition,
            string nodeId,
            Point3D targetScene,
            double physicalX,
            double physicalY,
            double physicalZ,
            out MachineGeometryPoint newAttachChild,
            out MachineGeometryPoint newMeshOffset)
        {
            newAttachChild = MachineGeometryPoint.Zero;
            newMeshOffset = MachineGeometryPoint.Zero;

            Point3D targetAssembly = SceneToAssembly(targetScene, definition);

            if (definition.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                if (!MachineKinematics.TrySetAttachmentPointAssemblyMcs(
                        builtIn,
                        definition,
                        nodeId,
                        targetAssembly,
                        physicalX,
                        physicalY,
                        physicalZ))
                {
                    return false;
                }

                newAttachChild = builtIn.AttachOnChild.Clone();
                newMeshOffset = builtIn.MeshOffset.Clone();
                return true;
            }

            if (definition.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                if (!MachineKinematics.TrySetAttachmentPointAssemblyMcs(
                        extra,
                        definition,
                        nodeId,
                        targetAssembly,
                        physicalX,
                        physicalY,
                        physicalZ))
                {
                    return false;
                }

                newAttachChild = extra.AttachOnChild.Clone();
                newMeshOffset = extra.MeshOffset.Clone();
                return true;
            }

            return false;
        }

        public static Point3D GetAttachmentSphereScenePosition(
            MachineDefinition definition,
            string sphereId,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            GetSphereScenePosition(definition, sphereId, physicalX, physicalY, physicalZ);

        public static void AlignMachineSoScenePointAtOrigin(
            MachineDefinition definition,
            double sceneX,
            double sceneY,
            double sceneZ) =>
            definition.AlignSceneGeometrySoWorldPointAtOrigin(sceneX, sceneY, sceneZ);

        /// <summary>
        /// Назначает MCS в точке сцены (мм): выбранная точка станка становится WORLD/MCS (0,0,0),
        /// вся сборка смещается с сохранением взаимного положения узлов.
        /// </summary>
        public static void PlaceMcsOriginAtScene(
            MachineDefinition definition,
            double sceneX,
            double sceneY,
            double sceneZ,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            definition.AlignSceneGeometrySoWorldPointAtOrigin(sceneX, sceneY, sceneZ);
    }
}
