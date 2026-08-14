[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$launcher = Join-Path $root 'dsh-launcher.ps1'
$fake = Join-Path $PSScriptRoot 'fake-dsh.cmd'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$previousBinary = $env:DEEPSEEK_DSH_BIN
$previousUrl = $env:DSH_WEB_URL
$previousAutoOpen = $env:DSH_AUTO_OPEN

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

    Write-Host 'dsh-launcher smoke tests passed.'
} finally {
    if ($null -eq $previousBinary) {
        Remove-Item Env:DEEPSEEK_DSH_BIN -ErrorAction SilentlyContinue
    } else {
        $env:DEEPSEEK_DSH_BIN = $previousBinary
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
}
