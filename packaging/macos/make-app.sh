#!/usr/bin/env bash
# Build "DeepSeek Harness Desktop.app" with Native AOT (no .NET install needed on the target).
#   packaging/macos/make-app.sh                     # arm64 (Apple Silicon)
#   RID=osx-x64 packaging/macos/make-app.sh         # Intel (cross-compiles fine on an arm64 host)
#   RID=osx-universal packaging/macos/make-app.sh   # arm64 + x86_64 in one bundle (lipo)
# Output: dist/<rid>/DeepSeek Harness Desktop.app
# Needs: .NET 10 SDK and Xcode or the Command Line Tools (Native AOT links with clang/ld).
set -euo pipefail

RID="${RID:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
NAME="DeepSeek Harness Desktop"
EXE="DshDesktop"
BUNDLE_ID="io.github.geeklv.dsh-desktop"
APP="$ROOT/dist/$RID/$NAME.app"
STAGE="$ROOT/dist/.publish"
# VERSION (e.g. from a release tag) overrides the csproj <Version>.
VERSION="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/DshDesktop.csproj" | head -1)}"
[ -n "$VERSION" ] || { echo "error: no <Version> in DshDesktop.csproj" >&2; exit 1; }
# CI builds carry a 4th build number (2.0.0.37); CFBundleShortVersionString allows only X.Y.Z.
SHORT_VERSION="$(echo "$VERSION" | cut -d. -f1-3)"

case "$RID" in
  osx-arm64)     RIDS="osx-arm64" ;;
  osx-x64)       RIDS="osx-x64" ;;
  osx-universal) RIDS="osx-arm64 osx-x64" ;;
  *) echo "error: RID must be osx-arm64, osx-x64 or osx-universal (got '$RID')" >&2; exit 1 ;;
esac

if ! xcrun --find clang >/dev/null 2>&1; then
  echo "error: Native AOT needs Xcode or the Command Line Tools (xcode-select --install)" >&2
  exit 1
fi

# Native AOT publish (the csproj sets PublishAot/trimming): one native executable plus the
# native libraries Avalonia loads (libAvaloniaNative, libSkiaSharp, libHarfBuzzSharp).
for r in $RIDS; do
  rm -rf "${STAGE:?}/$r"
  dotnet publish "$ROOT/DshDesktop.csproj" -c Release -r "$r" -o "$STAGE/$r" -nologo -p:Version="$VERSION"
  # Belt and braces: the csproj prunes symbols after publish; make sure none ship.
  find "$STAGE/$r" \( -name '*.pdb' -o -name '*.dbg' -o -name '*.dSYM' \) -prune -exec rm -rf {} +
done

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# The executable must live inside the bundle, or macOS shows no Dock tile and treats it as unbundled.
if [ "$RID" = "osx-universal" ]; then
  for f in "$STAGE/osx-arm64"/*; do
    b="$(basename "$f")"
    if [ "$b" = "$EXE" ]; then
      lipo -create "$STAGE/osx-arm64/$b" "$STAGE/osx-x64/$b" -output "$APP/Contents/MacOS/$b"
    else
      cp "$f" "$APP/Contents/MacOS/$b"   # NuGet's macOS dylibs are already fat (x86_64 + arm64)
    fi
  done
else
  cp "$STAGE/$RID"/* "$APP/Contents/MacOS/"
  # NuGet ships fat dylibs; keep only this architecture (halves their size).
  ARCH="arm64"; [ "$RID" = "osx-x64" ] && ARCH="x86_64"
  for f in "$APP/Contents/MacOS"/*.dylib; do
    if lipo -info "$f" | grep -q '^Architectures in the fat file'; then
      lipo -thin "$ARCH" "$f" -output "$f.thin" && mv "$f.thin" "$f"
    fi
  done
fi
chmod 755 "$APP/Contents/MacOS"/*

# AppIcon.icns from the 1024px master
ICONTMP="$(mktemp -d)"
ICONSET="$ICONTMP/AppIcon.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$ROOT/Assets/AppIcon-1024.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d "$ROOT/Assets/AppIcon-1024.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONTMP"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundleDisplayName</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
  <key>CFBundleExecutable</key><string>$EXE</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$SHORT_VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleAllowMixedLocalizations</key><true/>
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
# Sign inside-out (nested dylibs, then the bundle) instead of the deprecated --deep.
for f in "$APP/Contents/MacOS"/*.dylib; do codesign --force --sign - "$f" >/dev/null; done
codesign --force --sign - "$APP" >/dev/null
codesign --verify --strict "$APP"

echo "built: $APP ($(du -sh "$APP" | cut -f1))"
