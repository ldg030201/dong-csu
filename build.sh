#!/usr/bin/env bash
# dong-mcu.app 번들을 만든다. Xcode 없이 Command Line Tools + SwiftPM만 사용.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
CONFIG="${CONFIG:-release}"
APP="$ROOT/build/dong-mcu.app"

# 버전은 Info.plist와 main.swift 두 곳에 있다. 어긋난 채로 배포되면
# `dong-mcu --version`이 태그와 다른 값을 뱉으므로 여기서 막는다.
PLIST_VERSION="$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$ROOT/Resources/Info.plist")"
SOURCE_VERSION="$(sed -n 's/^let dongMCUVersion = "\(.*\)"$/\1/p' "$ROOT/Sources/DongMCU/main.swift")"
if [[ "$PLIST_VERSION" != "$SOURCE_VERSION" ]]; then
  echo "버전 불일치: Info.plist=$PLIST_VERSION, main.swift=$SOURCE_VERSION" >&2
  exit 1
fi

# Homebrew처럼 이미 샌드박스 안에서 도는 환경에서는 SwiftPM의 자체 샌드박스가 중첩되어
# 실패한다. 그럴 때 SWIFT_BUILD_FLAGS="--disable-sandbox" 로 넘긴다.
# 단어 분리를 의도한 것이라 따옴표를 씌우지 않는다.
# shellcheck disable=SC2086
swift build -c "$CONFIG" --package-path "$ROOT" ${SWIFT_BUILD_FLAGS:-}
# shellcheck disable=SC2086
BIN_DIR="$(swift build -c "$CONFIG" --package-path "$ROOT" ${SWIFT_BUILD_FLAGS:-} --show-bin-path)"

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
