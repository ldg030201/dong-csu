# 설치 안내

[← README](../README.md)

## 한 줄 설치

```bash
brew tap ldg030201/dong-csu https://github.com/ldg030201/dong-csu && brew trust ldg030201/dong-csu && brew install -y dong-csu && cp -R "$(brew --prefix dong-csu)/DongCSU.app" /Applications/ && open /Applications/DongCSU.app
```

| 명령 | 하는 일 |
| --- | --- |
| `brew tap` | 이 저장소를 Homebrew가 아는 목록에 넣는다 |
| `brew trust` | 서드파티 tap의 formula 실행을 허용한다 |
| `brew install` | 소스를 받아 빌드한다 (Xcode 불필요, 30초쯤) |
| `cp -R` | `/Applications`에 복사한다 |
| `open` | 실행한다 |

### `cp` 줄이 왜 필요한가

Homebrew에는 두 종류가 있다. GUI 앱용 **cask**(`brew install --cask`)는 `/Applications`에
자동으로 설치하지만, 이건 소스를 빌드하는 **formula**다. formula는 Homebrew 디렉터리 밖에
파일을 쓰지 않는 게 규칙이라 `/Applications`를 건드리지 않는다.

대신 코드 서명·공증 없이 설치되는 게 이 방식의 장점이다. cask로 바꾸려면 미리 빌드한 앱을
올려야 하고, 그러면 Gatekeeper 때문에 Apple Developer Program(연 $99) 공증이 필요해진다.

### 심볼릭 링크로는 안 된다

`ln -s`로 걸면 macOS가 `/Applications` 안의 심볼릭 링크를 **앱으로 등록하지 않는다.**
Launchpad와 Spotlight에 나타나지 않고 Finder에서 직접 열 수만 있다. 그래서 복사한다.

복사본이라는 점 때문에 **업그레이드할 때 다시 복사해야 한다.** 앱 안의 업데이트 버튼은
이 과정까지 대신 해준다.

### `brew trust`에 대한 경고

실행하면 Homebrew가 "권장하지 않으며 나중 릴리스에서 제거될 예정"이라는 경고를 낸다.
지금은 정상 동작하고, 제거되면 그때 Homebrew가 제시하는 방식으로 갈아타면 된다.

## 소스에서 빌드

```bash
git clone https://github.com/ldg030201/dong-csu.git
cd dong-csu/mac && ./build.sh && open build/DongCSU.app
```

Xcode는 필요 없다. Command Line Tools(`xcode-select --install`)만 있으면 된다.
**Swift 5.9 이상**이 필요하고 `swift --version`으로 확인할 수 있다.

미리 빌드된 결과물(bottle)은 **Apple Silicon**용만 올라간다. Intel 맥에서는 Homebrew가
자동으로 소스 빌드로 넘어가므로 설치가 30초쯤 걸린다.

## 업데이트

앱 안의 **업데이트** 버튼(버전 탭)을 누르면 터미널에서 brew가 돌고 `/Applications` 쪽까지
새 것으로 바꾼 뒤 앱을 다시 띄운다. 직접 하려면:

```bash
brew update && brew upgrade -y dong-csu && rm -rf /Applications/DongCSU.app && cp -R "$(brew --prefix dong-csu)/DongCSU.app" /Applications/ && open /Applications/DongCSU.app
```

`brew update`가 빠지면 tap이 갱신되지 않아 옛 formula를 보고 `already installed and
up-to-date`라고 나온다.

## 로그인할 때 자동으로 켜기

설정 창 → **표시** 탭 → **로그인할 때 자동 시작**을 켠다. (2.1.1부터)

시스템 설정 → 일반 → 로그인 항목에도 나타나고, 거기서 끄면 설정 창에도 꺼진 것으로
보인다. 그렇게 껐다면 설정 창에서 다시 켜지지 않으므로 시스템 설정에서 켜야 한다 —
그때는 설정 창이 그쪽으로 가는 버튼을 띄운다.

## 제거

```bash
rm -rf /Applications/DongCSU.app && brew uninstall dong-csu && brew untap ldg030201/dong-csu
```

설정(창 위치·아이콘·크기)은 남는다. 그것까지 지우려면:

```bash
defaults delete com.ldg.dong-csu
```

흔적을 남김없이 지우고 다시 설치하려면 [문제 해결](troubleshooting.md)의 완전 제거를 쓴다.
