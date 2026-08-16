[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dsh', 'deepseek')]
    [string]$CommandName,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-LauncherError {
    param([string]$Message)

    [Console]::Error.WriteLine("dsh-launcher: $Message")
}

function Find-Executable {
    param([string[]]$Names)

    foreach ($name in $Names) {
        $command = Get-Command -Name $name -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $command) {
            return $command.Source
        }
    }

    return $null
}

function Resolve-ExistingPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    try {
        return (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    } catch {
        return $null
    }
}

function Get-NormalizedPath {
    param([string]$Path)

    $resolved = Resolve-ExistingPath -Path $Path
    if ($null -ne $resolved) {
        return $resolved.TrimEnd('\')
    }

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Test-SamePath {
    param(
        [string]$Left,
        [string]$Right
    )

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }

    return [string]::Equals(
        (Get-NormalizedPath -Path $Left),
        (Get-NormalizedPath -Path $Right),
        [System.StringComparison]::OrdinalIgnoreCase
    )
}

function Get-RunnerFromSource {
    $configuredDirectory = $env:DEEPSEEK_HARNESS_DIR
    if ([string]::IsNullOrWhiteSpace($configuredDirectory)) {
        return $null
    }

    $harnessDirectory = Resolve-ExistingPath -Path $configuredDirectory
    if ($null -eq $harnessDirectory) {
        throw "DEEPSEEK_HARNESS_DIR does not exist: $configuredDirectory"
    }

    $packageJson = Join-Path $harnessDirectory 'package.json'
    if (-not (Test-Path -LiteralPath $packageJson -PathType Leaf)) {
        throw "DEEPSEEK_HARNESS_DIR is not a DeepSeek Harness source directory (package.json not found): $harnessDirectory"
    }

    $pnpm = Find-Executable -Names @('pnpm.cmd', 'pnpm.exe', 'pnpm')
    if ($null -eq $pnpm) {
        throw 'DEEPSEEK_HARNESS_DIR is set, but pnpm was not found on PATH.'
    }

    return [PSCustomObject]@{
        FilePath         = $pnpm
        PrefixArguments  = @('dsh')
        WorkingDirectory = $harnessDirectory
        Description      = "source tree at $harnessDirectory"
    }
}

function Get-RunnerFromConfiguredBinary {
    $configuredBinary = $env:DEEPSEEK_DSH_BIN
    if ([string]::IsNullOrWhiteSpace($configuredBinary)) {
        return $null
    }

    $binary = Resolve-ExistingPath -Path $configuredBinary
    if ($null -eq $binary) {
        throw "DEEPSEEK_DSH_BIN does not exist: $configuredBinary"
    }

    $extension = [System.IO.Path]::GetExtension($binary).ToLowerInvariant()
    if ($extension -eq '.js') {
        $node = Find-Executable -Names @('node.exe', 'node')
        if ($null -eq $node) {
            throw 'DEEPSEEK_DSH_BIN points to a JavaScript file, but node was not found on PATH.'
        }

        return [PSCustomObject]@{
            FilePath         = $node
            PrefixArguments  = @($binary)
            WorkingDirectory = (Get-Location).Path
            Description      = "configured JavaScript CLI at $binary"
        }
    }

    return [PSCustomObject]@{
        FilePath         = $binary
        PrefixArguments  = @()
        WorkingDirectory = (Get-Location).Path
        Description      = "configured CLI at $binary"
    }
}

function Find-LocalPackageEntry {
    $directory = (Get-Location).Path

    while ($true) {
        $entry = Join-Path $directory 'node_modules\@deepseek-ai\dsh\lib\bin.js'
        if (Test-Path -LiteralPath $entry -PathType Leaf) {
            return (Resolve-Path -LiteralPath $entry).Path
        }

        $parent = Split-Path -Path $directory -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or (Test-SamePath -Left $parent -Right $directory)) {
            break
        }

        $directory = $parent
    }

    return $null
}

function Find-GlobalPackageEntry {
    $npm = Find-Executable -Names @('npm.cmd', 'npm.exe', 'npm')
    if ($null -eq $npm) {
        return $null
    }

    $rootOutput = @(& $npm 'root' '--global' 2>$null)
    if ($LASTEXITCODE -ne 0 -or $rootOutput.Count -eq 0) {
        return $null
    }

    $root = [string]($rootOutput | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($root)) {
        return $null
    }

    $entry = Join-Path $root.Trim() '@deepseek-ai\dsh\lib\bin.js'
    if (Test-Path -LiteralPath $entry -PathType Leaf) {
        return (Resolve-Path -LiteralPath $entry).Path
    }

    return $null
}

function Find-ExistingDshCommand {
    $ownCommands = @(
        (Join-Path $PSScriptRoot 'dsh.cmd'),
        (Join-Path $PSScriptRoot 'dsh.ps1')
    )

    $commands = @(Get-Command -Name 'dsh' -CommandType Application -All -ErrorAction SilentlyContinue)
    foreach ($command in $commands) {
        $source = $command.Source
        if ([string]::IsNullOrWhiteSpace($source)) {
            continue
        }

        $isOwnCommand = $false
        foreach ($ownCommand in $ownCommands) {
            if (Test-SamePath -Left $source -Right $ownCommand) {
                $isOwnCommand = $true
                break
            }
        }

        if (-not $isOwnCommand) {
            return $source
        }
    }

    return $null
}

function Get-Runner {
    $runner = Get-RunnerFromSource
    if ($null -ne $runner) {
        return $runner
    }

    $runner = Get-RunnerFromConfiguredBinary
    if ($null -ne $runner) {
        return $runner
    }

    $localEntry = Find-LocalPackageEntry
    if ($null -ne $localEntry) {
        $node = Find-Executable -Names @('node.exe', 'node')
        if ($null -eq $node) {
            throw 'A local @deepseek-ai/dsh package was found, but node was not found on PATH.'
        }

        return [PSCustomObject]@{
            FilePath         = $node
            PrefixArguments  = @($localEntry)
            WorkingDirectory = (Get-Location).Path
            Description      = "local @deepseek-ai/dsh package at $localEntry"
        }
    }

    $globalEntry = Find-GlobalPackageEntry
    if ($null -ne $globalEntry) {
        $node = Find-Executable -Names @('node.exe', 'node')
        if ($null -eq $node) {
            throw 'A global @deepseek-ai/dsh package was found, but node was not found on PATH.'
        }

        return [PSCustomObject]@{
            FilePath         = $node
            PrefixArguments  = @($globalEntry)
            WorkingDirectory = (Get-Location).Path
            Description      = "global @deepseek-ai/dsh package at $globalEntry"
        }
    }

    $existingCommand = Find-ExistingDshCommand
    if ($null -ne $existingCommand) {
        return [PSCustomObject]@{
            FilePath         = $existingCommand
            PrefixArguments  = @()
            WorkingDirectory = (Get-Location).Path
            Description      = "existing dsh command at $existingCommand"
        }
    }

    $npx = Find-Executable -Names @('npx.cmd', 'npx.exe', 'npx')
    if ($null -eq $npx) {
        throw 'Could not find a DeepSeek Harness source tree, an installed dsh CLI, or npx.'
    }

    return [PSCustomObject]@{
        FilePath         = $npx
        PrefixArguments  = @('--yes', '@deepseek-ai/dsh')
        WorkingDirectory = (Get-Location).Path
        Description      = 'the @deepseek-ai/dsh package through npx'
    }
}

function Quote-WindowsArgument {
    param([AllowEmptyString()][string]$Value)

    if ($Value.Length -eq 0) {
        return '""'
    }

    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Get-WebUrl {
    if (-not [string]::IsNullOrWhiteSpace($env:DSH_WEB_URL)) {
        return $env:DSH_WEB_URL.Trim()
    }

    $port = 3080
    if (-not [string]::IsNullOrWhiteSpace($env:DSH_WEB_PORT)) {
        $parsedPort = 0
        if (-not [int]::TryParse($env:DSH_WEB_PORT, [ref]$parsedPort) -or $parsedPort -lt 1 -or $parsedPort -gt 65535) {
            throw "DSH_WEB_PORT must be an integer from 1 to 65535: $($env:DSH_WEB_PORT)"
        }

        $port = $parsedPort
    }

    return "http://127.0.0.1:$port/"
}

function Test-AutoOpenEnabled {
    if ([string]::IsNullOrWhiteSpace($env:DSH_AUTO_OPEN)) {
        return $true
    }

    return $env:DSH_AUTO_OPEN.Trim().ToLowerInvariant() -notin @('0', 'false', 'no', 'off')
}

function Test-WebReady {
    param([string]$Url)

    $request = $null
    $response = $null
    try {
        $request = [System.Net.HttpWebRequest]::Create($Url)
        $request.Method = 'GET'
        $request.Timeout = 500
        $request.ReadWriteTimeout = 500
        $response = $request.GetResponse()
        return $true
    } catch [System.Net.WebException] {
        return $null -ne $_.Exception.Response
    } catch {
        return $false
    } finally {
        if ($null -ne $response) {
            $response.Dispose()
        }
    }
}

function Invoke-Runner {
    param(
        [Parameter(Mandatory = $true)]
        $Runner,

        [Parameter(Mandatory = $true)]
        [string[]]$RunnerArguments,

        [Parameter(Mandatory = $true)]
        [bool]$OpenBrowser,

        [Parameter(Mandatory = $true)]
        [ref]$ExitCode
    )

    $allArguments = @($Runner.PrefixArguments) + @($RunnerArguments)

    $webUrl = Get-WebUrl
    if ($OpenBrowser -and (Test-WebReady -Url $webUrl)) {
        try {
            Start-Process -FilePath $webUrl | Out-Null
            Write-Host "The configured web URL is already responding; reused the existing service: $webUrl"
        } catch {
            Write-LauncherError "the web server is already responding at $webUrl, but the default browser could not be opened: $($_.Exception.Message)"
            $ExitCode.Value = 1
            return
        }

        $ExitCode.Value = 0
        return
    }

    if (-not $OpenBrowser) {
        & $Runner.FilePath @allArguments
        $ExitCode.Value = $LASTEXITCODE
        return
    }

    $quotedArguments = @($allArguments | ForEach-Object { Quote-WindowsArgument -Value ([string]$_) })
    $startParameters = @{
        FilePath         = $Runner.FilePath
        ArgumentList     = $quotedArguments
        WorkingDirectory = $Runner.WorkingDirectory
        NoNewWindow      = $true
        PassThru         = $true
    }

    $process = Start-Process @startParameters
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $opened = $false

    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        if (Test-WebReady -Url $webUrl) {
            try {
                Start-Process -FilePath $webUrl | Out-Null
                Write-Host "DeepSeek Harness is ready: $webUrl"
                $opened = $true
            } catch {
                Write-LauncherError "the web server is ready at $webUrl, but the default browser could not be opened: $($_.Exception.Message)"
            }

            break
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $opened -and -not $process.HasExited) {
        Write-Host "DeepSeek Harness is still starting. Open $webUrl when it is ready."
    }

    $process.WaitForExit()
    $ExitCode.Value = $process.ExitCode
}

function Invoke-Doctor {
    $lines = @('dsh-launcher diagnostics')
    $failed = $false
    try {
        $runner = Get-Runner
        $lines += "Harness CLI: OK ($($runner.Description))"
    } catch {
        $failed = $true
        $lines += "Harness CLI: FAILED ($($_.Exception.Message))"
    }

    $webUrl = Get-WebUrl
    $lines += "Web URL: $webUrl"
    $lines += if (Test-WebReady -Url $webUrl) { 'Web endpoint: RESPONDING' } else { 'Web endpoint: NOT RESPONDING (normal before dsh starts)' }
    Write-Host ($lines -join [Environment]::NewLine)
    return [int]$failed
}

try {
    $effectiveArguments = @($Arguments)
    $openBrowser = $false

    if ($effectiveArguments.Count -eq 1 -and $effectiveArguments[0].ToLowerInvariant() -eq 'doctor') {
        exit (Invoke-Doctor)
    }

    if ($effectiveArguments.Count -eq 0) {
        $effectiveArguments = @('web')
        $openBrowser = Test-AutoOpenEnabled
    }

    $runner = Get-Runner
    $exitCode = 0
    Invoke-Runner -Runner $runner -RunnerArguments $effectiveArguments -OpenBrowser $openBrowser -ExitCode ([ref]$exitCode)
    exit $exitCode
} catch {
    Write-LauncherError $_.Exception.Message
    exit 1
}
