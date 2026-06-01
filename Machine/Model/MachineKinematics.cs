using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Machine.Model
{
    /// <summary>
    /// Модель координат станка: MCS, WCS, точки привязки узлов и целевые позиции осей в MCS.
    /// </summary>
    /// <remarks>
    /// <para><b>MCS</b> — машинная СК: ноль в профиле (<see cref="MachineDefinition.McsZeroOffset"/> в сцене).
    /// При <c>McsZeroOffset = (0,0,0)</c> ноль MCS совпадает с центром сцены. Все перемещения осей
    /// в G-коде задаются как физическая поза = поза в MCS + смещение MCS.</para>
    /// <para><b>WCS</b> — рабочая СК (G54–G59): смещение <see cref="MachineState.WorkOffset"/> относительно MCS
    /// для программирования детали. Абсолютная цель оси: <c>X_физ = X_прог + WCS.X + MCS_смещение.X</c>.</para>
    /// <para><b>Точка привязки</b> — <see cref="MachineNodeDefinition.AttachOnChild"/> в локальной СК узла (STL);
    /// при движении осей узел трансформируется так, что шарнир следует за осями, а не центр меша.</para>
    /// </remarks>
    public static class MachineKinematics
    {
        /// <summary>Поза осей X/Y/Z в MCS из физических координат парсера (мм).</summary>
        public static (double X, double Y, double Z) GetAxisPoseMcs(
            MachineDefinition definition,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            var mcs = MachineMcsCoordinates.GetMcsZeroOffset(definition);
            return (
                MachineMcsCoordinates.PhysicalAxisToMcs(physicalX, mcs.X),
                MachineMcsCoordinates.PhysicalAxisToMcs(physicalY, mcs.Y),
                MachineMcsCoordinates.PhysicalAxisToMcs(physicalZ, mcs.Z));
        }

        /// <summary>
        /// Целевая позиция приводного узла в MCS: для стола — (X, Y, 0), для шпинделя — (0, 0, Z);
        /// для узлов без движения по оси компонента = 0.
        /// </summary>
        public static MachineGeometryPoint GetTargetPositionMcs(
            MachineDefinition definition,
            MachineProgramAxisMask motionAxes,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            (double mx, double my, double mz) = GetAxisPoseMcs(definition, physicalX, physicalY, physicalZ);
            return new MachineGeometryPoint
            {
                X = motionAxes.HasFlag(MachineProgramAxisMask.X) ? mx : 0,
                Y = motionAxes.HasFlag(MachineProgramAxisMask.Y) ? my : 0,
                Z = motionAxes.HasFlag(MachineProgramAxisMask.Z) ? mz : 0
            };
        }

        public static MachineGeometryPoint GetTargetPositionMcs(
            MachineDefinition definition,
            MachineNodeDefinition node,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            GetTargetPositionMcs(definition, node.MotionAxes, physicalX, physicalY, physicalZ);

        /// <summary>Смещение WCS (G54–G59) как вектор в MCS (мм).</summary>
        public static MachineGeometryPoint GetWcsOffsetMcs(MachineState.WorkOffset workOffset) =>
            new() { X = workOffset.X, Y = workOffset.Y, Z = workOffset.Z };

        /// <summary>
        /// Абсолютная физическая координата оси из значения в программе (G90) с учётом WCS и нуля MCS.
        /// </summary>
        public static double ProgramAxisToPhysical(
            double programAxis,
            double workOffsetComponent,
            double machineZeroOffsetComponent,
            bool isAbsolute) =>
            isAbsolute
                ? programAxis + workOffsetComponent + machineZeroOffsetComponent
                : throw new InvalidOperationException("Use current axis + increment for G91.");

        /// <summary>
        /// Кинематическая матрица узла для расчёта точки привязки (без двойного учёта AttachOnChild в travel).
        /// </summary>
        public static Transform3D GetKinematicTransformForAttachment(
            MachineDefinition definition,
            string nodeId,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            MachineNodeDefinition? node = definition.TryGetBuiltInNode(nodeId);
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (node == null)
            {
                return transforms.TryGetValue(nodeId, out Transform3D? t) ? t : Transform3D.Identity;
            }

            var saved = node.AttachOnChild.Clone();
            try
            {
                node.AttachOnChild = MachineGeometryPoint.Zero;
                transforms = KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
                return transforms.TryGetValue(nodeId, out Transform3D? t) ? t : Transform3D.Identity;
            }
            finally
            {
                node.AttachOnChild = saved;
            }
        }

        /// <summary>
        /// Точка привязки (<see cref="MachineNodeDefinition.AttachOnChild"/>) в СК сборки (MCS), мм.
        /// </summary>
        public static Point3D GetAttachmentPointAssemblyMcs(
            MachineDefinition definition,
            string nodeId,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinematic))
            {
                return new Point3D(0, 0, 0);
            }

            if (definition.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                return MachineNodeMeshTransforms.GetAttachOnChildScenePoint(kinematic, builtIn);
            }

            if (definition.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                return MachineNodeMeshTransforms.GetAttachOnChildScenePoint(kinematic, extra);
            }

            return new Point3D(0, 0, 0);
        }

        /// <summary>
        /// Начало координат STL узла в MCS (мм): сцена минус <see cref="MachineDefinition.McsZeroOffset"/>.
        /// Соответствует «положению узла» на схеме привязки к MCS/WORLD.
        /// </summary>
        public static Point3D GetNodeMeshOriginAssemblyMcs(
            MachineDefinition definition,
            string nodeId,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            Point3D scene = MachineAttachmentSceneMath.MeshLocalToScene(
                definition,
                nodeId,
                MachineGeometryPoint.Zero,
                physicalX,
                physicalY,
                physicalZ);
            var mcs = MachineMcsCoordinates.GetMcsZeroOffset(definition);
            return new Point3D(
                scene.X - mcs.X,
                scene.Y - mcs.Y,
                scene.Z - mcs.Z);
        }

        /// <summary>Шарнир узла в MCS (мм) — точка оси в схеме (зелёный/красный маркер).</summary>
        public static Point3D GetNodeCouplingAssemblyMcs(
            MachineDefinition definition,
            string nodeId,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            GetMotionCouplingPointAssemblyMcs(definition, nodeId, physicalX, physicalY, physicalZ);

        /// <summary>Точка привязки в абсолютной сцене (MCS + <see cref="MachineDefinition.McsZeroOffset"/>).</summary>
        public static Point3D GetAttachmentPointScene(
            MachineDefinition definition,
            string nodeId,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            Point3D assembly = GetAttachmentPointAssemblyMcs(definition, nodeId, physicalX, physicalY, physicalZ);
            var mcs = MachineMcsCoordinates.GetMcsZeroOffset(definition);
            return new Point3D(
                assembly.X + mcs.X,
                assembly.Y + mcs.Y,
                assembly.Z + mcs.Z);
        }

        /// <summary>
        /// Шарнир узла (AttachOnChild после хода по осям) в СК сборки — совпадает с
        /// <see cref="MachineNodeMeshTransforms.GetMotionCouplingPoint"/> при стандартном решателе.
        /// </summary>
        public static Point3D GetMotionCouplingPointAssemblyMcs(
            MachineDefinition definition,
            string nodeId,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinematic))
            {
                return new Point3D(0, 0, 0);
            }

            MachineGeometryPoint attachOnChild = definition.TryGetBuiltInNode(nodeId)?.AttachOnChild
                ?? definition.TryGetExtraNode(nodeId)?.AttachOnChild
                ?? MachineGeometryPoint.Zero;

            return MachineNodeMeshTransforms.GetMotionCouplingPoint(kinematic, attachOnChild);
        }

        /// <summary>
        /// Переносит <see cref="MachineNodeDefinition.AttachOnChild"/> так, чтобы точка привязки
        /// в сборке совпала с <paramref name="targetAssemblyMcs"/> при текущей позе осей.
        /// </summary>
        public static bool TrySetAttachmentPointAssemblyMcs(
            MachineNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            Point3D targetAssemblyMcs,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            TrySetAttachOnChildPreservingMeshInScene(
                definition,
                nodeId,
                targetAssemblyMcs,
                physicalX,
                physicalY,
                physicalZ,
                attach => node.AttachOnChild = attach,
                mesh => node.MeshOffset = mesh,
                () => node.MeshOffset,
                () => MachineNodeMeshTransforms.BuildMeshLocalTransform(node),
                () => node.MeshRotationDegrees,
                () => node.MeshScale);

        public static bool TrySetAttachmentPointAssemblyMcs(
            MachineExtraNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            Point3D targetAssemblyMcs,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            TrySetAttachOnChildPreservingMeshInScene(
                definition,
                nodeId,
                targetAssemblyMcs,
                physicalX,
                physicalY,
                physicalZ,
                attach => node.AttachOnChild = attach,
                mesh => node.MeshOffset = mesh,
                () => node.MeshOffset,
                () => MachineNodeMeshTransforms.BuildMeshLocalTransform(node),
                () => node.MeshRotationDegrees,
                () => node.MeshScale);

        /// <summary>
        /// Переносит точку привязки в сборке; STL в сцене остаётся на месте (компенсация MeshOffset).
        /// </summary>
        private static bool TrySetAttachOnChildPreservingMeshInScene(
            MachineDefinition definition,
            string nodeId,
            Point3D targetAssemblyMcs,
            double physicalX,
            double physicalY,
            double physicalZ,
            Action<MachineGeometryPoint> applyAttachOnChild,
            Action<MachineGeometryPoint> applyMeshOffset,
            Func<MachineGeometryPoint> getMeshOffset,
            Func<Transform3D> buildMeshTransform,
            Func<MachineGeometryPoint> getMeshRotation,
            Func<double> getMeshScale)
        {
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinBefore))
            {
                return false;
            }

            Transform3D meshBefore = buildMeshTransform();
            if (!MachineNodeMeshTransforms.TryResolveAttachOnChildLocalFromAssembly(
                    kinBefore,
                    meshBefore,
                    targetAssemblyMcs,
                    out MachineGeometryPoint attachLocal))
            {
                return false;
            }

            return TryApplyAttachOnChildLocalPreservingMeshInScene(
                definition,
                nodeId,
                physicalX,
                physicalY,
                physicalZ,
                applyAttachOnChild,
                applyMeshOffset,
                getMeshOffset,
                buildMeshTransform,
                getMeshRotation,
                getMeshScale,
                attachLocal);
        }

        /// <summary>
        /// Задаёт AttachOnChild в локальной СК меша; T_kinematic·T_mesh в сцене не меняется.
        /// </summary>
        private static bool TryApplyAttachOnChildLocalPreservingMeshInScene(
            MachineDefinition definition,
            string nodeId,
            double physicalX,
            double physicalY,
            double physicalZ,
            Action<MachineGeometryPoint> applyAttachOnChild,
            Action<MachineGeometryPoint> applyMeshOffset,
            Func<MachineGeometryPoint> getMeshOffset,
            Func<Transform3D> buildMeshTransform,
            Func<MachineGeometryPoint> getMeshRotation,
            Func<double> getMeshScale,
            MachineGeometryPoint attachLocal)
        {
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinBefore))
            {
                return false;
            }

            Dictionary<string, Matrix3D> subtreeChains =
                SnapshotSubtreeMeshWorldChains(definition, nodeId, physicalX, physicalY, physicalZ);

            MachineGeometryPoint meshOffsetBefore = getMeshOffset().Clone();
            Transform3D meshBefore = buildMeshTransform();

            applyAttachOnChild(attachLocal.Clone());

            transforms = KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinAfter))
            {
                return false;
            }

            if (!MachineNodeMeshTransforms.TryCompensateMeshOffsetAfterAttachChange(
                    meshOffsetBefore,
                    getMeshRotation(),
                    getMeshScale(),
                    kinBefore,
                    kinAfter,
                    out MachineGeometryPoint newMeshOffset))
            {
                return false;
            }

            applyMeshOffset(newMeshOffset);

            Transform3D meshAfter = buildMeshTransform();
            if (!MachineNodeMeshTransforms.IsSameMeshWorldTransform(
                    kinBefore,
                    meshBefore,
                    kinAfter,
                    meshAfter))
            {
                return false;
            }

            return TryRestoreDescendantMeshWorldChains(
                definition,
                nodeId,
                subtreeChains,
                physicalX,
                physicalY,
                physicalZ);
        }

        /// <summary>
        /// Задаёт AttachOnChild в локальной СК меша; меш узла и потомков в сцене не смещаются.
        /// </summary>
        public static bool TrySetAttachOnChildLocalPreservingSubtreeLayout(
            MachineNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            MachineGeometryPoint attachLocal,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            TryApplyAttachOnChildLocalPreservingMeshInScene(
                definition,
                nodeId,
                physicalX,
                physicalY,
                physicalZ,
                attach => node.AttachOnChild = attach,
                mesh => node.MeshOffset = mesh,
                () => node.MeshOffset,
                () => MachineNodeMeshTransforms.BuildMeshLocalTransform(node),
                () => node.MeshRotationDegrees,
                () => node.MeshScale,
                attachLocal);

        public static bool TrySetAttachOnChildLocalPreservingSubtreeLayout(
            MachineExtraNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            MachineGeometryPoint attachLocal,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            TryApplyAttachOnChildLocalPreservingMeshInScene(
                definition,
                nodeId,
                physicalX,
                physicalY,
                physicalZ,
                attach => node.AttachOnChild = attach,
                mesh => node.MeshOffset = mesh,
                () => node.MeshOffset,
                () => MachineNodeMeshTransforms.BuildMeshLocalTransform(node),
                () => node.MeshRotationDegrees,
                () => node.MeshScale,
                attachLocal);

        /// <summary>
        /// Меняет <see cref="MachineNodeDefinition.AttachOnParent"/>; меш узла и всех потомков в сцене остаются на месте.
        /// </summary>
        public static bool TrySetAttachOnParentPreservingSubtreeLayout(
            MachineNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            MachineGeometryPoint newAttachOnParent,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            TrySetAttachOnParentPreservingSubtreeLayout(
                definition,
                nodeId,
                newAttachOnParent,
                physicalX,
                physicalY,
                physicalZ,
                parent => node.AttachOnParent = parent,
                mesh => node.MeshOffset = mesh,
                () => node.MeshOffset,
                () => node.MeshRotationDegrees,
                () => node.MeshScale);

        public static bool TrySetAttachOnParentPreservingSubtreeLayout(
            MachineExtraNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            MachineGeometryPoint newAttachOnParent,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            TrySetAttachOnParentPreservingSubtreeLayout(
                definition,
                nodeId,
                newAttachOnParent,
                physicalX,
                physicalY,
                physicalZ,
                parent => node.AttachOnParent = parent,
                mesh => node.MeshOffset = mesh,
                () => node.MeshOffset,
                () => node.MeshRotationDegrees,
                () => node.MeshScale);

        private static bool TrySetAttachOnParentPreservingSubtreeLayout(
            MachineDefinition definition,
            string nodeId,
            MachineGeometryPoint newAttachOnParent,
            double physicalX,
            double physicalY,
            double physicalZ,
            Action<MachineGeometryPoint> applyAttachOnParent,
            Action<MachineGeometryPoint> applyMeshOffset,
            Func<MachineGeometryPoint> getMeshOffset,
            Func<MachineGeometryPoint> getMeshRotation,
            Func<double> getMeshScale)
        {
            Dictionary<string, Matrix3D> subtreeChains =
                SnapshotSubtreeMeshWorldChains(definition, nodeId, physicalX, physicalY, physicalZ);

            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinBefore))
            {
                return false;
            }

            Transform3D meshBefore = BuildMeshTransformForNode(definition, nodeId);
            MachineGeometryPoint meshOffsetBefore = getMeshOffset().Clone();

            applyAttachOnParent(newAttachOnParent.Clone());

            transforms = KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinAfter))
            {
                return false;
            }

            if (!MachineNodeMeshTransforms.TryCompensateMeshOffsetAfterAttachChange(
                    meshOffsetBefore,
                    getMeshRotation(),
                    getMeshScale(),
                    kinBefore,
                    kinAfter,
                    out MachineGeometryPoint newMeshOffset))
            {
                return false;
            }

            applyMeshOffset(newMeshOffset);

            Transform3D meshAfter = BuildMeshTransformForNode(definition, nodeId);
            if (!MachineNodeMeshTransforms.IsSameMeshWorldTransform(
                    kinBefore,
                    meshBefore,
                    kinAfter,
                    meshAfter))
            {
                return false;
            }

            return TryRestoreDescendantMeshWorldChains(
                definition,
                nodeId,
                subtreeChains,
                physicalX,
                physicalY,
                physicalZ);
        }

        /// <summary>Снимок T_kinematic·T_mesh для узла и всех дочерних extra-узлов.</summary>
        private static Dictionary<string, Matrix3D> SnapshotSubtreeMeshWorldChains(
            MachineDefinition definition,
            string rootNodeId,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            var chains = new Dictionary<string, Matrix3D>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);

            foreach (string nodeId in EnumerateSubtreeNodeIds(definition, rootNodeId))
            {
                if (!transforms.TryGetValue(nodeId, out Transform3D? kinematic))
                {
                    continue;
                }

                Transform3D mesh = BuildMeshTransformForNode(definition, nodeId);
                chains[nodeId] = MachineNodeMeshTransforms.GetKinMeshChainMatrix(kinematic, mesh);
            }

            return chains;
        }

        private static bool TryRestoreDescendantMeshWorldChains(
            MachineDefinition definition,
            string rootNodeId,
            IReadOnlyDictionary<string, Matrix3D> chainsBefore,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            List<string> descendants = CollectDescendantNodeIds(definition, rootNodeId);
            if (descendants.Count == 0)
            {
                return true;
            }

            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);

            foreach (string nodeId in descendants)
            {
                if (!chainsBefore.TryGetValue(nodeId, out Matrix3D targetChain))
                {
                    continue;
                }

                if (!transforms.TryGetValue(nodeId, out Transform3D? kinematic))
                {
                    return false;
                }

                if (!TryGetNodeMeshLayout(definition, nodeId, out _, out _, out MachineGeometryPoint rotation, out double scale))
                {
                    return false;
                }

                if (!MachineNodeMeshTransforms.TryRestoreMeshWorldChain(
                        kinematic,
                        targetChain,
                        rotation,
                        scale,
                        out MachineGeometryPoint newMeshOffset))
                {
                    return false;
                }

                ApplyNodeMeshOffset(definition, nodeId, newMeshOffset);
            }

            return true;
        }

        /// <summary>Снимок начала координат меша в сцене (мм) для всех узлов (при переносе MCS).</summary>
        public static Dictionary<string, Point3D> SnapshotAllNodeMeshSceneOrigins(
            MachineDefinition definition,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            var origins = new Dictionary<string, Point3D>(StringComparer.OrdinalIgnoreCase);
            foreach (string nodeId in EnumerateAllMeshNodeIds(definition))
            {
                origins[nodeId] = MachineAttachmentSceneMath.MeshLocalToScene(
                    definition,
                    nodeId,
                    MachineGeometryPoint.Zero,
                    physicalX,
                    physicalY,
                    physicalZ);
            }

            return origins;
        }

        /// <summary>Восстанавливает меши: начало STL в сцене совпадает со снимком.</summary>
        public static bool RestoreAllNodeMeshSceneOrigins(
            MachineDefinition definition,
            IReadOnlyDictionary<string, Point3D> sceneOriginsBefore,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            var mcs = MachineMcsCoordinates.GetMcsZeroOffset(definition);
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);

            foreach (KeyValuePair<string, Point3D> pair in sceneOriginsBefore)
            {
                if (!transforms.TryGetValue(pair.Key, out Transform3D? kinematic))
                {
                    return false;
                }

                if (!TryGetNodeMeshLayout(definition, pair.Key, out _, out _, out MachineGeometryPoint rotation, out double scale))
                {
                    return false;
                }

                Point3D targetAssembly = new(
                    pair.Value.X - mcs.X,
                    pair.Value.Y - mcs.Y,
                    pair.Value.Z - mcs.Z);

                if (!MachineNodeMeshTransforms.TryRestoreMeshAssemblyOrigin(
                        kinematic,
                        targetAssembly,
                        rotation,
                        scale,
                        out MachineGeometryPoint newMeshOffset))
                {
                    return false;
                }

                ApplyNodeMeshOffset(definition, pair.Key, newMeshOffset);
            }

            return true;
        }

        private static IEnumerable<string> EnumerateAllMeshNodeIds(MachineDefinition definition)
        {
            yield return MachineNodeIds.Base;
            yield return MachineNodeIds.Table;
            yield return MachineNodeIds.Spindle;
            foreach (MachineExtraNodeDefinition extra in definition.ExtraNodes)
            {
                yield return extra.Id;
            }
        }

        private static IEnumerable<string> EnumerateSubtreeNodeIds(MachineDefinition definition, string rootNodeId)
        {
            yield return rootNodeId;
            foreach (string descendant in CollectDescendantNodeIds(definition, rootNodeId))
            {
                yield return descendant;
            }
        }

        private static List<string> CollectDescendantNodeIds(MachineDefinition definition, string parentNodeId)
        {
            var result = new List<string>();
            CollectDescendantNodeIds(definition, parentNodeId, result);
            return result;
        }

        private static void CollectDescendantNodeIds(
            MachineDefinition definition,
            string parentNodeId,
            List<string> result)
        {
            foreach (MachineExtraNodeDefinition extra in definition.ExtraNodes)
            {
                if (!string.Equals(extra.ParentNodeId, parentNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(extra.Id);
                CollectDescendantNodeIds(definition, extra.Id, result);
            }
        }

        private static Transform3D BuildMeshTransformForNode(MachineDefinition definition, string nodeId)
        {
            if (definition.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                return MachineNodeMeshTransforms.BuildMeshLocalTransform(builtIn);
            }

            if (definition.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                return MachineNodeMeshTransforms.BuildMeshLocalTransform(extra);
            }

            return Transform3D.Identity;
        }

        private static bool TryGetNodeMeshLayout(
            MachineDefinition definition,
            string nodeId,
            out MachineGeometryPoint meshOffset,
            out MachineGeometryPoint attachOnChild,
            out MachineGeometryPoint rotation,
            out double scale)
        {
            meshOffset = MachineGeometryPoint.Zero;
            attachOnChild = MachineGeometryPoint.Zero;
            rotation = MachineGeometryPoint.Zero;
            scale = 1;

            if (definition.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                meshOffset = builtIn.MeshOffset;
                attachOnChild = builtIn.AttachOnChild;
                rotation = builtIn.MeshRotationDegrees;
                scale = builtIn.MeshScale;
                return true;
            }

            if (definition.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                meshOffset = extra.MeshOffset;
                attachOnChild = extra.AttachOnChild;
                rotation = extra.MeshRotationDegrees;
                scale = extra.MeshScale;
                return true;
            }

            return false;
        }

        private static void ApplyNodeMeshOffset(MachineDefinition definition, string nodeId, MachineGeometryPoint meshOffset)
        {
            if (definition.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                builtIn.MeshOffset = meshOffset.Clone();
                return;
            }

            if (definition.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                extra.MeshOffset = meshOffset.Clone();
            }
        }

        /// <summary>
        /// Пресет STL (низ/центр/верх): AttachOnChild в локальной СК меша; геометрия узла в сцене не смещается,
        /// сфера привязки переходит в выбранную точку на модели.
        /// </summary>
        public static bool TryApplyMeshAttachPreset(
            MachineNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            MeshAttachmentPreset preset,
            Rect3D meshBoundsLocal,
            double physicalX,
            double physicalY,
            double physicalZ,
            out Point3D attachScene)
        {
            attachScene = default;
            if (meshBoundsLocal.IsEmpty)
            {
                return false;
            }

            MachineGeometryPoint attachLocal =
                MachineAttachmentSceneMath.PresetToLocalPoint(preset, meshBoundsLocal).Clone();
            if (!TryApplyAttachOnChildLocalPreservingMeshInScene(
                    definition,
                    nodeId,
                    physicalX,
                    physicalY,
                    physicalZ,
                    attach => node.AttachOnChild = attach,
                    mesh => node.MeshOffset = mesh,
                    () => node.MeshOffset,
                    () => MachineNodeMeshTransforms.BuildMeshLocalTransform(node),
                    () => node.MeshRotationDegrees,
                    () => node.MeshScale,
                    attachLocal))
            {
                return false;
            }

            attachScene = GetAttachmentPointScene(definition, nodeId, physicalX, physicalY, physicalZ);
            return true;
        }

        public static bool TryApplyMeshAttachPreset(
            MachineExtraNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            MeshAttachmentPreset preset,
            Rect3D meshBoundsLocal,
            double physicalX,
            double physicalY,
            double physicalZ,
            out Point3D attachScene)
        {
            attachScene = default;
            if (meshBoundsLocal.IsEmpty)
            {
                return false;
            }

            MachineGeometryPoint attachLocal =
                MachineAttachmentSceneMath.PresetToLocalPoint(preset, meshBoundsLocal).Clone();
            if (!TryApplyAttachOnChildLocalPreservingMeshInScene(
                    definition,
                    nodeId,
                    physicalX,
                    physicalY,
                    physicalZ,
                    attach => node.AttachOnChild = attach,
                    mesh => node.MeshOffset = mesh,
                    () => node.MeshOffset,
                    () => MachineNodeMeshTransforms.BuildMeshLocalTransform(node),
                    () => node.MeshRotationDegrees,
                    () => node.MeshScale,
                    attachLocal))
            {
                return false;
            }

            attachScene = GetAttachmentPointScene(definition, nodeId, physicalX, physicalY, physicalZ);
            return true;
        }

        /// <summary>
        /// Задаёт AttachOnChild в локальной СК STL; точка привязки в сборке (и в сцене) не смещается,
        /// компенсацией MeshOffset.
        /// </summary>
        public static bool TrySetAttachOnChildLocalPreservingAttachmentAssembly(
            MachineNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            MachineGeometryPoint newAttachOnChildLocal,
            double physicalX,
            double physicalY,
            double physicalZ,
            out MachineGeometryPoint newMeshOffset)
        {
            newMeshOffset = node.MeshOffset.Clone();
            Point3D preserveAssembly = GetAttachmentPointAssemblyMcs(
                definition, nodeId, physicalX, physicalY, physicalZ);

            node.AttachOnChild = newAttachOnChildLocal.Clone();

            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinAfter))
            {
                return false;
            }

            if (!MachineNodeMeshTransforms.TryCompensateMeshOffsetSoAttachOnChildAtAssemblyPoint(
                    node.AttachOnChild,
                    node.MeshOffset,
                    node.MeshRotationDegrees,
                    node.MeshScale,
                    kinAfter,
                    preserveAssembly,
                    out newMeshOffset))
            {
                return false;
            }

            node.MeshOffset = newMeshOffset;
            return true;
        }

        /// <inheritdoc cref="TrySetAttachOnChildLocalPreservingAttachmentAssembly(MachineNodeDefinition, MachineDefinition, string, MachineGeometryPoint, double, double, double, out MachineGeometryPoint)"/>
        public static bool TrySetAttachOnChildLocalPreservingAttachmentAssembly(
            MachineExtraNodeDefinition node,
            MachineDefinition definition,
            string nodeId,
            MachineGeometryPoint newAttachOnChildLocal,
            double physicalX,
            double physicalY,
            double physicalZ,
            out MachineGeometryPoint newMeshOffset)
        {
            newMeshOffset = node.MeshOffset.Clone();
            Point3D preserveAssembly = GetAttachmentPointAssemblyMcs(
                definition, nodeId, physicalX, physicalY, physicalZ);

            node.AttachOnChild = newAttachOnChildLocal.Clone();

            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);
            if (!transforms.TryGetValue(nodeId, out Transform3D? kinAfter))
            {
                return false;
            }

            if (!MachineNodeMeshTransforms.TryCompensateMeshOffsetSoAttachOnChildAtAssemblyPoint(
                    node.AttachOnChild,
                    node.MeshOffset,
                    node.MeshRotationDegrees,
                    node.MeshScale,
                    kinAfter,
                    preserveAssembly,
                    out newMeshOffset))
            {
                return false;
            }

            node.MeshOffset = newMeshOffset;
            return true;
        }
    }
}
