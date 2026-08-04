#!/usr/bin/env bash
# Resources/AppIcon.icns 를 다시 만든다.
# 그림 자체는 Sources/DongMCU/AppIconArt.swift 에 있고, 앱이 스스로 PNG를 뽑는다.
# 아이콘은 자주 바뀌지 않으므로 결과물(.icns)을 커밋해 두고 build.sh는 복사만 한다.
set -euo pipefail

# 아이콘만 뽑으면 되니 최적화 없이 빠르게 빌드한다.
CONFIG="${CONFIG:-debug}"
source "$(dirname "$0")/lib.sh"
OUT="$ROOT/Resources/AppIcon.icns"

swift_build
# lib.sh 의 BIN(번들 안 실행 파일)과 다르다. 여기서는 번들을 만들지 않는다.
ICON_BIN="$(swift_bin_dir)/dong-mcu"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
ICONSET="$WORK/AppIcon.iconset"
mkdir -p "$ICONSET"

for SIZE in 16 32 64 128 256 512 1024; do
  "$ICON_BIN" --render-icon "$WORK/$SIZE.png" "$SIZE" >/dev/null
done

# @2x는 한 단계 큰 그림을 그대로 쓴다. 확대가 아니라 그 크기로 새로 그린 것이다.
cp "$WORK/16.png"   "$ICONSET/icon_16x16.png"
cp "$WORK/32.png"   "$ICONSET/icon_16x16@2x.png"
cp "$WORK/32.png"   "$ICONSET/icon_32x32.png"
cp "$WORK/64.png"   "$ICONSET/icon_32x32@2x.png"
cp "$WORK/128.png"  "$ICONSET/icon_128x128.png"
cp "$WORK/256.png"  "$ICONSET/icon_128x128@2x.png"
cp "$WORK/256.png"  "$ICONSET/icon_256x256.png"
cp "$WORK/512.png"  "$ICONSET/icon_256x256@2x.png"
cp "$WORK/512.png"  "$ICONSET/icon_512x512.png"
cp "$WORK/1024.png" "$ICONSET/icon_512x512@2x.png"

iconutil -c icns "$ICONSET" -o "$OUT"
echo "built: $OUT"
