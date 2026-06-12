using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Machine.Model
{
    public sealed class MachineDefinition
    {
        public string ProfileId { get; set; } = "default";

        public string DisplayName { get; set; } = "Станок по умолчанию";

        public MachineKinematicsScheme KinematicsScheme { get; set; } = MachineKinematicsScheme.TableXySpindleZ;

        /// <summary>Axis HOME pose relative to MCS (mm). Physical HOME = this + <see cref="McsZeroOffset"/>.</summary>
        public MachineGeometryPoint HomePosition { get; set; } = MachineGeometryPoint.Zero;

        public MachineNodeDefinition Base { get; set; } = MachineNodeDefinition.CreateDefault(MachineNodeKind.Base);

        public MachineNodeDefinition Table { get; set; } = MachineNodeDefinition.CreateDefault(MachineNodeKind.Table);

        public MachineNodeDefinition Spindle { get; set; } = MachineNodeDefinition.CreateDefault(MachineNodeKind.Spindle);

        public List<MachineExtraNodeDefinition> ExtraNodes { get; set; } = [];

        public List<MachineAxisDefinition> Axes { get; set; } = CreateDefaultAxes();

        /// <summary>Node that carries the tool holder (collet) point.</summary>
        public string ToolMountNodeId { get; set; } = MachineNodeIds.Spindle;

        /// <summary>TCP (мм) в локальной СК узла <see cref="ToolMountNodeId"/>.</summary>
        public MachineGeometryPoint ToolMount { get; set; } = new();

        /// <summary>True when <see cref="ToolMount"/> is stored in the local coordinate system of <see cref="ToolMountNodeId"/>.</summary>
        public bool ToolMountIsNodeLocal { get; set; }

        /// <summary>Default stick-out from holder to tip when no tool is selected (mm).</summary>
        public double DefaultToolStickOutMm { get; set; } = 50;

        /// <summary>Workpiece origin on table: XY at table center, Z on picked mounting face (table mesh-local, mm).</summary>
        public MachineGeometryPoint WorkpieceMount { get; set; } = MachineGeometryPoint.Zero;

        /// <summary>Fixture height above mounting plane (mm). Workpiece stacks on top when &gt; 0.</summary>
        public double FixtureHeightMm { get; set; }

        /// <summary>
        /// Положение нуля MCS в абсолютных координатах сцены (мм). В профиле все attach/лимиты узлов — в MCS.
        /// При сохранении из конструктора сбрасывается в (0,0,0) — см. <see cref="EnsureMcsOriginAtSceneZero"/>.
        /// </summary>
        public MachineGeometryPoint McsZeroOffset { get; set; } = new();

        /// <summary>Смещение WCS G54 (мм) относительно MCS — для симуляции и превью в конструкторе.</summary>
        public MachineGeometryPoint DefaultWorkOffsetG54 { get; set; } = new();

        /// <summary>Переносит ноль MCS в центр сцены (0,0,0), сохраняя геометрию станка в мире.</summary>
        public void EnsureMcsOriginAtSceneZero() =>
            MoveMcsOriginPreservingSceneGeometry(MachineGeometryPoint.Zero);

        /// <summary>
        /// Смещает модель станка в сцене: точка <paramref name="sceneX"/>/<paramref name="sceneY"/>/<paramref name="sceneZ"/>
        /// (мм) переносится в (0,0,0). Ноль MCS в сцене остаётся в (0,0,0); пересчитываются HOME, лимиты,
        /// точки крепления в MCS и WCS G54.
        /// </summary>
        public void AlignSceneGeometrySoWorldPointAtOrigin(double sceneX, double sceneY, double sceneZ)
        {
            MoveMcsOriginPreservingSceneGeometry(MachineGeometryPoint.Zero);
            TranslateMachineGeometryInScene(-sceneX, -sceneY, -sceneZ);
        }

        /// <summary>
        /// Жёсткий сдвиг всей геометрии станка в сцене (мм) при нуле MCS в (0,0,0).
        /// Смещаются крепления узлов и MeshOffset основания; меши стола/шпинделя следуют за AttachOnParent.
        /// </summary>
        public void TranslateMachineGeometryInScene(double dx, double dy, double dz)
        {
            ShiftMcsFrameAttachment(Base.MeshOffset, dx, dy, dz);
            ShiftMcsFrameAttachment(Table.AttachOnParent, dx, dy, dz);
            ShiftMcsFrameAttachment(Spindle.AttachOnParent, dx, dy, dz);
            foreach (MachineExtraNodeDefinition extra in ExtraNodes)
            {
                ShiftMcsFrameAttachment(extra.AttachOnParent, dx, dy, dz);
            }

            ShiftNodeLocalToolMountInScene(dx, dy, dz);

            var g54 = DefaultWorkOffsetG54 ?? MachineGeometryPoint.Zero;
            g54.X += dx;
            g54.Y += dy;
            g54.Z += dz;
            DefaultWorkOffsetG54 = g54;
        }

        /// <summary>Сдвиг только точек крепления в MCS (без MeshOffset основания).</summary>
        public void TranslateKinematicFrameInMcs(double dx, double dy, double dz) =>
            ShiftAllKinematicAttachmentsInMcs(dx, dy, dz);

        /// <summary>
        /// Сохранение профиля: записывает <see cref="McsZeroOffset"/> = (0,0,0), сохраняя геометрию в сцене.
        /// Поза превью уже учтена в точках крепления при переносе MCS; повторный
        /// <see cref="CommitMcsZeroAtPose"/> здесь ломает взаимное положение узлов.
        /// </summary>
        public void FinalizeProfileMcsAtSceneCenter(double physicalX, double physicalY, double physicalZ)
        {
            var mcs = McsZeroOffset ?? MachineGeometryPoint.Zero;
            if (Math.Abs(mcs.X) > 1e-6 || Math.Abs(mcs.Y) > 1e-6 || Math.Abs(mcs.Z) > 1e-6)
            {
                MoveMcsOriginPreservingSceneGeometry(MachineGeometryPoint.Zero, physicalX, physicalY, physicalZ);
            }
        }

        /// <summary>Физическая HOME для G-кода: HOME оси в MCS + <see cref="McsZeroOffset"/>.</summary>
        public MachineGeometryPoint GetPhysicalHomePosition()
        {
            var mcs = McsZeroOffset ?? MachineGeometryPoint.Zero;
            return new MachineGeometryPoint
            {
                X = MachineMcsCoordinates.McsAxisToPhysical(GetAxisHomeMcs("X"), mcs.X),
                Y = MachineMcsCoordinates.McsAxisToPhysical(GetAxisHomeMcs("Y"), mcs.Y),
                Z = MachineMcsCoordinates.McsAxisToPhysical(GetAxisHomeMcs("Z"), mcs.Z)
            };
        }

        /// <summary>Абсолютные min/max по оси в сцене (attach узла + MCS + лимит в MCS).</summary>
        public (double SceneMin, double SceneMax) GetSceneAxisTravel(string axisName)
        {
            MachineAxisDefinition? axis = Axes.FirstOrDefault(a =>
                a.Name.Equals(axisName, StringComparison.OrdinalIgnoreCase));
            if (axis == null)
            {
                return (0, 0);
            }

            double lo = Math.Min(axis.Min, axis.Max);
            double hi = Math.Max(axis.Min, axis.Max);
            return (
                MachineMcsCoordinates.GetSceneAxisLimit(this, axisName, lo),
                MachineMcsCoordinates.GetSceneAxisLimit(this, axisName, hi));
        }

        /// <summary>Синхронизирует MCS-ноль и HOME по осям из профиля в runtime-состояние парсера/симуляции.</summary>
        public void ApplyHomeToMachineState(MachineState state)
        {
            var mcs = McsZeroOffset ?? MachineGeometryPoint.Zero;
            state.MachineZeroOffsetX = mcs.X;
            state.MachineZeroOffsetY = mcs.Y;
            state.MachineZeroOffsetZ = mcs.Z;
            state.AxisHomeMcsX = GetAxisHomeMcs("X");
            state.AxisHomeMcsY = GetAxisHomeMcs("Y");
            state.AxisHomeMcsZ = GetAxisHomeMcs("Z");
            var g54 = DefaultWorkOffsetG54 ?? MachineGeometryPoint.Zero;
            state.SetWorkOffset(54, g54.X, g54.Y, g54.Z);
        }

        public double GetAxisHomeMcs(string axisName)
        {
            MachineAxisDefinition? axis = Axes.FirstOrDefault(a =>
                a.Name.Equals(axisName, StringComparison.OrdinalIgnoreCase));
            if (axis != null)
            {
                return axis.Home;
            }

            return axisName.ToUpperInvariant() switch
            {
                "X" => HomePosition.X,
                "Y" => HomePosition.Y,
                "Z" => HomePosition.Z,
                _ => 0
            };
        }

        /// <summary>Маркер MCS в сцене (мм). WCS и перемещения узлов отсчитываются от этой точки.</summary>
        public void SetMcsOriginScene(MachineGeometryPoint sceneOffset) =>
            McsZeroOffset = sceneOffset.Clone();

        /// <summary>
        /// Перенос нуля MCS в сцене: геометрия станка и физическая HOME в мире не смещаются;
        /// в профиле сдвигаются HOME/лимиты осей и точки крепления в MCS.
        /// </summary>
        public void MoveMcsOriginPreservingSceneGeometry(MachineGeometryPoint newSceneOffset)
        {
            MachineGeometryPoint home = GetPhysicalHomePosition();
            MoveMcsOriginPreservingSceneGeometry(newSceneOffset, home.X, home.Y, home.Z);
        }

        /// <inheritdoc cref="MoveMcsOriginPreservingSceneGeometry(MachineGeometryPoint)"/>
        public void MoveMcsOriginPreservingSceneGeometry(
            MachineGeometryPoint newSceneOffset,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            Dictionary<string, Point3D> meshSceneOrigins =
                MachineKinematics.SnapshotAllNodeMeshSceneOrigins(this, physicalX, physicalY, physicalZ);

            var old = McsZeroOffset ?? MachineGeometryPoint.Zero;
            double shiftX = old.X - newSceneOffset.X;
            double shiftY = old.Y - newSceneOffset.Y;
            double shiftZ = old.Z - newSceneOffset.Z;

            MachineGeometryPoint homeWorld = GetPhysicalHomePosition();
            McsZeroOffset = newSceneOffset.Clone();

            foreach (MachineAxisDefinition axis in Axes)
            {
                string name = axis.Name.ToUpperInvariant();
                switch (name)
                {
                    case "X":
                        axis.Home = homeWorld.X - newSceneOffset.X;
                        axis.Min += shiftX;
                        axis.Max += shiftX;
                        break;
                    case "Y":
                        axis.Home = homeWorld.Y - newSceneOffset.Y;
                        axis.Min += shiftY;
                        axis.Max += shiftY;
                        break;
                    case "Z":
                        axis.Home = homeWorld.Z - newSceneOffset.Z;
                        axis.Min += shiftZ;
                        axis.Max += shiftZ;
                        break;
                }
            }

            ShiftAllKinematicAttachmentsInMcs(shiftX, shiftY, shiftZ);
            SyncHomePositionFromAxes();

            if (!MachineKinematics.RestoreAllNodeMeshSceneOrigins(this, meshSceneOrigins, physicalX, physicalY, physicalZ))
            {
                throw new InvalidOperationException(
                    "Не удалось сохранить положение STL узлов при переносе MCS.");
            }
        }

        /// <summary>
        /// Переносит ноль MCS в точку сцены (мм): геометрия в мире не смещается,
        /// координаты узлов пересчитываются в MCS. В панели WORLD для MCS показывают (0,0,0)
        /// — отсчёт в системе MCS; <see cref="McsZeroOffset"/> хранит положение в сцене для отрисовки.
        /// </summary>
        public void PlaceMcsOriginAtSceneWithWorldAtZero(
            MachineGeometryPoint scenePoint,
            double physicalX,
            double physicalY,
            double physicalZ) =>
            MoveMcsOriginPreservingSceneGeometry(scenePoint, physicalX, physicalY, physicalZ);

        public void PlaceMcsOriginAtSceneWithWorldAtZero(MachineGeometryPoint scenePoint)
        {
            MachineGeometryPoint home = GetPhysicalHomePosition();
            PlaceMcsOriginAtSceneWithWorldAtZero(scenePoint, home.X, home.Y, home.Z);
        }

        /// <summary>Сдвигает все точки, заданные в MCS, сохраняя геометрию станка в сцене.</summary>
        private void ShiftAllKinematicAttachmentsInMcs(double shiftX, double shiftY, double shiftZ)
        {
            ShiftMcsFrameAttachment(Base.MeshOffset, shiftX, shiftY, shiftZ);
            ShiftMcsFrameAttachment(Table.AttachOnParent, shiftX, shiftY, shiftZ);
            ShiftMcsFrameAttachment(Spindle.AttachOnParent, shiftX, shiftY, shiftZ);
            foreach (MachineExtraNodeDefinition extra in ExtraNodes)
            {
                ShiftMcsFrameAttachment(extra.AttachOnParent, shiftX, shiftY, shiftZ);
            }

            ShiftNodeLocalToolMountInMcs(shiftX, shiftY, shiftZ);
            // WorkpieceMount — mesh-local стола; не переносим при смене нуля MCS.

            var g54 = DefaultWorkOffsetG54 ?? MachineGeometryPoint.Zero;
            g54.X += shiftX;
            g54.Y += shiftY;
            g54.Z += shiftZ;
            DefaultWorkOffsetG54 = g54;
        }

        private static void ShiftMcsFrameAttachment(
            MachineGeometryPoint attach,
            double shiftX,
            double shiftY,
            double shiftZ)
        {
            attach.X += shiftX;
            attach.Y += shiftY;
            attach.Z += shiftZ;
        }

        /// <summary>Legacy TCP в MCS сдвигается вместе с нулём MCS; node-local TCP не трогаем.</summary>
        private void ShiftNodeLocalToolMountInMcs(double shiftX, double shiftY, double shiftZ)
        {
            if (!ToolMountIsNodeLocal)
            {
                ShiftMcsFrameAttachment(ToolMount, shiftX, shiftY, shiftZ);
            }
        }

        private void ShiftNodeLocalToolMountInScene(double dx, double dy, double dz)
        {
            if (!ToolMountIsNodeLocal)
            {
                ShiftMcsFrameAttachment(ToolMount, dx, dy, dz);
            }
        }

        /// <summary>Запомнить HOME: положение HOME в MCS из точки сцены (центр сферы HOME).</summary>
        public void SetHomeFromScenePosition(double worldX, double worldY, double worldZ)
        {
            var mcs = McsZeroOffset ?? MachineGeometryPoint.Zero;
            foreach (MachineAxisDefinition axis in Axes)
            {
                axis.Home = axis.Name.ToUpperInvariant() switch
                {
                    "X" => worldX - mcs.X,
                    "Y" => worldY - mcs.Y,
                    "Z" => worldZ - mcs.Z,
                    _ => axis.Home
                };
            }

            SyncHomePositionFromAxes();
        }

        [Obsolete("Use SetMcsOriginScene or MoveMcsOriginPreservingSceneGeometry")]
        public void RelocateMcsMarkerPreservingPhysicalHome(MachineGeometryPoint newSceneOffset) =>
            MoveMcsOriginPreservingSceneGeometry(newSceneOffset);

        [Obsolete("Use SetMcsOriginScene")]
        public void SetMcsMarkerScenePosition(MachineGeometryPoint sceneOffset) =>
            SetMcsOriginScene(sceneOffset);

        /// <summary>Copies X/Y/Z axis HOME into <see cref="HomePosition"/> (MCS mm).</summary>
        public void SyncHomePositionFromAxes()
        {
            HomePosition = new MachineGeometryPoint
            {
                X = GetAxisHomeMcs("X"),
                Y = GetAxisHomeMcs("Y"),
                Z = GetAxisHomeMcs("Z")
            };
        }

        /// <summary>
        /// Commits MCS zero at the current preview pose (MCS mm). HOME becomes MCS (0,0,0);
        /// axis limits shift; node attach points absorb the pose so visuals stay unchanged.
        /// </summary>
        public void CommitMcsZeroAtPose(double poseMcsX, double poseMcsY, double poseMcsZ)
        {
            HomePosition = MachineGeometryPoint.Zero;

            foreach (MachineAxisDefinition axis in Axes)
            {
                double shift = axis.Name.ToUpperInvariant() switch
                {
                    "X" => poseMcsX,
                    "Y" => poseMcsY,
                    "Z" => poseMcsZ,
                    _ => 0
                };
                axis.Min -= shift;
                axis.Max -= shift;
                axis.Home = 0;
            }

            ApplyMcsPoseToNodeAttachments(poseMcsX, poseMcsY, poseMcsZ);
            SyncHomePositionFromAxes();
        }

        private void ApplyMcsPoseToNodeAttachments(double poseMcsX, double poseMcsY, double poseMcsZ)
        {
            ApplyMcsPoseToAttach(Table.MotionAxes, Table.AttachOnParent, poseMcsX, poseMcsY, poseMcsZ);
            ApplyMcsPoseToAttach(Spindle.MotionAxes, Spindle.AttachOnParent, poseMcsX, poseMcsY, poseMcsZ);

            foreach (MachineExtraNodeDefinition extra in ExtraNodes)
            {
                if (extra.MotionLink == MachineMotionLink.Fixed)
                {
                    continue;
                }

                ApplyMcsPoseToAttach(extra.MotionAxes, extra.AttachOnParent, poseMcsX, poseMcsY, poseMcsZ);
            }
        }

        private static void ApplyMcsPoseToAttach(
            MachineProgramAxisMask motionAxes,
            MachineGeometryPoint attach,
            double poseMcsX,
            double poseMcsY,
            double poseMcsZ)
        {
            if (motionAxes.HasFlag(MachineProgramAxisMask.X))
            {
                attach.X += poseMcsX;
            }

            if (motionAxes.HasFlag(MachineProgramAxisMask.Y))
            {
                attach.Y += poseMcsY;
            }

            if (motionAxes.HasFlag(MachineProgramAxisMask.Z))
            {
                attach.Z += poseMcsZ;
            }
        }

        public MachineNodeDefinition GetNode(MachineNodeKind kind) => kind switch
        {
            MachineNodeKind.Base => Base,
            MachineNodeKind.Table => Table,
            MachineNodeKind.Spindle => Spindle,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public MachineNodeDefinition? TryGetBuiltInNode(string nodeId)
        {
            if (string.Equals(nodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase))
            {
                return Base;
            }

            if (string.Equals(nodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase))
            {
                return Table;
            }

            if (string.Equals(nodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase))
            {
                return Spindle;
            }

            return null;
        }

        public MachineExtraNodeDefinition? TryGetExtraNode(string nodeId) =>
            ExtraNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<(string Id, string DisplayName)> EnumerateNodeCatalog()
        {
            yield return (MachineNodeIds.Base, "Основание");
            yield return (MachineNodeIds.Table, "Стол");
            yield return (MachineNodeIds.Spindle, "Шпиндель");
            foreach (MachineExtraNodeDefinition extra in ExtraNodes)
            {
                yield return (extra.Id, extra.DisplayName);
            }
        }

        public void NormalizeAfterLoad()
        {
            foreach (MachineAxisDefinition axis in Axes)
            {
                if (string.IsNullOrWhiteSpace(axis.DriverNodeId))
                {
                    axis.DriverNodeId = axis.ResolveDriverNodeId();
                }
            }

            foreach (MachineExtraNodeDefinition extra in ExtraNodes)
            {
                if (string.IsNullOrWhiteSpace(extra.Id))
                {
                    extra.Id = Guid.NewGuid().ToString("N");
                }
            }

            if (string.IsNullOrWhiteSpace(ToolMountNodeId))
            {
                ToolMountNodeId = MachineNodeIds.Spindle;
            }

            if (ToolMount.IsNearlyZero() && !Spindle.ToolMount.IsNearlyZero())
            {
                ToolMount = Spindle.ToolMount.Clone();
            }

            McsZeroOffset ??= MachineGeometryPoint.Zero;
            MigrateLegacyPhysicalHomeToMcs();
            NormalizeLegacyAxisHomeInMcs();
            NormalizeDuplicateMcsAxisHome();
            ToolMountMcsHelper.NormalizeLegacyMeshOffsetToolMount(this);
            MigrateLegacyToolMountMcsToNodeLocal();
            MachineNodeColors.ApplyDefaultBuiltInMeshColors(this);
            SyncHomePositionFromAxes();
        }

        private void MigrateLegacyToolMountMcsToNodeLocal()
        {
            if (ToolMountIsNodeLocal)
            {
                return;
            }

            MachineGeometryPoint physicalHome = GetPhysicalHomePosition();
            Transform3D nodeTransform = ToolMountMcsHelper.ResolveToolMountNodeTransform(
                this,
                physicalHome.X,
                physicalHome.Y,
                physicalHome.Z);
            Matrix3D node = nodeTransform.Value;
            if (!node.HasInverse)
            {
                ToolMountIsNodeLocal = true;
                return;
            }

            node.Invert();
            Point3D legacyMcsTcp = new(ToolMount.X, ToolMount.Y, ToolMount.Z);
            Point3D localTcp = node.Transform(legacyMcsTcp);
            ToolMount = new MachineGeometryPoint
            {
                X = localTcp.X,
                Y = localTcp.Y,
                Z = localTcp.Z
            };
            ToolMountIsNodeLocal = true;
        }

        /// <summary>
        /// Older profiles stored physical HOME (255…) in axis.Home while MCS offset was already set.
        /// HOME in MCS should be 0 when it matches the legacy physical constant.
        /// </summary>
        private void NormalizeLegacyAxisHomeInMcs()
        {
            var mcs = McsZeroOffset ?? MachineGeometryPoint.Zero;
            if (mcs.IsNearlyZero())
            {
                return;
            }

            foreach (MachineAxisDefinition axis in Axes)
            {
                double legacyPhysical = axis.Name.ToUpperInvariant() switch
                {
                    "X" => ProjectConstants.LEGACY_DEFAULT_PHYSICAL_HOME,
                    "Y" => ProjectConstants.LEGACY_DEFAULT_PHYSICAL_HOME,
                    "Z" => ProjectConstants.LEGACY_DEFAULT_PHYSICAL_HOME,
                    _ => double.NaN
                };
                if (double.IsNaN(legacyPhysical))
                {
                    continue;
                }

                if (Math.Abs(axis.Home - legacyPhysical) < 1)
                {
                    axis.Home = 0;
                }
            }
        }

        /// <summary>
        /// После <see cref="CommitMcsZeroAtPose"/> HOME по осям в MCS должен быть 0; старые сохранения могли
        /// дублировать смещение маркера в axis.Home и давать удвоенную физическую HOME в главном окне.
        /// </summary>
        private void NormalizeDuplicateMcsAxisHome()
        {
            var mcs = McsZeroOffset ?? MachineGeometryPoint.Zero;
            if (mcs.IsNearlyZero())
            {
                return;
            }

            const double tol = 1.5;
            foreach (MachineAxisDefinition axis in Axes)
            {
                double component = axis.Name.ToUpperInvariant() switch
                {
                    "X" => mcs.X,
                    "Y" => mcs.Y,
                    "Z" => mcs.Z,
                    _ => 0
                };
                if (Math.Abs(component) < 1e-6)
                {
                    continue;
                }

                if (Math.Abs(axis.Home - component) < tol || Math.Abs(axis.Home + component) < tol)
                {
                    axis.Home = 0;
                }
            }
        }

        /// <summary>
        /// Older profiles stored physical HOME in <see cref="HomePosition"/> with <see cref="McsZeroOffset"/> at origin.
        /// G28 and kinematics expect MCS at the retract point (physical HOME).
        /// </summary>
        private void MigrateLegacyPhysicalHomeToMcs()
        {
            if (!(McsZeroOffset ?? MachineGeometryPoint.Zero).IsNearlyZero())
            {
                return;
            }

            var home = HomePosition;
            double legacy = ProjectConstants.LEGACY_DEFAULT_PHYSICAL_HOME;
            bool looksLikeLegacyPhysical =
                Math.Abs(home.Z - legacy) < 1
                || Math.Abs(home.X - legacy) < 1
                || Math.Abs(home.Y - legacy) < 1;
            if (!looksLikeLegacyPhysical || home.IsNearlyZero())
            {
                return;
            }

            McsZeroOffset = home.Clone();
            HomePosition = MachineGeometryPoint.Zero;
            foreach (MachineAxisDefinition axis in Axes)
            {
                double shift = axis.Name.ToUpperInvariant() switch
                {
                    "X" => home.X,
                    "Y" => home.Y,
                    "Z" => home.Z,
                    _ => 0
                };
                axis.Min -= shift;
                axis.Max -= shift;
                axis.Home -= shift;
            }
        }

        public static MachineDefinition CreateDefault(string profileId = "default", string displayName = "Станок по умолчанию")
        {
            return new MachineDefinition
            {
                ProfileId = profileId,
                DisplayName = displayName,
                HomePosition = MachineGeometryPoint.Zero,
                McsZeroOffset = MachineGeometryPoint.Zero,
                ToolMountIsNodeLocal = true,
                Base = MachineNodeDefinition.CreateDefault(MachineNodeKind.Base),
                Table = MachineNodeDefinition.CreateDefault(MachineNodeKind.Table),
                Spindle = MachineNodeDefinition.CreateDefault(MachineNodeKind.Spindle),
                Axes = CreateDefaultAxes()
            };
        }

        public static List<MachineAxisDefinition> CreateDefaultAxes()
        {
            // Limits and HOME in MCS: X ±300, Y ±200, Z 0…-500, HOME = 0.
            return
            [
                MachineAxisDefinition.CreateDefault("X", MachineNodeIds.Table, -300, 300, 0),
                MachineAxisDefinition.CreateDefault("Y", MachineNodeIds.Table, -200, 200, 0),
                MachineAxisDefinition.CreateDefault("Z", MachineNodeIds.Spindle, -500, 0, 0)
            ];
        }

        public MachineDefinition Clone()
        {
            return new MachineDefinition
            {
                ProfileId = ProfileId,
                DisplayName = DisplayName,
                KinematicsScheme = KinematicsScheme,
                HomePosition = HomePosition.Clone(),
                Base = Base.Clone(),
                Table = Table.Clone(),
                Spindle = Spindle.Clone(),
                ExtraNodes = ExtraNodes.Select(n => n.Clone()).ToList(),
                Axes = Axes.Select(a => a.Clone()).ToList(),
                ToolMountNodeId = ToolMountNodeId,
                ToolMount = ToolMount.Clone(),
                ToolMountIsNodeLocal = ToolMountIsNodeLocal,
                DefaultToolStickOutMm = DefaultToolStickOutMm,
                WorkpieceMount = WorkpieceMount.Clone(),
                FixtureHeightMm = FixtureHeightMm,
                McsZeroOffset = McsZeroOffset.Clone(),
                DefaultWorkOffsetG54 = DefaultWorkOffsetG54.Clone()
            };
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                errors.Add("Укажите имя станка.");
            }

            if (string.IsNullOrWhiteSpace(ProfileId))
            {
                errors.Add("Идентификатор профиля не задан.");
            }

            foreach (var axis in Axes)
            {
                if (axis.Min >= axis.Max)
                {
                    errors.Add($"Ось {axis.Name}: минимум должен быть меньше максимума.");
                }

                double lo = Math.Min(axis.Min, axis.Max);
                double hi = Math.Max(axis.Min, axis.Max);
                if (axis.Home < lo || axis.Home > hi)
                {
                    errors.Add($"Ось {axis.Name}: HOME должна быть в пределах MIN…MAX включительно.");
                }
            }

            SyncHomePositionFromAxes();

            if (Base.MeshScale <= 0 || Table.MeshScale <= 0 || Spindle.MeshScale <= 0)
            {
                errors.Add("Масштаб STL должен быть больше нуля.");
            }

            var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                MachineNodeIds.Base,
                MachineNodeIds.Table,
                MachineNodeIds.Spindle
            };

            foreach (MachineExtraNodeDefinition extra in ExtraNodes)
            {
                if (string.IsNullOrWhiteSpace(extra.Id))
                {
                    errors.Add("Дополнительный узел без идентификатора.");
                    continue;
                }

                if (!knownIds.Add(extra.Id))
                {
                    errors.Add($"Повторяющийся идентификатор узла: {extra.Id}");
                }

                if (string.IsNullOrWhiteSpace(extra.DisplayName))
                {
                    errors.Add($"Узел {extra.Id}: укажите имя.");
                }

                if (!knownIds.Contains(extra.ParentNodeId) && ExtraNodes.All(n => !string.Equals(n.Id, extra.ParentNodeId, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Узел «{extra.DisplayName}»: неизвестный родитель.");
                }

                if (extra.MeshScale <= 0)
                {
                    errors.Add($"Узел «{extra.DisplayName}»: масштаб STL должен быть больше нуля.");
                }

                if (extra.MotionLink == MachineMotionLink.Fixed && extra.MotionAxes != MachineProgramAxisMask.None)
                {
                    errors.Add($"Узел «{extra.DisplayName}»: оси перемещения заданы для статичного узла.");
                }

                if (extra.MotionLink != MachineMotionLink.Fixed && extra.MotionAxes == MachineProgramAxisMask.None)
                {
                    errors.Add($"Узел «{extra.DisplayName}»: выберите оси перемещения (X/Y/Z).");
                }
            }

            foreach (MachineAxisDefinition axis in Axes)
            {
                string driver = axis.ResolveDriverNodeId();
                if (!knownIds.Contains(driver))
                {
                    errors.Add($"Ось {axis.Name}: неизвестный приводной узел «{driver}».");
                }
            }

            if (!knownIds.Contains(ToolMountNodeId))
            {
                errors.Add($"TCP: неизвестный узел крепления «{ToolMountNodeId}».");
            }

            return errors;
        }

        public Core.Vmc3AxisKinematicsModel ToKinematicsModel()
        {
            (double xMin, double xMax) = MachineMcsCoordinates.GetProgramPhysicalAxisLimits(this, "X");
            (double yMin, double yMax) = MachineMcsCoordinates.GetProgramPhysicalAxisLimits(this, "Y");
            (double zMin, double zMax) = MachineMcsCoordinates.GetProgramPhysicalAxisLimits(this, "Z");
            return new Core.Vmc3AxisKinematicsModel(xMin, xMax, yMin, yMax, zMin, zMax);
        }
    }
}
