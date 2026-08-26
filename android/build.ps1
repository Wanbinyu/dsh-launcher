[CmdletBinding()]
param(
    [switch]$DebugBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$androidRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $androidRoot '..')).Path
$sdk = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($sdk)) {
    $sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
}
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'platforms\android-36\android.jar'))) {
    throw 'Android SDK 36 was not found. Set ANDROID_HOME to an installed Android SDK.'
}

$env:ANDROID_HOME = $sdk
$env:ANDROID_SDK_ROOT = $sdk
$gradle = Join-Path $androidRoot 'gradlew.bat'
$variant = if ($DebugBuild) { 'Debug' } else { 'Release' }
$passwordPointer = [IntPtr]::Zero

try {
    if (-not $DebugBuild -and [string]::IsNullOrWhiteSpace($env:DSH_ANDROID_KEYSTORE)) {
        $signingDirectory = Join-Path $env:LOCALAPPDATA 'dsh-launcher\android-signing'
        $keyStore = Join-Path $signingDirectory 'dsh-launcher-android.p12'
        $passwordFile = Join-Path $signingDirectory 'password.dpapi'
        if (-not (Test-Path -LiteralPath $keyStore) -or -not (Test-Path -LiteralPath $passwordFile)) {
            throw 'Release signing is not configured. Set the DSH_ANDROID signing variables or run a debug build.'
        }

        $encryptedPassword = (Get-Content -LiteralPath $passwordFile -Raw).Trim()
        $securePassword = $encryptedPassword | ConvertTo-SecureString
        $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
        $env:DSH_ANDROID_KEYSTORE = $keyStore
        $env:DSH_ANDROID_STORE_PASSWORD = $password
        $env:DSH_ANDROID_KEY_ALIAS = 'dsh-launcher'
        $env:DSH_ANDROID_KEY_PASSWORD = $password
    }

    & $gradle --project-dir $androidRoot "testDebugUnitTest" "lint$variant" "assemble$variant"
    if ($LASTEXITCODE -ne 0) {
        throw "Android build failed with exit code $LASTEXITCODE."
    }

    $sourceName = if ($DebugBuild) { 'app-debug.apk' } else { 'app-release.apk' }
    $source = Join-Path $androidRoot "app\build\outputs\apk\$($variant.ToLowerInvariant())\$sourceName"
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Expected APK was not created: $source"
    }

    $dist = Join-Path $repositoryRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $appGradle = Get-Content -LiteralPath (Join-Path $androidRoot 'app\build.gradle') -Raw
    $versionMatch = [regex]::Match($appGradle, 'versionName\s+"([^"]+)"')
    if (-not $versionMatch.Success) {
        throw 'Android versionName was not found in app/build.gradle.'
    }
    $versionName = $versionMatch.Groups[1].Value
    $suffix = if ($DebugBuild) { '-debug' } else { '' }
    $fileName = "dsh-launcher-android-v$versionName-experimental-requires-windows-pc$suffix.apk"
    $target = Join-Path $dist $fileName
    Copy-Item -LiteralPath $source -Destination $target -Force
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$target.sha256" -Value "$hash *$fileName" -Encoding ascii
    Write-Host "Android APK created: $target"
} finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    Remove-Item Env:DSH_ANDROID_STORE_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:DSH_ANDROID_KEY_PASSWORD -ErrorAction SilentlyContinue
}
