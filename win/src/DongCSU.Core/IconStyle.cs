namespace DongCSU.Core;

/// <summary>HUD 링 한가운데에 그릴 그림.</summary>
public enum IconStyle
{
    /// <summary>
    /// **처음에 코드로 만든 부엉이.**
    ///
    /// 파츠를 겹쳐 매 틱 자세를 계산해서, 그림으로는 못 담는 것이 있다 — 끌 때
    /// 몸 → 얼굴 → 다리가 한 틱씩 늦게 따라오는 시차가 그것이다. 트레이 아이콘과
    /// <c>shared/owl.json</c> 도 계속 이 코드를 쓴다. **지우지 않는다.**
    /// </summary>
    Owl,
    /// <summary>Claude Code 마스코트 Clawd.</summary>
    Clawd,
    /// <summary>Claude 앱 아이콘.</summary>
    AppIcon,
    /// <summary>직접 그린 벡터 버스트 마크.</summary>
    Mark,
    /// <summary>
    /// 그림 파일 한 장으로 도는 부엉이.
    ///
    /// 앱에 규격 시트가 구워져 있고, **파일을 바꾸면 캐릭터가 바뀐다.**
    /// 앞으로 캐릭터를 더하는 것은 전부 이쪽이다.
    ///
    /// **목록 맨 끝에 둔다.** 저장된 설정이 이름이 아니라 숫자로 적혀 있어서,
    /// 가운데에 끼우면 이미 저장된 값이 다른 그림을 가리킨다.
    /// </summary>
    OwlSheet,
    /// <summary>
    /// 그림 파일 한 장으로 도는 라쿤.
    ///
    /// **부엉이와 코드를 하나도 안 나눠 쓴다.** 같은 규격 시트를 읽는 통로에 파일
    /// 이름만 다르게 물린 것이라, 캐릭터를 더하는 값은 <see cref="IconStyleExtensions.SheetResource"/>
    /// 한 줄이다.
    ///
    /// **맥과 달리 맨 끝에 둔다.** 맥은 문자열로 저장해서 가운데에 끼워도 되지만
    /// 여기는 위의 이유로 끝에만 더한다.
    /// </summary>
    RaccoonSheet,
}

/// <summary>
/// 아이콘을 묶는 단위.
///
/// dong-csu 가 직접 만든 캐릭터와 Claude 쪽 그림은 출처가 다르다. 섞어 두면 어느 게
/// 이 앱 것인지 알 수 없어서 나눠 보여준다.
/// </summary>
public enum IconStyleGroup
{
    Character,
    Claude,
    /// <summary>
    /// 예전 것. **접어 둔다** — 지우지는 않되 눈에 먼저 들어오지 않게 한다.
    ///
    /// 새로 오는 사람에게는 고를 것이 하나 늘어나는 것뿐이라 헷갈리고, 쓰던 사람은
    /// 없어졌다고 여기면 곤란하다. 접어 두면 찾는 사람만 찾는다.
    /// </summary>
    Original,
}

public static class IconStyleExtensions
{
    public static IconStyleGroup Group(this IconStyle style) => style switch
    {
        // **캐릭터 이름을 여기 늘어놓지 않는다.** 늘어놓으면 캐릭터를 더할 때마다
        // 흩어진 자리를 다 찾아야 하고, 하나만 빠뜨리면 그 캐릭터만 엉뚱한 묶음으로
        // 간다 — `UsesSheet()` 를 만든 이유가 그거다. (`IsAnimated` 는 일부러 이름을
        // 적는다. 새로 그린 캐릭터가 자세를 갖추기 전까지는 정지 그림이라서다.)
        _ when style.UsesSheet() => IconStyleGroup.Character,
        IconStyle.Owl => IconStyleGroup.Original,
        _ => IconStyleGroup.Claude,
    };

    /// <summary>목록에 펼쳐 놓을지. 접힌 묶음은 눌러야 열린다.</summary>
    public static bool IsCollapsed(this IconStyleGroup group) => group == IconStyleGroup.Original;

    public static string Title(this IconStyle style) => style switch
    {
        IconStyle.OwlSheet => "부엉이 (dong-csu 마스코트)",
        IconStyle.RaccoonSheet => "라쿤",
        IconStyle.Owl => "부엉이 오리지널 (코드로 그린 첫 판)",
        IconStyle.Clawd => "Clawd (Claude Code 마스코트)",
        IconStyle.AppIcon => "Claude 아이콘",
        _ => "버스트 마크",
    };

    /// <summary>미리보기 타일 밑에 붙일 짧은 이름.</summary>
    public static string ShortTitle(this IconStyle style) => style switch
    {
        IconStyle.OwlSheet => "부엉이",
        IconStyle.RaccoonSheet => "라쿤",
        IconStyle.Owl => "오리지널",
        IconStyle.Clawd => "Clawd",
        IconStyle.AppIcon => "Claude 아이콘",
        _ => "버스트",
    };

    /// <summary>
    /// 움직이는 그림인지.
    ///
    /// **Claude 쪽 그림에는 애니메이션을 넣지 않는다.** 저작권이 Anthropic 에 있어서
    /// 우리가 새 자세를 만들어 붙일 그림이 아니다. 움직이는 건 이 앱이 직접 만든
    /// 캐릭터뿐이다.
    ///
    /// <c>Group</c> 으로 판단하지 않는다. 캐릭터를 새로 그려도 자세와 기분을 만들기
    /// 전까지는 정지 그림이라, 그때 여기에 한 줄을 더하는 게 맞다.
    /// </summary>
    public static bool IsAnimated(this IconStyle style) =>
        style is IconStyle.Owl or IconStyle.OwlSheet or IconStyle.RaccoonSheet;

    /// <summary>
    /// 앱에 구워 둔 규격 시트의 리소스 이름. 시트로 도는 그림만 값이 있다.
    ///
    /// **캐릭터를 더할 때 손대는 곳이 여기다.** 그림을 맥 <c>Resources/&lt;이름&gt;.png</c> 로
    /// 두고 <c>DongCSU.App.csproj</c> 에 <c>EmbeddedResource</c> 한 줄과 여기 한 줄을
    /// 더하면 나머지(그리기 · 미리보기 · 창에 붙기)가 전부 따라온다.
    /// </summary>
    public static string? SheetResource(this IconStyle style) => style switch
    {
        IconStyle.OwlSheet => "mascot",
        IconStyle.RaccoonSheet => "raccoon",
        _ => null,
    };

    /// <summary>
    /// 그림 시트로 도는지. 창에 붙는 자세가 있는 것은 이쪽뿐이다.
    ///
    /// **캐릭터 이름으로 견주지 않는다.** <c>== IconStyle.OwlSheet</c> 로 적어 두면
    /// 캐릭터를 더할 때마다 흩어진 자리를 다 찾아 고쳐야 하고, 하나만 빠뜨려도 그
    /// 캐릭터에서만 붙이기가 조용히 죽는다.
    /// </summary>
    public static bool UsesSheet(this IconStyle style) => style.SheetResource() is not null;

    /// <summary>
    /// 아직 다듬는 중인 캐릭터인지. 타일 위에 <c>beta</c> 딱지가 붙는다.
    ///
    /// **고르는 것을 막지는 않는다.** 설정 탭의 <c>beta</c> 와 같은 뜻이다 — 써도
    /// 되는데 아직 손볼 데가 남았다는 표시다.
    /// </summary>
    public static bool IsBeta(this IconStyle style) => style is IconStyle.RaccoonSheet;

    public static string Title(this IconStyleGroup group) => group switch
    {
        IconStyleGroup.Character => "캐릭터",
        IconStyleGroup.Original => "오리지널",
        _ => "Claude",
    };
}

/// <summary>
/// Claude Code 마스코트 Clawd.
///
/// 그리드는 Claude Code 가 터미널에 그리는 블록 아트를 그대로 옮긴 것이다. 원본은
/// 4행이고 각 행의 <c>█</c> 은 칸 전체 / <c>▄</c> 는 칸 아래 절반만 칠한다. 터미널 칸은
/// 가로:세로가 1:2 라서, 아래 절반만 칠한 칸의 윗절반이 눈이 된다. 그래서 4행 × 11열
/// 아트를 **8행 × 11열 정사각 픽셀 그리드**로 펼쳤다.
/// </summary>
public static class ClawdMark
{
    public const int Columns = 11;
    public const int Lines = 8;

    public const string BodyHex = "#D77757";

    public static readonly string[] Rows =
    [
        ".#########.",
        ".#########.",
        "##.#####.##",
        "###########",
        ".#########.",
        ".#########.",
        ".#.#...#.#.",
        ".#.#...#.#.",
    ];

    /// <summary>눈 자리. 몸통에 둘러싸인 빈 칸이라 따로 어둡게 칠한다.</summary>
    public static readonly (int X, int Y)[] Eyes = [(2, 2), (8, 2)];
}
