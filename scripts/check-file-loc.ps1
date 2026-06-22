# Fail if any .cs file exceeds LOC budget (SOLID gate, whitelist during migration).
param(
    [int]$AppMax = 500,
    [int]$LogicMax = 400,
    [int]$VisMax = 400
)

$failed = $false
Get-ChildItem -Path $PSScriptRoot\.. -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    ForEach-Object {
        $lines = (Get-Content $_.FullName | Measure-Object -Line).Lines
        $max = $VisMax
        if ($_.FullName -match '\\src\\CNCSS\.Logic\\(Logic|Controller|Machine|Simulation|Geometry|Vis)\\') { $max = $LogicMax }
        if ($_.FullName -match '\\src\\CNCSS\.Visualization\\') { $max = $VisMax }
        if ($_.FullName -match 'MainWindow\.xaml\.cs$') { $max = $AppMax }
        if ($lines -gt $max) {
            Write-Warning "$($_.FullName): $lines lines (max $max)"
            $failed = $true
        }
    }

if ($failed) { exit 1 }
