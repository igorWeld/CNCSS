# Руководство для ИИ-агента (CNCSS)

Документ для автоматических ассистентов (Cursor Agent, Codex и т.п.), работающих с репозиторием **CNCSS** — симулятором ЧПУ на WPF + Helix Toolkit.

---

## Назначение проекта

Desktop-приложение Windows: загрузка G-кода, 3D-визуализация станка и траектории, симуляция удаления материала воксельной заготовкой. UI в стиле панели FANUC Series 0i.

**Не путать:** это не веб-приложение, не кроссплатформенный .NET без WPF. Целевая платформа: `net8.0-windows`, x64.

---

## Быстрые команды

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run -c Release --project CNCSS.csproj
```

Перед PR/коммитом по просьбе пользователя: `dotnet build` и `dotnet test` без ошибок.

---

## Архитектура (слои)

```
G-code файл
  → Logic/GCodeParser, ProgramLoadPipeline
  → Simulation.Execution (playback, CycleCoordinator)
  → Controller/Core (режимы, jog, MDI)
  → Simulation.Bus (события MachineState)
  → MainWindow.xaml.cs (композиция UI + Helix)
  → Vis/* (3D: станок, траектория, заготовка, WCS)
```

| Слой | Папка (физически) | Ответственность |
|------|-------------------|-----------------|
| App | корень (`MainWindow`, `App.xaml`) | Композиция и wiring |
| UI | `src/CNCSS.Visualization/UI/` | FANUC-панель, диалоги, ViewModels, презентеры |
| Визуализация | `src/CNCSS.Visualization/Vis/`, `Voxel/` | Helix (станок, toolpath, воксельная заготовка), маркеры |
| Машина | `src/CNCSS.Logic/Machine/` | Профили, кинематика, WCS, крепление заготовки |
| Данные | `src/CNCSS.Data/` | `MachineState`, конфиги, константы |
| Симуляция | `src/CNCSS.Logic/Simulation/` | Шина, исполнение УП, цикл |
| Логика G-code | `src/CNCSS.Logic/Logic/` | Парсер, загрузка УП |

**Правило:** не раздувать `MainWindow.xaml.cs` — выносить логику в сервисы/координаторы, если затрагивается не только привязка UI.

---

## Критические инварианты (не ломать без явного запроса)

### Заготовка

1. **Параметры заготовки** задаются только через **Конструктор заготовки** (`src/CNCSS.Visualization/UI/Dialogs/StockConstructorWindow.xaml`, меню *Симуляция → Заготовка → Конструктор заготовки…*).
2. После «Подтвердить» в сцене показывается **параметрическая (невоксельная)** модель (`_stockModel` в `MainWindow`).
3. **Воксельная заготовка** создаётся **только при Cycle Start** (`EnsureVoxelStockForRun` → `SurfaceShellVolume` + SharpDX overlay).
4. **Нативный CNCSS.VoxelEngineC (C) удалён** — не восстанавливать P/Invoke и `native/CNCSS.VoxelEngineC/`.
5. **Автоподбор габаритов по контуру УП отключён** — не восстанавливать `AutoStockCheck` / подгонку по `prepared.Bounds`.
6. Форма вокселей строится **по типу из конструктора** через `ShapeMaskBuilder`; хранение — **surface shell** (`Logic/Voxel/SurfaceShell/`: bitmap + sparse surface index, 0.1 мм).
7. После Cycle Start viewport переключается на **SharpDX instancing** (`SharpDxStockViewportHost` overlay поверх `HelixViewport3D`).
8. После конструктора обязательны: `ApplyStock()`, `SyncStockVisualToTable()`, `SetStockDisplayVisible(true)`.

### Видимость в 3D

| Переключатель | Назначение |
|---------------|------------|
| `FilterShowStock` | Показать/скрыть заготовку (SharpDX surface shell или параметрическая модель на Helix) |
| `FilterShowMachine` | Видимость узлов станка; **не** должен влиять на видимость заготовки |
| `FilterShowToolpath` / `FilterShowTool` | Траектория и инструмент |

Обработчик: `SceneFilter_Changed` — для `FilterShowStock` вызывать `SetStockDisplayVisible`, не смешивать с включением расчёта вокселей.

### Крепление к столу

На станках с table-mounted workpiece заготовка, контур и маркер WCS — дочерние элементы `table.Root` (`MachineVisualCoordinator.SetTableMountedVisuals`). Координаты заготовки в table-local (`WorkpieceMountPlacement`, `_stockBoundsAreTableLocal`).

### WCS-маркер

`src/CNCSS.Visualization/Vis/WcsMarkerVisualBuilder.cs`: полупрозрачная **белая сфера** (80% прозрачности), оси X/Y/Z (красный/зелёный/синий), подпись G54–G59. **Без текстуры** на сфере.

---

## Ключевые файлы

| Задача | Файлы |
|--------|--------|
| Загрузка УП | `ProgramLoadOrchestrator`, `MainPresenter.LoadProgram`, `ProgramLoadPipeline` |
| Конструктор заготовки | `src/CNCSS.Visualization/UI/Dialogs/StockConstructorWindow.xaml`, `StockConstructorViewModel.cs`, `src/CNCSS.Data/StockConstructorConfig.cs`, `StockConstructorPreviewBuilder.cs` |
| Жизненный цикл заготовки | `StockLifecycleCoordinator.cs`, `MainWindow`: `ApplyStockConstructor`, `ApplyStock`, `EnsureVoxelStockForRun` |
| Воксели при пуске | `StockSimulationCoordinator`, `VoxelStockVolume`, `SurfaceShellVolume`, `SharpDxStockViewportHost` |
| Разрешение вокселей | Меню *Симуляция → Разрешение вокселей* → `StockLifecycleCoordinator.VoxelResolutionMm` |
| Навигация по УП | `UI/Hosts/ProgramLineNavigator` (без скрытого ListBox) |
| 3D станок | `MachineVisualCoordinator.cs`, `MachineSetupWindow.xaml` |
| Стили FANUC | `UI/Themes/FanucPanelResources.xaml` |
| Инструменты | `ToolSettingsWindow.xaml` |
| Цикл УП | `src/CNCSS.Logic/Simulation/Execution/CycleCoordinator.cs`, `ProgramPlaybackHost.cs` |
| WCS / смещения | `src/CNCSS.Logic/Machine/Model/WorkpieceMountPlacement.cs`, `WcsMarkerVisualBuilder.cs` |

---

## UI и стили

- Общий словарь: `src/CNCSS.Visualization/UI/Themes/FanucPanelResources.xaml` (`FanucBezelOuter`, `FanucPanelButton`, `FanucConfirmButton`, `FanucToolFieldTextBox`, …).
- Новые диалоги оформлять как `MachineSetupWindow` / `ToolSettingsWindow` / `StockConstructorWindow`: серый фон `#FFB0B0B0`, двойная рамка bezel, Consolas в полях.
- Цвет инструмента/заготовки: `ToolPaletteSwatches`, `ToolColorPickerInterop` (WinForms color dialog — глобальные using WinForms отключены в csproj).

---

## Соглашения по коду

- **Минимальный diff** — не рефакторить соседний код без запроса.
- **Существующие паттерны** — MVVM в диалогах, `RelayCommand`, `INotifyPropertyChanged` в ViewModels.
- **Helix Toolkit** — `MeshBuilder`, `MaterialHelper`, `HelixViewport3D`; UV нужны только для текстурированных мешей.
- **Цвета WPF** — `System.Windows.Media.Color`, не путать с `System.Drawing.Color`.
- **Коммиты** — только по явной просьбе пользователя.
- **Тесты** — добавлять при изменении парсера, кинематики, контрактов съёма; не писать тривиальные тесты.

---

## Типичные ошибки агента

1. Включать расчёт вокселей при загрузке УП или в `ApplyStock()` — воксели только при **Cycle Start**.
2. Создавать воксели при переключении `FilterShowStock` или в `ApplyStock()` — только при Cycle Start.
3. Забыть `SyncStockVisualToTable()` после создания заготовки — модель не на столе.
4. Строить воксельную заготовку только как bbox — использовать `StockConstructorConfig` и маску формы.
5. Использовать `MeshBuilder.AddExtrudedGeometry` с одинаковыми start/end для шестигранника — меш пустой; строить призму явно (см. `StockConstructorPreviewBuilder.BuildHexPrism`).
6. Блокировать UI при инициализации вокселей на Cycle Start — `SurfaceShellVolume.Create` в фоне/worker; SharpDX attach без синхронного полного mesh.
7. Смешивать `System.Windows.Forms` и WPF типы без полной квалификации имён.
8. Удалять пустые чанки из native map после cut — probe вернёт implicit solid («висящий» материал).
9. Рендерить воксельную заготовку через WPF Helix mesh — только **SharpDX overlay**; Helix WPF для станка/траектории и parametric preview.

---

## Данные пользователя (runtime)

- Профили станка: `%USERPROFILE%\Documents\CNCSS\Machines\`
- Заводской снимок: `%USERPROFILE%\Documents\CNCSS\FactoryMachine\`

---

## Дополнительная документация

- `README.md` — обзор для людей
- `DEVELOPMENT.md` — smoke-сценарий, правила разработки
- `docs/ARCHITECTURE.md` — обзор слоёв
- `Readme.txt` — краткая шпаргалка

При противоречии между устаревшим комментарием в коде и этим файлом — сверяться с актуальным поведением в `MainWindow.xaml.cs` и тестами.
