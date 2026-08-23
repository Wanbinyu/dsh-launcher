[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$launcher = Join-Path $root 'dsh-launcher.ps1'
$fake = Join-Path $PSScriptRoot 'fake-dsh.cmd'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$previousBinary = $env:DEEPSEEK_DSH_BIN
$previousHarnessDirectory = $env:DEEPSEEK_HARNESS_DIR
$previousUrl = $env:DSH_WEB_URL
$previousAutoOpen = $env:DSH_AUTO_OPEN
$previousDshHome = $env:DSH_HOME
$previousLogDirectory = $env:DSH_LOG_DIR
$previousWebPort = $env:DSH_WEB_PORT
$previousDotnetRoot = $env:DOTNET_ROOT
$previousPath = $env:PATH
$doctorReport = Join-Path $env:TEMP "dsh-launcher-doctor-$PID.json"
$resolverReport = Join-Path $env:TEMP "dsh-launcher-resolver-$PID.json"
$doctorCommand = Join-Path $root 'src\DshLauncher\bin\Release\net8.0-windows\dsh.cmd'
$resolverTestDirectory = Join-Path $env:TEMP "dsh-launcher-resolver-$PID"

function Invoke-LauncherForTest {
    param([string[]]$TestArguments)

    $output = & $powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $launcher -CommandName dsh @TestArguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "launcher exited with ${LASTEXITCODE}: $($output -join [Environment]::NewLine)"
    }

    return ($output -join [Environment]::NewLine)
}

try {
    $env:DEEPSEEK_DSH_BIN = $fake
    $env:DSH_WEB_URL = 'http://127.0.0.1:1/'
    $env:DSH_AUTO_OPEN = '0'

    $defaultOutput = Invoke-LauncherForTest -TestArguments @()
    if ($defaultOutput -notmatch 'FAKE_DSH_ARGS:web') {
        throw "no-argument invocation did not add the web profile: $defaultOutput"
    }

    $passthroughOutput = Invoke-LauncherForTest -TestArguments @('--profile', 'tui', '--resume', 'demo')
    if ($passthroughOutput -notmatch 'FAKE_DSH_ARGS:--profile tui --resume demo') {
        throw "explicit arguments were not passed through unchanged: $passthroughOutput"
    }

    $doctorExecutable = Join-Path $root 'src\DshLauncher\bin\Release\net8.0-windows\dsh-launcher.exe'
    if (Test-Path -LiteralPath $doctorExecutable) {
        if (-not [string]::IsNullOrWhiteSpace($env:DSH_DOTNET)) {
            $env:DOTNET_ROOT = Split-Path -Path $env:DSH_DOTNET -Parent
        }
        Copy-Item -LiteralPath (Join-Path $root 'dsh.cmd') -Destination $doctorCommand -Force
        $env:DSH_HOME = Join-Path $env:TEMP "dsh-launcher-doctor-home-$PID"
        $env:DSH_LOG_DIR = $env:TEMP
        $env:DSH_WEB_PORT = '30991'
        Remove-Item Env:DSH_WEB_URL -ErrorAction SilentlyContinue

        & $doctorCommand doctor --json --report $doctorReport 2>&1 | Out-Null
        $savedJson = Get-Content -LiteralPath $doctorReport -Raw | ConvertFrom-Json
        if ($savedJson.launcherVersion -ne '0.3.6') {
            throw 'doctor did not save the expected JSON report'
        }
        if (@($savedJson.checks.id) -notcontains 'bundle-manifests') {
            throw 'doctor report omitted bundle manifest diagnostics'
        }

        $shimDirectory = Join-Path $resolverTestDirectory 'shim'
        $toolDirectory = Join-Path $resolverTestDirectory 'tools'
        New-Item -ItemType Directory -Force -Path $shimDirectory, $toolDirectory | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'dsh.cmd') -Destination (Join-Path $shimDirectory 'dsh.cmd')
        Copy-Item -LiteralPath (Join-Path $root 'dsh-launcher.ps1') -Destination (Join-Path $shimDirectory 'dsh-launcher.ps1')
        Copy-Item -LiteralPath $fake -Destination (Join-Path $toolDirectory 'npx.cmd')

        Remove-Item Env:DEEPSEEK_DSH_BIN -ErrorAction SilentlyContinue
        Remove-Item Env:DEEPSEEK_HARNESS_DIR -ErrorAction SilentlyContinue
        $env:PATH = "$shimDirectory;$toolDirectory"
        & $doctorCommand doctor --json --report $resolverReport 2>&1 | Out-Null
        $resolverJson = Get-Content -LiteralPath $resolverReport -Raw | ConvertFrom-Json
        $harnessCheck = @($resolverJson.checks | Where-Object id -eq 'harness-cli')
        if ($harnessCheck.Count -ne 1 -or $harnessCheck[0].message -notmatch 'through npx') {
            throw "resolver selected the dsh-launcher shim instead of npx: $($harnessCheck.message)"
        }
    }

    Write-Host 'dsh-launcher smoke tests passed.'
} finally {
    Remove-Item -LiteralPath $doctorReport -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $resolverReport -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $doctorCommand -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $resolverTestDirectory -Recurse -Force -ErrorAction SilentlyContinue
    $env:PATH = $previousPath
    if ($null -eq $previousBinary) {
        Remove-Item Env:DEEPSEEK_DSH_BIN -ErrorAction SilentlyContinue
    } else {
        $env:DEEPSEEK_DSH_BIN = $previousBinary
    }

    if ($null -eq $previousHarnessDirectory) {
        Remove-Item Env:DEEPSEEK_HARNESS_DIR -ErrorAction SilentlyContinue
    } else {
        $env:DEEPSEEK_HARNESS_DIR = $previousHarnessDirectory
    }

    if ($null -eq $previousUrl) {
        Remove-Item Env:DSH_WEB_URL -ErrorAction SilentlyContinue
    } else {
        $env:DSH_WEB_URL = $previousUrl
    }

    if ($null -eq $previousAutoOpen) {
        Remove-Item Env:DSH_AUTO_OPEN -ErrorAction SilentlyContinue
    } else {
        $env:DSH_AUTO_OPEN = $previousAutoOpen
    }

    if ($null -eq $previousDshHome) {
        Remove-Item Env:DSH_HOME -ErrorAction SilentlyContinue
    } else {
        $env:DSH_HOME = $previousDshHome
    }

    if ($null -eq $previousLogDirectory) {
        Remove-Item Env:DSH_LOG_DIR -ErrorAction SilentlyContinue
    } else {
        $env:DSH_LOG_DIR = $previousLogDirectory
    }

    if ($null -eq $previousWebPort) {
        Remove-Item Env:DSH_WEB_PORT -ErrorAction SilentlyContinue
    } else {
        $env:DSH_WEB_PORT = $previousWebPort
    }

    if ($null -eq $previousDotnetRoot) {
        Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
    } else {
        $env:DOTNET_ROOT = $previousDotnetRoot
    }
}

exit 0
