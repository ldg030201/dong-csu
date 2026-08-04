# 캐릭터

[← README](../../README.md)

HUD 링 가운데에 들어가는 그림 중 **이 앱이 직접 만든 것들**이다.
Claude 쪽 그림(Clawd, Claude 아이콘, 버스트 마크)은 출처가 달라서 여기 넣지 않는다.

| | 캐릭터 | 상태 | 문서 |
| --- | --- | --- | --- |
| <img src="owl/idle.gif" width="72" alt="부엉이"> | **부엉이** | 기본값 | [owl.md](owl.md) |

## 캐릭터를 하나 더 만들 때

1. 그림을 `Sources/DongMCU/<이름>Mark.swift`에 문자열 그리드로 넣는다
2. [`ClaudeIconStyle`](../../Sources/DongMCU/ClaudeIcon.swift)에 항목을 더하고 `group`을 `.character`로 둔다
3. 움직일 거면 기분 목록과 프레임을 만든다 ([부엉이 문서](owl.md#기분)의 구조를 그대로 따르면 된다)
4. 이 폴더에 `<이름>.md`를 만들고 위 표에 한 줄 더한다

설정 창의 **아이콘** 탭과 우클릭 메뉴는 `ClaudeIconStyle.allCases`를 그대로 훑기 때문에
목록을 따로 손볼 자리가 없다.
