# dsh-launcher

Windows launcher for the [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) CLI.

The official command requires an explicit profile, for example:

```text
npx @deepseek-ai/dsh web
```

This wrapper adds two short commands:

```text
dsh
deepseek
```

With no arguments, either command starts the official `web` profile and opens `http://127.0.0.1:3080/` when the server is ready. Any arguments are forwarded to the official CLI unchanged, so these still work:

```text
dsh --help
dsh web --help
dsh --profile tui --resume my-session
dsh plugin --profile tui add <package>
```

## Install

Requirements: Windows, Node.js/npm, and either pnpm for source mode or network access for the first npx fallback.

From PowerShell:

```powershell
cd G:\skill\dsh-launcher
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer adds this directory to the current user's `PATH`. Open a new terminal after installation.

To remove it from the user's `PATH`:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
```

## CLI resolution

The launcher chooses the first available option in this order:

1. `DEEPSEEK_HARNESS_DIR`, running `pnpm dsh` in that source tree.
2. `DEEPSEEK_DSH_BIN`, when explicitly configured.
3. A local `@deepseek-ai/dsh` package above the current directory.
4. A global `@deepseek-ai/dsh` package.
5. Another `dsh` command already on `PATH`.
6. `npx --yes @deepseek-ai/dsh`.

For a local DeepSeek Harness checkout, configure source mode once:

```powershell
[Environment]::SetEnvironmentVariable(
  'DEEPSEEK_HARNESS_DIR',
  'G:\path\to\deepseek-harness',
  'User'
)
```

The source tree must already be installed and runnable with `pnpm dsh`.

## Environment variables

| Variable | Purpose |
| --- | --- |
| `DEEPSEEK_HARNESS_DIR` | Use a checked-out Harness source tree. |
| `DEEPSEEK_DSH_BIN` | Use a specific `dsh.cmd`, executable, or JavaScript entry file. |
| `DSH_WEB_URL` | URL opened after the default web server becomes reachable. |
| `DSH_WEB_PORT` | Port used to build the default URL; defaults to `3080`. |
| `DSH_AUTO_OPEN` | Set to `0`, `false`, `no`, or `off` to disable browser opening for the no-argument form. |

The web server remains bound to the Harness default, normally loopback at `127.0.0.1`. The launcher does not expose it on the network.

## Development check

Run the argument and default-profile smoke tests without starting Harness:

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\smoke.ps1
```

## License

MIT. This is an independent convenience wrapper and is not an official DeepSeek project.
