# ⚠️ Android 下载前必读：必须有电脑

> [!IMPORTANT]
> DeepSeek Harness 官方目前没有提供 Android 本机安装与运行方式。这个 APK 不能在手机或平板上独立运行 Harness，也不是 DeepSeek 官方 Android 版本。

本应用是独立开发的非官方局域网客户端，只负责连接和控制一台已经运行 DeepSeek Harness 的 Windows 电脑。使用时，安卓设备和电脑必须连接同一个可信私有网络。

- 安装包文件名包含 `requires-windows-pc`。
- Android 安装页面显示的应用名称为“dsh-launcher（需电脑）”。
- 首次打开应用时，必须再次确认电脑端要求才能继续。
- Harness 局域网模式目前没有登录保护，不能用于公网、公共 Wi-Fi 或访客网络。

## Android v0.1.1

- 新增首次使用前的强制双语确认提示。
- 在连接页面永久显示电脑端依赖和局域网安全说明。
- 安装名称和 APK 文件名明确标注需要 Windows 电脑。
- 修复从仓库根目录执行 Android 正式打包脚本时找不到 Gradle 项目的问题。

要求：Android 10 或更高版本；Windows 电脑需要先安装并运行 DeepSeek Harness。

---

# ⚠️ Android download notice: a PC is required

> [!IMPORTANT]
> DeepSeek Harness does not currently provide an official way to install or run Harness locally on Android. This APK cannot run Harness independently on a phone or tablet, and it is not an official DeepSeek Android edition.

This independently developed, unofficial LAN client only connects to and controls a Windows PC that is already running DeepSeek Harness. The Android device and PC must be on the same trusted private network.

- The APK filename contains `requires-windows-pc`.
- The Android installer displays the application name “dsh-launcher (PC required).”
- The first launch requires another confirmation before the app can continue.
- Harness LAN mode currently has no login protection. Never use it over the public internet, public Wi-Fi, or a guest network.

Requirements: Android 10 or newer; DeepSeek Harness must already be installed and running on the Windows PC.
