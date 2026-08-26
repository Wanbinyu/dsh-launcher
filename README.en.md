# dsh-launcher

[简体中文](README.md) | [English](README.en.md)

> [!IMPORTANT]
> **The Android edition is experimental. A PC is required, and the APK cannot run DeepSeek Harness independently.** DeepSeek Harness does not currently provide an official way to run Harness locally on Android. On plain LAN HTTP origins, `0.1.1-rc.2` also has known UUID, Host/Origin trust, and remote-settings limitations, so a page opening does not guarantee complete functionality. This app does not bypass official security boundaries. Read the [Android v0.1.2 download page](https://github.com/Wanbinyu/dsh-launcher/releases/tag/android-v0.1.2) first; do not download it from third-party sources.

<p align="center"><img src="assets/dsh-launcher.png" alt="dsh-launcher icon" width="112"></p>

[![CI](https://github.com/Wanbinyu/dsh-launcher/actions/workflows/ci.yml/badge.svg)](https://github.com/Wanbinyu/dsh-launcher/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A Windows launcher for [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness). Run `dsh` or `deepseek` to start Harness in the background and open the browser when the local web server is ready. The tray process owns the child process, status, logs, and lifecycle controls. Windows remains the primary edition, and the repository also contains a separate [Android tablet client](android/README.en.md).

> [!NOTE]
> This is an independent community convenience tool. It does not modify the DeepSeek Harness source tree and is not an official DeepSeek tool. It is a Windows launcher, not a Cordis plugin, and it does not provide `dsh.bundle`.

## Online Project Hub

Visit **[Wanbinyu DSH Toolbox](https://wanbinyu.github.io/wanbinyu-harness-toolbox/)** for a single-page overview of the Windows launcher, companion plugins, pinned downloads, and project verification details. Download buttons lead directly to the corresponding GitHub Releases. The site has no ads or telemetry and does not read local Harness data while you browse it.

<p align="center">
  <a href="https://wanbinyu.github.io/wanbinyu-harness-toolbox/">
    <img src="assets/harness-toolbox-preview.png" alt="Wanbinyu DSH Toolbox interface preview" width="960">
  </a>
</p>

<p align="center"><sub>Click the screenshot to open DSH Toolbox. Both the site and this launcher are independent community projects, not official DeepSeek products.</sub></p>

## Platform Editions

| Platform | Version | Role |
| --- | --- | --- |
| Windows | `0.4.0` | Primary edition; installs, starts, and manages Harness locally. |
| Android | `0.1.2` | Experimental LAN client; connects to Windows but cannot guarantee complete remote Web functionality. |

The Android edition requires Android 10 or newer and must be tested only on a trusted private network. Harness LAN mode currently has no authentication and plain-HTTP remote access has known limitations. See the [Android setup and security guide](android/README.en.md).

## Features

- Starts Harness in the background without holding the current terminal.
- Shows startup progress immediately after a double-click, then closes it automatically once the web UI is ready and opened.
- Checks Node.js, npm, and Harness on first launch and shows a bilingual setup wizard when something is missing.
- With explicit consent, installs Node.js LTS through Windows Package Manager and the official `@deepseek-ai/dsh` package from npm.
- Uses a per-user managed Harness directory without overwriting a source checkout, global package, or existing Web profile; existing installations remain preferred.
- Shows live setup stages, npm output, cancellation, and actionable failures that are also written to the local log.
- Tray menu for start, open web, status, restart, stop, Harness install/update/repair/removal, logs, optional support, and exit.
- Manual launcher update checks plus an auto-check toggle; auto-check is enabled by default, contacts GitHub at most once per day, and only notifies instead of silently downloading or installing.
- An optional local support window; donations are entirely voluntary, unlock nothing, make no network calls, and are not tracked.
- Provides both `dsh` and `deepseek` command entrypoints; a desktop shortcut can launch it directly.
- Probes `127.0.0.1:3080` and opens the default browser when the server responds.
- Detects the Harness version and passes `--no-open` to `rc.8` and newer releases so the CLI and launcher do not open duplicate tabs.
- Coalesces repeated desktop, tray, and CLI requests, then opens the browser once after the shared startup is ready.
- Uses one multi-size Windows icon across the EXE, tray, desktop shortcut, and installer.
- Hides the Harness child process and kills the entire child tree on exit.
- Writes Harness stdout and stderr to local logs for troubleshooting.
- Doctor checks the Harness CLI, Node.js, package managers, Web profile, bundle manifests, duplicate core runtimes, endpoint, port, and log directory.
- Copies or saves a redacted diagnostic report that is practical to share in an issue.
- Keeps the PowerShell and `.cmd` entrypoints as compatibility fallbacks when the EXE is not present.

## Install

The recommended path is to download `dsh-launcher-setup.exe` from GitHub Releases and reopen your terminal after installation. The installer installs the self-contained EXEs, creates Start Menu and optional desktop shortcuts, adds the install directory to the current user's PATH, registers an uninstall entry, and offers to launch the Harness setup check from the completion page.

The launcher installer itself does not bundle Node.js or DeepSeek Harness. On the first double-click it reuses an existing Harness installation when possible; otherwise the setup wizard:

1. Detects Node.js `22.19.0+`, npm, `winget`, and existing Harness installations; older Node.js releases are treated as requiring an upgrade.
2. Installs Node.js LTS through `winget` after consent, or opens the Node.js site when `winget` is unavailable.
3. Resolves an exact current `@deepseek-ai/dsh` version from the configured npm registry and installs it through a managed pnpm into `%LOCALAPPDATA%\dsh-launcher\managed-harness`; build scripts are limited to an explicit allowlist required by the current official Harness package.
4. Validates the package name, version, and CLI entry before creating the Web profile, starting Harness, and opening the browser.

This setup path does not require GitHub access after the launcher installer has been obtained, but npm registry access is required. The launcher never elevates silently; Windows may display a permission prompt for `winget` or the Node.js installer. The Run once option preserves the previous temporary `npx` path.

Build the installer from a checkout:

```powershell
$env:DSH_DOTNET = 'C:\path\to\dotnet.exe' # omit this when dotnet is already on PATH
powershell -ExecutionPolicy Bypass -File .\installer\build.ps1
```

Build only the portable EXE:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build.ps1 -SkipInstaller
```

The full installer build requires [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

For development, the legacy PATH-only installer remains available:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

That command only adds the repository directory to PATH. If `dsh-launcher.exe` is not next to the scripts, the old PowerShell CLI implementation is used.

## Usage

Open a new PowerShell or Command Prompt after installation:

```powershell
dsh
# or
deepseek
```

Common lifecycle commands:

```powershell
dsh start       # start the tray instance and Harness
dsh stop        # stop Harness but keep the tray instance
dsh restart     # restart Harness and open the web UI
dsh status      # show status and PID in a dialog
dsh open        # open the configured web URL
dsh logs        # open the log directory
dsh exit        # stop Harness and exit the tray
dsh doctor      # show the complete diagnostic report
```

Diagnostics support machine-readable, clipboard, and file output:

```powershell
dsh doctor --json
dsh doctor --copy
dsh doctor --report .\dsh-doctor.txt
dsh doctor --json --report .\dsh-doctor.json
```

Reports remove URL credentials, query strings, fragments, and common token, password, and API-key assignments. Give a report a quick review before sharing it. Doctor exits with code `1` when it finds a blocking problem and `0` otherwise, so it can be used in scripts and CI. The tray menu also includes Copy diagnostics.

Double-click the `dsh-launcher` desktop or Start Menu shortcut to get the same behavior as `dsh`. Selecting Exit from the tray stops both Harness and the tray process.

The progress window and tray icon appear immediately while Harness starts. The window closes automatically after the ready web UI opens, and a longer-start hint appears when a cold start takes several seconds. Double-clicking the tray during startup waits for the same operation; it does not create multiple progress windows, open an unavailable `127.0.0.1` page, or start another Harness process.

The tray's Harness setup and repair submenu can reinstall, update, or repair the launcher-managed copy and can remove only that managed directory. Removal does not touch a global Harness installation, the `%USERPROFILE%\.dsh` Web profile, plugins, sessions, or workspaces. `DSH_LAUNCHER_MANAGED_ROOT` can override the managed path for testing or custom deployments.

DeepSeek Harness `rc.8` and newer releases open the Web UI themselves. When the launcher supervises those versions in the background, it reads the installed package version and passes `--no-open`, then keeps ownership of readiness and opens one tab. `rc.7` keeps its original arguments.

When the configured web URL is already responding before startup, the launcher does not start a second Harness process. It reuses the existing service and opens the browser, which also avoids duplicating a process when the port is occupied by another service.

Explicit arguments remain under the official CLI's control:

```powershell
dsh --help
dsh web --help
dsh --profile tui --resume my-session
dsh plugin --profile tui add <package>
```

Use foreground mode when debugging startup or when you need to see Harness output:

```powershell
dsh --foreground
```

The official equivalent of the no-argument launch is:

```text
npx @deepseek-ai/dsh web
```

## CLI Resolution Order

The launcher looks for a Harness CLI in this order:

1. `DEEPSEEK_HARNESS_DIR`: run `pnpm dsh` in a Harness source checkout.
2. `DEEPSEEK_DSH_BIN`: use a selected `dsh.cmd`, executable, or JavaScript entrypoint.
3. A local `@deepseek-ai/dsh` package in the current directory or an ancestor.
4. A globally installed `@deepseek-ai/dsh` package.
5. Another `dsh` command elsewhere on PATH.
6. `npx --yes @deepseek-ai/dsh`.

Source checkout mode:

```powershell
[Environment]::SetEnvironmentVariable(
  'DEEPSEEK_HARNESS_DIR',
  'C:\path\to\deepseek-harness',
  'User'
)
```

The checkout must already run successfully with `pnpm dsh`. Open a new terminal after changing a user-level environment variable.

## Configuration

| Variable | Purpose |
| --- | --- |
| `DSH_WEB_URL` | Full HTTP(S) URL to probe and open, for example `http://127.0.0.1:3081/`. |
| `DSH_WEB_PORT` | Port used when `DSH_WEB_URL` is not set; defaults to `3080`. |
| `DSH_AUTO_OPEN` | Set to `0`, `false`, `no`, or `off` to skip automatic browser opening. |
| `DSH_START_TIMEOUT_SECONDS` | Seconds to wait for the web server; defaults to `30`, range `1-300`. |
| `DSH_LOG_DIR` | Custom log directory. Defaults to `%LOCALAPPDATA%\dsh-launcher\logs`. |
| `DEEPSEEK_HARNESS_DIR` | Harness source checkout to use. |
| `DEEPSEEK_DSH_BIN` | Explicit CLI file or JavaScript entrypoint. |

The launcher only probes and opens a local URL. It does not change Harness's bind address or expose the service to the network.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| `dsh` is not recognized | Reopen the terminal and confirm the installer added its directory to the user PATH. |
| The CLI, profile, plugin manifest, duplicate runtime, port, or log directory is suspect | Run `dsh doctor`; attach the redacted output from `dsh doctor --copy` when opening an issue. |
| The browser does not open | Run `dsh status`, verify the port, and set `DSH_WEB_URL` when needed. |
| Harness fails to start | Run `dsh logs` and confirm Node.js/npm or pnpm is available. |
| The wrong CLI is selected | Set `DEEPSEEK_HARNESS_DIR` or `DEEPSEEK_DSH_BIN`, then reopen the terminal. |
| Foreground output is needed | Run `dsh --foreground`. |

## Development

```powershell
powershell -ExecutionPolicy Bypass -File .\assets\build-icon.ps1

powershell -ExecutionPolicy Bypass -File .\tests\smoke.ps1

# Build the .NET project only
& $env:DSH_DOTNET build .\src\DshLauncher\DshLauncher.csproj -c Release

# Build the portable executable and SHA256 files
powershell -ExecutionPolicy Bypass -File .\installer\build.ps1 -SkipInstaller

# Verify the version, files, and SHA256 contents
powershell -ExecutionPolicy Bypass -File .\tests\verify-release-assets.ps1 -SkipInstaller
```

The icon script generates the README preview and a nine-size ICO from the project SVG sources. Tests cover default `web` injection, explicit argument pass-through, configurable CLI paths, coalesced startup requests, and the embedded icon resource. Doctor can be tested without a dialog through `dsh doctor --json`. GitHub Actions builds the Windows portable executable and installer, verifies their version and SHA256 files, and runs Android Debug/Release tests, lint, R8, and packaging. Tray behavior must still be verified in a Windows desktop session.

## Project Boundary

This repository owns the Windows command entrypoints, background lifecycle, and tray controls. The Harness CLI, profiles, plugins, configuration, and web application remain owned by the official project.

## Links

- [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)
- [GitHub repository](https://github.com/Wanbinyu/dsh-launcher)
- [Issues](https://github.com/Wanbinyu/dsh-launcher/issues)
- [简体中文说明](README.md)

## License

MIT. See [`LICENSE`](LICENSE).
