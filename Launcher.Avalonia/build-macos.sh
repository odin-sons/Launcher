#!/usr/bin/env bash
#
# Builds OdinsonsLauncher.app — a single-file, self-contained macOS bundle
# (the equivalent of the Windows single-exe build).
#
#   ./build-macos.sh                 # arm64 (Apple Silicon)
#   ./build-macos.sh --arch x64      # Intel
#   ./build-macos.sh --arch both     # both, two .app bundles
#
# Output: Launcher.Avalonia/dist/OdinsonsLauncher[-<arch>].app
#
# Requires: dotnet on PATH (or ~/.dotnet/dotnet), plus macOS sips/iconutil/codesign.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$SCRIPT_DIR/Launcher.Avalonia.csproj"
ICON_SRC="$REPO_ROOT/Launcher/Resources/favicon.ico"
DIST_DIR="$SCRIPT_DIR/dist"

BUNDLE_ID="club.odinsons.launcher"
EXE_NAME="OdinsonsLauncher"
DISPLAY_NAME="Odinsons Launcher"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT" | head -1)"
VERSION="${VERSION:-1.3.6}"

ARCH="arm64"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch) ARCH="$2"; shift 2 ;;
        -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
done

DOTNET="$(command -v dotnet || true)"
[[ -z "$DOTNET" && -x "$HOME/.dotnet/dotnet" ]] && DOTNET="$HOME/.dotnet/dotnet"
[[ -z "$DOTNET" ]] && { echo "dotnet not found (install it, or add ~/.dotnet to PATH)" >&2; exit 1; }

case "$ARCH" in
    arm64) RIDS=("osx-arm64") ;;
    x64)   RIDS=("osx-x64") ;;
    both)  RIDS=("osx-arm64" "osx-x64") ;;
    *) echo "--arch must be arm64 | x64 | both" >&2; exit 2 ;;
esac

make_icns() {
    # favicon.ico -> .icns, via a temporary .iconset. Plain fit for now — the round
    # icon sits inside macOS 26's forced squircle with a small transparent margin.
    # (Making it bleed to the edges clipped the corners; revisit later.)
    local out="$1" work
    work="$(mktemp -d)"
    trap 'rm -rf "$work"' RETURN
    sips -s format png "$ICON_SRC"       --out "$work/base.png"     >/dev/null
    sips -z 1024 1024  "$work/base.png"  --out "$work/base1024.png" >/dev/null
    mkdir "$work/i.iconset"
    local s
    for s in 16 32 128 256 512; do
        sips -z "$s" "$s"           "$work/base1024.png" --out "$work/i.iconset/icon_${s}x${s}.png"    >/dev/null
        sips -z $((s*2)) $((s*2))   "$work/base1024.png" --out "$work/i.iconset/icon_${s}x${s}@2x.png" >/dev/null
    done
    iconutil -c icns "$work/i.iconset" -o "$out"
}

write_plist() {
    local path="$1" arch="$2"
    cat > "$path" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>            <string>${EXE_NAME}</string>
    <key>CFBundleDisplayName</key>     <string>${DISPLAY_NAME}</string>
    <key>CFBundleIdentifier</key>      <string>${BUNDLE_ID}</string>
    <key>CFBundleExecutable</key>      <string>${EXE_NAME}</string>
    <key>CFBundleIconFile</key>        <string>${EXE_NAME}</string>
    <key>CFBundlePackageType</key>     <string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key> <string>6.0</string>
    <key>CFBundleShortVersionString</key>    <string>${VERSION}</string>
    <key>CFBundleVersion</key>               <string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key>        <string>11.0</string>
    <key>LSApplicationCategoryType</key>     <string>public.app-category.games</string>
    <key>NSHighResolutionCapable</key>       <true/>
    <key>LSArchitecturePriority</key>        <array><string>${arch}</string></array>
</dict>
</plist>
PLIST
}

rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

for RID in "${RIDS[@]}"; do
    APP_ARCH="${RID#osx-}"                       # arm64 | x64
    [[ "$APP_ARCH" == "x64" ]] && PLIST_ARCH="x86_64" || PLIST_ARCH="arm64"

    if [[ ${#RIDS[@]} -gt 1 ]]; then
        APP="$DIST_DIR/${EXE_NAME}-${APP_ARCH}.app"
    else
        APP="$DIST_DIR/${EXE_NAME}.app"
    fi
    PUBLISH_DIR="$SCRIPT_DIR/bin/Release/net9.0/${RID}/publish"

    echo ">> publishing $RID ..."
    "$DOTNET" publish "$PROJECT" -c Release -r "$RID" --self-contained \
        -p:PublishSingleFile=true --nologo -v minimal

    echo ">> assembling ${APP##*/} ..."
    rm -rf "$APP"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

    # The single-file exe plus any native lib that could not be embedded.
    cp -R "$PUBLISH_DIR"/. "$APP/Contents/MacOS/"
    rm -f "$APP/Contents/MacOS/"*.pdb
    chmod +x "$APP/Contents/MacOS/${EXE_NAME}"

    make_icns "$APP/Contents/Resources/${EXE_NAME}.icns"
    write_plist "$APP/Contents/Info.plist" "$PLIST_ARCH"
    printf 'APPL????' > "$APP/Contents/PkgInfo"

    # Ad-hoc signature: required for the binary to run at all on Apple Silicon.
    codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || \
        echo "   (codesign failed — the app still runs, Gatekeeper may need a right-click > Open)"

    # Refresh LaunchServices so Finder picks up the icon / name without a re-login.
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
        -f "$APP" >/dev/null 2>&1 || true

    echo ">> done: $APP  ($(du -sh "$APP" | cut -f1))"
done

echo
echo "Bundles in: $DIST_DIR"
