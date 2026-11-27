<#
.SYNOPSIS
  Builds Mountly: the .NET solution, optionally its tests, a publish folder or a Velopack installer.
.EXAMPLE
  ./build.ps1                 # Release build of everything
  ./build.ps1 -Test           # ...and run the unit tests and the real-mount integration tests
  ./build.ps1 -Publish        # self-contained app + CLI in artifacts/publish/win-x64
  ./build.ps1 -Pack           # Velopack installer + update packages in artifacts/releases/win-x64
#>
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [switch] $Test,
    [switch] $Publish,
    [switch] $Pack,
    [ValidateSet('win-x64', 'win-arm64')] [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Step([string] $title, [scriptblock] $action) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$title failed (exit $LASTEXITCODE)" }
}

Invoke-Step "Build ($Configuration)" { dotnet build Mountly.slnx -c $Configuration }
if ($Test) {
    Invoke-Step "Unit tests" { dotnet test --project tests/Mountly.Tests -c $Configuration --no-build }
    Invoke-Step "Mount integration tests" { dotnet test --project tests/Mountly.Windows.Tests -c $Configuration --no-build }
}

if ($Publish) {
    Invoke-Step "Publish ($Runtime)" { ./build/publish.ps1 -Runtime $Runtime -OutDir "artifacts/publish/$Runtime" }
}

if ($Pack) {
    # Velopack wants a plain folder publish; it adds its own launcher, delta updates and Setup.exe.
    [xml] $props = Get-Content Directory.Build.props
    $version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $folder = "artifacts/pack-input/$Runtime"
    $releases = "artifacts/releases/$Runtime"
    # Velopack refuses to pack a version that is already in the releases folder; local builds replace it.
    $index = Join-Path $releases "releases.$Runtime.json"
    if ((Test-Path $index) -and ((Get-Content $index -Raw | ConvertFrom-Json).Assets.Version -contains $version)) {
        Write-Host "Replacing the existing $version build in $releases" -ForegroundColor Yellow
        Remove-Item -Recurse -Force $releases
    }
    Invoke-Step "Publish for packaging ($Runtime)" { ./build/publish.ps1 -Runtime $Runtime -Version $version -OutDir $folder }
    Invoke-Step "Restore vpk" { dotnet tool restore }
    Invoke-Step "Velopack pack $version" {
        dotnet vpk pack --packId Mountly --packVersion $version --packDir $folder --mainExe Mountly.exe `
            --packTitle Mountly --packAuthors "Alexander Rubtsov" --icon src/Mountly.App/Assets/mountly.ico `
            --runtime $Runtime --channel $Runtime --outputDir $releases
    }
    Write-Host "Installer and update packages in $releases" -ForegroundColor Green
}
