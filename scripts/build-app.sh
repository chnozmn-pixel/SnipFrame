#!/bin/bash
# Builds SnipFrame and assembles a runnable .app bundle in build/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="release"
APP="build/SnipFrame.app"

swift build -c "$CONFIG" --product SnipFrame

BIN_PATH="$(swift build -c "$CONFIG" --show-bin-path)/SnipFrame"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_PATH" "$APP/Contents/MacOS/SnipFrame"
cp Resources/Info.plist "$APP/Contents/Info.plist"
if [[ -f Resources/AppIcon.icns ]]; then
    cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
fi
if [[ -f Resources/AppGlyph.png ]]; then
    cp Resources/AppGlyph.png "$APP/Contents/Resources/AppGlyph.png"
fi

# Yerelleştirme: her .lproj klasörü pakete kopyalanır.
for lproj in Resources/*.lproj; do
    [[ -d "$lproj" ]] && cp -R "$lproj" "$APP/Contents/Resources/"
done

# Ad-hoc signature: gives the bundle a stable identity for the Screen Recording permission.
codesign --force --sign - "$APP" >/dev/null 2>&1 || true

echo "Hazır: $APP"
echo "Çalıştır: open \"$APP\""

if [[ "${1:-}" == "--run" ]]; then
    open "$APP"
fi
