#!/usr/bin/env bash
# Build "DeepSeek Harness Desktop.app" (self-contained, no .NET install needed on the target).
#   packaging/macos/make-app.sh              # arm64 (Apple Silicon)
#   RID=osx-x64 packaging/macos/make-app.sh  # Intel
# Output: dist/<rid>/DeepSeek Harness Desktop.app
set -euo pipefail

RID="${RID:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
NAME="DeepSeek Harness Desktop"
BUNDLE_ID="io.github.geeklv.dsh-desktop"
APP="$ROOT/dist/$RID/$NAME.app"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/DshDesktop.csproj" | head -1)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# The executable must live inside the bundle, or macOS shows no Dock tile and treats it as unbundled.
dotnet publish "$ROOT/DshDesktop.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:UseAppHost=true -o "$APP/Contents/MacOS"

# AppIcon.icns from the 1024px master
ICONSET="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$ROOT/Assets/AppIcon-1024.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d "$ROOT/Assets/AppIcon-1024.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundleDisplayName</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
  <key>CFBundleExecutable</key><string>DshDesktop</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSAppTransportSecurity</key>
  <dict>
    <!-- the DSH web UI is served over plain http on 127.0.0.1 -->
    <key>NSAllowsLocalNetworking</key><true/>
  </dict>
</dict>
</plist>
PLIST

# Apple Silicon refuses unsigned code; an ad-hoc signature is enough to run locally.
codesign --force --deep --sign - "$APP" >/dev/null
echo "built: $APP"
