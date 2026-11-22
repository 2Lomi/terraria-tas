<#
.SYNOPSIS
    Builds TerrariaTas.exe and TAS Studio, runs the TAS and opens Studio.

.DESCRIPTION
    Takes the same arguments as TerrariaTas.exe (see .\run.ps1 --help). Movies are found by name in this
    repository's movies folder, unless --movies says another. The game's log stays in this console. The script
    ends when the game closes and returns its exit code.

    Studio opens once the game window is up, so a run that stops on its arguments (a movie that is not
    there, --new with a name that is taken...) opens nothing and says why in red. Studio is built and opened
    only when it is not open yet: an open Studio connects to the new game by itself (close it before running
    the script to get a rebuilt one). It is not opened for --verify, --list and --help.

.PARAMETER NoBuild
    Skip the builds and run what was built last.

.PARAMETER NoStudio
    Do not open TAS Studio.

.EXAMPLE
    .\run.ps1 my-run
    Plays movies\my-run.tas from its start, to watch: nothing can change the movie.

.EXAMPLE
    .\run.ps1 my-run --edit
    Opens the movie to work on it: paused at its start, F1 records.

.EXAMPLE
    .\run.ps1 --new
    Creates a softcore character, a small corruption world in normal difficulty and a movie starting with them,
    then records from its first frame.

.EXAMPLE
    .\run.ps1 --new speedy --crimson --expert
    The same with a crimson world in expert, in movies\speedy.tas.

.EXAMPLE
    .\run.ps1 --list
    Lists the movies with their character and world, and checks those files.

.EXAMPLE
    .\run.ps1
    Free play: enter a world from the menus, the TAS starts there.
#>
[CmdletBinding()]
param(
    [switch] $NoBuild,
    [switch] $NoStudio,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $GameArgs
)

$ErrorActionPreference = 'Stop'

$game = Join-Path $PSScriptRoot 'src\TerrariaTas.Launcher\bin\Release\net48\TerrariaTas.exe'
$studio = Join-Path $PSScriptRoot 'src\TerrariaTas.Studio\bin\Release\net8.0-windows\TerrariaTas.Studio.exe'
if ($null -eq $GameArgs) { $GameArgs = @() }

function Stop-Run([string] $Message) {
    Write-Host $Message -ForegroundColor Red
    exit 1
}

function Build-Project([string] $Name) {
    Write-Host "Building $Name..."
    dotnet build (Join-Path $PSScriptRoot "src\$Name") -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Stop-Run "The build of $Name failed (see above)." }
}

# Start-Process joins its arguments with spaces: quote the ones that need it, the way the program splits them back.
function ConvertTo-Argument([string] $Value) {
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    return '"' + (($Value -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

$help = ($GameArgs -contains '--help') -or ($GameArgs -contains '-h')
$noGame = $help -or ($GameArgs -contains '--list')
$openStudio = -not ($NoStudio -or $noGame -or ($GameArgs -contains '--verify'))

# Movie names and --new go to the repository's movies folder, wherever the script is started from.
if (-not ($GameArgs -contains '--movies')) { $GameArgs = @($GameArgs) + @('--movies', (Join-Path $PSScriptRoot 'movies')) }

# A running game locks the files the build replaces, and holds the Studio link.
$running = @(Get-Process -Name 'TerrariaTas' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    if (-not $noGame) { Stop-Run "TerrariaTas.exe is already running (process $($running.Id -join ', ')). Close it first." }
    $NoBuild = $true # --list and --help can run beside the game, with what was built last
}

$studioOpen = @(Get-Process -Name 'TerrariaTas.Studio' -ErrorAction SilentlyContinue).Count -gt 0

if (-not $NoBuild) {
    Build-Project 'TerrariaTas.Launcher'
    if ($openStudio -and -not $studioOpen) { Build-Project 'TerrariaTas.Studio' }
}

if (-not (Test-Path -LiteralPath $game)) { Stop-Run "$game is missing: run without -NoBuild." }
if ($openStudio -and -not $studioOpen -and -not (Test-Path -LiteralPath $studio)) { Stop-Run "$studio is missing: run without -NoBuild." }

if (-not $noGame) { Write-Host "Starting TerrariaTas.exe $($GameArgs -join ' ')" }
$start = @{ FilePath = $game; NoNewWindow = $true; PassThru = $true }
if ($GameArgs.Count -gt 0) { $start.ArgumentList = ($GameArgs | ForEach-Object { ConvertTo-Argument $_ }) -join ' ' }
$process = Start-Process @start
$null = $process.Handle # Windows PowerShell keeps the exit code only of a process whose handle was read

if ($openStudio) {
    if ($studioOpen) {
        Write-Host 'TAS Studio is already open: it connects to the game by itself.'
    } else {
        # Only once the game window is up: a game that stopped on its arguments has none.
        while (-not $process.HasExited) {
            $process.Refresh()
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                Write-Host 'Opening TAS Studio...'
                Start-Process -FilePath $studio
                break
            }
            Start-Sleep -Milliseconds 200
        }
    }
}

$process.WaitForExit()
$code = $process.ExitCode
if ($code -ne 0) { Write-Host "TerrariaTas.exe stopped with exit code $code (the reason is above)." -ForegroundColor Red }
exit $code
