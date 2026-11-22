<#
.SYNOPSIS
    After a Terraria update: lists what changed for the TAS, rebuilds, and checks that the reference movies still
    replay in sync.

.DESCRIPTION
    1. GameDiff checks compat/terraria-surface.txt against the installed Terraria.exe: members the TAS uses that
       disappeared, and methods it hooks or mirrors whose code changed (docs/porting.md says what to do with each).
    2. Builds TerrariaTas.exe. The build refuses a Terraria version other than the one build/Version.props names:
       retarget it first when porting.
    3. Runs each reference movie with --verify twice: the second launch also compares every frame with the first.
       Reference movies are the ones given with -Movies, or else every movie in movies\ whose player and world are in
       the TAS data folder.

    The game window opens for every run and closes by itself (a run stops after 3 minutes at most). Exit code 0 when
    nothing needs review and every run is in sync.

.PARAMETER Movies
    Movie files to verify. Default: every movies\*.tas whose Player and World files are in the data folder.

.PARAMETER TerrariaDir
    The Terraria install. Default: TERRARIA_DIR, then the Steam default.

.PARAMETER DataDir
    The TAS data folder holding Players\ and Worlds\. Default: Documents\My Games\Terraria TAS.

.EXAMPLE
    .\build\port-check.ps1

.EXAMPLE
    .\build\port-check.ps1 -Movies movies\run.tas, movies\boss.tas
#>
[CmdletBinding()]
param(
    [string[]] $Movies = @(),
    [string] $TerrariaDir = $(if ($env:TERRARIA_DIR) { $env:TERRARIA_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Terraria' }),
    [string] $DataDir = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria TAS')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = Join-Path $root 'src\TerrariaTas.Launcher\bin\Release\net48\TerrariaTas.exe'
$summary = New-Object System.Collections.Generic.List[string]
$allGood = $true

function Write-Step([string] $Text) { Write-Host ""; Write-Host "== $Text" -ForegroundColor Cyan }

# The header value of a movie, or $null.
function Get-HeaderValue([string] $Path, [string] $Name) {
    foreach ($line in Get-Content $Path) {
        $text = ($line -replace '#.*$', '').Trim()
        if ($text -match '^\d') { return $null } # input lines: the header is over
        if ($text -match "^$Name\s*:\s*(.+)$") { return $Matches[1].Trim() }
    }
    return $null
}

Write-Step "Checking the TAS's game surface against $TerrariaDir\Terraria.exe"
& dotnet run --project (Join-Path $root 'tools\TerrariaTas.GameDiff') -c Release -- check (Join-Path $TerrariaDir 'Terraria.exe') (Join-Path $root 'compat\terraria-surface.txt')
switch ($LASTEXITCODE) {
    0 { $summary.Add('Game surface: unchanged') }
    1 { $summary.Add('Game surface: CHANGES TO REVIEW (listed above, see docs/porting.md)'); $allGood = $false }
    default { $summary.Add('Game surface: the check could not run (see above)'); $allGood = $false }
}

Write-Step "Checking the methods that draw random numbers against $TerrariaDir\Terraria.exe"
& dotnet run --project (Join-Path $root 'tools\TerrariaTas.GameDiff') -c Release -- rng-check (Join-Path $TerrariaDir 'Terraria.exe') (Join-Path $root 'compat\terraria-rng.txt')
switch ($LASTEXITCODE) {
    0 { $summary.Add('RNG map: unchanged') }
    1 { $summary.Add('RNG map: CHANGES TO REVIEW (listed above): forecasts and RNG manipulation routes may be affected'); $allGood = $false }
    default { $summary.Add('RNG map: the check could not run (see above)'); $allGood = $false }
}

Write-Step 'Building TerrariaTas.exe'
& dotnet build (Join-Path $root 'src\TerrariaTas.Launcher') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host 'The build failed (see above): nothing to verify. When porting, retarget build/Version.props first.' -ForegroundColor Red
    exit 1
}

if ($Movies.Count -eq 0) {
    foreach ($movie in Get-ChildItem (Join-Path $root 'movies') -Filter '*.tas') {
        $player = Get-HeaderValue $movie.FullName 'Player'
        $world = Get-HeaderValue $movie.FullName 'World'
        if ($player -and $world -and (Test-Path (Join-Path $DataDir "Players\$player")) -and (Test-Path (Join-Path $DataDir "Worlds\$world"))) {
            $Movies += $movie.FullName
        } else {
            $summary.Add("$($movie.Name): skipped (its player and world are not in $DataDir)")
        }
    }
}

foreach ($movie in $Movies) {
    $path = (Resolve-Path $movie).Path
    $name = Split-Path $path -Leaf
    foreach ($launch in 1, 2) {
        Write-Step "$name, launch $launch of 2: --verify (the game window opens and closes by itself, 3 minutes at most)"
        $process = Start-Process -FilePath $game -ArgumentList "`"$path`" --verify --data `"$DataDir`"" -NoNewWindow -PassThru
        $null = $process.Handle # keeps the exit code readable in Windows PowerShell
        $process.WaitForExit()
        $report = Join-Path $DataDir 'verify-result.txt'
        $lines = if (Test-Path $report) { @(Get-Content $report) } else { @('NO REPORT') }
        $previous = $lines | Where-Object { $_ -like 'Against the previous launch*' } | Select-Object -First 1
        $verdict = if ($process.ExitCode -eq 0) { 'in sync' } else { "FAILED (exit code $($process.ExitCode))" }
        if ($process.ExitCode -ne 0) { $allGood = $false }
        $summary.Add("$name, launch ${launch}: $verdict" + $(if ($previous) { "; $previous" } else { '' }))
    }
}

Write-Step 'Summary'
$summary | ForEach-Object { Write-Host "  $_" }
if ($Movies.Count -eq 0) { Write-Host '  No reference movie could run: copy their players and worlds into the data folder.' -ForegroundColor Yellow }
if ($allGood) { Write-Host 'Nothing to review.' -ForegroundColor Green } else { Write-Host 'Something needs review (above).' -ForegroundColor Red }
exit $(if ($allGood -and $Movies.Count -gt 0) { 0 } else { 1 })
