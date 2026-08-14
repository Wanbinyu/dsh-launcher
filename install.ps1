[CmdletBinding()]
param(
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installDirectory = (Resolve-Path -LiteralPath $PSScriptRoot).Path.TrimEnd('\')
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$entries = @()
if (-not [string]::IsNullOrWhiteSpace($userPath)) {
    $entries = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

$matchingEntries = @($entries | Where-Object {
    [string]::Equals($_.TrimEnd('\'), $installDirectory, [StringComparison]::OrdinalIgnoreCase)
})

if ($Uninstall) {
    $newEntries = @($entries | Where-Object {
        -not [string]::Equals($_.TrimEnd('\'), $installDirectory, [StringComparison]::OrdinalIgnoreCase)
    })
    [Environment]::SetEnvironmentVariable('Path', ($newEntries -join ';'), 'User')
    Write-Host "Removed $installDirectory from the current user's PATH."
    Write-Host 'Open a new terminal for the PATH change to take effect.'
    exit 0
}

if ($matchingEntries.Count -eq 0) {
    $entries += $installDirectory
    [Environment]::SetEnvironmentVariable('Path', ($entries -join ';'), 'User')
    Write-Host "Added $installDirectory to the current user's PATH."
} else {
    Write-Host "$installDirectory is already on the current user's PATH."
}

Write-Host ''
Write-Host 'Open a new terminal, then run:'
Write-Host '  dsh'
Write-Host '  deepseek'
Write-Host ''
Write-Host 'The no-argument form starts the web profile and opens http://127.0.0.1:3080/.'
