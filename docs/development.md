# 개발

[← README](../README.md) · 작업 규칙은 [CLAUDE.md](../CLAUDE.md)

## 감시하며 개발

```bash
./dev.sh
```

`Sources/`를 감시해서 저장할 때마다 재빌드하고 앱을 다시 띄운다(`brew install fswatch` 필요).
빌드가 깨지면 컴파일 에러만 추려서 보여주고 앱은 그대로 둔다.

개발 중에 띄우는 건 **`DongMCU-Test`** 라는 별개의 앱이다. 번들 ID가 `com.ldg.dong-mcu-test`로
달라서 설정·창 위치·메뉴바 자리를 정식판과 공유하지 않고, 둘을 동시에 띄워 비교할 수 있다.
메뉴바 아이콘은 테스트판만 몸 색이 보라색이다.

```bash
VARIANT=test ./build.sh    # DongMCU-Test.app  (dev.sh의 기본값)
./build.sh                 # DongMCU.app       (release.sh가 쓰는 정식 번들)
```

둘을 같이 띄우면 사용량 API도 각자 조회하니 요청이 두 배가 된다.

## 화면을 PNG로 뽑기

앱을 띄우지 않고 확인할 수 있다. 화면에 영향을 주는 변경은 이걸로 먼저 본다.

```bash
dong-mcu --render out.png 34 61 owl ok large   # HUD (사용률·아이콘·상태·배율)
dong-mcu --render-settings out.png version     # 설정 창의 특정 탭
dong-mcu --render-menubar out.png 16           # 메뉴바 아이콘
dong-mcu --render-icon out.png 1024            # 앱 아이콘
```

`--render`의 뒤쪽 인자는 순서와 무관하게 인식한다:
`collapsed` `light` `expandLeft` `stats` `update`, 배율(`small`~`extraLarge`),
상태(`ok` `stale` `reauth`), 0~1 사이 숫자는 배경 불투명도.

`--render-settings`는 탭 이름(`status` `display` `icon` `account` `version`)과
`update=1.2.0`(새 버전이 있는 것처럼 그리기)을 받는다.

> `ImageRenderer`는 `ScrollView` 안을 그리지 못한다. 스크롤이 필요한 화면은
> `isPreviewRender`로 스크롤을 벗긴 형태를 따로 그린다.

## 마스코트 부엉이

[`OwlMark.swift`](../Sources/DongMCU/OwlMark.swift)에 몸통·날개·눈·부리·발이 문자열 그리드로
나뉘어 있고 `OwlPose`로 조합해 그린다. 눈만 갈아끼우면 깜빡이고 날개 레이어만 바꾸면 펴진다.

HUD 링 안, 메뉴바, 앱 아이콘이 모두 같은 그리드를 쓴다. **한 칸이 정수 크기가 아니면** 어떤
행은 2px, 어떤 행은 3px로 그려져 자리마다 다른 얼굴이 된다. 그래서 칸 크기를 내려 정수로
맞추고 남는 여백은 가운데로 몬다. 메뉴바(높이 16pt)에서는 한 칸이 1pt다.

HUD 크기 설정도 같은 이유로 `scaleEffect`가 아니라 치수와 글자 크기에 배율을 곱하는 방식이다.
확대 변환을 걸면 픽셀 그림이 흐려지지만, 배율을 곱해 다시 그리면 한 칸도 같이 커져서 큰
크기에서 오히려 더 선명해진다.

앱 아이콘 그림은 [`AppIconArt.swift`](../Sources/DongMCU/AppIconArt.swift)에 있다. 고쳤으면
아래로 `.icns`를 다시 만든다. 결과물은 커밋해 두고 `build.sh`는 복사만 한다.

```bash
./make-icon.sh
```

## 릴리스

```bash
./release.sh 1.2.0
```

버전을 `Resources/Info.plist`와 `Sources/DongMCU/main.swift` 양쪽에 올리고, 빌드로 검증한 뒤
커밋·태그·푸시하고, 올라간 태그의 tarball 해시로 Homebrew formula까지 갱신한다. `gh`가 있으면
GitHub 릴리스도 만든다. main이 아니거나 작업 트리가 지저분하거나 태그가 겹치면 중단한다.

내보내기 전에 [`Changelog.swift`](../Sources/DongMCU/Changelog.swift)의 맨 위 항목에 버전과
날짜를 채운다. 설정 창의 버전 탭이 그걸 그대로 보여준다.
버전 자리 규칙과 변경 내역 문구 규칙은 [CLAUDE.md](../CLAUDE.md)에 있다.

## 에디터

VS Code + [Swift 확장](https://marketplace.visualstudio.com/items?itemName=swiftlang.swift-vscode)
기준으로 `.vscode/`에 태스크를 넣어뒀다.
