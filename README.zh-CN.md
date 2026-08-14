# dsh-launcher

[![许可证：MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

面向 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) CLI 的 Windows 快捷启动器。

> [!NOTE]
> 这是独立的便利工具，不修改 DeepSeek Harness 源码，也不是 DeepSeek 官方工具。

[English README](README.md)

## 一眼看懂

| 命令 | 行为 |
| --- | --- |
| `dsh` | 启动官方 `web` profile，服务就绪后打开网页。 |
| `deepseek` | 与 `dsh` 相同的更易理解的别名。 |
| `dsh --help` | 参数原样交给官方 CLI。 |
| `dsh plugin ...` | 保留官方插件管理命令。 |

官方当前等价命令是：

```text
npx @deepseek-ai/dsh web
```

启动器只会在完全无参数时自动补上 `web`。显式传入的命令和参数仍由官方 CLI 处理。

## 安装

环境要求：

- Windows PowerShell 5.1 或更高版本。
- 使用已安装 CLI 或 npx 回退时需要 Node.js/npm。
- 使用 Harness 源码目录时需要 pnpm。

克隆或下载本仓库后，在 PowerShell 中运行用户级安装脚本：

```powershell
cd G:\skill\dsh-launcher
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

安装器会把 `G:\skill\dsh-launcher` 放到当前用户 PATH 的最前面，从而优先使用这个快捷启动器。安装后请重新打开终端。

也可以运行：

```powershell
.\install.cmd
```

卸载 PATH 配置：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
```

## 使用

启动 Web UI：

```powershell
dsh
# 或
deepseek
```

无参数时会执行 `dsh web`，最多等待 30 秒检测 Web 地址，然后使用 Windows 默认浏览器打开。默认地址是 `http://127.0.0.1:3080/`。

显式参数会原样透传：

```powershell
dsh --help
dsh web --help
dsh --profile tui --resume my-session
dsh plugin --profile tui add <package>
```

## CLI 选择顺序

启动器按以下顺序寻找实际 CLI：

1. `DEEPSEEK_HARNESS_DIR`：在 Harness 源码目录中执行 `pnpm dsh`。
2. `DEEPSEEK_DSH_BIN`：使用指定的 `dsh.cmd`、可执行文件或 JavaScript 入口。
3. 当前目录及上级目录中的本地 `@deepseek-ai/dsh`。
4. 全局安装的 `@deepseek-ai/dsh`。
5. PATH 中已有的其他 `dsh` 命令。
6. `npx --yes @deepseek-ai/dsh`。

### 源码目录模式

如果你正在开发 Harness 源码，可以配置：

```powershell
[Environment]::SetEnvironmentVariable(
  'DEEPSEEK_HARNESS_DIR',
  'G:\path\to\deepseek-harness',
  'User'
)
```

该目录必须已经安装依赖，并且手动执行 `pnpm dsh` 成功。修改用户级环境变量后请重新打开终端。

## 浏览器设置

| 变量 | 作用 |
| --- | --- |
| `DSH_WEB_URL` | 指定完整检测和打开地址，例如 `http://127.0.0.1:3081/`。 |
| `DSH_WEB_PORT` | 将默认检测端口从 `3080` 改为其他端口；必须与 Harness 实际配置一致，本身不会修改 Harness 端口。 |
| `DSH_AUTO_OPEN` | 设置为 `0`、`false`、`no` 或 `off`，禁止无参数启动时自动打开浏览器。 |

启动器只打开本机地址，不会把 Harness 服务暴露到网络。

## 常见问题

| 问题 | 处理方式 |
| --- | --- |
| 找不到 `dsh` | 安装后重新打开终端，并确认 `G:\skill\dsh-launcher` 在用户 PATH 中。 |
| 实际运行了错误的 CLI | 用 `Get-Command dsh -All` 检查顺序，重新运行 `install.ps1` 将启动器目录置于用户 PATH 首位。系统级命令可能还需要单独调整 PATH。 |
| 浏览器打开了错误端口 | 设置 `DSH_WEB_URL` 为 Harness 实际地址。`DSH_WEB_PORT` 只改变启动器的检测地址。 |
| 第一次启动 npx 很慢 | npx 正在下载 `@deepseek-ai/dsh`；可以全局安装 CLI，或配置 `DEEPSEEK_HARNESS_DIR` 使用源码模式。 |
| 源码模式启动失败 | 确认目录含有 `package.json`、PATH 中有 pnpm，并且在那里手动执行 `pnpm dsh` 成功。 |

## 开发验证

无需启动 Harness 即可运行启动器测试：

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\smoke.ps1
```

测试覆盖默认补 `web`、显式参数透传、两个命令入口和可配置 CLI 路径。

## 项目边界

本仓库只负责 Windows 命令入口和启动方式。Harness CLI、profile、插件、配置和 Web 应用仍由官方项目维护。欢迎围绕 CLI 探测、Windows 参数处理、安装流程和文档提交改进。

## 链接

- [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)
- [GitHub 仓库](https://github.com/Wanbinyu/dsh-launcher)
- [Issues](https://github.com/Wanbinyu/dsh-launcher/issues)
- [English README](README.md)

## 许可证

MIT，详见 [`LICENSE`](LICENSE)。
