<#
.SYNOPSIS
    Builds a release zip of Terraria TAS for the Terraria version named in build/Version.props.

.DESCRIPTION
    Runs the Core tests, builds TerrariaTas.exe (which compiles against the installed Terraria, so that Terraria
    version must be installed) and TAS Studio, and zips them with the documentation and the smoke-test movie into
    artifacts/TerrariaTas-<version>.zip, where <version> is "<Terraria version>-r<revision>" (docs/porting.md).
    Nothing of Terraria's goes into the zip: the script stops if a game file shows up.

    Publishing stays a separate, deliberate step: the script prints the GitHub release command to run.

.PARAMETER SkipTests
    Do not run the Core tests first.

.EXAMPLE
    .\build\package.ps1
#>
[CmdletBinding()]
param([switch] $SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Stop-Packaging([string] $Message) {
    Write-Host $Message -ForegroundColor Red
    exit 1
}

function Invoke-Dotnet([string[]] $Arguments) {
    & dotnet @Arguments --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Stop-Packaging "dotnet $($Arguments -join ' ') failed (see above)." }
}

[xml] $props = Get-Content (Join-Path $PSScriptRoot 'Version.props')
$group = $props.Project.PropertyGroup
$version = "$($group.TargetTerrariaVersion)-r$($group.TasRevision)"
$name = "TerrariaTas-$version"
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts $name
$zip = Join-Path $artifacts "$name.zip"
Write-Host "Packaging $name"

if (-not $SkipTests) { Invoke-Dotnet @('test', (Join-Path $root 'tests\TerrariaTas.Core.Tests'), '-c', 'Release') }
Invoke-Dotnet @('build', (Join-Path $root 'src\TerrariaTas.Launcher'), '-c', 'Release')

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Invoke-Dotnet @('publish', (Join-Path $root 'src\TerrariaTas.Studio'), '-c', 'Release', '-o', (Join-Path $stage 'Studio'))

# TerrariaTas.exe with the DLLs it loads into the game's process (Terraria and XNA are referenced, never copied).
$launcher = Join-Path $root 'src\TerrariaTas.Launcher\bin\Release\net48'
Get-ChildItem $launcher -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config', '.pdb' } | Copy-Item -Destination $stage
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'TODO.md') -Destination $stage
Copy-Item (Join-Path $root 'docs') -Destination (Join-Path $stage 'docs') -Recurse
New-Item -ItemType Directory -Path (Join-Path $stage 'movies') | Out-Null
Copy-Item (Join-Path $root 'movies\smoke-test.tas') -Destination (Join-Path $stage 'movies')

# The repository and its releases never carry game code or assets.
$gameFiles = Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Name -eq 'Terraria.exe' -or $_.Name -like 'Microsoft.Xna.*' -or $_.Name -like 'ReLogic*' -or $_.Name -like 'steam_api*' -or $_.Extension -in '.xnb', '.xwb', '.xsb', '.xgs'
}
if ($gameFiles) { Stop-Packaging "Game files ended up in the package: $($gameFiles.Name -join ', ')" }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)

Write-Host ""
Write-Host "Done: $zip ($size MB)" -ForegroundColor Green
Write-Host "SHA-256: $hash"
Write-Host ""
Write-Host "To publish it (tag v$version on the commit it was built from):"
Write-Host "  gh release create v$version `"$zip`" --title `"Terraria TAS $version`" --notes `"For Terraria $($group.TargetTerrariaVersion). SHA-256: $hash`""
