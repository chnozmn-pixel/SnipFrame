#!/bin/bash
# Builds SnipFrame and assembles a runnable .app bundle in build/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="release"
APP="build/SnipFrame.app"

# Evrensel derleme: aynı .app hem Apple Silicon (arm64) hem Intel (x86_64) Mac'lerde çalışır.
if swift build -c "$CONFIG" --arch arm64 --arch x86_64 --product SnipFrame; then
    BIN_PATH="$(swift build -c "$CONFIG" --arch arm64 --arch x86_64 --show-bin-path)/SnipFrame"
else
    echo "Uyarı: evrensel derleme başarısız, yalnızca bu makinenin mimarisi derleniyor." >&2
    swift build -c "$CONFIG" --product SnipFrame
    BIN_PATH="$(swift build -c "$CONFIG" --show-bin-path)/SnipFrame"
fi

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

echo "Mimari: $(lipo -archs "$APP/Contents/MacOS/SnipFrame")"
echo "Hazır: $APP"
echo "Çalıştır: open \"$APP\""

if [[ "${1:-}" == "--run" ]]; then
    open "$APP"
fi
