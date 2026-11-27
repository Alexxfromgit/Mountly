<#
.SYNOPSIS
  Publishes the app and the CLI, self-contained, into one folder: the input for Velopack and the portable zip.
.EXAMPLE
  ./build/publish.ps1 -Runtime win-x64 -Version 0.2.0 -OutDir artifacts/pack-input/win-x64
#>
param(
    [ValidateSet('win-x64', 'win-arm64')] [string] $Runtime = 'win-x64',
    [string] $Version,
    [Parameter(Mandatory)] [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-Location (Split-Path $PSScriptRoot)

$common = @('-c', 'Release', '-r', $Runtime, '--self-contained', '-p:DebugType=None', '-p:CopyDebugSymbolFilesFromPackages=false',
            '-o', $OutDir)
if ($Version) { $common += "-p:Version=$Version" }

# The app first, then the CLI into the same folder: they share every library, and the CLI is what the
# Explorer verbs, scripts and elevated operations run.
dotnet publish src/Mountly.App @common
dotnet publish src/Mountly.Cli @common

# Native packages (SkiaSharp, HarfBuzz) ship ~100 MB of .pdb files that DebugType=None doesn't remove.
Get-ChildItem $OutDir -Filter *.pdb | Remove-Item

foreach ($file in 'Mountly.exe', 'mountly-cli.exe', 'Mountly.Core.dll', 'DiscUtils.Iso9660.dll') {
    if (-not (Test-Path (Join-Path $OutDir $file))) { throw "$file is missing from $OutDir" }
}
Write-Host "Published $Runtime to $OutDir" -ForegroundColor Green
