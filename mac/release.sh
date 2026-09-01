#!/usr/bin/env bash
# 새 버전을 내보낸다.
#
#   ./release.sh 0.2.0
#
# 하는 일:
#   1. main 브랜치·깨끗한 작업 트리·중복 태그 확인
#   2. 버전 두 곳(Info.plist, main.swift)을 올리고 빌드해서 검증
#   3. 커밋 → 태그 → 푸시
#   4. 그 태그의 tarball을 받아 sha256 계산
#   5. Homebrew formula의 url/sha256 갱신 후 커밋·푸시
#   6. gh가 있으면 GitHub 릴리스까지 생성
#
# 4번이 3번 뒤에 와야 한다. 태그가 올라가야 GitHub가 tarball을 만들고,
# 그래야 해시를 구할 수 있다.
set -euo pipefail

source "$(dirname "$0")/lib.sh"
cd "$ROOT"

VERSION="${1:-}"
# 네 번째 자리는 긴급 수정용이다. 0.2.0 다음 핫픽스는 0.2.0.1.
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ ]]; then
  echo "사용법: ./release.sh <버전>    예: ./release.sh 2.1.0  또는  ./release.sh 2.1.0.1" >&2
  exit 1
fi
# 판마다 태그를 갈라 붙인다 — 윈도우와 번호를 따로 세기 때문이다(../CLAUDE.md).
# v2.0.0 까지는 접두어가 없었다. 그 태그들은 그대로 둔다.
TAG="mac-v$VERSION"

# 뒷정리는 **어떻게 끝나든** 돌아야 한다.
#
# ERR 만 걸어 두면 명령이 실패했을 때만 돌고, 우리가 직접 부르는 `exit 1` 이나
# 빌드 도중의 Ctrl+C 에서는 안 돈다. 그러면 버전만 올라간 채 작업 트리가 지저분하게
# 남고, 다음 릴리스가 "커밋되지 않은 변경이 있다"로 막히면서 이유는 안 보인다.
BUMPED=0
COMMITTED=0
TMP=""
cleanup() {
  if [[ "$BUMPED" == "1" && "$COMMITTED" == "0" ]]; then
    git checkout -- Resources/Info.plist Sources/DongCSU/main.swift \
      "$REPO_ROOT/README.md" 2>/dev/null || true
    echo "→ 버전 변경을 되돌렸다." >&2
  fi
  if [[ -n "$TMP" ]]; then rm -rf "$TMP"; fi
}
trap cleanup EXIT
# 신호로 죽으면 EXIT 트랩이 안 도는 경우가 있다. 직접 빠져나가 트랩을 태운다.
trap 'exit 130' INT
trap 'exit 143' TERM

# ── 1. 사전 점검 ────────────────────────────────────────────────
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
if [[ "$BRANCH" != "main" ]]; then
  echo "main에서만 릴리스한다 (현재: $BRANCH)" >&2
  exit 1
fi

if [[ -n "$(git status --porcelain)" ]]; then
  echo "커밋되지 않은 변경이 있다:" >&2
  git status --short >&2
  exit 1
fi

git fetch --tags --quiet
if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
  echo "태그 $TAG 가 이미 있다." >&2
  exit 1
fi

# 원격이 앞서 있으면 먼저 따라잡는다.
#
# bottle 워크플로가 릴리스가 끝난 뒤 formula 커밋을 main 에 밀어 넣기 때문에,
# 연달아 릴리스하면 여기서 뒤처져 있다. 그대로 두면 태그만 올라가고 main 푸시가
# 거부되어, 태그가 main 에 없는 커밋을 가리키는 상태로 남는다.
if [[ -n "$(git log --oneline HEAD..origin/main)" ]]; then
  echo "▸ 원격이 앞서 있다 — 리베이스"
  git pull --rebase --quiet origin main
fi

echo "▸ $TAG 준비"

# ── 2. 버전 올리고 빌드로 검증 ──────────────────────────────────
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" Resources/Info.plist
sed -i '' "s/^let dongCSUVersion = \".*\"$/let dongCSUVersion = \"$VERSION\"/" \
  Sources/DongCSU/main.swift
# 뿌리 README 의 "지금 버전" 줄. 배지가 아니라 글자로 적혀 있어서 여기서 안 올리면 낡는다.
# 맥 칸(첫 번째 숫자)만 바꾼다 — 윈도우 번호는 윈도우 릴리스가 올린다.
# `\|` 는 BSD sed 가 빈 식으로 읽는다. 표 구분자는 `[|]` 로 적고 s 구분자는 `#` 을 쓴다.
sed -i '' -E "s#^([|] 지금 버전 [|] )[0-9.]+#\1$VERSION#" "$REPO_ROOT/README.md"
BUMPED=1

./build.sh >/dev/null
BUILT="$("$BIN" --version | awk '{print $2}')"

# **날짜를 여기서 박는다.** 업데이트 확인은 `date` 가 있는 항목만 "나간 버전"으로
# 세므로(`UpdateChecker.latest`), 비워 둔 채로 내면 태그도 릴리스도 나갔는데 앱에는
# 새 버전이 없다고 뜬다. 실제로 2.5.2 를 그렇게 냈다.
CHANGELOG=Sources/DongCSU/Changelog.swift
if grep -q "version: \"$VERSION\", date: nil" "$CHANGELOG"; then
  sed -i '' -E "s#(version: \"$VERSION\", date: )nil#\1\"$(date +%Y-%m-%d)\"#" "$CHANGELOG"
  echo "▸ 변경 내역 날짜 $(date +%Y-%m-%d)"
elif ! grep -q "version: \"$VERSION\"" "$CHANGELOG"; then
  echo "변경 내역에 $VERSION 항목이 없다 — Changelog.swift 에 먼저 적어라" >&2
  exit 1
fi

# 앱이 원격에서 받아보는 변경 내역. 태그를 올리기 전에 갱신해야
# 새 버전이 나온 걸 옛 버전 앱에서도 볼 수 있다.
dump_changelog >/dev/null
if [[ "$BUILT" != "$VERSION" ]]; then
  echo "빌드 결과 버전이 다르다: $BUILT (기대: $VERSION)" >&2
  exit 1
fi
echo "▸ 빌드 확인 ($BUILT)"

# ── 3. 커밋 · 태그 · 푸시 ───────────────────────────────────────
git add Resources/Info.plist Sources/DongCSU/main.swift "$CHANGELOG" docs/changelog.json \
  "$REPO_ROOT/docs/changelog.json" "$REPO_ROOT/README.md"
git commit -q -m "🔖 $TAG"
COMMITTED=1
git tag -a "$TAG" -m "$TAG"
git push -q origin main "$TAG"
echo "▸ 태그 푸시"

# ── 4. tarball 해시 ────────────────────────────────────────────
REPO_URL="$(git remote get-url origin)"
REPO_URL="${REPO_URL%.git}"
# SSH 주소(git@github.com:소유자/저장소)는 curl 에 그대로 넣을 수 없다 —
# `:` 뒤를 포트로 읽는다. `gh repo rename` 이 remote 를 SSH 로 바꿔 놓기도 해서
# 여기서 HTTPS 로 맞춘다.
REPO_URL="${REPO_URL/git@github.com:/https://github.com/}"
TARBALL="$REPO_URL/archive/refs/tags/$TAG.tar.gz"

TMP="$(mktemp -d)"

SHA=""
for attempt in 1 2 3 4 5; do
  if curl -fsSL -o "$TMP/src.tar.gz" "$TARBALL"; then
    SHA="$(shasum -a 256 "$TMP/src.tar.gz" | awk '{print $1}')"
    break
  fi
  echo "  tarball 생성 대기… ($attempt/5)"
  sleep 3
done

if [[ -z "$SHA" ]]; then
  echo "tarball을 받지 못했다: $TARBALL" >&2
  echo "태그는 이미 올라갔으니, 잠시 뒤 formula만 손으로 갱신하면 된다." >&2
  exit 1
fi
echo "▸ sha256 ${SHA:0:16}…"

# ── 5. formula 갱신 ────────────────────────────────────────────
sed -i '' -E "s|^  url \".*\"$|  url \"$TARBALL\"|" "$REPO_ROOT"/Formula/dong-csu.rb
sed -i '' -E "s|^  sha256 \".*\"$|  sha256 \"$SHA\"|" "$REPO_ROOT"/Formula/dong-csu.rb

# 지난 버전의 bottle 블록을 지운다.
#
# 그냥 두면 root_url과 체크섬이 옛 태그를 가리킨 채로 남는다. 새 버전에는 그런
# bottle이 없으므로 Homebrew가 받으려다 404를 내고 **설치가 통째로 실패한다.**
# 지우면 bottle 워크플로가 새로 만들어 붙일 때까지 소스 빌드로 넘어간다 —
# 느릴 뿐 실패하지는 않는다.
awk '
  /^  bottle do$/ { skip = 1 }
  skip && /^  end$/ { skip = 0; next }
  !skip { print }
' "$REPO_ROOT"/Formula/dong-csu.rb > "$REPO_ROOT"/Formula/dong-csu.rb.tmp
mv "$REPO_ROOT"/Formula/dong-csu.rb.tmp "$REPO_ROOT"/Formula/dong-csu.rb
ruby -c "$REPO_ROOT/Formula/dong-csu.rb" >/dev/null

git add "$REPO_ROOT/Formula/dong-csu.rb"
git commit -q -m "📦 formula를 $TAG 로 갱신"
git push -q origin main
echo "▸ formula 갱신"

# ── 6. GitHub 릴리스 ───────────────────────────────────────────
if command -v gh >/dev/null 2>&1; then
  # 제목 형식을 바꾸지 마라. 저장소 뿌리 README 의 버전 배지가 제목으로 판을 가른다
  # (GitHub 은 "Latest" 를 하나만 띄울 수 있어서, 두 판을 나란히 보여줄 방법이 이것뿐이다).
  if gh release create "$TAG" --title "macOS $VERSION" --generate-notes >/dev/null 2>&1; then
    echo "▸ GitHub 릴리스 생성"
  else
    echo "▸ GitHub 릴리스 생성 실패 (수동: gh release create $TAG --generate-notes)"
  fi
else
  echo "▸ gh 없음 — GitHub 릴리스는 건너뜀"
fi

echo
echo "완료: $TAG"
echo "사용자 쪽 업데이트:  brew update && brew upgrade dong-csu"
