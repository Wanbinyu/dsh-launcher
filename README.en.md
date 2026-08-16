# dsh-launcher

[简体中文](README.md) | [English](README.en.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A Windows launcher for [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness). Run `dsh` or `deepseek` to start Harness in the background and open the browser when the local web server is ready. The tray process owns the child process, status, logs, and lifecycle controls.

> [!NOTE]
> This is an independent community convenience tool. It does not modify the DeepSeek Harness source tree and is not an official DeepSeek tool. It is a Windows launcher, not a Cordis plugin, and it does not provide `dsh.bundle`.

## Features

- Starts Harness in the background without holding the current terminal.
- Tray menu for start, open web, status, restart, stop, logs, and exit.
- Installs both `dsh.exe` and `deepseek.exe`; a desktop shortcut can launch it directly.
- Probes `127.0.0.1:3080` and opens the default browser when the server responds.
- Hides the Harness child process and kills the entire child tree on exit.
- Writes Harness stdout and stderr to local logs for troubleshooting.
- Keeps the PowerShell and `.cmd` entrypoints as compatibility fallbacks when the EXE is not present.

## Install

The recommended path is to download `dsh-launcher-setup.exe` from GitHub Releases and reopen your terminal after installation. The installer installs the self-contained EXEs, creates Start Menu and optional desktop shortcuts, adds the install directory to the current user's PATH, and registers an uninstall entry.

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
```

Double-click the `dsh-launcher` desktop or Start Menu shortcut to get the same behavior as `dsh`. Selecting Exit from the tray stops both Harness and the tray process.

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
| The browser does not open | Run `dsh status`, verify the port, and set `DSH_WEB_URL` when needed. |
| Harness fails to start | Run `dsh logs` and confirm Node.js/npm or pnpm is available. |
| The wrong CLI is selected | Set `DEEPSEEK_HARNESS_DIR` or `DEEPSEEK_DSH_BIN`, then reopen the terminal. |
| Foreground output is needed | Run `dsh --foreground`. |

## Development

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\smoke.ps1

# Build the .NET project only
& $env:DSH_DOTNET build .\src\DshLauncher\DshLauncher.csproj -c Release
```

The legacy smoke suite covers default `web` injection, explicit argument pass-through, and configurable CLI paths. Tray behavior must be verified in a Windows desktop session.

## Project Boundary

This repository owns the Windows command entrypoints, background lifecycle, and tray controls. The Harness CLI, profiles, plugins, configuration, and web application remain owned by the official project.

## Links

- [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)
- [GitHub repository](https://github.com/Wanbinyu/dsh-launcher)
- [Issues](https://github.com/Wanbinyu/dsh-launcher/issues)
- [简体中文说明](README.md)

## License

MIT. See [`LICENSE`](LICENSE).
