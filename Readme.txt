================================================================================
  CNCSS — симулятор ЧПУ (WPF, Helix Toolkit, воксельная заготовка)
================================================================================

Полное описание: README.md
Для ИИ-агента:  AGENTS.md

Быстрый старт (Windows, .NET 8 SDK):
  dotnet restore
  dotnet build -c Release
  dotnet run  -c Release

Проверка:  dotnet test -c Release

Заготовка:
  Симуляция → Заготовка → Конструктор заготовки…
  После подтверждения — параметрическая модель на столе станка.
  Воксели строятся при Cycle Start (модальное окно прогресса).
  Фильтр сцены: иконка куба — видимость заготовки.

Данные пользователя:
  Профили станка:  %USERPROFILE%\Documents\CNCSS\Machines\
  Заводской снимок: %USERPROFILE%\Documents\CNCSS\FactoryMachine\

Технологии: net8.0-windows, UseWPF, WinForms только для диалога цвета (см. Using
Remove для System.Windows.Forms и System.Drawing в CNCSS.csproj).

Образцы УП: O0001.nc, O1204.nc копируются в выходной каталог при сборке.
