<div align="center">

<img src="../mac/docs/icon.png" width="112" alt="DongCSU">

# DongCSU for Windows

**Claude 사용량을 화면 위에 항상 띄워두는 Windows 앱**

![Windows](https://img.shields.io/badge/Windows-10%2B-F6A623?labelColor=0078D4&logo=data:image%2Fsvg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0iI2ZmZmZmZiI%2BPHBhdGggZD0iTTAgMy41IDkuNSAyLjJ2OS4zSDB6TTEwLjggMiAyNCAwdjExLjVIMTAuOHpNMCAxMi41aDkuNXY5LjNMMCAyMC41ek0xMC44IDEyLjVIMjRWMjRsLTEzLjItMS44eiIvPjwvc3ZnPgo%3D)
[![Release](https://img.shields.io/github/v/release/ldg030201/dong-csu?filter=win-v*&color=F6A623&label=release&labelColor=0E1B2E)](https://github.com/ldg030201/dong-csu/releases?q=tag%3Awin-v)
![License](https://img.shields.io/badge/license-MIT-9FC4EE?labelColor=0E1B2E)

</div>

---

> [!NOTE]
> **여기는 Windows 판입니다.** 맥을 쓰신다면 → [**macOS 판**](../mac/README.md)

## ✨ 이런 앱

Claude Code를 쓰다 보면 "지금 한도를 얼마나 썼지?"가 계속 궁금해진다.
확인하려면 하던 걸 멈추고 `/usage`를 쳐야 한다. 그 숫자를 화면 구석에 그냥 띄워둔다.

- 🔵 **이중 링** — 바깥이 5시간 세션, 안쪽이 7일 주간
- 🎨 사용률이 오를수록 **초록 → 노랑 → 빨강**으로 연속해서 변한다. 숫자 앞의 색점이
  어느 링 얘기인지 이어준다
- ⏱️ 초기화까지 남은 시간, 다음 조회까지 남은 시간
- 🔔 새 버전이 나오면 알려주고, **앱이 스스로 받아서 갈아 끼운다**
- 🦉 **[픽셀 마스코트](../docs/characters/README.md)** — 한도가 차면 지치고, 조회가 끊기면 색이 빠진다.
  Clawd · Claude 아이콘 · 버스트 마크로 바꿀 수도 있다
- 🔑 **토큰이 만료되면 스스로 갱신한다** — Claude Code를 켜 두지 않아도 계속 들어온다
- 🖥️ 항상 위에. 작업 표시줄 없이 트레이에만
- ⚙️ **로그인할 때 자동 시작** — 관리자 권한을 묻지 않는다

더블클릭하면 접었다 펴지고, **마스코트를 더블클릭하면** 펫 모드로 들어간다.
크기는 4단계, 배경 불투명도도 고를 수 있다.

가운데 마스코트는 상태에 따라 움직인다. **그림은 맥 판과 같은 것을 쓴다** —
자세한 건 **[캐릭터](../docs/characters/README.md)**.

<table>
<tr>
<td align="center" width="20%"><img src="../docs/characters/owl/idle.gif" width="96" alt="평소"><br><sub><b>평소</b></sub></td>
<td align="center" width="20%"><img src="../docs/characters/owl/tired.gif" width="96" alt="지침"><br><sub><b>지침</b><br>세션 80%↑</sub></td>
<td align="center" width="20%"><img src="../docs/characters/owl/exhausted.gif" width="96" alt="탈진"><br><sub><b>탈진</b><br>세션 95%↑</sub></td>
<td align="center" width="20%"><img src="../docs/characters/owl/offline.gif" width="96" alt="끊김"><br><sub><b>끊김</b><br>조회 실패</sub></td>
<td align="center" width="20%"><img src="../docs/characters/owl/dragged.gif" width="96" alt="끌림"><br><sub><b>끌림</b><br>드래그 중</sub></td>
</tr>
<tr>
<td align="center"><img src="../docs/characters/owl/dizzy.gif" width="96" alt="어지러움"><br><sub><b>어지러움</b><br>마구 흔들면</sub></td>
<td align="center"><img src="../docs/characters/owl/walk.gif" width="96" alt="걷기"><br><sub><b>걷기</b><br>혼자 다닐 때</sub></td>
<td align="center"><img src="../docs/characters/owl/run.gif" width="96" alt="달리기"><br><sub><b>달리기</b><br>커서가 쫓아올 때</sub></td>
<td colspan="2" align="left"><sub>펫 모드에서는 <b>혼자 돌아다닌다.</b><br>커서를 올려두면 비켜주고, 계속 쫓아가면 <b>뛰어서</b> 달아난다.<br><b>타이핑 중에는 가만히 있는다.</b> 설정 창의 <b>펫</b> 탭에서 끌 수 있다.</sub></td>
</tr>
</table>

## 설치

### WinGet

윈도우 11에는 이미 깔려 있습니다. 윈도우 10이면 스토어에서 "앱 설치 관리자"를 받으세요.

```powershell
winget install ldg030201.DongCSU
```

> [!NOTE]
> WinGet 등록은 아직입니다. 지금은 아래 "직접 받기"를 쓰세요.

### 직접 받기

[최신 릴리스](https://github.com/ldg030201/dong-csu/releases?q=tag%3Awin-v&expanded=true)에서
**`DongCSU-win-Setup.exe`** 를 받습니다 (72MB).

**관리자 권한을 묻지 않습니다.** 사용자 폴더(`%LOCALAPPDATA%\DongCSU`)에 깔리고,
시작 메뉴와 바탕화면에 바로가기가 생깁니다 — 이 앱은 트레이에만 뜨기 때문에
바로가기가 없으면 다시 켤 곳을 찾기 어렵습니다.

새 버전은 앱이 하루에 한 번 확인해서 **HUD 왼쪽 위에 파란 점**으로 알려줍니다.
설정 창의 **버전** 탭에서 **업데이트**를 누르면 받아서 깔고 저절로 다시 뜹니다 —
맥 판처럼 터미널을 열 필요가 없습니다.

> [!IMPORTANT]
> **"Windows에서 PC를 보호했습니다" 창이 뜹니다.** 코드 서명 인증서를 쓰지 않아서
> 그렇습니다(개인 개발자에게는 매년 수십만 원이 듭니다). **추가 정보 → 실행**을 누르면
> 됩니다. 소스와 빌드 과정은 전부 공개돼 있으니
> [워크플로 기록](https://github.com/ldg030201/dong-csu/actions)에서 이 exe 가 어떤
> 커밋으로 만들어졌는지 확인할 수 있습니다.

### 요구 사항

- Windows 10 1809 이상 또는 Windows 11
- **Claude Code에 로그인되어 있어야 합니다** — 이 앱은 Claude Code가 저장해 둔
  자격 증명을 읽습니다. 따로 로그인하지 않습니다.
  **터미널은 필요 없습니다** — Claude 앱 안에 Claude Code가 들어 있어서, 앱에서
  한 번 로그인하면 `%USERPROFILE%\.claude\.credentials.json` 이 만들어집니다.
  설정 창의 **계정** 탭이 그 파일을 찾았는지 알려줍니다
- .NET 런타임은 따로 안 깔아도 됩니다 (앱에 들어 있습니다). 받는 파일은 72MB 이고
  깔고 나면 **155MB쯤** 씁니다 — 맥 판이 가벼운 건 macOS 에 Swift 런타임이 이미
  있어서입니다

## 맥 판과 다른 점

| | macOS | Windows |
| --- | --- | --- |
| 설치 | Homebrew | WinGet · 설치 exe |
| 업데이트 | 터미널에서 `brew upgrade` | **앱이 스스로** |
| 자격 증명 | keychain | Claude Code 설정 파일 (+ 만료되면 스스로 갱신) |
| 상주 | 메뉴바 | 트레이 |
| CPU 표시 | 코어 하나 기준 | **전체 코어 기준** (작업 관리자와 맞춤) |

**버전은 판마다 따로 셉니다.** 고쳐야 할 버그가 서로 달라서, 맥에서 난 문제가
윈도우에는 없고 그 반대도 마찬가지입니다. 번호를 맞추면 한쪽은 고친 것이 없는데
번호만 올라갑니다. 지금 버전은 맨 위 배지에 있습니다.

## 만들기

[.NET 10 SDK](https://dotnet.microsoft.com/download) 가 필요합니다.

```powershell
dotnet test
./build.ps1                    # 정식판
./build.ps1 -Test -Run         # 테스트판을 만들고 바로 띄운다
```

**개발은 테스트판으로 합니다.** 설정·기록·자동 시작이 정식판과 갈려 있어서 둘을
동시에 띄워 비교할 수 있고, 테스트판은 자체 업데이트를 걸지 않습니다.

화면이 없는 부분(`DongCSU.Core`)은 맥·리눅스에서도 빌드·테스트됩니다.
자세한 건 [작업 규칙](CLAUDE.md).

## 문서

| | |
| --- | --- |
| [작업 규칙](CLAUDE.md) | 프로젝트 구성, 진단 통로, 배포 |
| [이어받기](docs/handoff.md) | 윈도우 쪽에서 개발을 이어받을 때 |
| [캐릭터](../docs/characters/README.md) | 마스코트 목록 · [🦉 부엉이](../docs/characters/owl.md) |
| [나눠 쓰는 데이터](../shared/README.md) | 맥과 같은 그림을 그리는 방법 |

## 라이선스

[MIT](../LICENSE)
