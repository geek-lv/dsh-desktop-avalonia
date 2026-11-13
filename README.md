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
- Linux: `libwebkit2gtk-4.1` and a tray that supports StatusNotifierItem (GNOME needs the AppIndicator extension)

## Build

```sh
dotnet build -c Release
dotnet publish -c Release -r win-x64      # or osx-arm64, osx-x64, linux-x64, linux-arm64
dotnet publish -c Release -r linux-x64 -p:SelfContained=true   # no .NET runtime needed on the target
```

## Where things live

| | Windows | macOS | Linux |
|---|---|---|---|
| App data, logs (`shell.log`), window state, WebView profile | `%LOCALAPPDATA%\dsh-desktop-avalonia` | `~/Library/Application Support/dsh-desktop-avalonia` | `~/.local/share/dsh-desktop-avalonia` |
| Start at login | `HKCU\...\Run\DSHDesktop` | `~/Library/LaunchAgents/io.github.geeklv.dsh-desktop.plist` | `~/.config/autostart/dsh-desktop-avalonia.desktop` |
| Output of the `dsh web` it starts | `~/.dsh/logs/shell-web.log` | same | same |

## Status

Verified on Windows 11 (WebView2 157): service start and token sign-in, tray, single instance, hide-to-tray, window state.
macOS and Linux builds compile but have not been run yet; a macOS `.app` bundle is not produced yet.

## License

MIT. Uses Avalonia and Avalonia.Controls.WebView (MIT).
