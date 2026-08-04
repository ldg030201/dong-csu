<div align="center">

<img src="docs/icon.png" width="112" alt="DongMCU">

# DongMCU

**Claude 사용량을 화면 위에 항상 띄워두는 macOS 앱**

![macOS](https://img.shields.io/badge/macOS-13+-000000?logo=apple&logoColor=white)
![Swift](https://img.shields.io/badge/Swift-5.9+-F05138?logo=swift&logoColor=white)
[![Release](https://img.shields.io/github/v/release/ldg030201/dong-mcu?color=3A72C4&label=release)](https://github.com/ldg030201/dong-mcu/releases/latest)
![Dependencies](https://img.shields.io/badge/dependencies-0-3A72C4)
![License](https://img.shields.io/badge/license-MIT-3A72C4)

<img src="docs/screenshot.png" width="620" alt="HUD">

</div>

---

## ✨ 이런 앱

Claude Code를 쓰다 보면 "지금 한도를 얼마나 썼지?"가 계속 궁금해진다.
확인하려면 하던 걸 멈추고 `/usage`를 쳐야 한다. 그 숫자를 화면 구석에 그냥 띄워둔다.

- 🔵 **이중 링** — 바깥이 5시간 세션, 안쪽이 7일 주간
- 🎨 사용률이 오를수록 **초록 → 노랑 → 빨강**으로 연속해서 변한다
- ⏱️ 초기화까지 남은 시간, 다음 조회까지 남은 시간
- 🔔 새 버전이 나오면 알려준다
- 🦉 픽셀 마스코트 부엉이
- 🖥️ 모든 Space와 전체화면 위에. Dock 아이콘 없이 메뉴바에만

<table>
<tr>
<td align="center" width="32%">
<img src="docs/collapsed.png" width="150" alt="접은 모습"><br>
<sub><b>접으면 링만 남는다</b></sub>
</td>
<td align="center" width="68%">
<img src="docs/sizes.png" width="430" alt="크기 4단계"><br>
<sub><b>크기 4단계</b></sub>
</td>
</tr>
</table>

## 📦 설치

```bash
brew tap ldg030201/dong-mcu https://github.com/ldg030201/dong-mcu && brew trust ldg030201/dong-mcu && brew install dong-mcu && cp -R "$(brew --prefix dong-mcu)/DongMCU.app" /Applications/ && open /Applications/DongMCU.app
```

> [!NOTE]
> **macOS 13(Ventura) 이상**과 **Claude Code 로그인**이 필요하다. Claude Code가 keychain에 저장해 둔
> 토큰으로 사용량을 읽기 때문에, Claude Code 없이는 동작하지 않는다.
> 첫 실행 때 keychain 접근을 허용할지 한 번 묻는다.

업데이트는 앱 안의 **업데이트** 버튼으로 하거나:

```bash
brew update && brew upgrade dong-mcu && rm -rf /Applications/DongMCU.app && cp -R "$(brew --prefix dong-mcu)/DongMCU.app" /Applications/ && open /Applications/DongMCU.app
```

각 단계가 무엇을 하는지, 소스 빌드, 제거는 **[설치 안내](docs/install.md)** 참고.
설치가 꼬였다면 **[문제 해결](docs/troubleshooting.md)**.

## 🎛️ 쓰는 법

<table>
<tr>
<td width="45%">
<img src="docs/settings-icon.png" width="330" alt="설정 창">
</td>
<td>

톱니 버튼이나 `⌘,`로 설정을 연다.
**메뉴바 아이콘**이나 **HUD 우클릭**으로 메뉴가 열린다.

| 탭 | 내용 |
| --- | --- |
| **상태** | 사용량, 초기화·조회 시각 |
| **표시** | 테마, 크기, 조회 주기, 방향 |
| **아이콘** | 가운데 그림 |
| **버전** | 업데이트와 변경 내역 |

- **드래그**로 이동, **더블클릭**으로 접기
- 위치·크기·아이콘 선택은 기억된다

</td>
</tr>
</table>

새 버전이 나오면 **왼쪽 위에 파란 표시**가 뜬다. 누르면 버전 화면이 열린다.

<div align="center">
<img src="docs/update.png" width="520" alt="업데이트 알림">
</div>

## 🔒 프라이버시

- 🔑 토큰은 **Authorization 헤더로만** 쓰이고 디스크에 쓰거나 로그에 남기지 않는다
- 🌐 Anthropic 사용량 API와 **업데이트 확인용 GitHub** 외에는 접속하지 않는다
- 🚫 통계·추적 없음. 외부 Swift 패키지 **의존성 0개**

업데이트 확인을 끄면 Anthropic API 외에 아무 데도 접속하지 않는다.
자세한 내용은 **[사용량과 토큰](docs/privacy.md)**.

## 📚 문서

| | |
| --- | --- |
| [설치 안내](docs/install.md) | 각 단계 설명, 소스 빌드, 업데이트, 제거 |
| [문제 해결](docs/troubleshooting.md) | 설치가 꼬였을 때, 앱이 안 뜰 때 |
| [사용량과 토큰](docs/privacy.md) | 어디서 무엇을 읽는지, 토큰 만료 |
| [개발](docs/development.md) | 빌드, 렌더 통로, 마스코트 구조 |
| [작업 규칙](CLAUDE.md) | 버전·변경 내역·커밋 규칙 |

## 📄 라이선스

MIT. [LICENSE](LICENSE) 참고.

> **Anthropic과 무관한 비공식 개인 도구다.**
> Claude, Claude Code, Clawd 및 관련 로고·마스코트의 저작권과 상표권은 전부 **Anthropic**에
> 있다. MIT 라이선스는 이 저장소의 코드에만 적용되며 Anthropic의 아트워크에는 적용되지 않는다.
> Anthropic 측에서 요청하면 해당 아트워크를 제거한다.
