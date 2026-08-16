[CmdletBinding()]
param(
    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$dotnet = $env:DSH_DOTNET
if ([string]::IsNullOrWhiteSpace($dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCommand) {
        $dotnet = $dotnetCommand.Source
    }
}

if ([string]::IsNullOrWhiteSpace($dotnet) -or -not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw 'dotnet was not found. Set DSH_DOTNET to a .NET 8 SDK executable or install the .NET SDK.'
}

$project = Join-Path $root 'src\DshLauncher\DshLauncher.csproj'
$publishDirectory = Join-Path $root 'artifacts\publish\win-x64'
$distDirectory = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $publishDirectory, $distDirectory | Out-Null

& $dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if ($SkipInstaller) {
    Write-Host "Published portable executable: $publishDirectory\dsh-launcher.exe"
    exit 0
}

$iscc = $env:ISCC_EXE
if ([string]::IsNullOrWhiteSpace($iscc)) {
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $isccCommand) {
        $iscc = $isccCommand.Source
    }
}
if ([string]::IsNullOrWhiteSpace($iscc)) {
    foreach ($candidate in @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $iscc = $candidate
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($iscc) -or -not (Test-Path -LiteralPath $iscc -PathType Leaf)) {
    throw 'Inno Setup was not found. Install JRSoftware.InnoSetup with winget, or set ISCC_EXE to ISCC.exe.'
}

$script = Join-Path $root 'installer\dsh-launcher.iss'
& $iscc $script
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Host "Installer created: $distDirectory\dsh-launcher-setup.exe"
