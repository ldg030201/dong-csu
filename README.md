<div align="center">

<img src="mac/docs/icon.png" width="112" alt="DongCSU">

# DongCSU

**Claude 사용량을 화면 위에 항상 띄워두는 앱**

![License](https://img.shields.io/badge/license-MIT-3A72C4)
![Dependencies](https://img.shields.io/badge/dependencies-0-3A72C4)

<img src="mac/docs/screenshot.png" width="620" alt="HUD">

</div>

---

Claude Code를 쓰다 보면 "지금 한도를 얼마나 썼지?"가 계속 궁금해진다. 확인하려면
하던 걸 멈추고 `/usage`를 쳐야 한다. 그 숫자를 화면 구석에 그냥 띄워둔다.

## 어느 쪽을 쓰시나요

<table>
<tr>
<td align="center" width="50%">

### 🍎 [macOS](mac/README.md)

메뉴바 · Homebrew · macOS 14+

**[설치하기 →](mac/README.md)**

</td>
<td align="center" width="50%">

### 🪟 [Windows](win/README.md)

트레이 · WinGet · Windows 10+

**[설치하기 →](win/README.md)**

</td>
</tr>
</table>

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
