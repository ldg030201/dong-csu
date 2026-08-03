# dong-mcu

Mac Claude UI — Claude 사용량을 화면에 띄워두는 macOS 메뉴 없는 오버레이 앱.

현재 구현: 오른쪽 위에 뜨는 사용량 HUD.
왼쪽에 이중 링(바깥 = 세션 한도, 안쪽 = 주간 한도)과 가운데 Claude 아이콘,
오른쪽에 세션·주간 사용률과 초기화까지 남은 시간,
오른쪽 아래에 다음 사용량 조회까지 남은 시간을 초 단위로 표시한다.

## 빌드 & 실행

```bash
./build.sh
open build/dong-mcu.app
```

## 개발

```bash
./dev.sh
```

`Sources/`, `Resources/`, `Package.swift`를 감시해서 저장할 때마다 재빌드 → 앱 재실행.
빌드가 깨지면 컴파일 에러만 추려서 보여주고 앱은 그대로 둔다. Ctrl+C로 감시만 끊고
앱은 계속 떠 있다.

| 명령 | 동작 |
|---|---|
| `./dev.sh` | 변경 감시 + 자동 재빌드·재실행 |
| `./dev.sh once` | 한 번만 빌드하고 실행 |
| `./dev.sh render 94 71 clawd` | HUD를 PNG로 렌더해서 열기(앱 안 띄움) |

렌더는 실패 상태도 재현할 수 있다.
`dong-mcu --render out.png <세션%> <주간%> <아이콘> <ok\|stale\|reauth>`

에디터는 **VS Code + `swiftlang.swift-vscode`** 확장 기준으로 맞춰뒀다(Xcode 불필요).
`.vscode/tasks.json`에 위 명령들이 태스크로 들어있어서 `Cmd+Shift+B`로 빌드·실행,
태스크 목록에서 렌더/`--probe`/앱 종료를 고를 수 있다.
필요한 것: `brew install fswatch`.

Xcode 없이 Command Line Tools + SwiftPM로 빌드하고, `.app` 번들은 `build.sh`가 직접 조립한다.
ad-hoc 서명(`codesign -s -`)이라 애플 개발자 계정은 필요 없다(로컬 실행 전용).

## 동작

- Dock 아이콘 없음(`LSUIElement`). 모든 Space와 전체화면 위에 떠 있다.
- **메뉴바에 Clawd 아이콘**이 뜬다. 아이콘이 보이면 실행 중이라는 뜻이고,
  클릭하면 사용량 요약 / 새로고침 / HUD 숨기기·보이기 / 위치 초기화 / 아이콘 선택 / 종료가 나온다.
  HUD를 숨겨도 여기로 다시 켤 수 있어서 Dock 아이콘 없는 앱의 고정 진입점이 된다.
  숨긴 상태는 기억되니 다음 실행에도 숨겨진 채로 시작한다.
- **드래그**로 위치 이동, 위치는 기억된다. 화면 밖으로 나가면 안쪽으로 되돌린다.
- **우클릭** → 메뉴바 아이콘과 같은 메뉴.
- **오른쪽 위 새로고침 버튼**. 평소엔 흐리게 있다가 마우스를 올리면 또렷해진다.
- **갱신에 실패하면** 링과 숫자가 흐려지고, 오른쪽 아래가 조회 카운트다운 대신
  `13시간 전 값`(마지막으로 성공한 시점) 또는 `재로그인 필요`로 바뀐다.
  새로고침 버튼도 노란색이 되고 툴팁에 실패 이유가 나온다.
  화면에 떠 있는 숫자가 지금 값이 아니라는 걸 숨기지 않기 위한 것이다.
- **마우스 올리면** 플랜과 사용률·초기화 시각이 툴팁으로 나온다.
- 링 색은 사용률에 따라 초록 → 라임 → 노랑 → 주황 → 빨강으로 연속 변화한다.
- 갱신이 실패하면(=화면 숫자가 오래된 값이면) 오른쪽 위에 작은 노란 점이 뜬다.

### 가운데 아이콘

기본값은 Claude Code 마스코트 **Clawd**. 우클릭 메뉴에서 세 가지로 전환된다.

| 스타일 | 내용 |
|---|---|
| `clawd` (기본) | Claude Code가 터미널에 그리는 블록 아트를 11×8 픽셀 그리드로 옮긴 것 |
| `appIcon` | 설치된 `/Applications/Claude.app`의 공식 아이콘을 런타임에 로드 |
| `mark` | 직접 그린 벡터 버스트 |

`appIcon`은 `Resources/claude-icon.png`를 두면 그 이미지가 우선한다.

Clawd 그리드와 색(`rgb(215,119,87)`)은 Claude Code 바이너리에 들어있는
`clawd_body` / `clawd_background` 정의와 블록 아트에서 그대로 가져왔다.
자세한 근거는 [ClawdMark.swift](Sources/DongMCU/ClawdMark.swift) 주석 참고.

## 사용량은 어디서 오나

Claude Code가 macOS 키체인(`Claude Code-credentials`)에 저장한 OAuth 토큰을 읽어
`GET https://api.anthropic.com/api/oauth/usage`를 호출한다. 응답의
`five_hour.utilization`(0~100)을 링에 그린다.

- 토큰은 `/usr/bin/security`로 읽는다. Apple 서명 고정 바이너리라 키체인에서 "항상 허용"을
  한 번 눌러두면 dong-mcu를 재빌드해도 권한이 유지된다.
- 첫 실행 때 키체인 접근 허용 프롬프트가 한 번 뜬다.
- 폴링 주기 600초(10분), 429가 나면 60초→최대 5분까지 백오프.
  화면이 꺼지면 멈추고, 켜지거나 절전에서 깨어나면 즉시 한 번 갱신한다.
- 조회가 나갈 때마다 주기를 그 시점부터 다시 센다. 수동 새로고침 직후에 타이머가
  곧바로 또 쏘지 않고, 오른쪽 아래 카운트다운도 실제 예정 시각과 어긋나지 않는다.

토큰 조회/API 응답만 따로 확인하려면:

```bash
.build/release/dong-mcu --probe
```

HUD 모양만 확인하려면(앱 안 띄우고 PNG로 렌더):

```bash
.build/release/dong-mcu --render /tmp/hud.png 94 71 appIcon
```

## 성능 / 전력

항상 떠 있는 앱이라 유휴 상태 비용을 낮추는 쪽으로 잡았다.

- **배경에 블러를 쓰지 않는다.** `NSVisualEffectView`의 `behindWindow` 블러는 창 뒤
  내용이 바뀔 때마다 WindowServer가 블러를 다시 합성한다. 항상 위에 떠 있는 창에서는
  이게 계속 돌아간다. 어두운 카드라 단색 반투명과 눈에 띄는 차이가 없어서 단색으로 갔다.
- **키체인 토큰을 메모리에 캐시한다.** 조회가 `/usr/bin/security` 프로세스를 띄우기
  때문에, 캐시가 없으면 폴링마다 프로세스를 하나 만든다. 토큰은 만료 전까지 재사용하고
  401/403이 오면 버린다.
- **주기 갱신 범위를 최소화한다.** 남은 시간 문구(60초)와 오른쪽 아래 조회 카운트다운(1초)만
  각각 `TimelineView`로 감싸고 링·아이콘은 밖에 뺐다. 조회 카운트다운은 HUD를 숨기면
  뷰 자체를 빼서 1초 타이머가 아예 돌지 않게 한다.
- **화면이 꺼지면 폴링을 멈춘다.** `screensDidSleep`에 타이머를 끄고
  `screensDidWake`에 다시 켜면서 즉시 갱신한다.
- **타이머에 큰 여유(tolerance)를 준다.** 다른 시스템 깨우기와 묶여서 처리된다.
- Claude 앱 아이콘 이미지는 한 번만 읽고 캐시한다(View body에서 호출되므로).

## 필요한 것

- macOS 14 이상
- Xcode Command Line Tools (`xcode-select --install`)
- **Claude Code에 로그인된 상태.** 이 앱은 Claude Code가 macOS 키체인에 저장한 OAuth
  토큰을 읽어서 사용량 API를 호출한다. 토큰을 만드는 주체가 Claude Code라서,
  Claude Code 없이는 동작하지 않는다.

`claude-hud`를 비롯한 다른 플러그인은 필요 없다. 외부 Swift 패키지 의존성도 없다.

토큰이 만료되면(수명 8시간) 화면 숫자가 흐려지고 오른쪽 아래에 `재로그인 필요`가 뜬다.
이 앱은 토큰을 스스로 갱신하지 않는다. 리프레시 토큰은 사용할 때 회전되는 경우가 많아,
우리가 먼저 쓰면 Claude Code 쪽 로그인이 풀릴 수 있기 때문이다.

## 앞으로 추가할 것

하나씩 붙일 예정 — 비용/토큰 통계, 세션 정보 등.

## 라이선스

MIT. [LICENSE](LICENSE) 참고.

## Claude / Anthropic 관련 표기

이 프로젝트는 **Anthropic과 무관한 비공식 개인 도구**다. 만든 사람은 Anthropic과 아무 관계가 없다.

Claude, Claude Code, Clawd 및 관련 로고·마스코트는 Anthropic의 자산이며, 그 권리는 전부
Anthropic에 있다. MIT 라이선스는 이 저장소의 코드에만 적용되고 Anthropic의 상표나
아트워크에는 적용되지 않는다.

- **Claude 앱 아이콘**(`appIcon` 스타일)은 이 저장소에 포함하지 않는다. 사용자 컴퓨터에
  이미 설치된 `/Applications/Claude.app`의 아이콘을 실행 중에 읽어올 뿐이다.
- **Clawd**(`clawd` 스타일, 기본값)는 Claude Code가 터미널에 그리는 블록 아트를 옮긴
  픽셀 그리드다. 이건 저장소에 포함되어 있다.

Anthropic 측에서 요청하면 해당 아트워크를 제거할 것.
