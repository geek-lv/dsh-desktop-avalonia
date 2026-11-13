#!/usr/bin/env bash
# Build dist/DeepSeek-Harness-Desktop-<version>-<arch>.AppImage: a Native AOT DshDesktop plus everything it
# needs that a stock Linux desktop may lack, WebKitGTK 4.1 (with its helper processes) and GTK 3 included.
#
#   packaging/linux/make-appimage.sh                 # x86_64
#   ARCH=aarch64 packaging/linux/make-appimage.sh    # arm64 (run it on an arm64 host: Native AOT and the
#                                                    # libraries it bundles come from the build host)
#
# Optional env:
#   ARCH            x86_64 (default) | aarch64
#   PUBLISH_DIR     use an existing `dotnet publish` output instead of publishing again
#   TOOLS_DIR       where linuxdeploy / the GTK plugin / appimagetool are cached
#                   (default ${XDG_CACHE_HOME:-~/.cache}/dsh-desktop-appimage)
#   DOTNET          dotnet executable (default: `dotnet` on PATH, else ~/.dotnet/dotnet)
#
# The AppImage carries the libraries of the BUILD host, so it runs on distributions whose glibc is at least
# as new as the build host's. Build on the oldest base you support (CI: ubuntu-22.04 -> glibc 2.35).
#
# Build host packages (Debian / Ubuntu 22.04+), plus the .NET 10 SDK:
#   sudo apt-get install -y --no-install-recommends \
#     clang zlib1g-dev \
#     libwebkit2gtk-4.1-0 libgtk-3-0 libgtk-3-bin librsvg2-common libice6 libsm6 glib-networking \
#     gsettings-desktop-schemas libglib2.0-bin gir1.2-glib-2.0 shared-mime-info \
#     pkg-config dpkg-dev file desktop-file-utils curl ca-certificates
#   (clang + zlib1g-dev: Native AOT link. The library packages are what gets bundled. pkg-config, dpkg-dev,
#   libglib2.0-bin (glib-compile-schemas), libgtk-3-bin (gtk-query-immodules-3.0) and gir1.2-glib-2.0 (the
#   plugin insists on a girepository-1.0 directory): linuxdeploy-plugin-gtk. No -dev packages are needed;
#   the plugin's "Package gtk+-3.0 was not found" messages are harmless, it falls back to the Debian paths.
#   file, desktop-file-utils: appimagetool. No FUSE needed: the tools run with APPIMAGE_EXTRACT_AND_RUN=1.)
#
# What is bundled vs. taken from the host follows linuxdeploy's built-in AppImage excludelist
# (https://github.com/AppImageCommunity/pkg2appimage/blob/master/excludelist): glibc, libGL/EGL/GLX/drm/gbm,
# X11/xcb client libs, fontconfig/freetype/harfbuzz and friends come from the host; the rest is bundled.
#
# WebKitGTK starts WebKitNetworkProcess / WebKitWebProcess from a directory compiled into libwebkit2gtk
# (/usr/lib/<triplet>/webkit2gtk-4.1). That directory is copied into the AppDir and the path inside the
# bundled libwebkit2gtk is rewritten, same length, to the relative "././/lib/<triplet>/webkit2gtk-4.1";
# AppRun runs the app with $APPDIR/usr as its working directory, so it resolves inside the AppImage
# (the approach of Tauri's AppImage bundler). The app itself never relies on the working directory.
set -euo pipefail

ARCH="${ARCH:-x86_64}"
case "$ARCH" in
  x86_64)  RID=linux-x64;   TRIPLET=x86_64-linux-gnu ;;
  aarch64) RID=linux-arm64; TRIPLET=aarch64-linux-gnu ;;
  *) echo "unsupported ARCH=$ARCH (x86_64 | aarch64)" >&2; exit 1 ;;
esac

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
APP_ID="dsh-desktop-avalonia"
# VERSION (e.g. from a release tag) overrides the csproj <Version>.
VERSION="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/DshDesktop.csproj" | head -1)}"
[ -n "$VERSION" ] || { echo "no <Version> in DshDesktop.csproj" >&2; exit 1; }

DIST="$ROOT/dist"
WORK="$DIST/appimage/$ARCH"
APPDIR="$WORK/AppDir"
OUT="$DIST/DeepSeek-Harness-Desktop-$VERSION-$ARCH.AppImage"
TOOLS_DIR="${TOOLS_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/dsh-desktop-appimage}"
SYS_LIB="/usr/lib/$TRIPLET"
WEBKIT_EXEC_DIR="$SYS_LIB/webkit2gtk-4.1"

# CI runners have no FUSE; the tools are AppImages themselves.
export APPIMAGE_EXTRACT_AND_RUN=1
export NO_APPSTREAM=1

# ------------------------------------------------------------------ tools (pinned, cached)
LINUXDEPLOY_URL="https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-$ARCH.AppImage"
GTK_PLUGIN_URL="https://raw.githubusercontent.com/linuxdeploy/linuxdeploy-plugin-gtk/7a3fbc31a9e5075073ff8790f26effbac5f84453/linuxdeploy-plugin-gtk.sh"
APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$ARCH.AppImage"

fetch() { # url dest
  [ -s "$2" ] && return 0
  echo "downloading $1"
  curl -fL --retry 3 --retry-delay 2 -o "$2.part" "$1"
  mv "$2.part" "$2"
  chmod +x "$2"
}
mkdir -p "$TOOLS_DIR"
APPIMAGETOOL="$TOOLS_DIR/appimagetool-$ARCH.AppImage"
fetch "$LINUXDEPLOY_URL" "$TOOLS_DIR/linuxdeploy-$ARCH.AppImage"
fetch "$GTK_PLUGIN_URL" "$TOOLS_DIR/linuxdeploy-plugin-gtk.sh"
fetch "$APPIMAGETOOL_URL" "$APPIMAGETOOL"
# linuxdeploy is used extracted: that also gives us the patchelf it ships with.
LINUXDEPLOY_DIR="$TOOLS_DIR/linuxdeploy-$ARCH.AppDir"
if [ ! -x "$LINUXDEPLOY_DIR/AppRun" ]; then
  rm -rf "$LINUXDEPLOY_DIR" "$TOOLS_DIR/squashfs-root"
  (cd "$TOOLS_DIR" && "./linuxdeploy-$ARCH.AppImage" --appimage-extract >/dev/null)
  mv "$TOOLS_DIR/squashfs-root" "$LINUXDEPLOY_DIR"
fi
LINUXDEPLOY="$LINUXDEPLOY_DIR/AppRun"
PATCHELF="$LINUXDEPLOY_DIR/usr/bin/patchelf"
export PATH="$TOOLS_DIR:$PATH" # linuxdeploy finds linuxdeploy-plugin-gtk.sh on PATH
echo "tools: linuxdeploy $("$LINUXDEPLOY" --version 2>&1 | head -1 || true); $("$APPIMAGETOOL" --version 2>&1 | head -1 || true)"

for f in "$SYS_LIB/libwebkit2gtk-4.1.so.0" "$WEBKIT_EXEC_DIR/WebKitWebProcess" "$WEBKIT_EXEC_DIR/WebKitNetworkProcess"; do
  [ -e "$f" ] || { echo "missing $f (install libwebkit2gtk-4.1-0, see the header of this script)" >&2; exit 1; }
done

# ------------------------------------------------------------------ publish
if [ -z "${PUBLISH_DIR:-}" ]; then
  DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
  PUBLISH_DIR="$WORK/publish"
  rm -rf "$PUBLISH_DIR"
  "$DOTNET" publish "$ROOT/DshDesktop.csproj" -c Release -r "$RID" -o "$PUBLISH_DIR" -nologo -p:Version="$VERSION"
fi
[ -x "$PUBLISH_DIR/DshDesktop" ] || { echo "no DshDesktop in $PUBLISH_DIR" >&2; exit 1; }

# ------------------------------------------------------------------ AppDir skeleton
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/lib/$TRIPLET" "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/512x512/apps"
# The executable and the native libraries .NET loads from its own directory (Skia, HarfBuzz).
cp -a "$PUBLISH_DIR"/. "$APPDIR/usr/bin/"
install -m644 "$ROOT/Assets/AppIcon-512.png" "$APPDIR/usr/share/icons/hicolor/512x512/apps/$APP_ID.png"
install -m644 "$HERE/$APP_ID.desktop" "$APPDIR/usr/share/applications/$APP_ID.desktop"

# WebKitGTK helper processes + injected bundle, at the same relative spot under usr/ as on the host.
cp -a "$WEBKIT_EXEC_DIR" "$APPDIR/usr/lib/$TRIPLET/"
rm -f "$APPDIR/usr/lib/$TRIPLET/webkit2gtk-4.1/MiniBrowser" # a demo browser, not used

# GIO modules: TLS for https in the network process (glib-networking). Host modules (gvfs, dconf) are
# deliberately not used: they are built against the host's GLib.
mkdir -p "$APPDIR/usr/lib/gio/modules"
for m in libgiognutls.so libgioopenssl.so; do
  [ -e "$SYS_LIB/gio/modules/$m" ] && cp -L "$SYS_LIB/gio/modules/$m" "$APPDIR/usr/lib/gio/modules/"
done

# Libraries the app dlopens by name (nothing links them, so linuxdeploy would not see them).
LIBS=()
for l in libwebkit2gtk-4.1.so.0 libjavascriptcoregtk-4.1.so.0 libgtk-3.so.0 libgdk-3.so.0 libsoup-3.0.so.0 \
         libICE.so.6 libSM.so.6; do
  [ -e "$SYS_LIB/$l" ] && LIBS+=("--library=$SYS_LIB/$l")
done

# Everything else is found by walking the ELF files already in the AppDir (helpers, modules, Skia ...).
DEPLOY_GTK_VERSION=3 "$LINUXDEPLOY" --appdir "$APPDIR" \
  --executable "$APPDIR/usr/bin/DshDesktop" \
  --desktop-file "$APPDIR/usr/share/applications/$APP_ID.desktop" \
  --icon-file "$APPDIR/usr/share/icons/hicolor/512x512/apps/$APP_ID.png" \
  "${LIBS[@]}" \
  --plugin gtk

# ------------------------------------------------------------------ rpaths of nested ELF files
# linuxdeploy copies every dependency flat into usr/lib but gives ELF files in subdirectories
# (WebKit helpers, injected bundle, GIO / GTK / pixbuf modules) a bare $ORIGIN rpath. Point them at usr/lib.
while IFS= read -r -d '' f; do
  head -c4 "$f" | grep -q $'\x7fELF' || continue
  rel="$(realpath --relative-to="$(dirname "$f")" "$APPDIR/usr/lib")"
  "$PATCHELF" --set-rpath "\$ORIGIN/$rel:\$ORIGIN" "$f"
done < <(find "$APPDIR/usr/lib" -mindepth 2 -type f -print0)

# ------------------------------------------------------------------ WebKit helper path
WK_APPDIR="$APPDIR/usr/lib/$TRIPLET/webkit2gtk-4.1"
WK_LIB="$(readlink -f "$APPDIR/usr/lib/libwebkit2gtk-4.1.so.0")"
OLD="$WEBKIT_EXEC_DIR"
NEW="././/lib/$TRIPLET/webkit2gtk-4.1" # same length as /usr/lib/<triplet>/webkit2gtk-4.1
[ "${#OLD}" -eq "${#NEW}" ] || { echo "path length mismatch" >&2; exit 1; }
hits="$(grep -c -a -F "$OLD" "$WK_LIB" || true)"
[ "$hits" -gt 0 ] || { echo "$OLD not found in $WK_LIB" >&2; exit 1; }
LC_ALL=C sed -i "s|$OLD|$NEW|g" "$WK_LIB"
! grep -q -a -F "$OLD" "$WK_LIB" || { echo "path rewrite failed" >&2; exit 1; }
echo "rewrote the WebKit helper path in $(basename "$WK_LIB") ($hits places)"
# Whatever is bundled must also be what the helpers and modules resolve (not the build host's copy).
while IFS= read -r -d '' f; do
  head -c4 "$f" | grep -q $'\x7fELF' || continue
  resolved="$(env -u LD_LIBRARY_PATH ldd "$f" 2>/dev/null)"
  # direct dependencies only (a host library's own dependencies come from the host, by design)
  for name in $(readelf -d "$f" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p'); do
    [ -e "$APPDIR/usr/lib/$name" ] || continue
    path="$(awk -v n="$name" '$1 == n { print $3 }' <<< "$resolved")"
    case "$path" in "$APPDIR"/*) ;; *) echo "$f resolves $name from '$path', not from the AppDir" >&2; exit 1 ;; esac
  done
done < <(find "$APPDIR/usr/lib/$TRIPLET" "$APPDIR/usr/lib/gio" -type f -print0)

# ------------------------------------------------------------------ AppRun
rm -f "$APPDIR/AppRun" "$APPDIR/AppRun.wrapped"
install -m755 "$HERE/AppRun" "$APPDIR/AppRun"
install -m755 "$HERE/xdg-open" "$APPDIR/usr/bin/xdg-open"
# GStreamer: no plugins are bundled (media playback is not used) and the host's must not be loaded into
# the bundled GStreamer.
cat > "$APPDIR/apprun-hooks/zz-dsh-desktop.sh" <<EOF
export GIO_MODULE_DIR="\$APPDIR/usr/lib/gio/modules"
export GST_PLUGIN_SYSTEM_PATH_1_0="\$APPDIR/usr/lib/gstreamer-1.0"
export GST_PLUGIN_PATH_1_0="\$APPDIR/usr/lib/gstreamer-1.0"
export GST_PLUGIN_SCANNER_1_0=/nonexistent
export GST_REGISTRY_FORK=no
export GST_REGISTRY_1_0="\${XDG_CACHE_HOME:-\$HOME/.cache}/$APP_ID/gstreamer-registry.bin"
EOF

# ------------------------------------------------------------------ pack
chmod -R a+rX "$APPDIR"
mkdir -p "$DIST"
rm -f "$OUT"
ARCH="$ARCH" VERSION="$VERSION" "$APPIMAGETOOL" --no-appstream "$APPDIR" "$OUT"
echo "built: $OUT ($(du -h "$OUT" | cut -f1))"
