# dong-mcu

Mac Claude UI — Claude 사용량을 화면에 띄워두는 macOS 메뉴 없는 오버레이 앱.

현재 구현: **오른쪽 위에 뜨는 원형 사용량 게이지(5시간 세션 사용률)** 하나.

## 빌드 & 실행

```bash
./build.sh
open build/dong-mcu.app
```

Xcode 없이 Command Line Tools + SwiftPM로 빌드하고, `.app` 번들은 `build.sh`가 직접 조립한다.
ad-hoc 서명(`codesign -s -`)이라 애플 개발자 계정은 필요 없다(로컬 실행 전용).

## 동작

- Dock 아이콘 없음(`LSUIElement`). 모든 Space와 전체화면 위에 떠 있다.
- **드래그**로 위치 이동, 위치는 기억된다.
- **우클릭** → 새로고침 / 위치 초기화 / 종료.
- **마우스 올리면** 플랜, 5시간·7일 사용률, 초기화 남은 시간이 툴팁으로 나온다.
- 색: 50% 미만 초록 / 50~80% 주황 / 80% 이상 빨강.

## 사용량은 어디서 오나

Claude Code가 macOS 키체인(`Claude Code-credentials`)에 저장한 OAuth 토큰을 읽어
`GET https://api.anthropic.com/api/oauth/usage`를 호출한다. 응답의
`five_hour.utilization`(0~100)을 링에 그린다.

- 토큰은 `/usr/bin/security`로 읽는다. Apple 서명 고정 바이너리라 키체인에서 "항상 허용"을
  한 번 눌러두면 dong-mcu를 재빌드해도 권한이 유지된다.
- 첫 실행 때 키체인 접근 허용 프롬프트가 한 번 뜬다.
- 폴링 주기 120초, 429가 나면 60초→최대 5분까지 백오프.

토큰 조회/API 응답만 따로 확인하려면:

```bash
.build/release/dong-mcu --probe
```

## 앞으로 추가할 것

하나씩 붙일 예정 — 7일 사용량, 비용/토큰 통계, 세션 정보 등.
