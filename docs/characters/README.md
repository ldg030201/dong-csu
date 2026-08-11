# 캐릭터

[← README](../../README.md)

HUD 링 가운데(그리고 펫 모드)에 들어가는 그림 중 **이 앱이 직접 만든 것들**이다.
Claude 쪽 그림(Clawd, Claude 아이콘, 버스트 마크)은 출처가 달라서 여기 넣지 않는다.

> [!IMPORTANT]
> **Claude 쪽 그림에는 애니메이션을 넣지 않는다.** 저작권이 Anthropic에 있어서
> 우리가 새 자세를 만들어 붙일 그림이 아니다. 움직이는 건 여기 있는 캐릭터뿐이고,
> 그 판정은 [`ClaudeIconStyle.isAnimated`](../../mac/Sources/DongCSU/ClaudeIcon.swift)에 있다.

| | 캐릭터 | 상태 | 문서 |
| --- | --- | --- | --- |
| <img src="owl/idle.gif" width="72" alt="부엉이"> | **부엉이** | 기본값 | [owl.md](owl.md) |
| | **부엉이 오리지널** | 코드로 그린 첫 판 | [owl.md](owl.md) |

## 캐릭터를 하나 더 만들 때

**그림 한 장을 넣는다.** 24칸에 자세를 나눠 담은 시트를 규격대로 만들어 넣으면
HUD와 펫이 그걸 읽는다. 코드는 건드리지 않는다 — 규격·프롬프트·다듬는 법은
**[캐릭터 만들기](making.md)**.

앞으로 캐릭터를 더하는 것은 전부 이쪽이다. 아래는 **오리지널 부엉이 한 마리만 쓰는**
옛 길이라, 새 캐릭터에는 쓰지 않는다.

<details>
<summary>코드로 그리는 길 (오리지널 부엉이가 쓴다)</summary>

1. 그림을 `Sources/DongCSU/<이름>Mark.swift`에 문자열 그리드로 넣는다
2. [`ClaudeIconStyle`](../../mac/Sources/DongCSU/ClaudeIcon.swift)에 항목을 더하고 `group`을 `.character`로 둔다
3. 움직일 거면 기분 목록과 프레임을 만들고 `isAnimated`에 한 줄 더한다
   ([부엉이 문서](owl.md#기분)의 구조를 그대로 따르면 된다)
4. 이 폴더에 `<이름>.md`를 만들고 위 표에 한 줄 더한다

**3번은 캐릭터를 만들자마자 하지 않아도 된다.** 자세와 기분을 만들기 전까지는
정지 그림이고, `isAnimated`가 `false`면 애니메이터가 아예 돌지 않는다. 그래서
`group == .character`로 판정하지 않고 케이스마다 따로 적는다.

파츠를 겹쳐 매 틱 자세를 계산하기 때문에 그림으로는 못 담는 것이 나온다 — 끌 때
몸·얼굴·다리가 한 틱씩 늦게 따라오는 시차가 그것이다. 메뉴바 아이콘·앱 아이콘과
[`shared/owl.json`](../../shared/owl.json)도 계속 이 코드를 쓴다.

</details>

설정 창의 **아이콘** 탭과 우클릭 메뉴는 `ClaudeIconStyle.allCases`를 그대로 훑기 때문에
목록을 따로 손볼 자리가 없다.
