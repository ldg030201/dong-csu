<div align="center">

<img src="mac/docs/icon.png" width="112" alt="DongCSU">

# DongCSU

**Claude 사용량을 화면 위에 항상 띄워두는 앱**

![macOS](https://img.shields.io/badge/macOS-14+-000000?logo=apple&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10+-0078D4?logo=windows&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-3A72C4)
![Dependencies](https://img.shields.io/badge/dependencies-0-3A72C4)

<img src="mac/docs/screenshot.png" width="620" alt="HUD">

<sub>화면은 macOS 판입니다.</sub>

</div>

---

Claude Code를 쓰다 보면 "지금 한도를 얼마나 썼지?"가 계속 궁금해진다. 확인하려면
하던 걸 멈추고 `/usage`를 쳐야 한다. 그 숫자를 화면 구석에 그냥 띄워둔다.

## 어느 쪽을 쓰시나요

| | 🍎 **[macOS](mac/README.md)** | 🪟 **[Windows](win/README.md)** |
| --- | --- | --- |
| **필요한 버전** | **macOS 14 (Sonoma) 이상** | **Windows 10 1809 이상** 또는 Windows 11 |
| 상주하는 곳 | 메뉴바 | 트레이 |
| 설치 | Homebrew | WinGet · 설치 exe |
| 업데이트 | 터미널에서 `brew upgrade` | 앱이 스스로 |
| 지금 버전 | 2.1.0 | 1.0.0 |
| | **[설치하기 →](mac/README.md)** | **[설치하기 →](win/README.md)** |

**어느 쪽이든 Claude Code에 로그인되어 있어야 합니다.** 이 앱은 Claude Code가 저장해 둔
자격 증명을 읽어서 사용량을 조회합니다. 따로 로그인하지 않습니다.

## 저장소 구조

두 판은 **각자 만들고 각자 버전을 매긴다.** 고쳐야 할 버그가 서로 다르기 때문이다.
맥에서 먼저 만들고 윈도우로 옮긴다.

| | |
| --- | --- |
| [`mac/`](mac/) | macOS 앱 (Swift · SwiftUI). 소스·스크립트·맥 전용 문서 |
| [`win/`](win/) | Windows 앱. 소스·윈도우 전용 문서 |
| [`shared/`](shared/README.md) | 두 판이 나눠 쓰는 데이터. **맥 소스에서 뽑아낸다** |
| [`docs/characters/`](docs/characters/README.md) | 마스코트 문서. 그림은 양쪽이 같다 |
| [`Formula/`](Formula/) | Homebrew formula. tap 이 뿌리에서 찾으므로 여기 있어야 한다 |

마스코트의 그리드·색·프레임표는 [`shared/owl.json`](shared/owl.json) 한 곳에서 나온다.
옮겨 적지 않기 때문에 **맥에서 자세를 고치면 윈도우도 같이 바뀐다.** 자세한 건
[`shared/README.md`](shared/README.md).

## 라이선스

[MIT](LICENSE)
