# dsh-launcher Android

[简体中文](README.md) | [English](README.en.md)

> [!IMPORTANT]
> **下载和安装前请确认：必须有一台电脑，Android APK 不能独立运行 DeepSeek Harness。** DeepSeek Harness 官方目前没有提供 Android 本机安装与运行方式。本应用是独立开发的非官方局域网客户端，只负责连接和控制一台已经运行 Harness 的 Windows 电脑；这不是 DeepSeek 官方 Android 版本，也不能在手机或平板上安装 Harness。

请从带有完整提示的 [Android v0.1.1 Release 页面](https://github.com/Wanbinyu/dsh-launcher/releases/tag/android-v0.1.1) 下载。安装包文件名包含 `requires-windows-pc`，Android 安装页面显示的应用名称也会标注“需电脑”。

Android 平板端的 DeepSeek Harness 局域网客户端。Windows 版本仍是主启动器：它负责运行 Harness，Android 应用保存主机地址、检查连接，并在受限 WebView 中提供完整 Web UI。

> [!WARNING]
> DeepSeek Harness 的 `0.0.0.0` 模式目前没有身份认证，并且 Agent 可以访问工作区和执行命令。只应在你控制的可信私有网络中临时使用，不能暴露到公网、公共 Wi-Fi 或访客网络。

## 要求

- Android 10（API 29）或更高版本。
- 平板与 Windows 电脑连接同一个可信私有网络。
- Windows 电脑已安装 Node.js 和 DeepSeek Harness。

## 在 Windows 主机启动

先退出正在运行的普通 dsh-launcher 托盘实例，然后在 Windows 终端运行：

```powershell
dsh web --host 0.0.0.0
```

使用最新版 Harness 时可以禁止电脑自动打开浏览器：

```powershell
npx @deepseek-ai/dsh@latest web --host 0.0.0.0 --no-open
```

终端会显示局域网地址，例如 `http://192.168.1.25:3080`。首次运行时，Windows 防火墙应只允许“专用网络”，不要允许公用网络。

## Android 使用

安装 APK 后，首次打开必须先确认电脑端要求和局域网安全提示，之后才能输入终端显示的局域网地址并点击“连接”。应用会记住最后一次成功连接的地址。顶部工具栏提供刷新、系统浏览器打开和更换主机；文件选择和下载使用 Android 系统能力。

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
