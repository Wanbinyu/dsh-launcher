# dsh-launcher Android

[简体中文](README.md) | [English](README.en.md)

> [!IMPORTANT]
> **Before downloading or installing: a PC is required, and the Android APK cannot run DeepSeek Harness independently.** DeepSeek Harness does not currently provide an official way to install or run Harness locally on Android. This app is an independently developed, unofficial LAN client that only connects to and controls a Windows PC already running Harness. It is not an official DeepSeek Android edition and cannot install Harness on a phone or tablet.

Download only from the [Android v0.1.1 Release page](https://github.com/Wanbinyu/dsh-launcher/releases/tag/android-v0.1.1), which shows the full warning. The APK filename includes `requires-windows-pc`, and the application name shown by the Android installer also says “PC required.”

An Android tablet client for DeepSeek Harness over a local network. The Windows edition remains the primary launcher and runs Harness. The Android app stores the host address, checks readiness, and presents the complete Web UI in a restricted WebView.

> [!WARNING]
> DeepSeek Harness currently has no authentication when bound to `0.0.0.0`, while its agent can access workspaces and run commands. Use it temporarily only on a trusted private network you control. Never expose it to the public internet, public Wi-Fi, or a guest network.

## Requirements

- Android 10 (API 29) or newer.
- The tablet and Windows computer are on the same trusted private network.
- Node.js and DeepSeek Harness are installed on Windows.

## Start the Windows host

Exit the regular dsh-launcher tray instance, then run this in a Windows terminal:

```powershell
dsh web --host 0.0.0.0
```

With a current Harness release, prevent the PC browser from opening automatically:

```powershell
npx @deepseek-ai/dsh@latest web --host 0.0.0.0 --no-open
```

The command prints a LAN URL such as `http://192.168.1.25:3080`. On the first run, allow Windows Firewall access only on private networks, never public networks.

## Android usage

After installing the APK, the first launch requires confirmation of the PC-host requirement and LAN safety notice before an address can be entered. Enter the printed LAN URL and select Connect. The app remembers the last successful address. The toolbar supports refresh, opening the system browser, and changing the host. File selection and downloads use Android system services.

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
