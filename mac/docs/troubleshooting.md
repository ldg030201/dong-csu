# 문제 해결

[← README](../README.md)

## 완전히 지우고 다시 설치

옛 버전에서 올라오다 꼬였을 때 쓴다. **이 한 줄로 지우는 것부터 다시 설치까지 끝난다.**

```bash
pkill -f DongCSU; rm -rf /Applications/DongCSU.app /Applications/dong-csu.app; brew uninstall dong-csu; brew untap ldg030201/dong-csu; brew untrust --tap https://github.com/ldg030201/dong-csu 2>/dev/null; rm -f ~/Library/Caches/Homebrew/dong-csu*; brew tap ldg030201/dong-csu https://github.com/ldg030201/dong-csu && brew trust ldg030201/dong-csu && brew install -y dong-csu && cp -R "$(brew --prefix dong-csu)/DongCSU.app" /Applications/ && open /Applications/DongCSU.app
```

지우기만 하려면 `rm -f ~/Library/Caches/Homebrew/dong-csu*` 까지만 실행한다.

| 지우는 것 | 왜 |
| --- | --- |
| 실행 중인 앱 | 파일을 지우는 동안 떠 있으면 안 된다 |
| `/Applications/DongCSU.app` | 복사본 |
| `/Applications/dong-csu.app` | 1.0.0 이전의 옛 이름. 심볼릭 링크였다면 깨진 채 남아 있다 |
| brew 패키지 · tap · trust | tap이 남아 있으면 옛 formula를 계속 쓴다 |
| Homebrew 캐시 | 받아둔 tarball. `sha256 mismatch`의 원인 대부분이 이것이다 |

지우는 부분은 `;`로, 설치하는 부분은 `&&`로 이었다. 이미 없는 것을 지우다 실패해도 나머지
정리는 계속돼야 하지만, 설치는 앞 단계가 성공해야 다음으로 넘어가야 하기 때문이다.

설정(창 위치·아이콘·크기)은 지워지지 않고 재설치하면 복원된다.
그것까지 밀려면 `defaults delete com.ldg.dong-csu`.

---

## 증상별

### `already installed and up-to-date`

`brew update`가 빠졌다. tap 저장소가 갱신되지 않아 옛 formula를 보고 있는 것이다.
설치 명령을 다시 실행하는 게 아니라 **업데이트 명령**을 써야 한다.

```bash
brew update && brew upgrade -y dong-csu
```

> `-y`는 Homebrew 6부터 업그레이드 전에 묻는 y/n 확인을 건너뛴다.
> 옛 Homebrew라 `-y`를 모른다고 하면 빼고 실행한다.

### `is using Swift tools version 6.0.0 but the installed version is ...`

Command Line Tools가 오래됐다. **Swift 5.9 이상이면 빌드된다.**
그 아래 버전이거나 그래도 실패하면 Command Line Tools를 새로 받는다.

```bash
sudo rm -rf /Library/Developer/CommandLineTools && sudo xcode-select --install
```

설치한 Swift 버전은 `swift --version`으로 확인한다.

### `xcrun: error: unable to lookup item 'PlatformPath'`

위와 같은 원인이다. Command Line Tools가 깨졌거나 오래된 것이다.

### `brew untrust` 사용법 오류

`Options --tap and --formula are mutually exclusive`가 뜨는 Homebrew 버전이 있다.
신뢰 목록이 남아도 재설치에는 지장이 없으니 그냥 넘어가도 된다. 굳이 지우려면:

```bash
brew untrust --tap ldg030201/dong-csu
```

### `sha256 mismatch`

받아둔 tarball이 캐시에 남아 있는 경우가 대부분이다.

```bash
rm -f ~/Library/Caches/Homebrew/dong-csu* && brew update && brew install -y dong-csu
```

### Launchpad·Spotlight에 안 보인다

`ln -s`로 심볼릭 링크를 걸었을 때 그렇다. macOS는 `/Applications` 안의 심볼릭 링크를 앱으로
등록하지 않는다. 링크를 지우고 복사한다.

```bash
rm -rf /Applications/DongCSU.app && cp -R "$(brew --prefix dong-csu)/DongCSU.app" /Applications/
```

### 업그레이드했는데 옛 버전이 뜬다

`/Applications`에 있는 건 복사본이라 `brew upgrade`로 갱신되지 않는다. 다시 복사한다.

```bash
rm -rf /Applications/DongCSU.app && cp -R "$(brew --prefix dong-csu)/DongCSU.app" /Applications/ && open /Applications/DongCSU.app
```

### 앱이 뜨지 않는다 / 아무 데도 안 보인다

Dock 아이콘이 없는 앱이라 **메뉴바에만** 뜬다. 부엉이 아이콘을 찾는다.
그래도 없으면 실행 여부부터 확인한다.

```bash
pgrep -fl DongCSU
```

### 숫자가 흐려지고 `재로그인 필요`가 뜬다

Claude Code의 토큰이 만료됐다. 메뉴의 `Claude Code 재로그인…`을 누르면 터미널에서
로그인 플로우가 열린다. 자세한 내용은 [사용량과 토큰](privacy.md).

### keychain 접근을 계속 묻는다

첫 실행 때 **"항상 허용"** 을 눌러야 다시 묻지 않는다. `/usr/bin/security`로 읽기 때문에
한 번 허용해 두면 앱을 다시 빌드해도 권한이 유지된다.
