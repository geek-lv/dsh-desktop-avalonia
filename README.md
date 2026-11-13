# DSH Desktop (Avalonia)

A lightweight cross-platform (Windows / macOS / Linux) desktop shell for the official DeepSeek Harness web UI, built with Avalonia 12 and its native WebView, with a system tray, start-at-login and DSH service controls.

It does not reimplement any UI: the window shows the official `dsh web` page, so official plugins, themes and the Remote sidebar all work as-is.

## Features

- Native window with the OS title bar; light / dark follows the DSH page's theme
- Platform WebView via `Avalonia.Controls.WebView`: WebView2 (Windows), WKWebView (macOS), WebKitGTK (Linux); nothing bundled
- Starts `dsh web --no-open` when nothing listens on `127.0.0.1:3080`, or adopts a running instance, and signs in with its access token
- Tray menu: open window, service status, restart / stop the DSH service, reload page, start at login, open log folder, quit (the service keeps running)
- Closing the window hides it to the tray; launching again brings the existing window back (single instance)
- `--tray` starts hidden (used by start-at-login)

## Requirements

- An installed DeepSeek Harness CLI (`npm i -g` the official package) so `dsh` is available. Set `DSH_BIN` to point at it explicitly if it is not found.
- .NET 10 runtime (or publish self-contained)
- Windows: Microsoft Edge WebView2 Runtime (preinstalled on Windows 11)
- Linux: `libwebkit2gtk-4.1-0` (WebKitGTK; 4.0 also works), plus `libicu`, `libice6`, `libsm6` (present on normal desktops, missing on minimal installs / WSL). The tray needs a StatusNotifierItem host (GNOME needs the AppIndicator extension; WSLg has none)
  ```sh
  sudo apt install libwebkit2gtk-4.1-0 libice6 libsm6 libicu-dev
  ```

## Build

```sh
dotnet build -c Release
dotnet publish -c Release -r win-x64      # or osx-arm64, osx-x64, linux-x64, linux-arm64
dotnet publish -c Release -r linux-x64 -p:SelfContained=true   # no .NET runtime needed on the target
```

macOS `.app` bundle (self-contained, app icon, ad-hoc signed; run on a Mac):

```sh
packaging/macos/make-app.sh                 # Apple Silicon -> dist/osx-arm64/DeepSeek Harness Desktop.app
RID=osx-x64 packaging/macos/make-app.sh     # Intel
```

Run it from the bundle: a bare binary launched outside a `.app` gets no Dock icon, and macOS hides its menu-bar item.

## Where things live

| | Windows | macOS | Linux |
|---|---|---|---|
| App data, logs (`shell.log`), window state, WebView profile | `%LOCALAPPDATA%\dsh-desktop-avalonia` | `~/Library/Application Support/dsh-desktop-avalonia` | `~/.local/share/dsh-desktop-avalonia` |
| Start at login | `HKCU\...\Run\DSHDesktop` | `~/Library/LaunchAgents/io.github.geeklv.dsh-desktop.plist` | `~/.config/autostart/dsh-desktop-avalonia.desktop` |
| Output of the `dsh web` it starts | `~/.dsh/logs/shell-web.log` | same | same |

## Status

| | Engine | Verified |
|---|---|---|
| Windows 11 | WebView2 157 | service start + token sign-in, tray, single instance, hide-to-tray, window state |
| macOS 26.6 (Apple Silicon) | WKWebView | same, plus Dock reopen; `.app` bundle with Dock icon and template menu-bar icon |
| Ubuntu 26.04 on WSL2 / WSLg | WebKitGTK 2.52 | service start + token sign-in, single instance, hide-to-tray, `linux-x64` publish (no tray host under WSLg) |

## License

MIT. Uses Avalonia and Avalonia.Controls.WebView (MIT).
