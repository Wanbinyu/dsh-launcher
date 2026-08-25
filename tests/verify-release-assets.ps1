[CmdletBinding()]
param(
    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $root 'src\DshLauncher\DshLauncher.csproj'
$portablePath = Join-Path $root 'artifacts\publish\win-x64\dsh-launcher.exe'
$portableHashPath = Join-Path $root 'dist\dsh-launcher.exe.sha256'
$installerPath = Join-Path $root 'dist\dsh-launcher-setup.exe'
$installerHashPath = Join-Path $root 'dist\dsh-launcher-setup.exe.sha256'

function Assert-File {
    param([string]$Path, [string]$Label)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not created: $Path"
    }
    if ((Get-Item -LiteralPath $Path).Length -le 1MB) {
        throw "$Label is unexpectedly small: $Path"
    }
}

function Assert-HashFile {
    param(
        [string]$AssetPath,
        [string]$HashPath,
        [string]$ExpectedFileName
    )

    if (-not (Test-Path -LiteralPath $HashPath -PathType Leaf)) {
        throw "SHA-256 file was not created: $HashPath"
    }

    $line = (Get-Content -LiteralPath $HashPath -Raw).Trim()
    $match = [regex]::Match($line, '^([0-9a-fA-F]{64}) \*(.+)$')
    if (-not $match.Success) {
        throw "Invalid SHA-256 file format: $HashPath"
    }
    if ($match.Groups[2].Value -cne $ExpectedFileName) {
        throw "SHA-256 file names '$($match.Groups[2].Value)' instead of '$ExpectedFileName'."
    }

    $expected = $match.Groups[1].Value.ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $AssetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $expected) {
        throw "SHA-256 mismatch for $ExpectedFileName."
    }
}

$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$expectedVersion = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($expectedVersion)) {
    throw "Project version was not found in $projectPath."
}

Assert-File -Path $portablePath -Label 'Portable executable'
Assert-HashFile -AssetPath $portablePath -HashPath $portableHashPath -ExpectedFileName 'dsh-launcher.exe'

$portableVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($portablePath).FileVersion
if ($portableVersion -ne "$expectedVersion.0") {
    throw "Portable executable version is '$portableVersion'; expected '$expectedVersion.0'."
}

if (-not $SkipInstaller) {
    Assert-File -Path $installerPath -Label 'Windows installer'
    Assert-HashFile -AssetPath $installerPath -HashPath $installerHashPath -ExpectedFileName 'dsh-launcher-setup.exe'
}

Write-Host "Release assets verified for dsh-launcher v$expectedVersion."
