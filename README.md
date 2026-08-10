<div align="center">

<img src="mac/docs/icon.png" width="112" alt="DongCSU">

# DongCSU

**Claude 사용량을 화면 위에 항상 띄워두는 앱**

[![macOS](https://img.shields.io/github/v/release/ldg030201/dong-csu?filter=macOS*&display_name=release&label=%20&logo=apple&logoColor=white&labelColor=0E1B2E&color=3A72C4)](https://github.com/ldg030201/dong-csu/releases?q=tag%3Amac-v)
[![Windows](https://img.shields.io/github/v/release/ldg030201/dong-csu?filter=Windows*&display_name=release&label=%20&labelColor=0078D4&color=F6A623&logo=data:image%2Fsvg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0iI2ZmZmZmZiI%2BPHBhdGggZD0iTTAgMy41IDkuNSAyLjJ2OS4zSDB6TTEwLjggMiAyNCAwdjExLjVIMTAuOHpNMCAxMi41aDkuNXY5LjNMMCAyMC41ek0xMC44IDEyLjVIMjRWMjRsLTEzLjItMS44eiIvPjwvc3ZnPgo%3D)](https://github.com/ldg030201/dong-csu/releases?q=tag%3Awin-v)
![License](https://img.shields.io/badge/license-MIT-9FC4EE?labelColor=0E1B2E)
![Dependencies](https://img.shields.io/badge/dependencies-0-57CC85?labelColor=0E1B2E)

<img src="mac/docs/screenshot.png" width="620" alt="HUD">

<sub>화면은 macOS 판입니다.</sub>

</div>

---

Claude Code를 쓰다 보면 "지금 한도를 얼마나 썼지?"가 계속 궁금해진다. 확인하려면
하던 걸 멈추고 `/usage`를 쳐야 한다. 그 숫자를 화면 구석에 그냥 띄워둔다.

## 어느 쪽을 쓰시나요

| | [![macOS](https://img.shields.io/badge/macOS-0E1B2E?logo=apple&logoColor=white)](mac/README.md) | [![Windows](https://img.shields.io/badge/Windows-0078D4?logo=data:image%2Fsvg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0iI2ZmZmZmZiI%2BPHBhdGggZD0iTTAgMy41IDkuNSAyLjJ2OS4zSDB6TTEwLjggMiAyNCAwdjExLjVIMTAuOHpNMCAxMi41aDkuNXY5LjNMMCAyMC41ek0xMC44IDEyLjVIMjRWMjRsLTEzLjItMS44eiIvPjwvc3ZnPgo%3D)](win/README.md) |
| --- | --- | --- |
| **필요한 버전** | **macOS 14 (Sonoma) 이상** | **Windows 10 1809 이상** 또는 Windows 11 |
| 상주하는 곳 | 메뉴바 | 트레이 |
| 설치 | Homebrew | WinGet · 설치 exe |
| 업데이트 | 터미널에서 `brew upgrade` | 앱이 스스로 |
| 지금 버전 | 2.2.0 | 2.1.0 |
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

## 만드는 사람을 위해

**테스트판과 정식판을 따로 띄워 놓고 개발한다.** 번들 ID가 달라서 설정도 창 위치도
섞이지 않고, 쓰던 앱을 켜 둔 채로 고칠 수 있다. 테스트판은 마스코트가 보라색이라
한눈에 구분된다.

| | |
| --- | --- |
| [개발](docs/development.md) | 테스트판 만드는 법, 정식판과 뭐가 다른지, 화면 없이 확인하는 통로 |
| [작업 규칙](CLAUDE.md) | 버전 자리 · 변경 내역 문구 · 커밋 |
| [macOS 개발](mac/docs/development.md) | 빌드 · 렌더 통로 · 코드 서명 |
| [Windows 개발](win/CLAUDE.md) | 프로젝트 구성 · 진단 통로 · 배포 |

**커밋 제목 맨 앞에 어느 판인지 붙인다.** 두 판의 기록이 한 줄기로 섞여서, 경로를
펴 보지 않아도 갈라 볼 수 있어야 한다.

```
[Mac] ✨ 펫 모드에 설정·새로고침 버튼 추가
[Win] 🐛 배율이 100%가 아닌 화면에서 위치가 초기화되던 문제 수정
📝 커밋 앞머리 규칙 추가
```

`mac/` 만 고쳤으면 `[Mac]`, `win/` 만 고쳤으면 `[Win]`, 양쪽에 걸리거나 어느 쪽도
아니면(`shared/` · 뿌리 문서 · `.github/`) 안 붙인다. 자세한 건 [작업 규칙](CLAUDE.md#커밋).

## 라이선스

[MIT](LICENSE)
