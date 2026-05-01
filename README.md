# CNCSS

Симулятор траектории ЧПУ и удаления материала на **.NET 8**, **WPF** и **Helix Toolkit (Wpf)**. Интерфейс оператора оформлен в духе панели стойки FANUC: список программы, MDI, режимы, заготовка (воксели), библиотека инструментов.

---

## Сборка и запуск

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release --project CNCSS.csproj
```

- **Платформа:** Windows (`net8.0-windows`).
- **SDK:** .NET 8 или новее.
- Образцы УП `O0001.nc`, `O1204.nc` при сборке копируются в выходной каталог (`PreserveNewest`).

### Особенность проекта (WinForms)

Для диалога выбора цвета инструмента включён `UseWindowsForms`; глобальные `using` для `System.Windows.Forms` и `System.Drawing` **отключены** в `CNCSS.csproj` (`Using Remove`), чтобы не конфликтовать с типами WPF (`Color`, `Application`, …).

---

## Основные возможности

| Область | Описание |
|--------|----------|
| **G-код** | Разбор файла, список строк, переход к строке с корректным `MachineState`. |
| **3D** | Траектория, позиция инструмента, воксельная заготовка с «вырезанием» цилиндром инструмента. |
| **Симуляция** | Пуск/удержание цикла, скорость, single block, optional stop, сухой прогон (через события шины и сервисов). |
| **Инструменты** | Диалог «TOOL DATA»: тип, T, геометрия, палитра цвета + системный ColorDialog, превью без view cube. |

---

## Структура решения (папки)

| Папка | Назначение |
|-------|------------|
| **Data** | `MachineState`, `ParsedCommand`, `GCodeRegistry`, `GCodeTemplate`, `ArcGeometry`, `ProjectConstants`, инструменты `Tools/*` (`ITool`, фрезы/сверло). |
| **Logic** | `GCodeParser` / `IGCodeParser`, `CommandReplayer`, `ArcCalculator`. |
| **Machine** | `IMachineCore`, `MachineCore` — ось X/Y/Z и реакция на команды контроллера; `Machine.Model` (`MachineAxesState`, `MotionBlock`). |
| **Controller** | `IControllerCore`, `ControllerCore` — цикл, режимы, jog, MDI; модель `ControllerMode`. |
| **Simulation** | `Simulation.Bus` — `ISimulationBus`, `SimulationBus`, события (`MachineStateChangedEvent`, …). `Simulation.Execution` — шаг УП, интерполяция, `PlaybackLoopService`, `ProgramExecutionService`, `ProgramStateService`, `InterpolationService`. |
| **Vis** | `ToolpathBuilder`, `VoxelStock`, `StockCutWorker`, `VoxelSimulationProfile`, `IVisualizer`. |
| **UI** | `MainWindow`, `FanucPanel`, `OperatorStation`, презентеры (`UiRenderService`, `ToolpathRenderService`, `StockRenderService`), `MainPresenter`, ViewModels, диалоги инструментов, `RelayCommand`. |

Поток данных в общих чертах: **парсер → команды и состояния → построение траектории и движений → шина событий ↔ ядро станка/контроллер → MainWindow обновляет Helix и панели.**

---

## Воксельная заготовка

- Заготовка делится на **чанки** (`Data/Tools/VoxelChunk.cs`), компактное хранение битами.
- Удаление материала учитывает траекторию и радиус инструмента (`VoxelStock`).
- Визуализация: пересчёт изменённых чанков, greedy meshing по внешней поверхности (см. также комментарии и профиль в `Vis`).

Разрешение сетки задаётся пользователем (константы высокой/средней/грубой сетки — `ProjectConstants`).

---

## Разбор G-кода

- Реестр кодов и модальность — `GCodeRegistry`, `GCodeTemplate`.
- Парсер поддерживает типичные адреса X/Y/Z, F/S/T, плоскости и дуги с I/J/K или R (подробнее — реализация в `GCodeParser`, `ArcCalculator`).
- `ParsedCommand` связывает строку файла с параметрами и результатом в состоянии станка (для перемотки и симуляции).

---

## Документация в коде

Включена генерация XML-документации сборки (`GenerateDocumentationFile`). Для типов и членов без описания предупреждение CS1591 подавлено; ключевые интерфейсы и сервисы снабжены тегами `<summary>`.

---

## Лицензия и вклад

Укажите лицензию в репозитории при публикации. Правки приветствуются через pull request с понятным описанием и проверкой `dotnet build`.
