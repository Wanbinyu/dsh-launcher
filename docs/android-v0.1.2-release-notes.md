# ⚠️ Android v0.1.2 是实验性客户端

> [!IMPORTANT]
> 必须有一台运行 DeepSeek Harness 的 Windows 电脑。APK 不能在手机或平板本机运行 Harness，也不能保证当前官方版本的完整远程 Web 功能。

DeepSeek Harness 官方目前没有提供 Android 本机安装与运行方式。进一步核查发现，Harness `0.1.1-rc.2` 在普通局域网 HTTP 地址下还存在浏览器安全上下文、UUID、Host/Origin 信任和远程设置限制。即使首页能够打开，设置、供应商、预设或会话功能仍可能失败。

本应用是独立开发的非官方实验性客户端，不会注入脚本、伪装 Host/Origin 或绕过 Harness 的安全边界。

## 本次调整

- 安装名称和 APK 文件名增加“实验性”标记。
- 已安装 `0.1.1` 的用户升级后会重新看到强制限制说明。
- Web 界面顶部永久显示实验性连接提示。
- 中英文 README 增加官方社区已确认的局域网限制与安全说明。
- 启动示例补充 `--trusted-host`，并明确它不是身份认证、不能解除远程设置限制。

参考：[上游讨论 #4209](https://github.com/deepseek-ai/deepseek-harness/discussions/4209) · [上游讨论 #3302](https://github.com/deepseek-ai/deepseek-harness/discussions/3302)

仅可在你控制的可信私有网络中测试，不能暴露到公网、公共 Wi-Fi 或访客网络。

---

# ⚠️ Android v0.1.2 is experimental

> [!IMPORTANT]
> A Windows PC running DeepSeek Harness is required. The APK cannot run Harness locally on a phone or tablet, and complete remote Web functionality is not guaranteed with the current upstream release.

DeepSeek Harness does not currently provide an official Android runtime. Harness `0.1.1-rc.2` also has browser secure-context, UUID, Host/Origin trust, and remote-settings limitations on plain LAN HTTP origins. Settings, providers, presets, or sessions may fail even when the first page opens.

This independently developed, unofficial experimental client does not inject scripts, normalize Host/Origin, or bypass Harness security boundaries.

## Changes

- Marks the installer label and APK filename as experimental.
- Forces users upgrading from `0.1.1` to acknowledge the new limitations.
- Keeps an experimental-connection warning visible above the Web UI.
- Documents the confirmed upstream LAN limitations and security model in Chinese and English.
- Adds `--trusted-host` to the test command while explaining that it is not authentication and cannot unlock remote settings.

References: [upstream discussion #4209](https://github.com/deepseek-ai/deepseek-harness/discussions/4209) · [upstream discussion #3302](https://github.com/deepseek-ai/deepseek-harness/discussions/3302)

Test only on a trusted private network you control. Never expose it to the public internet, public Wi-Fi, or a guest network.
