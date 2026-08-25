# dsh-launcher

[简体中文](README.md) | [English](README.en.md)

<p align="center"><img src="assets/dsh-launcher.png" alt="dsh-launcher 图标" width="112"></p>

[![CI](https://github.com/Wanbinyu/dsh-launcher/actions/workflows/ci.yml/badge.svg)](https://github.com/Wanbinyu/dsh-launcher/actions/workflows/ci.yml)
[![许可证：MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

面向 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 的 Windows 快捷启动器。输入 `dsh` 或 `deepseek`，后台启动 Harness，服务就绪后自动打开网页；进程、日志和状态由系统托盘管理。Windows 是主版本，仓库同时提供独立的 [Android 平板客户端](android/README.md)。

> [!NOTE]
> 这是独立的社区便利工具，不修改 DeepSeek Harness 源码，也不是 DeepSeek 官方工具。它是 Windows 启动器，不是 Cordis 插件，不提供 `dsh.bundle`。

## 平台版本

| 平台 | 版本 | 定位 |
| --- | --- | --- |
| Windows | `0.3.7` | 主版本；在本机启动并管理 Harness。 |
| Android | `0.1.0` | 局域网客户端；连接 Windows 主机，不在平板本机运行 Harness。 |

Android 版要求 Android 10 或更高版本，只能在可信私有网络中使用。Harness 的 LAN 模式目前没有身份认证，详见 [Android 安装与安全说明](android/README.md)。

## 功能

- 无控制台后台启动，不占用当前终端。
- 双击后立即显示启动进度窗；服务就绪并打开网页后自动关闭，冷启动期间不再没有反馈。
- 系统托盘菜单：启动、打开网页、查看状态、重启、停止、打开日志、赞赏作者和退出。
- 托盘支持手动检查启动器更新，并可勾选自动检查；自动检查默认开启、每天最多访问一次 GitHub，发现新版后只提示，不静默下载安装。
- 可选的本地赞赏窗口；完全自愿、不影响任何功能，不联网也不记录赞赏状态。
- 提供 `dsh`、`deepseek` 两个命令入口，双击桌面快捷方式也可以启动。
- 自动检测 `127.0.0.1:3080`，服务就绪后打开默认浏览器。
- 自动识别 Harness 版本；对 `rc.8` 及更高版本使用 `--no-open`，避免官方 CLI 与启动器重复打开网页。
- 合并桌面、托盘和命令行发出的重复启动请求，只在同一次启动真正就绪后打开一次浏览器。
- EXE、托盘、桌面快捷方式和安装器使用统一的多尺寸 Windows 图标。
- 隐藏运行 Harness 子进程，退出托盘时清理整个子进程树。
- Harness 标准输出和错误输出写入本地日志，便于排查启动失败。
- Doctor 诊断 Harness CLI、Node.js、包管理器、Web profile、bundle 清单、重复核心运行时、端口和日志目录。
- 可复制或保存已脱敏的诊断报告，便于在 Issue 中安全分享。
- 保留 PowerShell 和 `.cmd` 入口；没有安装 EXE 时仍能使用旧版 CLI 回退逻辑。

## 安装

推荐从 GitHub Releases 下载 `dsh-launcher-setup.exe`，运行安装器后重新打开终端。安装器会安装自包含的 EXE、创建开始菜单和可选桌面快捷方式、把安装目录加入当前用户 PATH，并注册卸载入口。

从源码构建安装包：

```powershell
$env:DSH_DOTNET = 'C:\path\to\dotnet.exe' # dotnet 已在 PATH 中时可以省略
powershell -ExecutionPolicy Bypass -File .\installer\build.ps1
```

只构建便携版 EXE：

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build.ps1 -SkipInstaller
```

完整安装包需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)：

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

开发者仍可以使用旧的 PATH 安装方式：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

这个方式只把仓库目录加入 PATH；如果目录旁边没有 `dsh-launcher.exe`，会回退到 PowerShell 实现。

## 使用

安装后重新打开 PowerShell 或命令提示符：

```powershell
dsh
# 或
deepseek
```

常用管理命令：

```powershell
dsh start       # 启动后台托盘实例和 Harness
dsh stop        # 停止 Harness，保留托盘实例
dsh restart     # 重启 Harness 并打开网页
dsh status      # 弹窗显示状态和 PID
dsh open        # 打开当前配置的网页地址
dsh logs        # 打开日志目录
dsh exit        # 停止 Harness 并退出托盘
dsh doctor      # 显示完整诊断报告
```

诊断报告支持机器可读、剪贴板和文件输出：

```powershell
dsh doctor --json
dsh doctor --copy
dsh doctor --report .\dsh-doctor.txt
dsh doctor --json --report .\dsh-doctor.json
```

报告会移除 URL 凭据、查询参数、片段，以及常见的 Token、密码和 API Key 值。分享前仍建议快速检查一次报告内容。Doctor 发现阻塞问题时返回退出码 `1`，否则返回 `0`，便于脚本和 CI 使用。托盘菜单也提供“复制诊断报告”。

双击桌面或开始菜单中的 `dsh-launcher` 快捷方式，效果等同于 `dsh`。点击托盘图标的“退出”会同时停止 Harness 和托盘进程。

启动期间进度窗和托盘图标会立即出现，服务就绪并打开网页后进度窗自动关闭；启动时间较长时会提示首次启动或更新可能需要多等几秒。此时再次双击托盘会等待同一个启动任务，不会弹出多个进度窗、提前打开不可访问的 `127.0.0.1` 页面或重复启动 Harness。

DeepSeek Harness `rc.8` 开始会自行打开 Web 页面。启动器读取本地包版本，并在后台托管 `rc.8+` 时传入 `--no-open`，继续由托盘统一等待服务就绪后只打开一次；`rc.7` 保持原有启动参数。

如果目标网页地址在启动前已经有服务响应，启动器不会再拉起第二个 Harness 进程，而是复用现有服务并打开网页；这也可以避免端口被其他服务占用时重复启动。

显式参数仍会交给官方 CLI：

```powershell
dsh --help
dsh web --help
dsh --profile tui --resume my-session
dsh plugin --profile tui add <package>
```

调试启动器或需要看到 Harness 前台输出时使用：

```powershell
dsh --foreground
```

无参数启动的官方等价命令是：

```text
npx @deepseek-ai/dsh web
```

## CLI 选择顺序

启动器按以下顺序寻找 Harness CLI：

1. `DEEPSEEK_HARNESS_DIR`：在 Harness 源码目录执行 `pnpm dsh`。
2. `DEEPSEEK_DSH_BIN`：使用指定的 `dsh.cmd`、可执行文件或 JavaScript 入口。
3. 当前目录及上级目录中的本地 `@deepseek-ai/dsh`。
4. 全局安装的 `@deepseek-ai/dsh`。
5. PATH 中其他位置的 `dsh` 命令。
6. `npx --yes @deepseek-ai/dsh`。

源码目录模式：

```powershell
[Environment]::SetEnvironmentVariable(
  'DEEPSEEK_HARNESS_DIR',
  'C:\path\to\deepseek-harness',
  'User'
)
```

源码目录需要已经可以执行 `pnpm dsh`。修改用户环境变量后请重新打开终端。

## 配置

| 变量 | 作用 |
| --- | --- |
| `DSH_WEB_URL` | 完整的 HTTP(S) 检测和打开地址，例如 `http://127.0.0.1:3081/`。 |
| `DSH_WEB_PORT` | 未设置 `DSH_WEB_URL` 时使用的端口，默认 `3080`。 |
| `DSH_AUTO_OPEN` | 设置为 `0`、`false`、`no` 或 `off`，禁止服务就绪后自动打开浏览器。 |
| `DSH_START_TIMEOUT_SECONDS` | 等待网页就绪的秒数，默认 `30`，范围 `1-300`。 |
| `DSH_LOG_DIR` | 自定义日志目录。默认 `%LOCALAPPDATA%\dsh-launcher\logs`。 |
| `DEEPSEEK_HARNESS_DIR` | 指定 Harness 源码目录。 |
| `DEEPSEEK_DSH_BIN` | 指定 CLI 文件或 JavaScript 入口。 |

启动器只检测并打开本机地址，不会修改 Harness 的监听地址，也不会把服务暴露到网络。

## 故障排查

| 现象 | 处理方式 |
| --- | --- |
| `dsh` 不是命令 | 重新打开终端，确认安装器已把安装目录加入用户 PATH。 |
| 怀疑 CLI、profile、插件清单、重复运行时、端口或日志目录有问题 | 执行 `dsh doctor` 查看诊断结果；提交 Issue 时可附上 `dsh doctor --copy` 的脱敏报告。 |
| 浏览器没有自动打开 | 执行 `dsh status`，检查端口；必要时设置 `DSH_WEB_URL`。 |
| Harness 启动失败 | 执行 `dsh logs` 查看最新日志，确认 Node.js/npm 或 pnpm 可用。 |
| 运行了错误的 CLI | 设置 `DEEPSEEK_HARNESS_DIR` 或 `DEEPSEEK_DSH_BIN`，并重新打开终端。 |
| 想恢复前台输出 | 执行 `dsh --foreground`。 |

## 开发验证

```powershell
powershell -ExecutionPolicy Bypass -File .\assets\build-icon.ps1

powershell -ExecutionPolicy Bypass -File .\tests\smoke.ps1

# 仅编译 .NET 项目
& $env:DSH_DOTNET build .\src\DshLauncher\DshLauncher.csproj -c Release

# 生成便携版和 SHA256 校验文件
powershell -ExecutionPolicy Bypass -File .\installer\build.ps1 -SkipInstaller

# 核对版本、文件和 SHA256 内容
powershell -ExecutionPolicy Bypass -File .\tests\verify-release-assets.ps1 -SkipInstaller
```

图标脚本从项目内 SVG 源文件生成 README 预览图和包含 9 种尺寸的 ICO。测试覆盖默认补 `web`、显式参数透传、可配置 CLI 路径、启动请求合并和图标资源嵌入。Doctor 可用 `dsh doctor --json` 做无弹窗验证。GitHub Actions 会实际生成 Windows 便携版与安装器并核对版本和 SHA256，同时执行 Android Debug/Release 测试、lint、R8 和打包。托盘运行仍需在 Windows 桌面会话中验证。

## 项目边界

本仓库只负责 Windows 命令入口、后台生命周期和托盘控制。Harness CLI、profile、插件、配置和 Web 应用仍由官方项目维护。

## 链接

- [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)
- [GitHub 仓库](https://github.com/Wanbinyu/dsh-launcher)
- [Issues](https://github.com/Wanbinyu/dsh-launcher/issues)
- [English README](README.en.md)

## 许可证

MIT，详见 [`LICENSE`](LICENSE)。
