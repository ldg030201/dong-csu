# 개발

[← README](../README.md) · 작업 규칙은 [`CLAUDE.md`](../CLAUDE.md)

플랫폼별 자세한 내용은 [`mac/docs/development.md`](../mac/docs/development.md) 와
[`win/CLAUDE.md`](../win/CLAUDE.md) 에 있다. 여기에는 **두 판에 다 걸리는 개발 방식**만 쓴다.

## 앱을 둘로 나눠 개발한다

**테스트판과 정식판이 별개의 앱으로 동시에 떠 있다.**

| | 정식판 | 테스트판 |
| --- | --- | --- |
| 화면에 보이는 이름 | `DongCSU` | `DongCSU-Test` |
| 번들 ID | `com.ldg.dong-csu` | `com.ldg.dong-csu-test` |
| 어디서 오나 | Homebrew 로 설치 | 소스에서 직접 빌드 |
| 누가 쓰나 | 사용자 (그리고 나) | 개발 중인 것 |

**번들 ID가 다르다는 게 핵심이다.** macOS 는 설정(UserDefaults)·창 위치·메뉴바 자리를
번들 ID 로 가르기 때문에, 둘이 서로의 설정을 건드리지 않는다. 그래서 **쓰던 앱을 켜 둔
채로** 개발할 수 있다 — 고치는 동안 사용량 표시가 끊기지 않는다.

한눈에 구분되도록 테스트판은 보이는 것도 다르다.

- **메뉴바 아이콘과 마스코트가 보라색이다.** 펫 모드에는 글자를 붙일 자리가 없어서
  색이 유일한 단서다
- **HUD 왼쪽 위 버전 딱지에 `test` 가 붙는다** (`2.1.2 test`)
- **새 버전을 확인하지 않는다.** 설정 창 버전 탭에서 업데이트 버튼도 잠긴다 —
  brew 가 설치한 게 아니라서 눌러도 자기를 갈아 끼울 수 없다

## 테스트판 만들고 띄우기

```bash
cd mac
VARIANT=test ./build.sh                 # build/DongCSU-Test.app
pkill -f DongCSU-Test; open build/DongCSU-Test.app
```

**빌드했다고 화면이 바뀌지 않는다.** 이미 떠 있는 앱은 옛 바이너리 그대로라 반드시
다시 띄워야 한다. 파일이 바뀔 때마다 저절로 다시 띄우려면:

```bash
cd mac && ./dev.sh                      # 감시하며 자동 재실행
./dev.sh once                           # 한 번만
```

`dev.sh` 는 `VARIANT=test` 를 기본으로 쓴다. 따로 지정할 필요가 없다.

### 정식판을 직접 만들지 않는다

`./build.sh` 를 그냥 돌리면 `build/DongCSU.app` (정식 번들)이 나온다. **개발 중에는
만들지 마라.** 사용자가 brew 로 설치해 쓰는 배포본이 바뀐 것처럼 보여서, 무엇이
실제로 나가 있는지 헷갈리게 된다. 정식 번들은 `release.sh` 가 릴리스할 때만 만든다.

실수로 만들었으면 지운다.

```bash
rm -rf mac/build/DongCSU.app
```

## 화면을 안 띄우고 확인하기

앱을 켜서 눈으로 보는 것 말고, **PNG 로 뽑거나 글자로 찍어서** 확인하는 통로가 있다.
CI 도 이걸 부른다 — 화면이 없는 곳에서 앱이 멀쩡한지 알아내는 유일한 방법이다.

```bash
# 맥
dong-csu --render out.png 34 61 owl ok large   # HUD
dong-csu --render-settings out.png display     # 설정 창의 한 탭
dong-csu --render-owl out.png 96               # 부엉이 전 프레임

# 윈도우
DongCSU.exe --probe                            # 자격 증명·조회
DongCSU.exe --probe-owl idle                   # 부엉이를 글자로
DongCSU.exe --log                              # 기록 파일
```

**화면에 영향을 주는 변경은 이걸로 먼저 본다.** 앱을 띄우고 상태를 만들어 내는 것보다
빠르고, 결과가 파일로 남아서 전후 비교가 된다.

> 오프스크린 렌더는 토글·피커 같은 AppKit 컨트롤을 그리지 못해서 노란 칸에 🚫 로
> 나온다. **배치와 글자를 보는 용도**이지 컨트롤 모양을 보는 용도가 아니다.

전체 목록은 [`mac/CLAUDE.md`](../mac/CLAUDE.md) 와 [`win/CLAUDE.md`](../win/CLAUDE.md) 에 있다.

## 윈도우는 아직 테스트판이 없다

윈도우판에는 `VARIANT=test` 에 해당하는 것이 없다. 지금은 이렇게 나눈다.

| | |
| --- | --- |
| 개발 중 | `dotnet run --project src/DongCSU.App` — 설치본이 아니라 자체 업데이트가 꺼진다 |
| 설치본 | 릴리스의 `DongCSU-win-Setup.exe` |

**설정 파일(`%APPDATA%\DongCSU\settings.json`)을 함께 쓴다**는 점이 맥과 다르다.
개발 중에 설정을 헤집으면 설치해 둔 쪽도 같이 바뀐다. 맥처럼 갈라놓는 건 언제
할지 안 정했다 — 필요해지면 그때 한다.

## 두 판 사이에서 조심할 것

- **부엉이는 맥이 원본이다.** 윈도우에서 고치지 말고, 맥 소스를 고친 뒤
  `dump_owl` 로 [`shared/owl.json`](../shared/README.md) 을 다시 뽑는다
- **버전은 판마다 따로 센다.** 태그도 `mac-v` / `win-v` 로 갈라 붙인다
- **릴리스는 혼자 올리지 않는다.** 버전 자리를 정하기 전에 물어본다
