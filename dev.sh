#!/usr/bin/env bash
# 소스가 바뀔 때마다 자동으로 재빌드하고 앱을 다시 띄운다.
#
#   ./dev.sh          변경 감시 루프 (Ctrl+C로 종료, 앱은 계속 떠 있음)
#   ./dev.sh once     한 번만 빌드하고 실행
#   ./dev.sh render   한 번 빌드하고 HUD를 PNG로 렌더 (앱 안 띄움)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
# 개발 중에는 테스트판(dong-mcu-test)을 띄운다. brew로 설치한 정식판과 섞이지 않는다.
export VARIANT="${VARIANT:-test}"
APP_NAME="dong-mcu"
[[ "$VARIANT" == "test" ]] && APP_NAME="dong-mcu-test"
APP="$ROOT/build/$APP_NAME.app"
LOG="$ROOT/build/dev-build.log"
MODE="${1:-watch}"

mkdir -p "$ROOT/build"

stop_app() {
  pkill -f "$APP_NAME.app/Contents/MacOS/$APP_NAME" 2>/dev/null || true
}

build() {
  if "$ROOT/build.sh" >"$LOG" 2>&1; then
    return 0
  fi
  printf '\033[31m✗ 빌드 실패\033[0m\n'
  # 컴파일 에러만 추려서 보여주고, 없으면 로그 끝부분을 보여준다.
  if grep -qE "error:" "$LOG"; then
    grep -E "error:" "$LOG" | head -20
  else
    tail -20 "$LOG"
  fi
  return 1
}

cycle() {
  printf '\n\033[2m──\033[0m %s \033[2m재빌드…\033[0m\n' "$(date +%H:%M:%S)"
  build || return 1
  stop_app
  open "$APP"
  printf '\033[32m✓\033[0m 실행 중\n'
}

case "$MODE" in
  render)
    build || exit 1
    OUT="$ROOT/build/hud.png"
    "$ROOT/.build/release/dong-mcu" --render "$OUT" "${2:-8}" "${3:-60}" "${4:-clawd}"
    open "$OUT"
    exit 0
    ;;
  once)
    cycle
    exit $?
    ;;
esac

cycle

if ! command -v fswatch >/dev/null 2>&1; then
  printf '\033[31mfswatch 없음.\033[0m brew install fswatch 후 다시 실행.\n' >&2
  exit 1
fi

printf '\n감시 중: Sources/ Resources/ Package.swift  \033[2m(Ctrl+C 종료 — 앱은 계속 떠 있음)\033[0m\n'

# -o: 변경 묶음마다 이벤트 1개, -l: 디바운스(연속 저장을 한 번으로 합침)
fswatch -o -l 0.4 "$ROOT/Sources" "$ROOT/Resources" "$ROOT/Package.swift" | while read -r _; do
  cycle
done
