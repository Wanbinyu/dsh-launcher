# dsh-launcher Android

[简体中文](README.md) | [English](README.en.md)

> [!IMPORTANT]
> **This is an experimental client: a PC is required, the APK cannot run DeepSeek Harness independently, and complete remote Web functionality is not guaranteed.** DeepSeek Harness does not currently provide an official way to run Harness locally on Android. This independently developed, unofficial LAN client can only attempt to connect to a Windows PC already running Harness. It is not an official DeepSeek Android edition and does not bypass Harness security restrictions.

Download only from the [Android v0.1.2 Release page](https://github.com/Wanbinyu/dsh-launcher/releases/tag/android-v0.1.2), which shows the full warning. The APK filename includes `experimental-requires-windows-pc`, and the application name shown by the Android installer also says “experimental; PC required.”

An experimental Android tablet client for DeepSeek Harness over a local network. The Windows edition remains the primary launcher and runs Harness. The Android app stores a host address, performs a basic HTTP readiness check, and attempts to display the Web UI in a restricted WebView.

> [!WARNING]
> DeepSeek Harness currently has no authentication when bound to `0.0.0.0`, while its agent can access workspaces and run commands. Use it temporarily only on a trusted private network you control. Never expose it to the public internet, public Wi-Fi, or a guest network.

## Current known limitations

As of DeepSeek Harness `0.1.1-rc.2`:

- A plain-HTTP LAN origin is not a browser secure context. `crypto.randomUUID` may be unavailable, causing the first RPC to fail or reconnect forever.
- Even with a UUID fallback, Host/Origin trust checks may still return HTTP 403.
- Settings and credential APIs intentionally remain loopback-only, so a remote browser may report that settings are unavailable.
- A successful basic HTTP probe in the Android app only proves that the server is reachable. It does not prove that sessions, settings, providers, or presets work.

See upstream reports [#4209](https://github.com/deepseek-ai/deepseek-harness/discussions/4209) and [#3302](https://github.com/deepseek-ai/deepseek-harness/discussions/3302). This app does not inject scripts, normalize Host/Origin, or bypass those boundaries. The safer complete remote options remain authenticated HTTPS or a controlled SSH/platform port forward that preserves a localhost browser origin; this app does not configure either option.

## Requirements

- Android 10 (API 29) or newer.
- The tablet and Windows computer are on the same trusted private network.
- Node.js and DeepSeek Harness are installed on Windows.

## Start the Windows host

The following command is only for compatibility testing on a trusted private network. Exit the regular dsh-launcher tray instance, replace the example address with the PC's real LAN address, then run:

```powershell
dsh web --host 0.0.0.0 --trusted-host 192.168.1.25:3080 --no-open
```

Or use the official CLI directly:

```powershell
npx @deepseek-ai/dsh@latest web --host 0.0.0.0 --trusted-host 192.168.1.25:3080 --no-open
```

`--trusted-host` is only one part of the Host/Origin fence. It is not authentication and does not remove the remote-settings limitations above. On the first run, allow Windows Firewall access only on private networks, never public networks.

## Android usage

After installing the APK, the first launch requires confirmation of the PC-host requirement, experimental status, and LAN safety notice before an address can be entered. A persistent banner remains above the Web UI. The app remembers the last successful address; the toolbar supports refresh, opening the system browser, and changing the host.

Close the Windows terminal or press `Ctrl+C` to stop LAN access.

## Privacy and security

- The app does not read API keys, prompts, responses, tool arguments, or workspace files.
- Only the endpoint address is stored in local Android preferences.
- There is no telemetry, advertising, or additional network service.
- HTTPS certificate errors are rejected, and external links open in the system browser.
- The WebView cannot access local files or `content://` resources and exposes no JavaScript-native bridge.

## Build

JDK 17+, Android SDK 36, and Build Tools 36.0.0 are required:

```powershell
cd .\android
.\gradlew.bat testDebugUnitTest assembleDebug
```

Release builds read signing details from `DSH_ANDROID_KEYSTORE`, `DSH_ANDROID_STORE_PASSWORD`, `DSH_ANDROID_KEY_ALIAS`, and `DSH_ANDROID_KEY_PASSWORD`.

Maintainers must keep an offline backup of the signing key and password. Losing the key prevents installed users from upgrading directly to builds signed with a replacement key.
