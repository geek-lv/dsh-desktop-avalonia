# DSH Desktop (Avalonia)

A lightweight cross-platform (Windows / macOS / Linux) desktop shell for the official DeepSeek Harness web UI, built with Avalonia 12 and its native WebView, with a system tray, start-at-login and DSH service controls.

It does not reimplement any UI: the window shows the official `dsh web` page, so official plugins, themes and the Remote sidebar all work as-is.

## Install

Download from [Releases](https://github.com/geek-lv/dsh-desktop-avalonia/releases) (every CI run also keeps the packages as workflow artifacts):

| | Package | Notes |
|---|---|---|
| Windows x64 | `DeepSeek-Harness-Desktop-<ver>-win-x64.msi` | Double-click. Installs for the current user to `%LOCALAPPDATA%\Programs\DeepSeek Harness Desktop` (no admin rights), adds Start menu and desktop shortcuts. Uninstall from Settings › Apps |
| macOS (Apple Silicon + Intel) | `DeepSeek-Harness-Desktop-<ver>-osx-universal.dmg` | Drag to Applications. Ad-hoc signed: on first launch right-click › Open (or `xattr -dr com.apple.quarantine "/Applications/DeepSeek Harness Desktop.app"`) |
| Linux x86_64 | `DeepSeek-Harness-Desktop-<ver>-x86_64.AppImage` | `chmod +x` and run. Carries WebKitGTK and its other dependencies |

Then install DeepSeek Harness itself (Node.js 24.9+), the same command on every OS:

```sh
npm install -g @deepseek-ai/dsh
```

If no DSH is running and none is installed, the app says so and offers this command with a Copy button.

## Features

- Native window with the OS title bar; light / dark follows the DSH page's theme
- Platform WebView via `Avalonia.Controls.WebView`: WebView2 (Windows), WKWebView (macOS), WebKitGTK (Linux)
- Starts `dsh web --no-open` when nothing listens on `127.0.0.1:3080`, or adopts a running instance, and signs in with its access token
- Tray menu: open window, service status, restart / stop the DSH service, reload page, start at login, open log folder, quit (the service keeps running)
- Closing the window hides it to the tray; launching again brings the existing window back (single instance)
- Chinese UI on Chinese systems, English everywhere else (`DSH_DESKTOP_LANG=zh|en` overrides)
- `--tray` starts hidden (used by start-at-login)

## Build

Native AOT, fully trimmed: one native executable plus the native libraries Avalonia loads (Skia, HarfBuzz; ANGLE for GPU rendering on Windows; libAvaloniaNative on macOS). AOT cannot cross-compile between operating systems, so each package is built on its own OS.

```sh
dotnet build -c Release                       # development build (JIT)
dotnet publish -c Release -r <rid>            # Native AOT: win-x64, osx-arm64, osx-x64, linux-x64, linux-arm64
```

Packages (`VERSION=x.y.z` overrides the csproj version):

| | Command | Needs |
|---|---|---|
| Windows | `pwsh packaging/windows/make-msi.ps1` | .NET 10 SDK, MSVC build tools; WiX comes from NuGet |
| macOS | `RID=osx-universal packaging/macos/make-app.sh && RID=osx-universal packaging/macos/make-dmg.sh` | .NET 10 SDK, Xcode or Command Line Tools |
| Linux | `packaging/linux/make-appimage.sh` | .NET 10 SDK, clang, the apt packages listed in the script header |

`.github/workflows/build.yml` runs all three on every push and PR; a `v*` tag publishes them as a GitHub release with SHA256 sums.

Run the macOS app from its bundle: a bare binary launched outside a `.app` gets no Dock icon, and macOS hides its menu-bar item.

## Where things live

| | Windows | macOS | Linux |
|---|---|---|---|
| App data, logs (`shell.log`), window state, WebView profile | `%LOCALAPPDATA%\dsh-desktop-avalonia` | `~/Library/Application Support/dsh-desktop-avalonia` | `~/.local/share/dsh-desktop-avalonia` |
| Start at login | `HKCU\...\Run\DSHDesktop` | `~/Library/LaunchAgents/io.github.geeklv.dsh-desktop.plist` | `~/.config/autostart/dsh-desktop-avalonia.desktop` |
| Output of the `dsh web` it starts | `~/.dsh/logs/shell-web.log` | same | same |

## Troubleshooting

- **`dsh` not found although installed**: GUI apps do not inherit a shell's PATH. The app probes PATH, npm / nvm / volta / Homebrew locations and then asks your login shell (`type -ap dsh`). Set `DSH_BIN=/path/to/dsh` to force it.
- **WSL2**: the Windows host's npm shim (`/mnt/c/.../npm/dsh`) is skipped; install DSH inside the distro. If Linux windows show up empty and their Windows title starts with `[WARN:COPY MODE]`, WSLg's display channel failed to start: run `wsl --shutdown` and open the distro again.
- **Linux tray icon missing**: the desktop needs a StatusNotifierItem host (GNOME: AppIndicator extension). WSLg has none.

## Status

| | Engine | Verified |
|---|---|---|
| Windows 11 | WebView2 157 | AOT build, MSI install / uninstall / reinstall, service start + token sign-in, tray, single instance, hide-to-tray, window state |
| macOS 26.6 (Apple Silicon) | WKWebView | `.app` bundle with Dock icon and template menu-bar icon, Dock reopen, single instance, hide-to-tray |
| Ubuntu 26.04 on WSL2 / WSLg | WebKitGTK 2.52 | service start + token sign-in, single instance, hide-to-tray (no tray host under WSLg) |

## License

MIT. Uses Avalonia and Avalonia.Controls.WebView (MIT).
