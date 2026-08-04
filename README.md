# dong-mcu

Claude 사용량을 화면 위에 항상 띄워두는 macOS 앱.

![dong-mcu HUD](docs/screenshot.png)

Claude Code를 쓰다 보면 "지금 한도를 얼마나 썼지?"가 계속 궁금해진다.
확인하려면 하던 걸 멈추고 `/usage`를 쳐야 한다. dong-mcu는 그 숫자를 화면 구석에 그냥 띄워둔다.

- **이중 링** — 바깥이 5시간 세션 한도, 안쪽이 7일 주간 한도
- **사용률과 초기화까지 남은 시간**을 각각 표시
- **다음 조회까지 남은 시간**을 초 단위로
- 사용률이 오를수록 링 색이 초록 → 노랑 → 빨강으로 연속해서 바뀐다
- 라이트·다크 테마, 크기(작게~매우 크게), 배경 불투명도, 펼침 방향을 고를 수 있다
- Dock 아이콘 없이 메뉴바에만 뜬다. 모든 Space와 전체화면 위에 떠 있다

서랍처럼 접으면 링과 버튼만 남는다.

![접은 모습](docs/collapsed.png)

## 설치

### Homebrew

설치부터 `/Applications` 등록, 실행까지 한 번에:

```bash
brew tap ldg030201/dong-mcu https://github.com/ldg030201/dong-mcu && brew trust ldg030201/dong-mcu && brew install dong-mcu && ln -sfn "$(brew --prefix dong-mcu)/DongMCU.app" /Applications/ && open /Applications/DongMCU.app
```

각 단계가 하는 일:

| 명령 | 하는 일 |
| --- | --- |
| `brew tap` | 이 저장소를 Homebrew가 아는 저장소 목록에 넣는다 |
| `brew trust` | 서드파티 tap의 formula 실행을 허용한다. Homebrew가 기본적으로 거부해서 필요하다 |
| `brew install` | 소스를 내려받아 빌드하고 `$(brew --prefix)/opt/dong-mcu/` 안에 넣는다 |
| `ln -sfn` | `/Applications`에 링크를 건다 |
| `open` | 실행한다 |

**`ln` 줄이 왜 필요한가** — Homebrew에는 두 종류가 있다. GUI 앱용 **cask**(`brew install --cask`)는
`/Applications`에 자동으로 설치하지만, 이건 소스를 빌드하는 **formula**다. formula는 Homebrew
디렉터리 밖에 파일을 쓰지 않는 게 규칙이라 `/Applications`를 건드리지 않는다. 대신 코드 서명·공증
없이도 설치되는 게 이 방식의 장점이다.

첫 실행 때 keychain 접근을 허용할지 한 번 묻는다.

#### 업데이트

```bash
brew update && brew upgrade dong-mcu
```

`/Applications` 링크는 `opt` 경로를 가리키므로 버전이 올라가도 그대로 유지된다.
앱 이름 자체가 바뀐 버전으로 올릴 때만 위의 `ln -sfn` 줄을 다시 실행하면 된다.

`brew --prefix dong-mcu`는 `$(brew --prefix)/opt/dong-mcu`와 같은 경로를 돌려준다.

#### 로그인할 때 자동으로 켜기

시스템 설정 → 일반 → 로그인 항목에서 `+`를 누르고 `/Applications/DongMCU.app`을 고른다.

#### 제거

```bash
rm -f /Applications/DongMCU.app && brew uninstall dong-mcu && brew untap ldg030201/dong-mcu
```

설정(창 위치·아이콘·크기)은 남는다. 그것까지 지우려면:

```bash
defaults delete com.ldg.dong-mcu
```

### 소스에서 빌드

```bash
git clone https://github.com/ldg030201/dong-mcu.git
cd dong-mcu
./build.sh
open build/DongMCU.app
```

Xcode는 필요 없다. Command Line Tools(`xcode-select --install`)만 있으면 된다.

## 필요한 것

- macOS 14 이상
- **Claude Code에 로그인된 상태**

두 번째가 핵심이다. 이 앱은 Claude Code가 keychain에 저장해 둔 OAuth 토큰을 읽어서
사용량 API를 호출한다. 토큰을 만드는 주체가 Claude Code라서, Claude Code 없이는 동작하지 않는다.

첫 실행 때 keychain 접근을 허용할지 묻는 창이 한 번 뜬다.

## 사용법

- **드래그**로 위치 이동. 모니터를 넘어 다닐 수 있고 위치는 기억된다
- **더블클릭**하거나 화살표 버튼을 누르면 서랍처럼 접힌다. 접으면 링과 버튼만 남는다
- **버튼 세 개** — 접기/펼치기, 설정(톱니), 새로고침. 접은 상태에서도 그대로 보인다
- **펼침 방향**을 설정에서 고를 수 있다. 오른쪽으로 펼치면 왼쪽 변을, 왼쪽으로 펼치면
  오른쪽 변을 붙잡고 늘어나며, 링·버튼·화살표 방향이 전부 그에 맞춰 뒤집힌다
- **메뉴바 아이콘**이나 **HUD 우클릭**으로 메뉴를 연다

```
Max · 세션 34% (3시간 11분 남음) · 주간 61% (1일 2시간 남음)
──────────
새로고침                    ⌘R
설정…                       ⌘,
Claude Code 재로그인…
접기 / 펼치기
HUD 숨기기
위치 초기화
가운데 아이콘  ▸  캐릭터 ─ 부엉이
                  Claude ─ Clawd / Claude 아이콘 / 버스트 마크
크기          ▸  작게 / 보통 / 크게 / 매우 크게
변경 내역…
테마          ▸  시스템 설정 따름 / 라이트 / 다크
──────────
DongMCU 종료                ⌘Q
```

설정 창(톱니 버튼 또는 `⌘,`)은 왼쪽 탭으로 나뉜다.

- **상태** — 사용량과 초기화까지 남은 시간, 마지막·다음 조회 시각, 새로고침
- **표시** — 테마, 크기, 조회 주기, 펼침 방향, 배경 불투명도, CPU·메모리 표시, HUD 표시·접기, 위치 초기화
- **아이콘** — 가운데 아이콘. 실제로 그려서 보여주고 고른다
- **계정** — Claude Code 재로그인
- **변경 내역** — 버전별로 무엇이 달라졌는지

아이콘 탭은 **캐릭터**와 **Claude** 두 묶음으로 나뉜다. 앞은 dong-mcu가 직접 만든
그림이고, 뒤는 Claude 쪽 그림이다.

설정 창은 가장자리를 끌어 크기를 조절할 수 있고, 줄이면 가로·세로 스크롤이 생긴다.

**이 앱 자신의 CPU·메모리**를 HUD 아래 줄에 표시하는 항목도 있다. 켜면 조회 카운트다운이
같은 줄로 내려와 나란히 놓인다. 꺼두면 표본을 뜨는 타이머도 돌지 않고, HUD를 접거나
숨겨도 자동으로 멈춘다. 메모리는 `phys_footprint` 기준이다. RSS는 공용 프레임워크
페이지까지 포함해서 실제보다 훨씬 크게 보인다.

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

조회 주기는 기본 10분이고 설정에서 1·3·5·10·30분 중에 고를 수 있다.
조회가 나갈 때마다 그 시점부터 다시 세므로, 수동으로 새로고침하면
다음 자동 조회도 10분 뒤로 밀린다. 화면이 꺼지면 멈추고, 켜지거나 절전에서 깨어나면
즉시 한 번 갱신한다. 429가 오면 최대 5분까지 백오프한다.

이 조회는 **모델을 부르지 않는다.** 계정 사용량만 읽는 엔드포인트라 토큰을 전혀 쓰지 않는다.

유휴 상태에서 이 앱이 쓰는 자원은 CPU 0.2% 안팎, 메모리 22MB(`phys_footprint`) 정도다.
CPU·메모리 표시를 켜면 2초마다 표본을 떠서 조금 더 쓴다.

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

개발 중에 띄우는 건 **`DongMCU-Test`** 라는 별개의 앱이다. 번들 ID가 `com.ldg.dong-mcu-test`로
달라서 설정·창 위치·메뉴바 자리를 정식판과 공유하지 않고, 둘을 동시에 띄워 비교할 수 있다.
메뉴바 아이콘은 테스트판만 몸 색이 보라색으로 나온다.

```bash
VARIANT=test ./build.sh    # DongMCU-Test.app  (dev.sh의 기본값)
./build.sh                 # DongMCU.app       (release.sh가 쓰는 정식 번들)
```

둘을 같이 띄우면 사용량 API도 각자 조회하니 요청이 두 배가 된다. 토큰은 쓰지 않지만
필요 없을 때는 한쪽을 종료해 두는 게 좋다.

앱을 띄우지 않고 HUD 모양만 PNG로 확인할 수도 있다.

```bash
./dev.sh render 94 71 owl                      # 사용률 94% / 71%
dong-mcu --render out.png 100 71 owl reauth    # 실패 상태까지 재현
dong-mcu --render out.png 34 61 owl ok large   # 크기 배율
dong-mcu --render-settings out.png icon        # 설정 창의 특정 탭
dong-mcu --render-menubar out.png 16           # 메뉴바 아이콘
dong-mcu --render-icon out.png 1024            # 앱 아이콘
```

### 마스코트와 앱 아이콘

가운데 기본 아이콘과 앱 아이콘은 dong-mcu 마스코트인 부엉이다.
[`OwlMark.swift`](Sources/DongMCU/OwlMark.swift)에 몸통·날개·눈·부리·발이 각각 문자열
그리드로 나뉘어 있고, `OwlPose`로 조합해서 그린다. 눈만 갈아끼우면 깜빡이고 날개
레이어만 바꾸면 펴진다.

HUD 링 안, 메뉴바, 앱 아이콘이 모두 같은 그리드를 쓴다. 한 칸이 정수 크기가 아니면
어떤 행은 2px, 어떤 행은 3px로 그려져 자리마다 다른 얼굴이 되므로, 칸 크기를 내려서
정수로 맞추고 남는 여백은 가운데로 몬다. 메뉴바(높이 16pt)에서는 한 칸이 1pt다.

HUD 크기 설정도 같은 이유로 `scaleEffect`가 아니라 치수와 글자 크기에 배율을 곱하는
방식이다. 확대 변환을 걸면 픽셀 그림이 흐려지지만, 배율을 곱해 다시 그리면 한 칸도
같이 커져서 큰 크기에서 오히려 더 선명해진다.

앱 아이콘 그림은 [`AppIconArt.swift`](Sources/DongMCU/AppIconArt.swift)에 있다. 고쳤으면
아래로 `.icns`를 다시 만든다. 결과물은 커밋해 두고 `build.sh`는 복사만 한다.

```bash
./make-icon.sh
```

에디터는 VS Code + [Swift 확장](https://marketplace.visualstudio.com/items?itemName=swiftlang.swift-vscode)
기준으로 `.vscode/`에 태스크를 넣어뒀다.

### 이름

화면에 보이는 앱 이름은 **DongMCU**이고, 번들 ID(`com.ldg.dong-mcu`)와 명령 이름
(`dong-mcu`), Homebrew tap은 예전 그대로다. 번들 ID를 바꾸면 UserDefaults 키가
달라져서 창 위치·아이콘·크기 설정이 전부 초기화되기 때문이다.

### 릴리스

버전은 Git 태그로 관리한다. Homebrew formula가 특정 태그의 tarball과 그 sha256을 가리키므로,
`main`에 미출시 커밋이 쌓여도 설치하는 사람은 마지막 태그를 받는다.

```bash
./release.sh 0.2.0
```

버전을 `Resources/Info.plist`와 `Sources/DongMCU/main.swift` 양쪽에 올리고, 빌드로 검증한 뒤
커밋·태그·푸시하고, 올라간 태그의 tarball 해시로 formula까지 갱신한다. `gh`가 있으면
GitHub 릴리스도 만든다. main이 아니거나 작업 트리가 지저분하거나 태그가 겹치면 중단한다.

버전은 `주.부.수` 세 자리를 쓰고, **이미 나간 버전을 급히 고칠 때만 네 번째 자리**를
붙인다(`1.0.0.1`). 내보내기 전에 [`Changelog.swift`](Sources/DongMCU/Changelog.swift)에
이번 버전 항목을 채운다. 설정 창의 변경 내역 탭이 그걸 그대로 보여준다.

## 라이선스

MIT. [LICENSE](LICENSE) 참고.

## Anthropic 관련 표기

이 프로젝트는 **Anthropic과 무관한 비공식 개인 도구**다.

Claude, Claude Code, Clawd 및 관련 로고·마스코트에 대한 저작권과 상표권은 전부
**Anthropic**에 있다. MIT 라이선스는 이 저장소의 코드에만 적용되며 Anthropic의
아트워크에는 적용되지 않는다. Anthropic 측에서 요청하면 해당 아트워크를 제거한다.
