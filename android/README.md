# dsh-launcher Android

[简体中文](README.md) | [English](README.en.md)

> [!IMPORTANT]
> **这是实验性客户端：必须有一台电脑，Android APK 不能独立运行 DeepSeek Harness，也不能保证完整远程 Web 功能。** DeepSeek Harness 官方目前没有提供 Android 本机安装与运行方式。本应用是独立开发的非官方局域网客户端，只能尝试连接一台已经运行 Harness 的 Windows 电脑；它不是 DeepSeek 官方 Android 版本，也不会绕过 Harness 的安全限制。

请从带有完整提示的 [Android v0.1.2 Release 页面](https://github.com/Wanbinyu/dsh-launcher/releases/tag/android-v0.1.2) 下载。安装包文件名包含 `experimental-requires-windows-pc`，Android 安装页面显示的应用名称也会标注“实验性，需电脑”。

Android 平板端的实验性 DeepSeek Harness 局域网客户端。Windows 版本仍是主启动器：它负责运行 Harness，Android 应用只保存主机地址、检查基础 HTTP 连接，并尝试在受限 WebView 中显示 Web UI。

> [!WARNING]
> DeepSeek Harness 的 `0.0.0.0` 模式目前没有身份认证，并且 Agent 可以访问工作区和执行命令。只应在你控制的可信私有网络中临时使用，不能暴露到公网、公共 Wi-Fi 或访客网络。

## 当前已知限制

截至 DeepSeek Harness `0.1.1-rc.2`：

- 普通局域网 HTTP 地址不是浏览器安全上下文，`crypto.randomUUID` 可能不可用，导致首次 RPC 失败或持续重连。
- 即使补足 UUID，Host/Origin 信任校验仍可能返回 403。
- 设置和凭据相关 API 有意保持仅本机可用，远程浏览器可能显示“设置不可用”。
- Android 应用的基础 HTTP 探测成功只代表服务器可达，不代表会话、设置、供应商或预设功能都能使用。

详见官方社区报告 [#4209](https://github.com/deepseek-ai/deepseek-harness/discussions/4209) 和 [#3302](https://github.com/deepseek-ai/deepseek-harness/discussions/3302)。本应用不会注入脚本、伪装 Host/Origin 或绕过这些边界。当前较安全的完整远程方案仍是带身份认证的 HTTPS，或让浏览器保持 localhost 来源的受控 SSH/平台端口转发；本应用不负责配置这些方案。

## 要求

- Android 10（API 29）或更高版本。
- 平板与 Windows 电脑连接同一个可信私有网络。
- Windows 电脑已安装 Node.js 和 DeepSeek Harness。

## 在 Windows 主机启动

以下命令只用于可信私有网络中的兼容性测试。先退出正在运行的普通 dsh-launcher 托盘实例，将示例 IP 替换为电脑真实局域网 IP，然后在 Windows 终端运行：

```powershell
dsh web --host 0.0.0.0 --trusted-host 192.168.1.25:3080 --no-open
```

也可以直接使用官方 CLI：

```powershell
npx @deepseek-ai/dsh@latest web --host 0.0.0.0 --trusted-host 192.168.1.25:3080 --no-open
```

`--trusted-host` 只是 Host/Origin 防护的一部分，不是登录认证，也不能解除上述远程设置限制。首次运行时，Windows 防火墙应只允许“专用网络”，不要允许公用网络。

## Android 使用

安装 APK 后，首次打开必须先确认电脑端要求、实验性状态和局域网安全提示，之后才能输入局域网地址并继续测试。Web 界面顶部会一直显示实验性连接提示。应用会记住最后一次成功连接的地址；顶部工具栏提供刷新、系统浏览器打开和更换主机。

关闭 Windows 终端或按 `Ctrl+C` 会停止局域网服务。

## 隐私与安全

- 应用不读取 API Key、提示词、响应、工具参数或工作区文件。
- 地址仅保存在 Android 应用本地偏好中。
- 没有遥测、广告或额外网络请求。
- HTTPS 证书错误会被拒绝，外部链接交给系统浏览器。
- WebView 禁止本地文件和 `content://` 访问，不提供 JavaScript 原生桥。

## 构建

需要 JDK 17+、Android SDK 36 和 Build Tools 36.0.0：

```powershell
cd .\android
.\gradlew.bat testDebugUnitTest assembleDebug
```

发布构建通过 `DSH_ANDROID_KEYSTORE`、`DSH_ANDROID_STORE_PASSWORD`、`DSH_ANDROID_KEY_ALIAS` 和 `DSH_ANDROID_KEY_PASSWORD` 提供签名信息。

发布者必须离线备份签名密钥和密码；丢失密钥后，已安装用户将无法直接升级到使用新密钥签名的版本。
