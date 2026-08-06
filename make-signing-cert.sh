#!/usr/bin/env bash
# 로컬 코드 서명 인증서를 만든다. **한 번만** 실행하면 된다.
#
# 왜 필요한가:
#   손쉬운 사용(Accessibility) 권한은 **코드 서명 신원**에 걸린다. ad-hoc 서명
#   (`codesign --sign -`)에는 신원이 없어서 macOS는 바이너리 해시(cdhash)로 앱을
#   알아보는데, 그 해시는 코드가 바뀔 때마다 달라진다. 그래서 앱을 업데이트하거나
#   다시 빌드하면 **허용해 둔 권한이 매번 풀린다.**
#
#   자체 서명 인증서를 하나 두면 신원이 고정돼서, 몇 번을 다시 빌드해도 macOS가
#   같은 앱으로 본다. 인증서는 이 기계 밖으로 나가지 않는다 — brew도 각자 기계에서
#   소스를 빌드하므로 배포에는 영향이 없다.
#
# 지우고 싶으면:
#   security delete-certificate -c "DongCSU Local Signing" ~/Library/Keychains/login.keychain-db
#   (지우면 build.sh가 다시 ad-hoc 서명으로 떨어진다)
set -euo pipefail

source "$(dirname "$0")/lib.sh"

KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"

# codesign이 개인키를 꺼내 쓸 수 있게 키 접근 목록에 넣는다.
#
# **이 단계가 없으면 codesign이 `errSecInternalComponent`로 죽는다.** 키를 넣기만
# 해서는 안 되고, 어떤 도구가 그 키를 쓸 수 있는지 따로 적어 줘야 한다.
# 여기서 묻는 암호는 **로그인 암호**다. 화면에 찍히지 않고, 이 스크립트는 그 값을
# 어디에도 저장하지 않는다 — `security`가 직접 받아서 키체인을 연다.
allow_codesign() {
  echo "codesign이 키를 쓸 수 있게 여는 중…"
  echo "  로그인 암호를 물어본다. 입력해도 화면에는 안 보인다."
  security set-key-partition-list \
    -S apple-tool:,apple:,codesign: \
    -s -l "$SIGN_CERT_NAME" "$KEYCHAIN" >/dev/null
}

if security find-certificate -c "$SIGN_CERT_NAME" "$KEYCHAIN" >/dev/null 2>&1; then
  echo "인증서는 이미 있다: $SIGN_CERT_NAME"
  # 접근 목록만 어긋난 상태일 수 있다. 그것만 다시 잡아 준다.
  allow_codesign
  echo "지금 서명 신원: $(sign_identity)"
  exit 0
fi

WORK="$(mktemp -d)"
# 개인키가 담긴 파일이 남으면 안 된다. 어떻게 끝나든 지운다.
trap 'rm -rf "$WORK"' EXIT

echo "인증서를 만드는 중…"
openssl req -x509 -newkey rsa:2048 -sha256 -days 3650 -nodes \
  -keyout "$WORK/key.pem" -out "$WORK/cert.pem" \
  -subj "/CN=$SIGN_CERT_NAME" \
  -addext "basicConstraints=critical,CA:false" \
  -addext "keyUsage=critical,digitalSignature" \
  -addext "extendedKeyUsage=critical,codeSigning" \
  2>/dev/null

openssl pkcs12 -export -inkey "$WORK/key.pem" -in "$WORK/cert.pem" \
  -out "$WORK/bundle.p12" -name "$SIGN_CERT_NAME" -passout pass:dongmcu

# -T 로 codesign에 미리 접근을 열어 두지 않으면, 빌드할 때마다 키체인 암호를 묻는다.
echo "키체인에 넣는 중… (암호를 물으면 로그인 암호를 넣어라)"
security import "$WORK/bundle.p12" -k "$KEYCHAIN" -P dongmcu -T /usr/bin/codesign -A

# 자체 서명 루트라 그대로 두면 codesign이 신뢰 사슬을 못 만든다.
# 이 인증서에 한해 **코드 서명 용도로만** 신뢰한다고 표시한다.
echo "코드 서명 용도로 신뢰 설정 중… (암호를 한 번 더 물을 수 있다)"
security add-trusted-cert -r trustRoot -p codeSign -k "$KEYCHAIN" "$WORK/cert.pem"

allow_codesign

echo
echo "끝났다. 지금 서명 신원: $(sign_identity)"
echo "이제 다시 빌드해도 손쉬운 사용 권한이 풀리지 않는다."
echo "확인: dong-csu --probe-accessibility"
