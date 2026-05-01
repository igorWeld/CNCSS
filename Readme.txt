================================================================================
  CNCSS — симулятор ЧПУ (WPF, Helix Toolkit, воксельная заготовка)
================================================================================

Полное описание архитектуры, структуры папок и инструкции по сборке см. в файле
README.md (Markdown в корне проекта).

Быстрый старт (Windows, .NET 8 SDK):
  dotnet restore
  dotnet build -c Release
  dotnet run  -c Release

Технологии: net8.0-windows, UseWPF, WinForms только для диалога цвета (см. Using
Remove для System.Windows.Forms и System.Drawing в CNCSS.csproj).

Образцы УП: O0001.nc, O1204.nc копируются в выходной каталог при сборке.
