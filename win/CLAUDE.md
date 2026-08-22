# Windows 판 작업 규칙

공통 규칙(버전 자릿수·변경 내역 문구·캐릭터·커밋)은 [`../CLAUDE.md`](../CLAUDE.md)에 있다.
여기에는 **윈도우에서만 걸리는 것**을 쓴다.

**모든 명령은 `win/` 안에서 돌린다.**

## 테스트판과 정식판

```powershell
./build.ps1                    # 정식판   src/DongCSU.App/bin/Release/.../DongCSU.exe
./build.ps1 -Test              # 테스트판 build/test/DongCSU-Test.exe
./build.ps1 -Test -Run         # 만들고 바로 띄운다
./build.ps1 -Test -Shortcut    # 시작 메뉴에 바로가기까지 만든다
```

**개발 빌드는 검색해도 안 나온다.** 설치본이 아니라 폴더에 놓인 exe 라 시작 메뉴가 모른다.
게다가 이 앱은 트레이에만 뜨고 작업 표시줄에 안 나와서, 한 번 끄면 다시 켤 곳을 찾기 어렵다.
`-Shortcut` 이 그 바로가기를 만들어 준다. 정식판은 설치본이 알아서 만든다.

**개발은 테스트판으로 하고, 정식판은 받아서 업데이트가 되는지 확인한다.** 맥의
`VARIANT=test ./build.sh` 와 같은 자리다.

갈리는 값은 **어셈블리 이름 하나뿐**이고 나머지는 전부 거기서 파생된다.

| | 정식판 | 테스트판 |
| --- | --- | --- |
| 실행 파일 | `DongCSU.exe` | `DongCSU-Test.exe` |
| 설정·기록 | `%APPDATA%\DongCSU\` | `%APPDATA%\DongCSU-Test\` |
| 자동 시작 등록 이름 | `DongCSU` | `DongCSU-Test` |
| 자체 업데이트 | 함 | **안 함** (`AppInfo.IsTestBuild`) |
| 마스코트 · 버전 딱지 | 평소 색 | **보라** · `2.4.0 test` |

**갱신한 토큰(`token.json`)만은 함께 쓴다.** 서버가 갱신할 때마다 리프레시 토큰을
회전시켜서, 판마다 따로 두면 두 판이 서로의 토큰을 죽이고 둘 다 재로그인으로 떨어진다.
자격 증명은 앱의 상태가 아니라 사용자의 것이다 — 맥도 두 판이 같은 키체인 항목을 읽는다.

지금 이 실행이 어느 쪽 파일을 보는지는 `DongCSU.exe --where` 로 찍어 본다.

## 윈도우에서 작업한다면 — 먼저 읽어라

이 앱은 처음에 **맥에서 눈으로 한 번도 못 보고** 만들어졌다. WPF 가 맥에서 컴파일되지
않아 확인 수단이 CI 빌드와 테스트뿐이었기 때문이다. **2.0.0 에서 그 벽이 걷혔다** —
윈도우에서 띄워 보고 눌러 가며 고쳤다.

```powershell
./build.ps1 -Test -Run
```

### 눈으로 확인된 것 (2.0.0)

| | 결과 |
| --- | --- |
| HUD 배치 | 배율 0.85·1.0·1.25·1.5 전부에서 240×88 안에 들어간다. 두 줄로 나눈 뒤로 남은 시간이 안 잘린다 |
| 링 · 마스코트 | 0%·50%·100% 정상. 0%도 점만큼 남는다. 마스코트가 링 안에 들어간다 |
| 투명 배경·항상 위 | 창틀 없이 뜨고 드래그·더블클릭이 먹는다 |
| HUD 위 버튼 | 접기·설정·새로고침과 호버가 다 먹는다. `WS_EX_NOACTIVATE` 라도 마우스 메시지는 온다 |
| 우클릭 메뉴 | 트레이와 같은 메뉴가 뜬다 |
| 설정 창 | 여섯 탭, 다크·라이트, 최대화까지 확인 |
| 토큰 자동 갱신 | 만료된 토큰으로 조회 → 갱신 → 재조회까지 실제로 돈다 |

### 눈으로 확인된 것 (2.1.0)

| | 결과 |
| --- | --- |
| 펫 모드 | 드나들기·링 페이드·아래 버튼 둘·새 버전 표시가 다 먹는다 |
| 혼자 돌아다니기 | 걷고 쉬고, 글을 쓰면 그 자리에 선다 |
| 커서 피하기 | 마스코트 위·아래 버튼 줄 어디에 올려도 비킨다. 계속 올려 두면 계속 비킨다 |
| 끌기 · 흔들기 | 끄는 방향으로 몸이 처지고, 멈추면 매달려 선다. 흔드는 **도중에** 눈이 풀린다 |
| 지친 채로 걷기 | 세션 80% 넘은 상태에서 걸어도 눈꺼풀이 그대로다 |
| 보기 전환 | 펼침↔접힘은 7~8칸에 걸쳐 미끄러지고, 펫은 중간 크기 없이 곧바로 바뀐다 |
| 설정 창 배치 | 여섯 탭 전부에서 조작부가 설명 글을 안 가린다 |

**아직 안 본 것**: 자체 업데이트가 실제로 갈아 끼우고 다시 뜨는 것(설치본이 필요하다),
트레이 아이콘의 눈 깜빡임, 배율 100% 가 아닌 화면에서의 펫 모드.

### 확인 방법

화면에 관한 것은 **띄워서 눌러 보는 것이 정본**이다. 다만 배치·색·문구는 아래
"눈으로 확인하기" 의 `--render` 넷으로 먼저 본다 — 훨씬 빠르고 어긋나지 않는다.

개발을 통째로 이어받는 경우라면 [`docs/handoff.md`](docs/handoff.md) 에 시작 지점과
맥에서 옮겨 올 목록이 정리돼 있다.

## 프로젝트가 셋인 이유

| | 플랫폼 | 무엇 |
| --- | --- | --- |
| `src/DongCSU.Core` | 어디서나 | 화면 없는 전부 — 부엉이 데이터·사용량 조회·설정·변경 내역 |
| `src/DongCSU.App` | **윈도우만** | WPF 창·트레이·그리기 |
| `tools/DongCSU.Tools` | 어디서나 | 파일 뽑는 작은 도구 |

**WPF 는 맥에서 컴파일되지 않는다.** 그래서 화면이 아닌 것은 전부 `Core` 로 민다 —
거기 있으면 맥에서 개발하면서 바로 테스트할 수 있고, `App` 에는 그리는 코드만 남는다.
**`Core` 에 UI 코드를 넣지 않는다.** 넣는 순간 그게 안 된다.

윈도우에서 작업하더라도 이 경계를 지켜라. 맥 쪽에서 기능을 옮겨 올 때 화면 없는
부분을 먼저 `Core` 에 넣고 테스트로 굳혀 두면, 그 다음이 훨씬 수월하다.

```bash
# 맥에서 (App 을 건드리지 않는다)
dotnet test tests/DongCSU.Core.Tests/DongCSU.Core.Tests.csproj

# 윈도우에서 (전부)
dotnet test && dotnet build src/DongCSU.App -c Release
```

## 기록 파일

화면만 있는 앱은 조용히 실패한다. 무엇을 어디서 읽었고 무엇이 실패했는지 남긴다.

```
%APPDATA%\DongCSU\log.txt
```

설정 창 **계정** 탭의 **기록 열기** 버튼으로도 열린다. `DongCSU.exe --log` 로 찍어 볼
수도 있다. **토큰이나 자격 증명 내용은 절대 남기지 않는다** — 경로와 성공·실패만 적는다.

## 눈으로 확인하기

앱을 띄우지 않고 확인하는 통로다. **CI 가 이걸 부른다** — 화면을 볼 수 없는 곳에서
앱이 멀쩡한지 알아내는 유일한 방법이다.

```bash
# 어디서나 (도구)
dotnet run --project tools/DongCSU.Tools -- --dump-changelog docs/changelog.json
dotnet run --project tools/DongCSU.Tools -- --dump-owl       out.json
dotnet run --project tools/DongCSU.Tools -- --print-owl      idle    # 글자로 찍어 본다

# 윈도우에서 (앱)
DongCSU.exe --version
DongCSU.exe --where          # 어느 판인지, 설정·기록·토큰이 어느 폴더인지
DongCSU.exe --probe          # 자격 증명이 읽히는지, 사용량이 오는지
DongCSU.exe --probe-layout   # 탭마다 세로로 몇 pt 인지, 가로로 잘리는 곳은 없는지
DongCSU.exe --probe-mascot   # 시트가 어떻게 구워져 있는지 재서 칸 표와 대조
DongCSU.exe --probe-owl idle
DongCSU.exe --probe-tokens   # 최근 몇 분(기본 30) 동안 Claude Code 가 쓴 토큰. 처음부터 훑는다
DongCSU.exe --probe-meter    # 저장된 측정 상태 · selftest 계산 검사 · scan 실제로 훑어 얹기
DongCSU.exe --log            # 기록 파일 내용

# 화면을 PNG 로 (창을 안 띄운다)
DongCSU.exe --render out.png 34 61 expanded owlsheet normal dark
DongCSU.exe --render-settings out.png version 760x760 light
DongCSU.exe --render-owl out.png 64
DongCSU.exe --render-menubar out.png 32 6   # 트레이 아이콘. 기분마다 한 줄, 프레임마다 한 칸
```

**측정 둘(`--probe-tokens` · `--probe-meter`)은 CI 에 넣지 않는다.** 빌드 기계에는
Claude Code 기록 폴더도 `meter.json` 도 없어서 늘 1로 떨어진다. 리셋 넘김 계산이 맞는지는
`MeterSelfTest` 를 부르는 `dotnet test` 가 이미 본다 — `--probe-meter selftest` 는 **같은
표**를 사람이 눈으로 보는 통로다. `scan` 은 사용자의 진짜 `meter.json` 에 쓴다(그게 목적이다 —
두 번 연달아 부르면 두 번째는 0 이 더해져야 증분 읽기가 맞는 것이다).

**화면에 관한 것은 이 넷으로 먼저 본다.** 앱을 띄우고 창을 찾아 마우스를 옮겨 가며
찍는 것은 느리고 잘 어긋난다 — 설정 창이 다른 창 뒤에 깔리면 엉뚱한 것이 찍힌다.
실제로 그렇게 두 번 헛돌았다.

**문서 그림도 여기서 뽑는다**(`docs/images/`). 테스트 바이너리로 뽑아도 **정식판 색**이
나온다 — 렌더 통로가 `MascotRenderer.TestLook = false` 를 꽂는다. 안 꽂으면 문서의
마스코트가 전부 보라색이 되고 버전 딱지에 `test` 가 붙는다.

> **`VisualBrush` 로 뷰를 찍지 마라.** 기본이 `Stretch.Fill` 이라 **뷰의 내용 경계**를
> 대상 사각형에 맞춰 늘린다. 펫처럼 배경이 없는 보기에서는 경계가 마스코트만큼
> 줄어들어 **그림이 확대된 채로 찍힌다.** `RenderProbe` 는 `RenderTargetBitmap` 을 쓴다.

`--print-owl` 은 **우리가 합성한 것**을 찍고 `owl.json` 에 실린 맥의 결과와 대조한다.
다르면 `⚠` 가 붙는다. 자세를 고칠 일이 있으면 이걸로 먼저 본다.

> 이 앱은 `WinExe` 라 콘솔이 없다. 진단 통로들은 부모 터미널에 **직접 붙어서** 찍는다
> (`Diagnostics.AttachToConsole`). 그 처리를 빼면 PowerShell 에서 실행해도 아무것도
> 안 보인다 — 1.1.0 에서 실제로 그랬다.

### 스크립트에서 부를 때 걸리는 것 둘

`WinExe` 라서 생기는 함정이고 **둘 다 실제로 CI 를 빨갛게 만들었다.**

**하나 — `& $exe` 로 부르면 종료 코드를 못 받는다.** PowerShell 은 창 프로그램이
끝나기를 기다리지 않아서 `$LASTEXITCODE` 가 아예 안 채워진다(`$null`). `$null -ne 0`
이 참이라 **통과했는데도 실패로 떨어진다.** `Start-Process -Wait -PassThru` 로 부르고
`.ExitCode` 를 봐라.

```powershell
$run = Start-Process -FilePath $exe -ArgumentList '--probe-mascot' `
  -Wait -PassThru -NoNewWindow -RedirectStandardOutput $log
Get-Content $log -Encoding UTF8
if ($run.ExitCode -ne 0) { exit 1 }
```

**둘 — 출력을 파이프로 받으면 사라질 수 있었다.** 붙을 부모 콘솔이 없으면
`AllocConsole()` 이 새 콘솔을 만들고 **그것이 표준 핸들을 덮어써서**, 찍은 것이 아무도
안 보는 콘솔로 간다. 지금은 `Console.IsOutputRedirected` 면 아예 안 붙고 UTF-8 로
못 박는다 — 안 그러면 코드페이지 949 로 나가서 한글이 깨진다.

## 부엉이

**여기서 부엉이를 고치지 않는다.** 그림의 원본은 맥 소스이고 `shared/owl.json` 으로
넘어온다. 자세를 바꾸고 싶으면 맥 쪽 `OwlMark.swift` 를 고치고 `dump_owl` 을 돌린다.

알고리즘인 것만 여기 옮겨 적혀 있다. 둘이다.

| | 어디 |
| --- | --- |
| 레이어 겹치기 | `OwlComposer` |
| 자세 만들기 — 걷기·매달림·눈 깜빡임 | `OwlAnimator` (`GaitPose`·`CarriedPose`·`Wings`·`BlinkingEyes`) |

자세 쪽에는 맥에서 옮겨 온 숫자가 같이 있다 — 처지는 속도 140, 날개 200·620,
걷는 중 깜빡임 36±20, 다리 네 칸 주기. **맥의 `gaitPose`·`wings(for:)`·`blinkingEyes` 를
고쳤으면 여기도 같이 고쳐야 한다.** `dump_owl` 만 다시 돌려서는 안 옮겨진다.

**옮겨 적은 것은 언젠가 어긋나므로**, 테스트가 그 결과를 `owl.json` 에 실린 맥의 합성
결과와 글자 단위로 대조한다 — 전 프레임(`OwlComposerTests`), 걷기 네 칸과 매달림 여섯 칸
(`OwlGaitTests`·`DizzyDragTests`). 그 테스트가 깨지면 옮겨 적은 쪽이 틀린 것이다.

부엉이를 고쳤으면 **아이콘도 다시 만든다.** 아이콘은 손으로 그리지 않는다.

```bash
python3 make-icon.py
```

## 그림 마스코트 (시트)

HUD·펫에 실제로 그려지는 것은 격자 부엉이가 아니라 **그림 한 장**이다.
`mascot.png` 는 사본을 두지 않고 **맥 번들에 들어가는 파일을 그대로 박는다**
(`DongCSU.App.csproj` 의 `EmbeddedResource`). 그래서 **맥에서 그림을 바꾸면 다음
빌드부터 윈도우도 그 그림이 된다** — 옮겨 적을 것이 없고, 대신 **릴리스를 안 내면
사용자 화면은 안 바뀐다.**

**칸 표(`MascotSheet`)는 맥 `MascotSprite.swift` 를 손으로 옮겨 적은 것이다.**
배치 · 대체 사슬 · 눈감음 짝 · `Anchor` · `GripDepth` 가 전부 거기서 왔다.
부엉이 데이터와 달리 **파일로 넘어오지 않으므로 맥에서 고치면 여기도 같이 고쳐야 한다.**

```bash
DongCSU.exe --probe-mascot
```

시트를 실제로 재서 칸 표와 맞대 본다. **CI 가 이걸 부른다**(`build-win.yml`) —
옮겨 적은 것이 어긋나면 여기서 걸린다.

### 그릴 때 칸마다 자리를 다시 잡지 않는다

맥은 자를 때 **모든 칸을 공통 상자 하나로 함께 잘라내고**(`trimTogether`) 그릴 때는
그 상자에만 배율을 매긴다. 칸에 구워 둔 상대 위치가 그대로 남는다는 뜻이다.
`MascotRenderer` 도 같은 셈을 한다.

**한동안 칸마다 바닥·머리를 다시 맞췄고 그건 틀렸다.** 서 있는 자세끼리는 같은 답이
나와서(시트가 이미 그렇게 구워져 있다) 안 드러났지만, 매달린 칸은 칸 위쪽에 벽붙기
칸은 왼쪽 끝에 그려져 있어서 그것을 바닥·가운데로 끌어내렸다 — **벽붙기가 39px
밀렸다.** 배율 기준도 칸(256)이라 맥(공통 상자 253)보다 **1.2% 작게** 그렸다.

`Anchor` 는 **그릴 때 쓰는 값이 아니라 시트가 그렇게 구워져 있다는 사실**이다.
그리는 쪽은 칸을 그대로 옮기기만 하고, 그 사실이 맞는지는 `--probe-mascot` 이 본다.

**`KeepsLift` · `CentersOnHead` 는 이제 그리는 데 안 쓴다.** 맥에서 그것은 시트를
**구울 때**(`SheetPrep`) 자리를 잡는 값이고, 우리는 아직 굽지 않는다 — 남겨 둔 것은
나중에 사용자 그림을 받게 될 때 필요해서다.

## 변경 내역

[`src/DongCSU.Core/Changelog.cs`](src/DongCSU.Core/Changelog.cs) 맨 위 항목에 한 줄
추가하고 JSON 을 다시 뽑는다. CI 가 소스와 다르면 실패시킨다.

**2.3.0 부터 `Groups` 를 쓴다.** 기능 단위로 묶고(`ChangelogGroup`) 항목마다 갈래
(`ChangelogNote.New/Improve/Change/Fix/Remove`)를 단다. 문구 규칙은 뿌리
[`../CLAUDE.md`](../CLAUDE.md) 의 "묶음과 갈래" 절에 있다.

- **설정 탭 이야기면 `Tab` 에 그 탭 키를 적는다** — 제목 앞에 사이드바와 **같은 아이콘**이
  나온다. 아이콘 이름을 여기 적지 않는 이유가 그거다. 표는 [`Settings/TabIcon.cs`](src/DongCSU.App/Settings/TabIcon.cs)
  한 곳뿐이고, 탭에 없는 것(마스코트·HUD·설치)은 비워 두면 공통 아이콘으로 묶인다
- **평평한 `Notes` 를 손으로 적지 않는다.** `Groups` 에서 뽑아 낸다 — 두 곳에 적으면
  반드시 어긋난다. 2.2.0 이하 앱이 같은 JSON 을 받아보는데 그쪽은 `notes` 만 읽는다
- **이미 나간 버전은 뒤늦게 나누지 않는다.** 사용자가 그때 본 것과 달라진다.
  2.2.0 이하는 평평한 목록 그대로 둔다 (`ChangelogGroupTests` 가 검사한다)

```bash
dotnet run --project tools/DongCSU.Tools -- --dump-changelog docs/changelog.json
```

**맥과 번호를 맞추지 않는다.** 태그는 `win-v1.1.0` 처럼 붙인다.

### 네 번째 자리를 쓸 수 없다

공통 규칙에는 긴급 수정용 네 번째 자리가 있지만 **윈도우에서는 못 쓴다.** 설치본이
NuGet 패키지 형식이고 거기 버전은 SemVer2 세 자리여야 한다.

```
--packVersion contains an invalid package version '1.0.0.1':
it must be a 3-part SemVer2 compliant version string.
```

빠져나갈 길을 찾아봤지만 없다.

| | 왜 안 되나 |
| --- | --- |
| `1.0.0+1` | SemVer 는 **빌드 꼬리표를 순서 비교에서 무시한다.** 1.0.0 과 같은 버전으로 봐서 업데이트가 안 걸린다 |
| `1.0.1-hotfix.1` | 순서는 맞는데 `GithubSource(prerelease: false)` 가 걸러낸다. 켜면 진짜 프리릴리스까지 딸려온다 |
| 화면 따로 · 패키지 따로 | 같은 걸 두 이름으로 부르게 된다 |

**그래서 윈도우는 긴급 수정도 세 번째 자리로 올린다.** 어차피 판마다 번호를 따로
세므로 맥과 갈려도 된다.

## 배포

**버전 자리를 정하거나 태그를 붙이기 전에 반드시 사용자에게 물어본다.** 코드가 다
되고 검사를 전부 통과해도 릴리스는 별개다. 되돌리기가 비싸다 — 태그·릴리스·자체
업데이트 피드가 한꺼번에 나가고, 누가 하나라도 받아 가면 그 번호는 영영 고정된다.

태그를 밀면 `win-release` 워크플로가 만들어 릴리스에 올린다.

```bash
git tag win-v1.0.0 && git push origin win-v1.0.0
```

릴리스에 올라가는 것 중 **`.nupkg` 와 `releases.win.json` 은 빼면 안 된다** —
앞의 것이 자체 업데이트가 실제로 받아 가는 파일이고, 뒤의 것이 새 버전을 찾는 목록이다.
Portable zip 은 설치본과 내용이 같아서 올리지 않는다.

Velopack 이 설치 exe 와 델타를 만든다. **사용자 폴더에 깔려서 관리자 권한을 묻지 않고**,
앱이 GitHub 릴리스를 보고 스스로 업데이트한다. 맥처럼 터미널을 띄우지 않는다.

버전은 `src/DongCSU.App/DongCSU.App.csproj` 의 `<Version>` 한 곳에만 적는다 —
`AppInfo.Version` 이 어셈블리에서 읽으므로 소스에 또 적으면 한쪽을 빠뜨린다.

## 로그인할 때 자동 시작

레지스트리 `HKCU\...\CurrentVersion\Run` 이다. **HKCU 라 권한을 묻지 않는다.**

맥판과 같은 이유로 값을 따로 저장하지 않는다 — 사용자가 작업 관리자에서 끌 수 있어서,
우리가 적어 두면 껐는데도 켜진 것으로 보인다. 항상 레지스트리를 읽는다.

**업데이트하면 앱 경로가 바뀐다.** 뜰 때마다 `StartupService.RepairIfEnabled()` 로
경로를 맞춘다. 빠뜨리면 업데이트한 뒤로 로그인해도 아무것도 안 뜬다.

## 그릴 때 걸리는 것

- **한 칸은 정수 크기로.** 나누어떨어지지 않게 그리면 어떤 행은 2px, 어떤 행은 3px가
  되어 자리마다 다른 얼굴이 된다. `OwlRenderer.CellSize` 가 내림한다
- `RenderOptions.SetEdgeMode(this, EdgeMode.Aliased)` — 픽셀 아트라 부드럽게 하면 뭉개진다
- **링 100%는 `ArcTo` 한 번으로 못 그린다.** 시작점과 끝점이 같아서 아무것도 안 그려진다.
  반 바퀴씩 두 번 그린다
- 프레임 타이머는 **반복이 아니라 한 번씩** 건다. 프레임마다 시간이 다르고(눈 깜빡임
  0.05초, 평소 2초) 흔들림도 붙는다. 프레임이 하나뿐인 기분(끊김)에서는 아예 안 건다 —
  0초 타이머를 걸면 쉬지 않고 도는 루프가 된다

## XAML 을 쓰지 않는다

창이 몇 개 안 되는데 XAML 을 끼우면 코드와 마크업 두 곳을 맞춰야 하고, 빌드해 보지
않으면 어긋난 걸 알 수 없다. **맥에서 개발하면 빌드해 볼 수 없다.** 전부 C# 으로 짠다.
