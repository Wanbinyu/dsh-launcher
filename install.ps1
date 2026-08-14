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

$otherEntries = @($entries | Where-Object {
    -not [string]::Equals($_.TrimEnd('\'), $installDirectory, [StringComparison]::OrdinalIgnoreCase)
})
$newEntries = @($installDirectory) + $otherEntries

if ($matchingEntries.Count -eq 0) {
    Write-Host "Added $installDirectory to the current user's PATH."
} elseif ($entries.Count -gt 0 -and [string]::Equals($entries[0].TrimEnd('\'), $installDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "$installDirectory is already first in the current user's PATH."
} else {
    Write-Host "Moved $installDirectory to the front of the current user's PATH."
}

[Environment]::SetEnvironmentVariable('Path', ($newEntries -join ';'), 'User')

Write-Host ''
Write-Host 'Open a new terminal, then run:'
Write-Host '  dsh'
Write-Host '  deepseek'
Write-Host ''
Write-Host 'The no-argument form starts the web profile and opens http://127.0.0.1:3080/.'
