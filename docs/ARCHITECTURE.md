# Архитектура CNCSS

## Слои

| Сборка | Папка | Ответственность |
|--------|-------|-----------------|
| **CNCSS.Data** | `src/CNCSS.Data/` | DTO, `MachineState`, конфиги, константы |
| **CNCSS.Logic** | `src/CNCSS.Logic/` (`Logic/`, `Controller/`, `Machine/`, `Simulation/`, `Geometry/`, `Vis/` mesh+toolpath) | G-code, симуляция, кинематика, **воксельный движок** |
| **CNCSS.Visualization** | `src/CNCSS.Visualization/` (`Vis/`, `UI/`, `Infrastructure/`, `Voxel/`) | Helix-сцена, SharpDX заготовка, WPF, презентеры |
| **CNCSS** (App) | корень (`MainWindow`, `App.xaml`) | Тонкая композиция и wiring |

## Воксельный движок (Surface Shell + SharpDX, C#)

- **Модель:** bitmap занятости (`OccupancyBitmap`, ~62.5 МБ для 100×100×50 мм @ 0.1 мм) + sparse **surface index** (только внешний слой вокселей) + чанки 16³.
- **Ядро:** `src/CNCSS.Logic/Logic/Voxel/SurfaceShell/` — `SurfaceShellVolume`, `CylinderCutEngine`, `SurfaceShellSimulationWorker`.
- **Маски формы:** `ShapeMaskBuilder` (Engine/) — прямоугольник, цилиндр, шестигранник, труба.
- **Съём:** swept cylinder режущей части; параллельная обработка чанков; обновление surface layer при удалении.
- **Синхронизация:** фоновый поток `SurfaceShellSimulationWorker` → double-buffer `SurfaceShellRenderSnapshot`.
- **Визуализация:** гибридный viewport — станок/инструмент/траектория на `HelixViewport3D`; заготовка на **HelixToolkit.Wpf.SharpDX** (`SharpDxStockViewportHost`, `InstancingMeshGeometryModel3D`).
- **Lifecycle:** параметрическая модель → Cycle Start (`SurfaceShellVolume.Create` + `ActivateShellDisplay`) → cut+render в playback.
- Glue: `CutVisualSynchronizer`, `StockLifecycleCoordinator`, `VoxelStockVolume`.

## Зависимости

```
App → Visualization → Logic → Data
App → Logic
```

Запрещено: Data → Logic/Visualization, Logic → Visualization.
