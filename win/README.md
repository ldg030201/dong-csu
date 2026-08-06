<div align="center">

<img src="../mac/docs/icon.png" width="112" alt="DongCSU">

# DongCSU for Windows

**Claude 사용량을 화면 위에 항상 띄워두는 Windows 앱**

![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-3A72C4)

</div>

---

> [!NOTE]
> **여기는 Windows 판입니다.** 맥을 쓰신다면 → [**macOS 판**](../mac/README.md)

> [!WARNING]
> **아직 배포 전입니다.** 기능은 다 만들었지만 릴리스를 올리지 않았습니다.
> 아래 [진행 상황](#진행-상황)을 보세요.

## 이런 앱

Claude Code를 쓰다 보면 "지금 한도를 얼마나 썼지?"가 계속 궁금해진다. 확인하려면
하던 걸 멈추고 `/usage`를 쳐야 한다. 그 숫자를 화면 구석에 그냥 띄워둔다.

- 🔵 **이중 링** — 바깥이 5시간 세션, 안쪽이 7일 주간
- 🎨 사용률이 오를수록 **초록 → 노랑 → 빨강**으로 연속해서 변한다
- ⏱️ 초기화까지 남은 시간, 다음 조회까지 남은 시간
- 🦉 **[픽셀 마스코트](../docs/characters/README.md)** — 맥 판과 **같은 그림**을 쓴다
- 🖥️ 항상 위에. 작업 표시줄 없이 트레이에만

## 진행 상황

| | 상태 |
| --- | --- |
| HUD (이중 링 · 사용률 · 남은 시간) | 만듦 |
| 부엉이 마스코트 (기분 4가지) | 만듦 |
| 트레이 아이콘과 메뉴 | 만듦 |
| 사용량 조회 · 자격 증명 읽기 | 만듦 |
| 설정 창 (상태 · 표시 · 계정 · 버전) | 만듦 |
| 로그인할 때 자동 시작 | 만듦 |
| 자체 업데이트 | 만듦 |
| 펫 모드 (혼자 돌아다니기 · 커서 피하기) | 나중에 |

맥 판 2.1.0 기준으로 옮겼습니다. **펫 모드는 첫 배포에 넣지 않습니다** —
창을 스스로 움직이는 부분이라 맥과 구현이 가장 많이 달라서, 나머지가 자리 잡은
뒤에 따로 합니다.

## 설치 (준비되면)

### WinGet

윈도우 11에는 이미 깔려 있습니다. 윈도우 10이면 스토어에서 "앱 설치 관리자"를 받으세요.

```powershell
winget install ldg030201.DongCSU
```

### 직접 받기

[릴리스](https://github.com/ldg030201/dong-csu/releases)에서 `DongCSU-Setup.exe` 를 받습니다.

**관리자 권한을 묻지 않습니다.** 사용자 폴더에 깔리고, 새 버전이 나오면
앱이 알아서 받아서 다시 뜹니다. 맥 판처럼 터미널을 열 필요가 없습니다.

> [!IMPORTANT]
> **"Windows에서 PC를 보호했습니다" 창이 뜹니다.** 코드 서명 인증서를 쓰지 않아서
> 그렇습니다(개인 개발자에게는 매년 수십만 원이 듭니다). **추가 정보 → 실행**을 누르면
> 됩니다. 소스와 빌드 과정은 전부 공개돼 있으니
> [워크플로 기록](https://github.com/ldg030201/dong-csu/actions)에서 이 exe 가 어떤
> 커밋으로 만들어졌는지 확인할 수 있습니다.

### 요구 사항

- Windows 10 1809 이상 또는 Windows 11
- **Claude Code에 로그인되어 있어야 합니다** — 이 앱은 Claude Code가 저장해 둔
  자격 증명을 읽습니다. 따로 로그인하지 않습니다
- .NET 런타임은 따로 안 깔아도 됩니다 (앱에 들어 있습니다). 그래서 설치본이
  **150MB쯤** 됩니다 — 맥 판이 가벼운 건 macOS 에 Swift 런타임이 이미 있어서입니다

## 맥 판과 다른 점

| | macOS | Windows |
| --- | --- | --- |
| 버전 | 2.1.0 | **1.0.0부터 새로 셉니다** |
| 설치 | Homebrew | WinGet · 설치 exe |
| 업데이트 | 터미널에서 `brew upgrade` | **앱이 스스로** |
| 자격 증명 | keychain | Claude Code 설정 파일 |
| 상주 | 메뉴바 | 트레이 |

버전을 따로 세는 이유는 **고쳐야 할 버그가 서로 다르기 때문**입니다. 맥에서 난
문제가 윈도우에는 없고, 그 반대도 마찬가지라서 번호를 맞추면 오히려 헷갈립니다.

## 만들기

[.NET 10 SDK](https://dotnet.microsoft.com/download) 가 필요합니다.

```powershell
dotnet test
dotnet build src/DongCSU.App -c Release
dotnet run  --project src/DongCSU.App
```

화면이 없는 부분(`DongCSU.Core`)은 맥·리눅스에서도 빌드·테스트됩니다.
자세한 건 [작업 규칙](CLAUDE.md).

## 문서

| | |
| --- | --- |
| [작업 규칙](CLAUDE.md) | 프로젝트 구성, 진단 통로, 배포 |
| [캐릭터](../docs/characters/README.md) | 마스코트 목록 · [🦉 부엉이](../docs/characters/owl.md) |
| [나눠 쓰는 데이터](../shared/README.md) | 맥과 같은 그림을 그리는 방법 |

## 라이선스

[MIT](../LICENSE)
