using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic.Voxel;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;
using CNCSS.UI.Dialogs;
using CNCSS.Vis;
using Microsoft.Win32;

namespace CNCSS
{
    public partial class MainWindow
    {
        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "G-code (*.nc;*.txt)|*.nc;*.txt|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                LoadAndRender(dialog.FileName);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private void ZoomExtents_Click(object sender, RoutedEventArgs e) => Viewport.ZoomExtents();

        private void MenuMachineSetup_Click(object sender, RoutedEventArgs e)
        {
            if (_machineSetupWindow != null && _machineSetupWindow.IsLoaded)
            {
                _machineSetupWindow.Activate();
                return;
            }

            _machineSetupWindow = new MachineSetupWindow(_machineProfileService, _stlMeshLoader)
            {
                Owner = this
            };
            _machineSetupWindow.Closed += (_, _) => _machineSetupWindow = null;
            _machineSetupWindow.Show();
        }

        private void MenuMachineProfile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new MachineProfilePickerWindow(_machineProfileService, () => MenuMachineSetup_Click(sender, e))
            {
                Owner = this
            };
            if (picker.ShowDialog() == true)
            {
                _ = ApplyActiveMachineProfileAsync(moveAxesToProfileHome: false);
            }
        }

        private void MenuMachineSaveFactoryDefault_Click(object sender, RoutedEventArgs e)
        {
            _machineProfileService.SaveActiveProfileAsFactoryDefault();
            StatusText.Text = "Заводской снимок профиля сохранён.";
        }

        private void MenuMachineFactoryReset_Click(object sender, RoutedEventArgs e)
        {
            _machineProfileService.ResetToFactoryDefault(_stlMeshLoader);
            _ = ApplyActiveMachineProfileAsync();
        }

        private Task ApplyActiveMachineProfileAsync() =>
            ApplyActiveMachineProfileAsync(moveAxesToProfileHome: true);

        private async Task ApplyActiveMachineProfileAsync(bool moveAxesToProfileHome)
        {
            var profile = _machineProfileService.ActiveProfile;
            _machineCore.UpdateKinematics(profile.ToKinematicsModel());
            ApplySceneOriginFromMcs(profile.McsZeroOffset ?? MachineGeometryPoint.Zero);

            var physicalHome = profile.GetPhysicalHomePosition();
            _machineCore.ApplyProfileHome(profile);
            if (_currentParser != null)
            {
                profile.ApplyHomeToMachineState(_currentParser.State);
            }

            _programExecutionService.SetHomeTarget(physicalHome.X, physicalHome.Y, physicalHome.Z);

            if (moveAxesToProfileHome)
            {
                _machineCore.HomeTo(physicalHome.X, physicalHome.Y, physicalHome.Z);
                var homePosition = new Point3D(physicalHome.X, physicalHome.Y, physicalHome.Z);
                _programPlaybackHost.SetCurrentPosition(homePosition);
                SyncCutTrackingFromMachinePosition(homePosition);
            }

            await _machineVisualCoordinator.RebuildAsync();
            SyncStockVisualToTable();
        }

        private void ApplySceneOriginFromMcs(MachineGeometryPoint mcsZeroOffset)
        {
            _sceneWorldShift = new TranslateTransform3D(mcsZeroOffset.X, mcsZeroOffset.Y, mcsZeroOffset.Z);
            _machineVisualCoordinator.SetWorldTransform(_sceneWorldShift);
            foreach (Visual3D v in _toolpathVisuals)
            {
                v.Transform = Transform3D.Identity;
            }
        }

        private static void CreateDemoNc(string filePath)
        {
            const string demo = """
                O0001
                G21 G17 G54
                G0 Z50
                M30
                """;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
            File.WriteAllText(filePath, demo);
        }

        private static StockConfig ToStockConfig(WorkpiecePlacement.StockBounds bounds) =>
            new(bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY, bounds.MinZ, bounds.MaxZ);

        private static WorkpiecePlacement.StockBounds ToStockBounds(StockConfig cfg) =>
            new(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);

        private bool TryReadStockConfig(out StockConfig cfg)
        {
            cfg = default;
            if (_stockLifecycle.Bounds is not WorkpiecePlacement.StockBounds bounds)
            {
                return false;
            }

            cfg = ToStockConfig(bounds);
            return cfg.MaxX > cfg.MinX && cfg.MaxY > cfg.MinY && cfg.MaxZ > cfg.MinZ;
        }

        private Model3D GetStockViewportContent() =>
            _stockLifecycle.Stock is VoxelStockVolume { UseVoxelForDisplay: true, HasHelixChunkMeshes: true } d3d
                ? d3d.MainModel
                : _stockLifecycle.Stock?.MainModel ?? _stockModel;

        private Color GetSelectedStockColor() =>
            _stockLifecycle.ResolveStockColor(
                _stockLifecycle.ConstructorConfig?.Color,
                Colors.LightGray);

        private double GetToolStickOutMm() =>
            _selectedTool?.OverallLength ?? _tools.FirstOrDefault()?.OverallLength ?? 50.0;

        private Point3D GetToolTcpPosition(double machineX, double machineY, double machineZ) =>
            _machineVisualCoordinator.GetToolCenterPoint(machineX, machineY, machineZ, GetToolStickOutMm());

        private Point3D GetToolHolderPosition(double machineX, double machineY, double machineZ) =>
            _machineVisualCoordinator.GetToolHolderPoint(machineX, machineY, machineZ);

        private Point3D GetToolHolderPosition(Point3D machineAxisPosition) =>
            GetToolHolderPosition(machineAxisPosition.X, machineAxisPosition.Y, machineAxisPosition.Z);

        private void ApplyTableKinematicPose(double machineX, double machineY, double machineZ)
        {
            var profile = _machineProfileService.ActiveProfile;
            if (!MachineKinematics.UsesTableMountedWorkpiece(profile))
            {
                return;
            }

            SyncWcsMarkersToScenePose(machineX, machineY, machineZ);
            if (_stockLifecycle.AnchoredToTable)
            {
                _stockTableToWorld = TableSceneTransforms.BuildTableToSceneMatrix(profile, machineX, machineY, machineZ);
                _stockVisual.Transform = BuildStockTableLocalToSceneTransform(profile, machineX, machineY, machineZ);
            }
        }

        private void EnsureWcsTableLocalCacheForToolpath(MachineState seedState)
        {
            var profile = _machineProfileService.ActiveProfile;
            if (!MachineKinematics.UsesTableMountedWorkpiece(profile))
            {
                return;
            }

            WorkpieceMountPlacement.SyncWcsOriginTableLocalFromMcs(
                profile,
                _workOffsets,
                seedState.X,
                seedState.Y,
                seedState.Z);
        }

        private static bool TryParseCoordinateSystemNumber(string coordinateSystem, out int systemNumber)
        {
            systemNumber = 54;
            if (string.IsNullOrWhiteSpace(coordinateSystem))
            {
                return false;
            }

            string digits = new string(coordinateSystem.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out systemNumber);
        }

        private void SyncActiveWcsOriginMarker() =>
            SyncActiveWcsOriginMarker(
                _selectedOffsetSystem,
                _machineCore.State.X,
                _machineCore.State.Y,
                _machineCore.State.Z);

        private void SyncActiveWcsOriginMarker(
            int activeSystemNumber,
            double machineX,
            double machineY,
            double machineZ,
            bool updateTableLocalPosition = true)
        {
            const double sphereRadiusMm = 2.8;
            const double axisLenMm = 18.0;
            var profile = _machineProfileService.ActiveProfile;
            bool onTable = MachineKinematics.UsesTableMountedWorkpiece(profile);

            foreach (ModelVisual3D marker in _wcsMarkers.Values)
            {
                if (marker.Children.Contains(_toolpathRoot))
                {
                    marker.Children.Remove(_toolpathRoot);
                }

                _machineVisualCoordinator.DetachFromMachineHierarchy(marker);
                if (Viewport.Children.Contains(marker))
                {
                    Viewport.Children.Remove(marker);
                }
            }

            _wcsMarkers.Clear();
            _machineVisualCoordinator.SetTableMountedWcsMarker(null);

            if (!_workOffsets.TryGetValue(activeSystemNumber, out (double X, double Y, double Z) offset))
            {
                offset = (0, 0, 0);
            }

            var markerVisual = WcsMarkerVisualBuilder.Build($"G{activeSystemNumber}", sphereRadiusMm, axisLenMm);
            _wcsMarkers[activeSystemNumber] = markerVisual;
            AttachToolpathRootToWcsMarker(markerVisual);

            if (onTable)
            {
                Point3D tableLocal = WorkpieceMountPlacement.GetWcsOriginTableRootForSystem(
                    profile,
                    activeSystemNumber,
                    offset.X,
                    offset.Y,
                    offset.Z,
                    machineX,
                    machineY,
                    machineZ);
                if (updateTableLocalPosition)
                {
                    WorkpieceMountPlacement.RegisterWcsOriginTableLocal(activeSystemNumber, tableLocal);
                }

                Point3D scene = TableSceneTransforms.TableRootLocalToSceneAbsolute(
                    profile,
                    machineX,
                    machineY,
                    machineZ,
                    tableLocal);
                markerVisual.Transform = new TranslateTransform3D(scene.X, scene.Y, scene.Z);
                EnsureWcsMarkerOnViewport(markerVisual);
            }
            else
            {
                markerVisual.Transform = new TranslateTransform3D(offset.X, offset.Y, offset.Z);
                EnsureWcsMarkerOnViewport(markerVisual);
            }
        }

        private void EnsureWcsMarkerOnViewport(ModelVisual3D marker)
        {
            _machineVisualCoordinator.DetachFromMachineHierarchy(marker);
            EnsureVisualOnViewport(marker);
            BringVisualToViewportFront(marker);
        }

        private void BringVisualToViewportFront(ModelVisual3D visual)
        {
            if (Viewport.Children.Contains(visual))
            {
                Viewport.Children.Remove(visual);
                Viewport.Children.Add(visual);
            }
        }

        private void BringWcsMarkersToViewportFront()
        {
            foreach (ModelVisual3D marker in _wcsMarkers.Values)
            {
                BringVisualToViewportFront(marker);
            }
        }

        private void SyncWcsMarkersToScenePose(double machineX, double machineY, double machineZ)
        {
            MachineDefinition profile = _machineProfileService.ActiveProfile;
            if (!MachineKinematics.UsesTableMountedWorkpiece(profile))
            {
                return;
            }

            foreach ((int systemNumber, ModelVisual3D marker) in _wcsMarkers)
            {
                if (!WorkpieceMountPlacement.TryGetRegisteredWcsOriginTableLocal(systemNumber, out Point3D tableLocal))
                {
                    continue;
                }

                Point3D scene = TableSceneTransforms.TableRootLocalToSceneAbsolute(
                    profile,
                    machineX,
                    machineY,
                    machineZ,
                    tableLocal);
                marker.Transform = new TranslateTransform3D(scene.X, scene.Y, scene.Z);
            }

            BringWcsMarkersToViewportFront();
        }

        /// <summary>Отсоединяет контур от viewport/станка и от прежнего маркера WCS.</summary>
        private void DetachToolpathRootFromParent()
        {
            foreach (ModelVisual3D marker in _wcsMarkers.Values)
            {
                if (marker.Children.Contains(_toolpathRoot))
                {
                    marker.Children.Remove(_toolpathRoot);
                    break;
                }
            }

            if (System.Windows.Media.VisualTreeHelper.GetParent(_toolpathRoot) is ModelVisual3D parent &&
                parent.Children.Contains(_toolpathRoot))
            {
                parent.Children.Remove(_toolpathRoot);
            }

            _machineVisualCoordinator.DetachFromMachineHierarchy(_toolpathRoot);
            RemoveVisualFromViewport(_toolpathRoot);
        }

        /// <summary>Контур УП в координатах WCS (program), дочерний к маркеру нуля WCS.</summary>
        private void AttachToolpathRootToWcsMarker(ModelVisual3D wcsMarker)
        {
            DetachToolpathRootFromParent();
            _toolpathRoot.Transform = Transform3D.Identity;
            wcsMarker.Children.Add(_toolpathRoot);
        }

        private void SyncStockVisualToTable(bool syncWcsMarker = true)
        {
            var profile = _machineProfileService.ActiveProfile;
            bool tableMachine = MachineKinematics.UsesTableMountedWorkpiece(profile);
            bool stockOnTable = _stockLifecycle.AnchoredToTable && tableMachine;

            if (!tableMachine)
            {
                _machineVisualCoordinator.SetTableMountedVisuals(null, null);
                _machineVisualCoordinator.SetTableMountedWcsMarker(null);
                EnsureVisualOnViewport(_stockVisual);
                _stockVisual.Transform = Transform3D.Identity;
                if (syncWcsMarker)
                {
                    SyncActiveWcsOriginMarker();
                }

                return;
            }

            DetachToolpathRootFromParent();
            _toolpathRoot.Transform = Transform3D.Identity;

            if (stockOnTable)
            {
                // Заготовка остаётся отдельным viewport overlay, чтобы режимы/видимость узла стола
                // не могли скрыть её вместе со станком. Сам mesh остаётся в table-local координатах.
                EnsureVisualOnViewport(_stockVisual);
                Point3D pose = GetCurrentPosition();
                _stockVisual.Transform = BuildStockTableLocalToSceneTransform(profile, pose.X, pose.Y, pose.Z);
            }
            else
            {
                EnsureVisualOnViewport(_stockVisual);
            }

            _machineVisualCoordinator.SetTableMountedVisuals(null, null);

            if (stockOnTable)
            {
                Point3D pose = GetCurrentPosition();
                _stockTableToWorld = TableSceneTransforms.BuildTableToSceneMatrix(profile, pose.X, pose.Y, pose.Z);
            }

            if (syncWcsMarker)
            {
                SyncActiveWcsOriginMarker();
            }
        }

        private void EnsureVisualOnViewport(ModelVisual3D visual)
        {
            if (!Viewport.Children.Contains(visual))
            {
                Viewport.Children.Add(visual);
            }
        }

        private void RemoveVisualFromViewport(ModelVisual3D visual)
        {
            if (Viewport.Children.Contains(visual))
            {
                Viewport.Children.Remove(visual);
            }
        }

        /// <summary>
        /// Точка контура УП в СК заготовки. Для table-mounted она уже table-local
        /// (<see cref="MapPhysicalToToolpathLocal"/>); повторный SceneToTableLocal смещает рез мимо материала.
        /// </summary>
        private static Point3D ProgramPointToStockLocal(Point3D programPointInStockSpace) =>
            programPointInStockSpace;

        /// <summary>Держатель инструмента (MCS) → СК заготовки.</summary>
        private Point3D HolderMcsToStockLocal(Point3D holderMcs, Point3D machinePose)
        {
            if (!_stockLifecycle.AnchoredToTable ||
                !MachineKinematics.UsesTableMountedWorkpiece(_machineProfileService.ActiveProfile))
            {
                MachineGeometryPoint mcsZero = _machineProfileService.ActiveProfile.McsZeroOffset ?? MachineGeometryPoint.Zero;
                return new Point3D(
                    holderMcs.X + mcsZero.X,
                    holderMcs.Y + mcsZero.Y,
                    holderMcs.Z + mcsZero.Z);
            }

            MachineDefinition profile = _machineProfileService.ActiveProfile;
            return TableSceneTransforms.TcpMcsToTableRootLocal(
                profile,
                machinePose.X,
                machinePose.Y,
                machinePose.Z,
                holderMcs);
        }

        private void SyncCutTrackingFromMachinePosition(Point3D machinePose)
        {
            _lastPosition = GetToolpathLocalTcpForCut(machinePose);
        }

        /// <summary>Абсолютная сцена viewport → СК заготовки.</summary>
        private Point3D ScenePointToStockLocal(Point3D scenePoint)
        {
            if (!TryCreateStockSceneToLocalMapper(out Func<Point3D, Point3D>? map))
            {
                return scenePoint;
            }

            return map!(scenePoint);
        }

        private Point3D GetToolpathLocalTcpForCut(Point3D machinePhysical)
        {
            if (_currentParser == null)
            {
                return machinePhysical;
            }

            return MapPhysicalToToolpathLocal(machinePhysical, _currentParser.State);
        }

        private bool TryCreateStockSceneToLocalMapper(out Func<Point3D, Point3D>? sceneToStockLocal)
        {
            sceneToStockLocal = null;
            if (!_stockLifecycle.AnchoredToTable ||
                !MachineKinematics.UsesTableMountedWorkpiece(_machineProfileService.ActiveProfile))
            {
                return false;
            }

            MachineDefinition profile = _machineProfileService.ActiveProfile;
            Point3D pose = GetCurrentPosition();
            sceneToStockLocal = scene => TableSceneTransforms.SceneToTableLocal(
                profile,
                pose.X,
                pose.Y,
                pose.Z,
                scene);
            return true;
        }

        private bool TryCreateStockLocalToSceneMapper(out Func<Point3D, Point3D>? stockLocalToScene)
        {
            stockLocalToScene = null;
            if (!_stockLifecycle.AnchoredToTable ||
                !MachineKinematics.UsesTableMountedWorkpiece(_machineProfileService.ActiveProfile))
            {
                return false;
            }

            MachineDefinition profile = _machineProfileService.ActiveProfile;
            Point3D pose = GetCurrentPosition();
            stockLocalToScene = local => TableSceneTransforms.TableRootLocalToSceneAbsolute(
                profile,
                pose.X,
                pose.Y,
                pose.Z,
                local);
            return true;
        }

        private Func<Point3D, Point3D>? GetStockTableLocalToSceneTransform()
        {
            TryCreateStockLocalToSceneMapper(out Func<Point3D, Point3D>? mapper);
            return mapper;
        }

        private static Transform3D BuildStockTableLocalToSceneTransform(
            MachineDefinition profile,
            double machineX,
            double machineY,
            double machineZ)
        {
            var group = new Transform3DGroup();
            group.Children.Add(new MatrixTransform3D(
                TableSceneTransforms.BuildTableKinematicMatrix(profile, machineX, machineY, machineZ)));

            MachineGeometryPoint mcs = profile.McsZeroOffset ?? MachineGeometryPoint.Zero;
            if (!mcs.IsNearlyZero())
            {
                group.Children.Add(new TranslateTransform3D(mcs.X, mcs.Y, mcs.Z));
            }

            return group;
        }

        private void RealignStockCenteredOnMount(MachineDefinition profile, double width, double depth, double height)
        {
            Rect3D? tableBounds = _machineVisualCoordinator.TryGetTableMeshBoundsLocal(out Rect3D bounds) && !bounds.IsEmpty
                ? bounds
                : null;
            WorkpiecePlacement.StockBounds aligned =
                WorkpieceMountPlacement.AlignStockTableLocal(profile, tableBounds, width, depth, height);
            _stockLifecycle.Bounds = aligned;
            _stockLifecycle.AnchoredToTable = true;
            _stockLifecycle.BoundsAreTableLocal = true;
        }
    }
}
