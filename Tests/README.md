# Руководство по запуску тестов CNCSS

## Структура тестового проекта

```
Tests/
├── CNCSS.Tests.csproj      # Проект тестов (xUnit)
├── ArcCalculatorTests.cs   # Тесты для расчета дуг G2/G3
├── MachineStateTests.cs    # Тесты для состояния станка
└── GCodeRegistryTests.cs   # Тесты для реестра G/M кодов
```

## Установка .NET SDK

### Windows
1. Скачайте установщик: https://dotnet.microsoft.com/download
2. Установите .NET 8.0 SDK
3. Перезапустите терминал

### Linux (Debian/Ubuntu)
```bash
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

### macOS
```bash
brew install --cask dotnet-sdk
```

## Запуск тестов

Откройте терминал в корне проекта и выполните:

```bash
# Восстановление пакетов
dotnet restore Tests/CNCSS.Tests.csproj

# Запуск всех тестов
dotnet test Tests/CNCSS.Tests.csproj

# Запуск с подробным выводом
dotnet test Tests/CNCSS.Tests.csproj --logger "console;verbosity=detailed"

# Запуск с покрытием кода
dotnet test Tests/CNCSS.Tests.csproj --collect:"XPlat Code Coverage"

# Запуск конкретных тестов по имени
dotnet test Tests/CNCSS.Tests.csproj --filter "FullyQualifiedName~ArcCalculator"

# Запуск тестов в режиме watch (автоматический перезапуск при изменениях)
dotnet watch test --project Tests/CNCSS.Tests.csproj
```

## Покрытие тестами

### ArcCalculatorTests (10 тестов)
- `TryComputeArc_G17_Clockwise_CenterFromIJK_ReturnsTrue` - дуга G2 в плоскости XY
- `TryComputeArc_G17_CounterClockwise_CenterFromIJK_ReturnsTrue` - дуга G3 в плоскости XY
- `TryComputeArc_G18_Plane_XZ_CenterFromIJK_ReturnsTrue` - дуга в плоскости XZ
- `TryComputeArc_G19_Plane_YZ_CenterFromIJK_ReturnsTrue` - дуга в плоскости YZ
- `TryComputeArc_WithR_MinorArc_ReturnsTrue` - дуга через радиус R (≤180°)
- `TryComputeArc_WithR_Negative_MajorArc_ReturnsTrue` - дуга через радиус R (>180°)
- `TryComputeArc_NoIJK_NoR_ReturnsFalse` - отсутствие данных о центре
- `TryComputeArc_InvalidPlane_ReturnsFalse` - неверная плоскость
- `TryComputeArc_FullCircle_StartEqualsEnd_IJK_ReturnsTrue` - полная окружность

### MachineStateTests (24 теста)
- `Constructor_InitialState_HasDefaultValues` - проверка начальных значений
- `Reset_ResetsAllPropertiesToDefaults` - сброс состояния
- `UpdatePosition_AbsoluteMode_UpdatesCoordinates` - абсолютное позиционирование
- `UpdatePosition_RelativeMode_AddsToCoordinates` - относительное позиционирование
- `SetPosition_ForcesAbsolutePosition` - принудительная установка позиции
- `SavePreviousPosition_SavesCurrentCoordinates` - сохранение предыдущей позиции
- `HasPositionChanged_ReturnsTrue_WhenPositionChanged` - проверка изменения позиции
- `HasPositionChanged_ReturnsFalse_WhenPositionNotChanged` - позиция не изменилась
- `GetDelta_ReturnsCorrectDifferences` - получение дельты перемещения
- `Clone_CreatesIndependentCopy` - клонирование состояния
- `SetCoordinateMode_ChangesIsAbsolute` - режим координат G90/G91
- `SetUnits_ChangesIsMetric` - единицы измерения G20/G21
- `SetMotionMode_UpdatesMotionMode` - режим движения
- `SetFeedRate_UpdatesFeedRateAndEffectiveFeedRate` - скорость подачи
- `EffectiveFeedRate_IsRapidFeed_WhenMotionModeIsG0` - быстрое перемещение G0
- `SetSpindle_UpdatesSpindleState` - управление шпинделем
- `SetCoolant_UpdatesCoolantState` - управление охлаждением
- `SetTool_UpdatesToolProperties` - установка инструмента
- `GetPosition_ReturnsAllCoordinates` - получение всех координат
- `GetXYZ_ReturnsXYZCoordinates` - получение координат XYZ

### GCodeRegistryTests (11 тестов)
- `GetGCode_ValidNumber_ReturnsCorrectCode` - получение G-кода
- `GetGCode_InvalidNumber_ReturnsNull` - несуществующий G-код
- `GetMCode_ValidNumber_ReturnsCorrectCode` - получение M-кода
- `GetMCode_InvalidNumber_ReturnsNull` - несуществующий M-код
- `IsMovementCode_G0G1G2G3_ReturnsTrue` - проверка кодов движения
- `IsMovementCode_OtherCodes_ReturnsFalse` - другие коды
- `IsArcCode_G2G3_ReturnsTrue` - проверка кодов дуг
- `IsArcCode_OtherCodes_ReturnsFalse` - другие коды
- `AllGCodes_ContainsAllDefinedGCodes` - полнота реестра G-кодов
- `AllMCodes_ContainsAllDefinedMCodes` - полнота реестра M-кодов
- `GCodeTemplate_HasCorrectLetterAndNumber` - структура шаблона

## Добавление новых тестов

### Для GCodeParser
Создайте файл `Tests/GCodeParserTests.cs`:

```csharp
using CNCSS.Data;
using CNCSS.Logic;

namespace CNCSS.Tests;

public class GCodeParserTests
{
    [Fact]
    public void ProcessLine_SimpleG0Command_ParsesCorrectly()
    {
        // Arrange
        var parser = new GCodeParser();
        
        // Act
        parser.ProcessLine("G0 X10 Y20 Z30", 1);
        
        // Assert
        Assert.Single(parser.Commands);
        var cmd = parser.Commands[0];
        Assert.Equal(10, cmd.X);
        Assert.Equal(20, cmd.Y);
        Assert.Equal(30, cmd.Z);
    }
    
    [Fact]
    public void ProcessFile_EmptyFile_ReturnsEmptyCommands()
    {
        // Arrange
        var parser = new GCodeParser();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "");
        
        try
        {
            // Act
            parser.ProcessFile(tempFile);
            
            // Assert
            Assert.Empty(parser.Commands);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
```

## Интеграция с CI/CD

### GitHub Actions
Создайте `.github/workflows/tests.yml`:

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore
    
    - name: Test
      run: dotnet test --no-build --verbosity normal
```

## Рекомендуемые следующие шаги

1. **Добавить тесты для GCodeParser** - парсинг файлов, обработка ошибок
2. **Добавить тесты для VoxelStock** - симуляция материала
3. **Добавить тесты для ToolpathBuilder** - построение траекторий
4. **Настроить покрытие кода** - цель >80%
5. **Добавить интеграционные тесты** - полный цикл обработки файла
