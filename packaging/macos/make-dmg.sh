#!/usr/bin/env bash
# Package the .app into a drag-to-install disk image.
#   packaging/macos/make-dmg.sh                       # builds dist/osx-arm64/*.app first, then the DMG
#   RID=osx-x64 packaging/macos/make-dmg.sh           # Intel   (RID=osx-universal also works)
#   packaging/macos/make-dmg.sh --no-build            # reuse the .app make-app.sh already built
#   DMG_FORMAT=UDZO packaging/macos/make-dmg.sh       # zlib; default ULFO (lzfse, ~7% smaller, macOS 10.11+)
# Output: dist/DeepSeek-Harness-Desktop-<version>-<rid>.dmg  (the .app + an /Applications symlink)
set -euo pipefail

RID="${RID:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
NAME="DeepSeek Harness Desktop"
FORMAT="${DMG_FORMAT:-ULFO}"
# VERSION (e.g. from a release tag) overrides the csproj <Version>.
VERSION="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/DshDesktop.csproj" | head -1)}"
[ -n "$VERSION" ] || { echo "error: no <Version> in DshDesktop.csproj" >&2; exit 1; }
APP="$ROOT/dist/$RID/$NAME.app"
DMG="$ROOT/dist/DeepSeek-Harness-Desktop-$VERSION-$RID.dmg"

if [ "${1:-}" != "--no-build" ]; then
  RID="$RID" "$ROOT/packaging/macos/make-app.sh"
fi
[ -d "$APP" ] || { echo "error: $APP not found (run make-app.sh first)" >&2; exit 1; }

SRC="$(mktemp -d)/dmg"
mkdir -p "$SRC"
ditto "$APP" "$SRC/$NAME.app"          # ditto keeps the code signature / xattrs intact
ln -s /Applications "$SRC/Applications"

rm -f "$DMG"
# hdiutil occasionally fails with "Resource busy" on CI runners; retry a few times.
for i in 1 2 3 4 5; do
  if hdiutil create -quiet -volname "$NAME" -srcfolder "$SRC" -fs HFS+ -format "$FORMAT" -ov "$DMG"; then
    break
  fi
  [ "$i" = 5 ] && { echo "error: hdiutil create failed" >&2; exit 1; }
  echo "hdiutil create failed (attempt $i), retrying..." >&2
  sleep $((i * 3))
done
rm -rf "$(dirname "$SRC")"

hdiutil verify -quiet "$DMG"
echo "built: $DMG ($(du -h "$DMG" | cut -f1))"
