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

| Слой | Папка | Ответственность |
|------|-------|-----------------|
| UI | `UI/` | FANUC-панель, диалоги, ViewModels, презентеры |
| Визуализация | `Vis/` | Helix, `MachineVisualCoordinator`, `VoxelStock`, маркеры |
| Машина | `Machine/` | Профили, кинематика, WCS, крепление заготовки |
| Данные | `Data/` | `MachineState`, парсер, воксельные чанки |
| Симуляция | `Simulation/` | Шина, исполнение УП, цикл |
| GPU | `CNCSS.GpuVerification/` | Опциональная верификация занятости |

**Правило:** не раздувать `MainWindow.xaml.cs` — выносить логику в сервисы/координаторы, если затрагивается не только привязка UI.

---

## Критические инварианты (не ломать без явного запроса)

### Заготовка

1. **Параметры заготовки** задаются только через **Конструктор заготовки** (`UI/Dialogs/StockConstructorWindow.xaml`, меню *Симуляция → Заготовка → Конструктор заготовки…*).
2. После «Подтвердить» в сцене показывается **параметрическая (невоксельная)** модель (`_stockModel` в `MainWindow`).
3. **Воксельная заготовка** создаётся **только при Cycle Start** (`EnsureVoxelStockForRun` → `TryEnsureVoxelStock`), с **модальным окном** прогресса.
4. При старте обработки цельная модель **заменяется** воксельной (`GetStockViewportContent()` отдаёт `_stock.MainModel`, если `_stock != null`).
5. **Автоподбор габаритов по контуру УП отключён** — не восстанавливать `AutoStockCheck` / подгонку по `prepared.Bounds`.
6. Форма вокселей строится **по типу из конструктора** (прямоугольник, шестигранник, круг, труба) через маску в `StockSimulationCoordinator.BuildOccupancyMask`.
7. После конструктора обязательны: `ApplyStock()`, `SyncStockVisualToTable()`, `SetStockDisplayVisible(true)`.

### Видимость в 3D

| Переключатель | Назначение |
|---------------|------------|
| `FilterShowStock` | Показать/скрыть заготовку (только визуал, не расчёт вокселей) |
| `FilterShowMachine` | Видимость узлов станка; **не** должен влиять на видимость заготовки |
| `FilterShowToolpath` / `FilterShowTool` | Траектория и инструмент |

Обработчик: `SceneFilter_Changed` — для `FilterShowStock` вызывать `SetStockDisplayVisible`, не смешивать с включением расчёта вокселей.

### Крепление к столу

На станках с table-mounted workpiece заготовка, контур и маркер WCS — дочерние элементы `table.Root` (`MachineVisualCoordinator.SetTableMountedVisuals`). Координаты заготовки в table-local (`WorkpieceMountPlacement`, `_stockBoundsAreTableLocal`).

### WCS-маркер

`Vis/WcsMarkerVisualBuilder.cs`: полупрозрачная **белая сфера** (80% прозрачности), оси X/Y/Z (красный/зелёный/синий), подпись G54–G59. **Без текстуры** на сфере.

---

## Ключевые файлы

| Задача | Файлы |
|--------|--------|
| Загрузка УП | `ProgramLoadOrchestrator`, `MainPresenter.LoadProgram`, `ProgramLoadPipeline` |
| Конструктор заготовки | `UI/Dialogs/StockConstructorWindow.xaml`, `UI/ViewModels/StockConstructorViewModel.cs`, `Data/StockConstructorConfig.cs`, `Vis/StockConstructorPreviewBuilder.cs` |
| Жизненный цикл заготовки | `Vis/StockLifecycleCoordinator.cs`, `MainWindow`: `ApplyStockConstructor`, `ApplyStock`, `EnsureVoxelStockForRun` |
| Воксели при пуске | `StockSimulationCoordinator.CreateRuntime(resolutionMm, …)`, `Vis/VoxelStock.cs` |
| Разрешение вокселей | Меню *Симуляция → Разрешение вокселей* → `StockLifecycleCoordinator.VoxelResolutionMm` |
| Навигация по УП | `UI/Hosts/ProgramLineNavigator` (без скрытого ListBox) |
| 3D станок | `Vis/MachineVisualCoordinator.cs`, `UI/Dialogs/MachineSetupWindow.xaml` |
| Стили FANUC | `UI/Themes/FanucPanelResources.xaml` |
| Инструменты | `UI/Dialogs/ToolSettingsWindow.xaml` |
| Цикл УП | `Simulation/Execution/CycleCoordinator.cs`, `UI/Hosts/ProgramPlaybackHost.cs` |
| WCS / смещения | `Machine/Model/WorkpieceMountPlacement.cs`, `Vis/WcsMarkerVisualBuilder.cs` |

---

## UI и стили

- Общий словарь: `UI/Themes/FanucPanelResources.xaml` (`FanucBezelOuter`, `FanucPanelButton`, `FanucConfirmButton`, `FanucToolFieldTextBox`, …).
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
6. Блокировать UI при вокселях без модального окна — `ShowVoxelLoadingWindow` / `WarmUpStockVisualFullySync` в `EnsureVoxelStockForRun`.
7. Смешивать `System.Windows.Forms` и WPF типы без полной квалификации имён.

---

## Данные пользователя (runtime)

- Профили станка: `%USERPROFILE%\Documents\CNCSS\Machines\`
- Заводской снимок: `%USERPROFILE%\Documents\CNCSS\FactoryMachine\`

---

## Дополнительная документация

- `README.md` — обзор для людей
- `DEVELOPMENT.md` — smoke-сценарий, правила разработки
- `Readme.txt` — краткая шпаргалка

При противоречии между устаревшим комментарием в коде и этим файлом — сверяться с актуальным поведением в `MainWindow.xaml.cs` и тестами.
