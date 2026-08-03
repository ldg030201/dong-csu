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

ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

VERSION="${1:-}"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "사용법: ./release.sh <버전>    예: ./release.sh 0.2.0" >&2
  exit 1
fi
TAG="v$VERSION"

COMMITTED=0
restore_version_files() {
  if [[ "$COMMITTED" == "0" ]]; then
    git checkout -- Resources/Info.plist Sources/DongMCU/main.swift 2>/dev/null || true
    echo "→ 버전 변경을 되돌렸다." >&2
  fi
}
trap restore_version_files ERR

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

echo "▸ $TAG 준비"

# ── 2. 버전 올리고 빌드로 검증 ──────────────────────────────────
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" Resources/Info.plist
sed -i '' "s/^let dongMCUVersion = \".*\"$/let dongMCUVersion = \"$VERSION\"/" \
  Sources/DongMCU/main.swift

./build.sh >/dev/null
BUILT="$(./build/dong-mcu.app/Contents/MacOS/dong-mcu --version | awk '{print $2}')"
if [[ "$BUILT" != "$VERSION" ]]; then
  echo "빌드 결과 버전이 다르다: $BUILT (기대: $VERSION)" >&2
  exit 1
fi
echo "▸ 빌드 확인 ($BUILT)"

# ── 3. 커밋 · 태그 · 푸시 ───────────────────────────────────────
git add Resources/Info.plist Sources/DongMCU/main.swift
git commit -q -m "🔖 $TAG"
COMMITTED=1
git tag -a "$TAG" -m "$TAG"
git push -q origin main "$TAG"
echo "▸ 태그 푸시"

# ── 4. tarball 해시 ────────────────────────────────────────────
REPO_URL="$(git remote get-url origin)"
REPO_URL="${REPO_URL%.git}"
TARBALL="$REPO_URL/archive/refs/tags/$TAG.tar.gz"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

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
sed -i '' -E "s|^  url \".*\"$|  url \"$TARBALL\"|" Formula/dong-mcu.rb
sed -i '' -E "s|^  sha256 \".*\"$|  sha256 \"$SHA\"|" Formula/dong-mcu.rb
git add Formula/dong-mcu.rb
git commit -q -m "📦 formula를 $TAG 로 갱신"
git push -q origin main
echo "▸ formula 갱신"

# ── 6. GitHub 릴리스 ───────────────────────────────────────────
if command -v gh >/dev/null 2>&1; then
  if gh release create "$TAG" --title "$TAG" --generate-notes >/dev/null 2>&1; then
    echo "▸ GitHub 릴리스 생성"
  else
    echo "▸ GitHub 릴리스 생성 실패 (수동: gh release create $TAG --generate-notes)"
  fi
else
  echo "▸ gh 없음 — GitHub 릴리스는 건너뜀"
fi

echo
echo "완료: $TAG"
echo "사용자 쪽 업데이트:  brew update && brew upgrade dong-mcu"
