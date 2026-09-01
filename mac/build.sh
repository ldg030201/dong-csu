#!/usr/bin/env bash
# dong-csu.app 번들을 만든다. Xcode 없이 Command Line Tools + SwiftPM만 사용.
set -euo pipefail

# 앱 이름·경로·빌드 호출은 lib.sh 한 곳에서 정한다.
source "$(dirname "$0")/lib.sh"

# 버전은 Info.plist와 main.swift 두 곳에 있다. 어긋난 채로 배포되면
# `dong-csu --version`이 태그와 다른 값을 뱉으므로 여기서 막는다.
PLIST_VERSION="$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$ROOT/Resources/Info.plist")"
SOURCE_VERSION="$(sed -n 's/^let dongCSUVersion = "\(.*\)"$/\1/p' "$ROOT/Sources/DongCSU/main.swift")"
if [[ "$PLIST_VERSION" != "$SOURCE_VERSION" ]]; then
  echo "버전 불일치: Info.plist=$PLIST_VERSION, main.swift=$SOURCE_VERSION" >&2
  exit 1
fi

swift_build
BIN_DIR="$(swift_bin_dir)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/dong-csu" "$APP/Contents/MacOS/$APP_NAME"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"

for KEY_VALUE in \
  "CFBundleName:$APP_NAME" \
  "CFBundleDisplayName:$APP_NAME" \
  "CFBundleExecutable:$APP_NAME" \
  "CFBundleIdentifier:$BUNDLE_ID"
do
  /usr/libexec/PlistBuddy -c "Set :${KEY_VALUE%%:*} ${KEY_VALUE#*:}" "$APP/Contents/Info.plist"
done

# 가운데 아이콘을 직접 교체하고 싶으면 Resources/claude-icon.png 를 두면 된다.
if [[ -f "$ROOT/Resources/claude-icon.png" ]]; then
  cp "$ROOT/Resources/claude-icon.png" "$APP/Contents/Resources/claude-icon.png"
fi

# 앱 아이콘. 그림을 고쳤으면 ./make-icon.sh 로 다시 만든다.
if [[ -f "$ROOT/Resources/AppIcon.icns" ]]; then
  cp "$ROOT/Resources/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
fi

# 기본 마스코트 시트. **그림으로 도는 부엉이는 언제나 이 파일에서 읽는다.**
#
# 그린 그림을 Resources/mascot.png 에 두면 그걸 쓰고, 없으면 격자 부엉이에서 굽는다.
# 그림을 갈아끼우는 것이 파일 하나를 바꾸는 일이 되게 하려는 것이다.
#
# **구울 때는 방금 만든 바이너리로 뽑는다.** 색이 변형마다 달라서(테스트판은 보라)
# 소스에 넣어 둘 수가 없고, 자세를 고치면 시트도 같이 바뀌어야 하기 때문이다.
if [[ -f "$ROOT/Resources/mascot.png" ]]; then
  cp "$ROOT/Resources/mascot.png" "$APP/Contents/Resources/mascot.png"
  echo "mascot: Resources/mascot.png 를 넣었다"
else
  "$APP/Contents/MacOS/$APP_NAME" --dump-sprites "$APP/Contents/Resources/mascot.png" sheet \
    >/dev/null
  echo "mascot: 격자 부엉이에서 구웠다"
fi

# 마스코트를 더 넣는다. **여기 이름은 ClaudeIconStyle.sheetResource 와 같아야 한다** —
# 어긋나면 그 캐릭터만 격자 부엉이로 떨어지고, 화면에는 아무 말도 안 나온다.
for extra in raccoon; do
  if [[ -f "$ROOT/Resources/$extra.png" ]]; then
    cp "$ROOT/Resources/$extra.png" "$APP/Contents/Resources/$extra.png"
    echo "mascot: Resources/$extra.png 를 넣었다"
  else
    echo "mascot: Resources/$extra.png 가 없다 — 그 캐릭터는 격자로 떨어진다" >&2
  fi
done

# 개발자 계정 없이 로컬 실행에 필요한 최소 서명.
#
# 자체 서명 인증서가 있으면 그걸 쓴다 — **손쉬운 사용 권한이 서명 신원에 걸리기**
# 때문에, ad-hoc으로 서명하면 다시 빌드할 때마다 허용해 둔 게 풀린다.
# 인증서가 없으면 예전처럼 ad-hoc(`-`)이다. `./make-signing-cert.sh` 참고.
#
# **인증서로 서명하지 못해도 빌드는 끝나게 한다.** 키체인 접근을 거절하거나 잠겨
# 있으면 여기서 멈추는데, 그러면 서명만 못 한 게 아니라 앱이 통째로 안 나온다.
IDENTITY="$(sign_identity)"
if ! codesign --force --sign "$IDENTITY" "$APP" 2>/dev/null; then
  if [[ "$IDENTITY" != "-" ]]; then
    echo "'$IDENTITY' 으로 서명하지 못해 ad-hoc으로 서명한다." >&2
    echo "키체인 창이 뜨면 '항상 허용'을 눌러라 — 그래야 다음 빌드부터 안 묻는다." >&2
  fi
  codesign --force --sign - "$APP"
fi

echo "built: $APP"
