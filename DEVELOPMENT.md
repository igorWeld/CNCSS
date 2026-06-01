# DEVELOPMENT

## Minimum Check

Run this before and after behavior changes:

```powershell
dotnet test -c Release
```

This restores packages, builds the WPF app, builds `CNCSS.GpuVerification`, and runs the xUnit suite.

## Local Run

```powershell
dotnet run -c Release --project CNCSS.csproj
```

The sample programs `O0001.nc` and `O1204.nc` are copied to the output directory during build.

## Manual Smoke Scenario

1. Start the app and open `O0001.nc` or `O1204.nc`.
2. Confirm the program appears in the list and the toolpath is visible.
3. Press Cycle Start, then Feed Hold, then Cycle Start again.
4. Try Reset and verify the axes return to home.
5. Switch to JOG or HANDLE and jog X/Y/Z inside soft limits.
6. Switch to MDI and execute a simple move such as `G90 G0 X0 Y0 Z10`.
7. Enable stock display and confirm material removal updates while the program runs.
8. Open **Станок → Настройка модели станка**, load STL files for base/table/spindle (optional), set travel limits, save, and confirm the machine assembly appears in the main viewport.

## Development Rules

- Keep controller and machine behavior behind `Controller/Core`, `Machine/Core`, and `Simulation/Bus`.
- Keep `MainWindow.xaml.cs` as a WPF composition host; move reusable logic into services.
- Add or update tests when changing controller modes, interlocks, MDI/JOG behavior, G-code modal interpretation, or material removal contracts.
- Treat `Vis/VoxelStock.cs` as the runtime stock backend; keep BRep/boolean code experimental until it is explicitly wired into the UI.
