# dong-mcu 작업 규칙

## 버전

`주.부.수` 세 자리를 쓰고, **긴급 패치는 네 번째 자리**를 붙인다.

| 자리 | 예 | 언제 |
| --- | --- | --- |
| 주 | `2.0.0` | **크게 뜯어고쳤을 때만.** 아껴서 올린다 |
| 부 | `1.1.0` → `1.12.0` | 기능이 늘거나 화면이 달라질 때 |
| 수 | `1.0.1` | 버그 수정을 모아서 낼 때 |
| 긴급 | `1.0.0.1` | 이미 나간 버전을 급히 고칠 때. 네 번째 자리만 올린다 |

**두 번째 자리는 10을 넘어도 그대로 올린다.** `1.9.0` 다음은 `1.10.0`이고,
자리가 두 자리가 됐다는 이유로 주 버전을 올리지 않는다.

`./release.sh 1.0.0.1` 처럼 그대로 넘기면 된다. 버전은
`Resources/Info.plist`와 `Sources/DongMCU/main.swift` 두 곳에 있고
release.sh가 양쪽을 함께 올린 뒤 어긋나지 않았는지 확인한다.

## 변경 내역 (매번 적는다)

**무언가를 만들거나 고칠 때마다 [`Sources/DongMCU/Changelog.swift`](Sources/DongMCU/Changelog.swift)
맨 위 항목에 한 줄을 추가한다.** 빼먹지 않는다.

- 설정 창의 **변경 내역** 탭이 이 파일을 그대로 보여준다
- 사용자가 화면에서 무엇이 달라지는지 알 수 있게 쓴다.
  내부 구조나 이유는 커밋 메시지에 남기고 여기에는 쓰지 않는다
- 아직 안 나간 변경은 다음 버전 항목에 쌓아 두고, 릴리스할 때
  `ChangelogEntry`의 버전과 날짜를 확정한다

## 빌드

검증은 **test 변형으로만** 한다.

```bash
VARIANT=test ./build.sh     # DongMCU-Test.app
```

`./build.sh`(정식 번들)는 배포할 때 release.sh가 알아서 부른다. 개발 중에 직접 만들면
사용자가 brew로 설치해 쓰는 배포본이 바뀐 것처럼 보여서 혼란스럽다. 실수로 만들었으면
`build/DongMCU.app`을 지운다.

**빌드했다고 화면에 반영되지 않는다.** 이미 떠 있는 앱은 옛 바이너리 그대로다.
`dev.sh`(fswatch)가 돌고 있지 않다면 직접 다시 띄운다.

```bash
pkill -f DongMCU-Test; open build/DongMCU-Test.app
```

## 눈으로 확인하기

앱을 띄우지 않고도 대부분을 PNG로 뽑아 볼 수 있다. 화면에 영향을 주는 변경은
이걸로 먼저 확인한다.

```bash
dong-mcu --render out.png 34 61 owl ok large   # HUD (사용률·아이콘·상태·배율)
dong-mcu --render-settings out.png changelog   # 설정 창의 특정 탭
dong-mcu --render-menubar out.png 16           # 메뉴바 아이콘
dong-mcu --render-icon out.png 1024            # 앱 아이콘
```

`ImageRenderer`는 `ScrollView` 안을 그리지 못한다. 스크롤이 필요한 화면은
`isPreviewRender`로 스크롤을 벗긴 형태를 따로 그린다.

## 이름

화면에 보이는 이름은 **DongMCU**, 번들 ID는 `com.ldg.dong-mcu`, 명령과 Homebrew tap은
`dong-mcu`다. **번들 ID는 바꾸지 않는다** — UserDefaults 키가 달라져서 창 위치·아이콘·크기
설정이 전부 초기화된다.

## 커밋

이모지 하나 + 한국어 한 줄로 제목을 쓴다(`✨ 기능`, `🐛 수정`, `🎨 모양`, `📝 문서`,
`🔧 설정`, `⚡ 성능`, `🔖 버전`, `📦 formula`). 본문에는 **왜** 그렇게 했는지를 적는다.
무엇을 바꿨는지는 diff에 이미 있다.
