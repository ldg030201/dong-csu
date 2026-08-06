# Windows 판 작업 규칙

공통 규칙(버전 자릿수·변경 내역 문구·캐릭터·커밋)은 [`../CLAUDE.md`](../CLAUDE.md)에 있다.
여기에는 **윈도우에서만 걸리는 것**을 쓴다.

**모든 명령은 `win/` 안에서 돌린다.**

## 프로젝트가 셋인 이유

| | 플랫폼 | 무엇 |
| --- | --- | --- |
| `src/DongCSU.Core` | 어디서나 | 화면 없는 전부 — 부엉이 데이터·사용량 조회·설정·변경 내역 |
| `src/DongCSU.App` | **윈도우만** | WPF 창·트레이·그리기 |
| `tools/DongCSU.Tools` | 어디서나 | 파일 뽑는 작은 도구 |

**WPF 는 맥에서 컴파일되지 않는다.** 그래서 화면이 아닌 것은 전부 `Core` 로 민다 —
거기 있으면 맥에서 개발하면서 바로 테스트할 수 있고, `App` 에는 그리는 코드만 남는다.
**`Core` 에 UI 코드를 넣지 않는다.** 넣는 순간 그게 안 된다.

```bash
# 맥에서 (App 을 건드리지 않는다)
dotnet test tests/DongCSU.Core.Tests/DongCSU.Core.Tests.csproj

# 윈도우에서 (전부)
dotnet test && dotnet build src/DongCSU.App -c Release
```

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
DongCSU.exe --probe          # 자격 증명이 읽히는지, 사용량이 오는지
DongCSU.exe --probe-owl idle
```

`--print-owl` 은 **우리가 합성한 것**을 찍고 `owl.json` 에 실린 맥의 결과와 대조한다.
다르면 `⚠` 가 붙는다. 자세를 고칠 일이 있으면 이걸로 먼저 본다.

## 부엉이

**여기서 부엉이를 고치지 않는다.** 그림의 원본은 맥 소스이고 `shared/owl.json` 으로
넘어온다. 자세를 바꾸고 싶으면 맥 쪽 `OwlMark.swift` 를 고치고 `dump_owl` 을 돌린다.

레이어 겹치기(`OwlComposer`)만은 알고리즘이라 여기 옮겨 적혀 있다. **옮겨 적은 것은
언젠가 어긋나므로**, 테스트가 전 프레임을 `owl.json` 의 합성 결과와 글자 단위로 대조한다.
그 테스트가 깨지면 `OwlComposer` 가 틀린 것이다.

부엉이를 고쳤으면 **아이콘도 다시 만든다.** 아이콘은 손으로 그리지 않는다.

```bash
python3 make-icon.py
```

## 변경 내역

[`src/DongCSU.Core/Changelog.cs`](src/DongCSU.Core/Changelog.cs) 맨 위 항목에 한 줄
추가하고 JSON 을 다시 뽑는다. CI 가 소스와 다르면 실패시킨다.

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

태그를 밀면 `win-release` 워크플로가 만들어 릴리스에 올린다.

```bash
git tag win-v1.0.0 && git push origin win-v1.0.0
```

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
