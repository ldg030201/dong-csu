#!/usr/bin/env bash
# build.sh · dev.sh · release.sh · make-icon.sh 가 공유하는 값과 함수.
#
# 앱 이름·번들 경로·빌드 호출을 스크립트마다 따로 계산하면, 이름을 바꿀 때
# 한 곳을 놓쳐서 없는 번들을 열거나 엉뚱한 앱을 종료하게 된다. 여기 한 곳만 고친다.
#
# 쓰는 쪽: source "$(dirname "$0")/lib.sh"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# VARIANT=test 로 부르면 번들 ID가 다른 별개의 앱(DongCSU-Test)이 나온다.
# 설정·창 위치·메뉴바 자리를 정식판과 공유하지 않아서 둘을 동시에 띄울 수 있다.
#
# 번들 이름은 화면에 보이는 이름이고, 번들 ID는 예전 그대로다.
# ID를 바꾸면 UserDefaults 키가 달라져서 창 위치·아이콘·크기 설정이 초기화된다.
VARIANT="${VARIANT:-release}"
if [[ "$VARIANT" == "test" ]]; then
  APP_NAME="DongCSU-Test"
  BUNDLE_ID="com.ldg.dong-csu-test"
else
  APP_NAME="DongCSU"
  BUNDLE_ID="com.ldg.dong-csu"
fi

APP="$ROOT/build/$APP_NAME.app"
# 번들 안의 실행 파일. 렌더 통로는 .build 를 직접 뒤지지 말고 이걸 쓴다
# (CONFIG 를 바꿔도 방금 만든 번들과 어긋나지 않는다).
BIN="$APP/Contents/MacOS/$APP_NAME"

CONFIG="${CONFIG:-release}"

# 코드 서명 신원.
#
# **손쉬운 사용 권한은 이 신원에 걸린다.** ad-hoc(`-`)에는 신원이 없어서 macOS는
# 바이너리 해시로 앱을 알아보는데, 그 해시는 코드가 바뀔 때마다 달라진다. 그래서
# ad-hoc으로 서명하면 업데이트하거나 다시 빌드할 때마다 허용해 둔 권한이 풀린다.
#
# 자체 서명 인증서가 있으면 그걸 쓰고, 없으면 예전처럼 ad-hoc으로 떨어진다.
# 인증서는 `./make-signing-cert.sh` 로 한 번 만든다. 없어도 빌드는 된다.
SIGN_CERT_NAME="${SIGN_CERT_NAME:-DongCSU Local Signing}"

sign_identity() {
  if security find-identity -v -p codesigning 2>/dev/null | grep -qF "$SIGN_CERT_NAME"; then
    printf '%s' "$SIGN_CERT_NAME"
  else
    printf '%s' '-'
  fi
}

# Homebrew처럼 이미 샌드박스 안에서 도는 환경에서는 SwiftPM의 자체 샌드박스가
# 중첩되어 실패한다. 그럴 때 SWIFT_BUILD_FLAGS="--disable-sandbox" 로 넘긴다.
# 단어 분리를 의도한 것이라 따옴표를 씌우지 않는다.
swift_build() {
  # shellcheck disable=SC2086
  swift build -c "$CONFIG" --package-path "$ROOT" ${SWIFT_BUILD_FLAGS:-} "$@"
}

# 방금 빌드한 산출물이 놓인 디렉터리.
swift_bin_dir() {
  swift_build --show-bin-path
}

# 윈도우판과 나눠 쓰는 부엉이 데이터를 소스에서 다시 뽑는다.
# 변경 내역과 같은 이유로 반드시 먼저 빌드한다.
dump_owl() {
  swift_build
  mkdir -p "$ROOT/shared"
  "$(swift_bin_dir)/dong-csu" --dump-owl "$ROOT/shared/owl.json"
}

# 앱이 원격에서 받아보는 변경 내역을 Changelog.swift 에서 다시 뽑는다.
# 반드시 먼저 빌드한다 — 옛 바이너리로 뽑으면 방금 추가한 항목이 빠진다.
dump_changelog() {
  swift_build
  "$(swift_bin_dir)/dong-csu" --dump-changelog "$ROOT/docs/changelog.json"
}
