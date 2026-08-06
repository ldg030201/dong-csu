# dong-mcu 를 쓰던 분께

[← README](../README.md)

**앱 이름이 `dong-mcu` 에서 `dong-csu` 로 바뀌었습니다.** 자동으로 업데이트되지 않으니
한 번만 손으로 옮겨 주세요. 아래 한 줄이면 지우기부터 설치까지 끝납니다.

```bash
pkill -f DongMCU; rm -rf /Applications/DongMCU.app; brew uninstall dong-mcu; brew untap ldg030201/dong-mcu; rm -f ~/Library/Caches/Homebrew/dong-mcu*; brew tap ldg030201/dong-csu https://github.com/ldg030201/dong-csu && brew trust ldg030201/dong-csu && brew install -y dong-csu && cp -R "$(brew --prefix dong-csu)/DongCSU.app" /Applications/ && open /Applications/DongCSU.app
```

**설정은 그대로 넘어옵니다.** 창 위치·아이콘·크기·펫 설정을 첫 실행 때 옛 앱에서
한 번 읽어 옵니다. 옛 앱을 지운 뒤에 새 앱을 실행해도 됩니다 — 설정은 앱이 아니라
따로 남아 있습니다.

## 왜 바뀌었나

`m` 이 macOS 를 뜻했습니다. 윈도우판을 만들기로 하면서 틀린 글자가 됐고,
쓰는 사람이 적은 지금 치우는 게 나중보다 쌉니다.

`csu` 는 **C**laude **S**tatus **U**I 입니다.

## 한 줄씩 무엇을 하는지

| 명령 | 하는 일 |
| --- | --- |
| `pkill -f DongMCU` | 떠 있는 옛 앱을 끕니다. 파일을 지우는 동안 실행 중이면 안 됩니다 |
| `rm -rf /Applications/DongMCU.app` | `/Applications` 의 복사본을 지웁니다 |
| `brew uninstall dong-mcu` | brew 패키지를 지웁니다 |
| `brew untap ldg030201/dong-mcu` | 옛 tap 을 뗍니다. 남겨 두면 옛 formula 를 계속 봅니다 |
| `rm -f ~/Library/Caches/Homebrew/dong-mcu*` | 받아둔 tarball 을 지웁니다 |
| `brew tap … dong-csu` | 새 tap 을 답니다 |
| `brew install -y dong-csu` | 새로 설치합니다 (소스 빌드라 몇십 초 걸립니다) |
| `cp -R … /Applications/` | Launchpad·Spotlight 에 뜨게 복사합니다. 심볼릭 링크는 안 됩니다 |

## 설정까지 완전히 지우고 싶다면

옛 설정을 물려받고 싶지 않을 때만 쓰세요. **새 앱을 처음 실행하기 전에** 지워야 합니다.

```bash
defaults delete com.ldg.dong-mcu
```

## 로그인 항목

시스템 설정 > 일반 > 로그인 항목에 `DongMCU` 를 넣어 두셨다면, 거기서 빼고
`DongCSU` 를 다시 넣어 주세요.

## 막히면

[문제 해결](troubleshooting.md) 을 보세요.
