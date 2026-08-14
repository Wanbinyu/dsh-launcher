# dsh-launcher

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Short Windows commands for the [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) CLI.

> [!NOTE]
> This is an independent convenience wrapper. It does not change the DeepSeek Harness source tree or claim to be an official DeepSeek tool.

## At A Glance

| Command | Behavior |
| --- | --- |
| `dsh` | Start the official `web` profile and open the UI when it is ready. |
| `deepseek` | Same launcher, with a more descriptive alias. |
| `dsh --help` | Forward arguments to the official CLI without changing them. |
| `dsh plugin ...` | Keep official plugin-management commands available. |

The official equivalent of the first command is currently:

```text
npx @deepseek-ai/dsh web
```

The launcher only adds the `web` argument when the command has no arguments. Explicit invocations remain under the official CLI's control.

## Install

Requirements:

- Windows PowerShell 5.1 or later.
- Node.js/npm for an installed CLI or the npx fallback.
- pnpm when using a checked-out Harness source tree.

Clone or download this repository, then run the user-level installer:

```powershell
cd G:\skill\dsh-launcher
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer puts `G:\skill\dsh-launcher` first in the current user's `PATH`, so it takes precedence over another user-level `dsh.cmd`. Open a new terminal after installation.

The command-file equivalent is:

```powershell
.\install.cmd
```

Remove the PATH entry later with:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
```

## Use It

Start the browser UI:

```powershell
dsh
# or
deepseek
```

The no-argument form runs `dsh web`, waits up to 30 seconds for the configured URL to respond, and opens it with the Windows default browser. The default URL is `http://127.0.0.1:3080/`.

All explicit arguments are passed through:

```powershell
dsh --help
dsh web --help
dsh --profile tui --resume my-session
dsh plugin --profile tui add <package>
```

## Select The CLI

The launcher resolves a runner in this order:

1. `DEEPSEEK_HARNESS_DIR`: run `pnpm dsh` in a checked-out Harness source tree.
2. `DEEPSEEK_DSH_BIN`: use an explicitly selected `dsh.cmd`, executable, or JavaScript entry file.
3. A local `@deepseek-ai/dsh` package found above the current directory.
4. A global `@deepseek-ai/dsh` package.
5. Another `dsh` command already on `PATH`.
6. `npx --yes @deepseek-ai/dsh`.

### Source Checkout Mode

Use this mode when you are developing the Harness repository itself:

```powershell
[Environment]::SetEnvironmentVariable(
  'DEEPSEEK_HARNESS_DIR',
  'G:\path\to\deepseek-harness',
  'User'
)
```

The checkout must already be installed and runnable with `pnpm dsh`. Open a new terminal after changing a user-level environment variable.

## Browser Settings

| Variable | Purpose |
| --- | --- |
| `DSH_WEB_URL` | Full URL to probe and open, for example `http://127.0.0.1:3081/`. |
| `DSH_WEB_PORT` | Port used to build the default URL instead of `3080`. It must match the Harness server configuration; it does not configure Harness by itself. |
| `DSH_AUTO_OPEN` | Set to `0`, `false`, `no`, or `off` to skip opening the browser for the no-argument form. |

The launcher only opens a local URL. It does not bind the Harness server to another interface or expose it on the network.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| `dsh` is not recognized | Open a new terminal after running the installer and confirm `G:\skill\dsh-launcher` is on the user PATH. |
| The wrong CLI runs | Run `Get-Command dsh -All`; rerun `install.ps1` so the launcher directory is first in the user PATH. A system-level command may still require an explicit path or PATH adjustment. |
| The browser opens the wrong port | Set `DSH_WEB_URL` to the exact URL used by Harness. `DSH_WEB_PORT` only changes the launcher's probe URL. |
| npx is slow on first launch | The fallback is downloading `@deepseek-ai/dsh`; install the package globally or set `DEEPSEEK_HARNESS_DIR` for source mode. |
| Source mode fails | Confirm the directory contains `package.json`, pnpm is on PATH, and `pnpm dsh` works when run manually there. |

## Development

Run the launcher smoke tests without starting Harness:

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\smoke.ps1
```

The smoke suite checks default `web` injection, explicit argument pass-through, both command shims, and the configurable runner path.

## Project Boundary

This repository is a Windows entrypoint wrapper. The Harness CLI, profiles, plugins, configuration, and browser application remain owned by the official project. Pull requests that improve runner detection, Windows quoting, installation, or documentation are welcome.

## Links

- [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)
- [GitHub repository](https://github.com/Wanbinyu/dsh-launcher)
- [Issues](https://github.com/Wanbinyu/dsh-launcher/issues)
- [简体中文说明](README.zh-CN.md)

## License

MIT. See [`LICENSE`](LICENSE).
