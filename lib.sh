#!/usr/bin/env bash
# build.sh · dev.sh · release.sh · make-icon.sh 가 공유하는 값과 함수.
#
# 앱 이름·번들 경로·빌드 호출을 스크립트마다 따로 계산하면, 이름을 바꿀 때
# 한 곳을 놓쳐서 없는 번들을 열거나 엉뚱한 앱을 종료하게 된다. 여기 한 곳만 고친다.
#
# 쓰는 쪽: source "$(dirname "$0")/lib.sh"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# VARIANT=test 로 부르면 번들 ID가 다른 별개의 앱(DongMCU-Test)이 나온다.
# 설정·창 위치·메뉴바 자리를 정식판과 공유하지 않아서 둘을 동시에 띄울 수 있다.
#
# 번들 이름은 화면에 보이는 이름이고, 번들 ID는 예전 그대로다.
# ID를 바꾸면 UserDefaults 키가 달라져서 창 위치·아이콘·크기 설정이 초기화된다.
VARIANT="${VARIANT:-release}"
if [[ "$VARIANT" == "test" ]]; then
  APP_NAME="DongMCU-Test"
  BUNDLE_ID="com.ldg.dong-mcu-test"
else
  APP_NAME="DongMCU"
  BUNDLE_ID="com.ldg.dong-mcu"
fi

APP="$ROOT/build/$APP_NAME.app"
# 번들 안의 실행 파일. 렌더 통로는 .build 를 직접 뒤지지 말고 이걸 쓴다
# (CONFIG 를 바꿔도 방금 만든 번들과 어긋나지 않는다).
BIN="$APP/Contents/MacOS/$APP_NAME"

CONFIG="${CONFIG:-release}"

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
