# dong-mcu

Claude 사용량을 화면 위에 항상 띄워두는 macOS 앱.

![dong-mcu HUD](docs/screenshot.png)

Claude Code를 쓰다 보면 "지금 한도를 얼마나 썼지?"가 계속 궁금해진다.
확인하려면 하던 걸 멈추고 `/usage`를 쳐야 한다. dong-mcu는 그 숫자를 화면 구석에 그냥 띄워둔다.

- **이중 링** — 바깥이 5시간 세션 한도, 안쪽이 7일 주간 한도
- **사용률과 초기화까지 남은 시간**을 각각 표시
- **다음 조회까지 남은 시간**을 오른쪽 아래에 초 단위로
- 사용률이 오를수록 링 색이 초록 → 노랑 → 빨강으로 연속해서 바뀐다
- Dock 아이콘 없이 메뉴바에만 뜬다. 모든 Space와 전체화면 위에 떠 있다

## 설치

### Homebrew

```bash
brew tap ldg030201/dong-mcu https://github.com/ldg030201/dong-mcu
brew install dong-mcu
```

### 소스에서 빌드

```bash
git clone https://github.com/ldg030201/dong-mcu.git
cd dong-mcu
./build.sh
open build/dong-mcu.app
```

Xcode는 필요 없다. Command Line Tools(`xcode-select --install`)만 있으면 된다.

## 필요한 것

- macOS 14 이상
- **Claude Code에 로그인된 상태**

두 번째가 핵심이다. 이 앱은 Claude Code가 keychain에 저장해 둔 OAuth 토큰을 읽어서
사용량 API를 호출한다. 토큰을 만드는 주체가 Claude Code라서, Claude Code 없이는 동작하지 않는다.

첫 실행 때 keychain 접근을 허용할지 묻는 창이 한 번 뜬다.

## 사용법

- **드래그**로 위치 이동. 위치는 기억되고, 화면 밖으로 나가면 안쪽으로 되돌린다
- **오른쪽 위 버튼**으로 즉시 새로고침
- **메뉴바 아이콘**이나 **HUD 우클릭**으로 메뉴를 연다

```
Max · 세션 34% (3시간 11분 남음) · 주간 61% (1일 2시간 남음)
──────────
새로고침                    ⌘R
Claude Code 재로그인…
HUD 숨기기
위치 초기화
가운데 아이콘  ▸  Clawd / Claude 앱 아이콘 / 버스트 마크
──────────
dong-mcu 종료               ⌘Q
```

HUD를 숨겨도 메뉴바 아이콘으로 다시 켤 수 있다. 숨긴 상태는 기억된다.

## 사용량은 어디서 오나

keychain 항목 `Claude Code-credentials`에서 OAuth 토큰을 읽어
`GET https://api.anthropic.com/api/oauth/usage`를 호출하고, 응답의
`five_hour.utilization` / `seven_day.utilization`(각 0~100)을 링에 그린다.

- 토큰은 `/usr/bin/security`로 읽는다. Apple이 서명한 고정 바이너리라 keychain에서
  "항상 허용"을 한 번 눌러두면 앱을 다시 빌드해도 권한이 유지된다
- 토큰은 Authorization 헤더로만 쓰이고 **디스크에 쓰거나 로그에 남기지 않는다**
- Anthropic API 외에 아무 데도 접속하지 않는다. 통계·추적 없음
- 로컬에 저장하는 건 창 위치, 아이콘 선택, 숨김 여부뿐
- 외부 Swift 패키지 의존성 0개

조회 주기는 10분이다. 화면이 꺼지면 멈추고, 켜지거나 절전에서 깨어나면 즉시 한 번 갱신한다.
429가 오면 최대 5분까지 백오프한다.

### 토큰이 만료되면

Claude Code의 액세스 토큰은 수명이 8시간이라 종종 만료된다. 그러면 링과 숫자가 흐려지고
오른쪽 아래가 `재로그인 필요`로 바뀐다. **화면의 숫자가 지금 값이 아닐 때 그 사실을 숨기지 않는다.**
메뉴의 `Claude Code 재로그인…`을 누르면 터미널에서 로그인 플로우가 열린다.

이 앱은 토큰을 스스로 갱신하지 않는다. keychain에 리프레시 토큰이 같이 들어있지만,
리프레시 토큰은 사용할 때 회전되는 경우가 많아서 우리가 먼저 쓰면 Claude Code가 들고 있던
값이 무효가 되고 사용자의 Claude Code 로그인이 풀릴 수 있다. 갱신은 Claude Code에게 맡긴다.

## 개발

```bash
./dev.sh
```

`Sources/`를 감시해서 저장할 때마다 재빌드하고 앱을 다시 띄운다(`brew install fswatch` 필요).
빌드가 깨지면 컴파일 에러만 추려서 보여주고 앱은 그대로 둔다.

앱을 띄우지 않고 HUD 모양만 PNG로 확인할 수도 있다.

```bash
./dev.sh render 94 71 clawd          # 사용률 94% / 71%
dong-mcu --render out.png 100 71 clawd reauth   # 실패 상태까지 재현
```

에디터는 VS Code + [Swift 확장](https://marketplace.visualstudio.com/items?itemName=swiftlang.swift-vscode)
기준으로 `.vscode/`에 태스크를 넣어뒀다.

## 라이선스

MIT. [LICENSE](LICENSE) 참고.

## Anthropic 관련 표기

이 프로젝트는 **Anthropic과 무관한 비공식 개인 도구**다.

Claude, Claude Code, Clawd 및 관련 로고·마스코트에 대한 저작권과 상표권은 전부
**Anthropic**에 있다. MIT 라이선스는 이 저장소의 코드에만 적용되며 Anthropic의
아트워크에는 적용되지 않는다. Anthropic 측에서 요청하면 해당 아트워크를 제거한다.
