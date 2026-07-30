#!/usr/bin/env bash
# dong-mcu.app 번들을 만든다. Xcode 없이 Command Line Tools + SwiftPM만 사용.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
CONFIG="${CONFIG:-release}"
APP="$ROOT/build/dong-mcu.app"

swift build -c "$CONFIG" --package-path "$ROOT"
BIN_DIR="$(swift build -c "$CONFIG" --package-path "$ROOT" --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/dong-mcu" "$APP/Contents/MacOS/dong-mcu"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"

# 가운데 아이콘을 직접 교체하고 싶으면 Resources/claude-icon.png 를 두면 된다.
if [[ -f "$ROOT/Resources/claude-icon.png" ]]; then
  cp "$ROOT/Resources/claude-icon.png" "$APP/Contents/Resources/claude-icon.png"
fi

# ad-hoc 서명. 개발자 계정 없이 로컬 실행에 필요한 최소 서명이다.
codesign --force --sign - "$APP"

echo "built: $APP"
