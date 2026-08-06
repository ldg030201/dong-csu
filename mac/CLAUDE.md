# macOS 판 작업 규칙

공통 규칙(버전 자릿수·변경 내역 문구·캐릭터·커밋)은 [`../CLAUDE.md`](../CLAUDE.md)에 있다.
여기에는 **맥에서만 걸리는 것**을 쓴다.

**모든 명령은 `mac/` 안에서 돌린다.** 아래 경로는 전부 `mac/` 기준이다.

## 버전

```bash
./release.sh 1.0.0.1
```

버전은 `Resources/Info.plist`와 `Sources/DongCSU/main.swift` 두 곳에 있고
release.sh가 양쪽을 함께 올린 뒤 어긋나지 않았는지 확인한다.
태그는 `mac-` 을 붙인다 — 윈도우와 번호를 따로 세기 때문이다.

## 변경 내역

[`Sources/DongCSU/Changelog.swift`](Sources/DongCSU/Changelog.swift) 맨 위 항목에 한 줄
추가하고, 앱이 원격에서 받아보는 JSON을 다시 뽑는다.

```bash
source ./lib.sh && dump_changelog
```

**반드시 이 함수를 쓴다** — 직접 `--dump-changelog`를 부르면 빌드를 건너뛰어
옛 바이너리가 방금 추가한 항목을 빠뜨린 채 뽑는다.

**같은 것이 두 곳에 나간다.** `mac/docs/changelog.json`이 진짜고, 저장소 뿌리의
`docs/changelog.json`은 **이미 나간 2.0.0 이하가 아직 그 주소를 보고 있어서** 남겨 둔
사본이다. 함수가 둘 다 쓰고 CI가 둘 다 검사한다. 다들 2.1.0을 넘기면 뿌리 쪽은 지운다.

## 빌드

검증은 **test 변형으로만** 한다.

```bash
VARIANT=test ./build.sh     # DongCSU-Test.app
```

`./build.sh`(정식 번들)는 배포할 때 release.sh가 알아서 부른다. 개발 중에 직접 만들면
사용자가 brew로 설치해 쓰는 배포본이 바뀐 것처럼 보여서 혼란스럽다. 실수로 만들었으면
`build/DongCSU.app`을 지운다.

**빌드했다고 화면에 반영되지 않는다.** 이미 떠 있는 앱은 옛 바이너리 그대로다.
`dev.sh`(fswatch)가 돌고 있지 않다면 직접 다시 띄운다.

```bash
pkill -f DongCSU-Test; open build/DongCSU-Test.app
```

## 눈으로 확인하기

앱을 띄우지 않고도 대부분을 PNG로 뽑아 볼 수 있다. 화면에 영향을 주는 변경은
이걸로 먼저 확인한다.

```bash
dong-csu --render out.png 34 61 owl ok large      # HUD (사용률·아이콘·상태·배율)
dong-csu --render-settings out.png changelog      # 설정 창의 특정 탭
dong-csu --render-menubar out.png 16              # 메뉴바 아이콘
dong-csu --render-icon out.png 1024               # 앱 아이콘
dong-csu --render-owl out.png 96                  # 부엉이 애니메이션 전 프레임 (기분 + 걷기·달리기)
dong-csu --render-owl-gif ../docs/characters/owl  # 하나마다 움직이는 GIF (문서용)
dong-csu --dump-owl ../shared/owl.json            # 윈도우판과 나눠 쓸 부엉이 데이터
dong-csu --probe-login [on|off]                   # 로그인 항목 등록 상태 확인·변경
```

`--probe-login` 은 **번들 기준**이라 터미널에서 불러도 앱과 같은 것을 본다. 확인하고
나면 원래 상태로 되돌려 놓는다 — 안 그러면 테스트판이 로그인할 때마다 뜬다.

`--render-owl`은 `OwlAnimation.all`을 한 장에 늘어놓는다. 자세를 고칠 때 앱을 띄우고
몇 초씩 기다리지 말고 이걸로 본다. **걸음걸이는 기분이 아니라서 `OwlMood.allCases`에 없다**
— 보여줄 것을 더할 자리는 `OwlAnimation.all` 한 곳이다. 다만 **크기가 달라 보인다는 인상은 재지 말고
픽셀로 확인한다** — 주저앉은 자세는 다리가 몸에 가려져 실제로 한 칸 짧다.

**자세나 프레임 시간을 고쳤으면 `--render-owl-gif`를 다시 돌린다.** 캐릭터 문서의
GIF가 실제 애니메이션과 어긋나면 안 된다. GIF는 손으로 만들지 않는다.

## 메뉴

우클릭 메뉴와 메뉴바 메뉴는 `HUDController.populateMenu` 하나를 같이 쓴다.
**여기에 설정 항목을 늘리지 않는다** — 모드·크기·테마·아이콘은 전부 설정 창에 있고,
메뉴에 한 벌 더 두면 두 곳을 함께 고쳐야 하는 데다 자주 누르는 항목이 파묻힌다.
메뉴에는 바로 누르는 것(새로고침·설정·종료)만 남긴다.

## 성능을 숫자로 말할 때

**폴링이 도는 동안 잰 CPU는 못 믿는다.** 사용량 조회 한 번이 애니메이션 몇 분 치보다
비싸서, 창 안에 조회가 들어갔는지에 따라 부호까지 뒤집힌다. 비교할 때는 조회 주기를
길게 막고, **같은 앱에서 조건만 바꿔** 여러 창을 번갈아 잰다.

`ImageRenderer`는 `ScrollView` 안을 그리지 못한다. 스크롤이 필요한 화면은
`isPreviewRender`로 스크롤을 벗긴 형태를 따로 그린다.

## 번들 ID

번들 ID는 `com.ldg.dong-csu` (테스트판은 `-test`).

**번들 ID를 바꾸면 UserDefaults 도메인이 통째로 갈린다** — 창 위치·아이콘·크기·펫 설정이
전부 초기화된다. 함부로 바꾸지 않되, 꼭 바꿔야 하면 `HUDSettings.migrateLegacyDefaults`
처럼 **옛 도메인에서 한 번 옮겨 오는 코드를 같이 넣는다.** 샌드박스가 아니라서 읽을 수 있다.

## 서명

손쉬운 사용 권한은 **코드 서명 신원**에 걸린다. ad-hoc 서명은 신원이 없어서 macOS가
바이너리 해시로 앱을 알아보는데, 그 해시는 코드가 바뀔 때마다 달라진다 — 다시 빌드할
때마다 허용해 둔 권한이 풀린다. `./make-signing-cert.sh` 를 한 번 돌려 자체 서명
인증서를 만들어 두면 신원이 고정된다.

권한을 확인할 때 **터미널에서 바이너리를 직접 부르면 안 된다.** TCC가 Terminal.app에
권한을 물어서 결과가 거짓이 된다. 앱 번들을 통해 확인한다.

```bash
open -n build/DongCSU-Test.app --args --probe-accessibility /tmp/ax.txt
```
