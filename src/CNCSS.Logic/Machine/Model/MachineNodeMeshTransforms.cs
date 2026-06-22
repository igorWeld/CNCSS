using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    public static class MachineNodeMeshTransforms
    {
        public static Transform3D BuildMeshLocalTransform(
            MachineGeometryPoint offset,
            MachineGeometryPoint rotationDeg,
            double scale)
        {
            var group = new Transform3DGroup();
            if (Math.Abs(scale - 1.0) > 1e-9)
            {
                group.Children.Add(new ScaleTransform3D(scale, scale, scale));
            }

            if (Math.Abs(rotationDeg.X) > 1e-9)
            {
                group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), rotationDeg.X)));
            }

            if (Math.Abs(rotationDeg.Y) > 1e-9)
            {
                group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), rotationDeg.Y)));
            }

            if (Math.Abs(rotationDeg.Z) > 1e-9)
            {
                group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), rotationDeg.Z)));
            }

            group.Children.Add(new TranslateTransform3D(offset.X, offset.Y, offset.Z));
            return group;
        }

        public static Transform3D BuildMeshLocalTransform(MachineNodeDefinition node) =>
            BuildMeshLocalTransform(node.MeshOffset, node.MeshRotationDegrees, node.MeshScale);

        public static Transform3D BuildMeshLocalTransform(MachineExtraNodeDefinition node) =>
            BuildMeshLocalTransform(node.MeshOffset, node.MeshRotationDegrees, node.MeshScale);

        /// <summary>Child pose in parent space: pivot at attach-on-parent, axis travel from HOME, then child origin.</summary>
        public static Matrix3D ComposeAttachmentMatrix(
            MachineGeometryPoint attachOnParent,
            MachineGeometryPoint attachOnChild,
            double axisX,
            double axisY,
            double axisZ)
        {
            var attach = Matrix3D.Identity;
            attach.Translate(new Vector3D(attachOnParent.X, attachOnParent.Y, attachOnParent.Z));
            attach.Translate(new Vector3D(axisX, axisY, axisZ));
            attach.Translate(new Vector3D(-attachOnChild.X, -attachOnChild.Y, -attachOnChild.Z));
            return attach;
        }

        public static Matrix3D ComposeAttachmentMatrix(
            MachineNodeDefinition parent,
            MachineNodeDefinition child,
            double axisX,
            double axisY,
            double axisZ) =>
            ComposeAttachmentMatrix(child.AttachOnParent, child.AttachOnChild, axisX, axisY, axisZ);

        public static Matrix3D ComposeAttachmentMatrix(
            MachineNodeDefinition parent,
            MachineExtraNodeDefinition child,
            double axisX,
            double axisY,
            double axisZ) =>
            ComposeAttachmentMatrix(child.AttachOnParent, child.AttachOnChild, axisX, axisY, axisZ);

        public static Point3D TransformPoint(Transform3D transform, Point3D point)
        {
            if (transform == null || transform == Transform3D.Identity)
            {
                return point;
            }

            return transform.Transform(point);
        }

        public static Point3D TransformPoint(Matrix3D matrix, Point3D point) => matrix.Transform(point);

        /// <summary>
        /// Точка шарнира узла в мировой (сценовой) СК: attach-on-child после текущего хода по осям
        /// (= attach-on-parent + перемещение оси в СК родителя).
        /// </summary>
        public static Point3D GetMotionCouplingPoint(
            Transform3D nodeKinematicTransform,
            MachineGeometryPoint attachOnChild) =>
            TransformPoint(
                nodeKinematicTransform,
                new Point3D(attachOnChild.X, attachOnChild.Y, attachOnChild.Z));

        /// <summary>
        /// Точка AttachOnChild на узле в СК сборки (мм): T_kinematic · T_mesh · attachOnChild.
        /// </summary>
        public static Point3D GetAttachOnChildScenePoint(
            Transform3D kinematicTransform,
            MachineGeometryPoint attachOnChild,
            MachineGeometryPoint meshOffset,
            MachineGeometryPoint meshRotationDegrees,
            double meshScale)
        {
            Transform3D mesh = BuildMeshLocalTransform(meshOffset, meshRotationDegrees, meshScale);
            Matrix3D chain = Multiply(GetMatrix(kinematicTransform), GetMatrix(mesh));
            return chain.Transform(new Point3D(attachOnChild.X, attachOnChild.Y, attachOnChild.Z));
        }

        public static Point3D GetAttachOnChildScenePoint(
            Transform3D kinematicTransform,
            MachineNodeDefinition node) =>
            GetAttachOnChildScenePoint(
                kinematicTransform,
                node.AttachOnChild,
                node.MeshOffset,
                node.MeshRotationDegrees,
                node.MeshScale);

        public static Point3D GetAttachOnChildScenePoint(
            Transform3D kinematicTransform,
            MachineExtraNodeDefinition node) =>
            GetAttachOnChildScenePoint(
                kinematicTransform,
                node.AttachOnChild,
                node.MeshOffset,
                node.MeshRotationDegrees,
                node.MeshScale);

        /// <summary>
        /// Переносит точку AttachOnChild в позицию сборки (мм); геометрия меша в сцене не смещается.
        /// </summary>
        public static bool TryMoveAttachOnChildPreservingMeshScene(
            MachineNodeDefinition node,
            Transform3D kinematicBeforeAttachChange,
            Point3D desiredAssemblyPoint,
            out MachineGeometryPoint newAttachOnChild,
            out MachineGeometryPoint newMeshOffset)
        {
            newAttachOnChild = node.AttachOnChild.Clone();
            newMeshOffset = node.MeshOffset.Clone();
            return TryMoveAttachOnChildPreservingMeshScene(
                node.AttachOnChild,
                node.MeshOffset,
                node.MeshRotationDegrees,
                node.MeshScale,
                kinematicBeforeAttachChange,
                desiredAssemblyPoint,
                out newAttachOnChild,
                out newMeshOffset);
        }

        public static bool TryMoveAttachOnChildPreservingMeshScene(
            MachineExtraNodeDefinition node,
            Transform3D kinematicBeforeAttachChange,
            Point3D desiredAssemblyPoint,
            out MachineGeometryPoint newAttachOnChild,
            out MachineGeometryPoint newMeshOffset)
        {
            newAttachOnChild = node.AttachOnChild.Clone();
            newMeshOffset = node.MeshOffset.Clone();
            return TryMoveAttachOnChildPreservingMeshScene(
                node.AttachOnChild,
                node.MeshOffset,
                node.MeshRotationDegrees,
                node.MeshScale,
                kinematicBeforeAttachChange,
                desiredAssemblyPoint,
                out newAttachOnChild,
                out newMeshOffset);
        }

        public static bool TryMoveAttachOnChildPreservingMeshScene(
            MachineGeometryPoint attachOnChild,
            MachineGeometryPoint meshOffset,
            MachineGeometryPoint meshRotationDegrees,
            double meshScale,
            Transform3D kinematicBeforeAttachChange,
            Point3D desiredAssemblyPoint,
            out MachineGeometryPoint newAttachOnChild,
            out MachineGeometryPoint newMeshOffset)
        {
            newAttachOnChild = attachOnChild.Clone();
            newMeshOffset = meshOffset.Clone();

            Transform3D meshOld = BuildMeshLocalTransform(meshOffset, meshRotationDegrees, meshScale);
            Matrix3D chainOld = Multiply(GetMatrix(kinematicBeforeAttachChange), GetMatrix(meshOld));
            if (!chainOld.HasInverse)
            {
                return false;
            }

            Matrix3D inv = chainOld;
            inv.Invert();
            Point3D attachLocal = inv.Transform(desiredAssemblyPoint);
            newAttachOnChild = new MachineGeometryPoint { X = attachLocal.X, Y = attachLocal.Y, Z = attachLocal.Z };
            return true;
        }

        /// <summary>
        /// После смены <see cref="MachineGeometryPoint"/> AttachOnChild пересчитывает MeshOffset так,
        /// чтобы точка привязки в сборке (MCS) осталась в <paramref name="desiredAttachAssemblyMcs"/>.
        /// Геометрия STL в сцене смещается; шарнир на оси — нет.
        /// </summary>
        public static bool TryCompensateMeshOffsetSoAttachOnChildAtAssemblyPoint(
            MachineGeometryPoint attachOnChild,
            MachineGeometryPoint meshOffset,
            MachineGeometryPoint meshRotationDegrees,
            double meshScale,
            Transform3D kinematic,
            Point3D desiredAttachAssemblyMcs,
            out MachineGeometryPoint newMeshOffset)
        {
            newMeshOffset = meshOffset.Clone();
            Matrix3D kin = GetMatrix(kinematic);
            if (!kin.HasInverse)
            {
                return false;
            }

            kin.Invert();
            Point3D targetInNode = kin.Transform(desiredAttachAssemblyMcs);

            Transform3D rotScale = BuildMeshLocalTransform(MachineGeometryPoint.Zero, meshRotationDegrees, meshScale);
            Point3D attachRs = rotScale.Transform(new Point3D(attachOnChild.X, attachOnChild.Y, attachOnChild.Z));

            var translate = Matrix3D.Identity;
            translate.Translate(new Vector3D(
                targetInNode.X - attachRs.X,
                targetInNode.Y - attachRs.Y,
                targetInNode.Z - attachRs.Z));

            Matrix3D meshNew = Multiply(translate, GetMatrix(rotScale));
            newMeshOffset = ExtractMeshOffset(meshNew, meshRotationDegrees, meshScale);
            return true;
        }

        /// <summary>Точка в локальной СК STL (AttachOnChild) для заданной позиции в сборке (MCS).</summary>
        public static bool TryResolveAttachOnChildLocalFromAssembly(
            Transform3D kinematic,
            Transform3D meshLocal,
            Point3D targetAssemblyMcs,
            out MachineGeometryPoint attachOnChildLocal)
        {
            attachOnChildLocal = MachineGeometryPoint.Zero;
            Matrix3D toAssembly = Multiply(GetMatrix(kinematic), GetMatrix(meshLocal));
            if (!toAssembly.HasInverse)
            {
                return false;
            }

            toAssembly.Invert();
            Point3D local = toAssembly.Transform(targetAssemblyMcs);
            attachOnChildLocal = new MachineGeometryPoint { X = local.X, Y = local.Y, Z = local.Z };
            return true;
        }

        /// <summary>Проверяет, что T_kinematic·T_mesh для узла не изменился (меш в сцене на месте).</summary>
        public static bool IsSameMeshWorldTransform(
            Transform3D kinematicBefore,
            Transform3D meshBefore,
            Transform3D kinematicAfter,
            Transform3D meshAfter,
            double toleranceMm = 0.05)
        {
            Matrix3D before = Multiply(GetMatrix(kinematicBefore), GetMatrix(meshBefore));
            Matrix3D after = Multiply(GetMatrix(kinematicAfter), GetMatrix(meshAfter));
            if (!before.HasInverse || !after.HasInverse)
            {
                return false;
            }

            Point3D[] probes =
            [
                new(0, 0, 0),
                new(1, 0, 0),
                new(0, 1, 0),
                new(0, 0, 1)
            ];

            foreach (Point3D probe in probes)
            {
                Point3D worldBefore = before.Transform(probe);
                Point3D worldAfter = after.Transform(probe);
                if ((worldBefore - worldAfter).Length > toleranceMm)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Подбирает MeshOffset так, чтобы T_kinematic·T_mesh·(0,0,0) = <paramref name="targetAssemblyOrigin"/>.
        /// </summary>
        public static bool TryRestoreMeshAssemblyOrigin(
            Transform3D kinematic,
            Point3D targetAssemblyOrigin,
            MachineGeometryPoint meshRotationDegrees,
            double meshScale,
            out MachineGeometryPoint newMeshOffset)
        {
            newMeshOffset = MachineGeometryPoint.Zero;
            Matrix3D kin = GetMatrix(kinematic);
            if (!kin.HasInverse)
            {
                return false;
            }

            kin.Invert();
            Point3D targetInNode = kin.Transform(targetAssemblyOrigin);

            Transform3D rotScale = BuildMeshLocalTransform(MachineGeometryPoint.Zero, meshRotationDegrees, meshScale);
            Point3D originRs = rotScale.Transform(new Point3D(0, 0, 0));

            var translate = Matrix3D.Identity;
            translate.Translate(new Vector3D(
                targetInNode.X - originRs.X,
                targetInNode.Y - originRs.Y,
                targetInNode.Z - originRs.Z));

            Matrix3D meshNew = Multiply(translate, GetMatrix(rotScale));
            newMeshOffset = ExtractMeshOffset(meshNew, meshRotationDegrees, meshScale);
            return true;
        }

        /// <summary>
        /// Подбирает MeshOffset так, чтобы T_kinematic·T_mesh совпала с <paramref name="targetKinMeshChain"/>.
        /// </summary>
        public static bool TryRestoreMeshWorldChain(
            Transform3D kinematic,
            Matrix3D targetKinMeshChain,
            MachineGeometryPoint meshRotationDegrees,
            double meshScale,
            out MachineGeometryPoint newMeshOffset)
        {
            newMeshOffset = MachineGeometryPoint.Zero;
            Matrix3D kin = GetMatrix(kinematic);
            if (!kin.HasInverse)
            {
                return false;
            }

            kin.Invert();
            Matrix3D meshNew = Multiply(kin, targetKinMeshChain);
            newMeshOffset = ExtractMeshOffset(meshNew, meshRotationDegrees, meshScale);
            return true;
        }

        /// <summary>После смены AttachOnChild пересчитывает MeshOffset, сохраняя T_kinematic·T_mesh.</summary>
        public static bool TryCompensateMeshOffsetAfterAttachChange(
            MachineGeometryPoint meshOffset,
            MachineGeometryPoint meshRotationDegrees,
            double meshScale,
            Transform3D kinematicBeforeAttachChange,
            Transform3D kinematicAfterAttachChange,
            out MachineGeometryPoint newMeshOffset)
        {
            newMeshOffset = meshOffset.Clone();
            Transform3D meshOld = BuildMeshLocalTransform(meshOffset, meshRotationDegrees, meshScale);
            Matrix3D chainOld = Multiply(GetMatrix(kinematicBeforeAttachChange), GetMatrix(meshOld));
            Matrix3D kinNew = GetMatrix(kinematicAfterAttachChange);
            if (!kinNew.HasInverse)
            {
                return false;
            }

            Matrix3D kinNewInv = kinNew;
            kinNewInv.Invert();
            Matrix3D meshNew = Multiply(kinNewInv, chainOld);
            newMeshOffset = ExtractMeshOffset(meshNew, meshRotationDegrees, meshScale);
            return true;
        }

        public static MachineGeometryPoint ExtractMeshOffset(
            Matrix3D meshLocal,
            MachineGeometryPoint rotationDegrees,
            double scale)
        {
            Transform3D withoutOffset = BuildMeshLocalTransform(MachineGeometryPoint.Zero, rotationDegrees, scale);
            Matrix3D rotationAndScale = GetMatrix(withoutOffset);
            if (!rotationAndScale.HasInverse)
            {
                Point3D t = meshLocal.Transform(new Point3D(0, 0, 0));
                return new MachineGeometryPoint { X = t.X, Y = t.Y, Z = t.Z };
            }

            rotationAndScale.Invert();
            Point3D offset = rotationAndScale.Transform(meshLocal.Transform(new Point3D(0, 0, 0)));
            return new MachineGeometryPoint { X = offset.X, Y = offset.Y, Z = offset.Z };
        }

        public static Matrix3D GetKinMeshChainMatrix(Transform3D kinematic, Transform3D meshLocal)
        {
            Matrix3D chain = GetMatrix(kinematic);
            chain.Append(GetMatrix(meshLocal));
            return chain;
        }

        private static Matrix3D GetMatrix(Transform3D transform) =>
            transform is MatrixTransform3D matrixTransform ? matrixTransform.Matrix : transform.Value;

        private static Matrix3D Multiply(Matrix3D left, Matrix3D right)
        {
            left.Append(right);
            return left;
        }
    }
}
