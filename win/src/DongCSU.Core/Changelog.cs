using System.Text.Json;
using System.Text.Json.Serialization;

namespace DongCSU.Core;

/// <summary>변경 한 줄이 어느 갈래인지. 항목 앞에 딱지로 붙는다.</summary>
[JsonConverter(typeof(ChangeKindConverter))]
public enum ChangeKind { New, Improve, Change, Fix, Remove }

/// <summary>
/// 갈래를 <c>"new"</c> 처럼 소문자로 싣는다.
///
/// **JSON 의 나머지가 camelCase 라 여기만 대문자로 시작하면 눈에 걸린다.** 읽을 때는
/// 대소문자를 가리지 않으므로 이미 나간 파일과도 어긋나지 않는다.
/// </summary>
internal sealed class ChangeKindConverter()
    : JsonStringEnumConverter<ChangeKind>(JsonNamingPolicy.CamelCase);

public static class ChangeKindExtensions
{
    public static string Title(this ChangeKind kind) => kind switch
    {
        ChangeKind.New => "신규",
        ChangeKind.Improve => "개선",
        ChangeKind.Change => "변경",
        ChangeKind.Fix => "오류",
        _ => "제거",
    };
}

/// <summary>변경 한 줄.</summary>
public sealed record ChangelogNote(ChangeKind Kind, string Text)
{
    public static ChangelogNote New(string text) => new(ChangeKind.New, text);
    public static ChangelogNote Improve(string text) => new(ChangeKind.Improve, text);
    public static ChangelogNote Change(string text) => new(ChangeKind.Change, text);
    public static ChangelogNote Fix(string text) => new(ChangeKind.Fix, text);
    public static ChangelogNote Remove(string text) => new(ChangeKind.Remove, text);
}

/// <summary>
/// 기능 단위 묶음. 화면·메뉴 이름을 그대로 쓴다("펫 모드", "마스코트").
///
/// 한 버전에 스무 줄이 쌓이면 평평한 목록으로는 무엇이 달라졌는지 안 잡힌다.
/// 쓰는 사람은 자기가 쓰는 기능만 보면 되므로 그 단위로 묶는다.
/// </summary>
public sealed record ChangelogGroup
{
    public required string Title { get; init; }

    /// <summary>
    /// 이 묶음이 어느 설정 탭 이야기인지(사이드바의 탭 이름).
    ///
    /// 제목 앞에 **그 탭에 실제로 붙어 있는 아이콘**이 그대로 나온다. 여기에 아이콘을
    /// 직접 적지 않는 이유가 그거다 — 탭 아이콘을 바꾸면 변경 내역도 같이 바뀌어야 한다.
    ///
    /// **탭에 없는 것은 null.** 마스코트·HUD·설치처럼 메뉴가 아닌 이야기는 공통 아이콘
    /// 하나로 묶는다.
    /// </summary>
    public string? Tab { get; init; }

    /// <summary>이 묶음 자체가 이번에 새로 생긴 기능인지. 제목 오른쪽에 "신규"가 붙는다.</summary>
    public bool IsNew { get; init; }

    public required IReadOnlyList<ChangelogNote> Notes { get; init; }
}

public sealed record ChangelogEntry
{
    public required string Version { get; init; }
    /// <summary>아직 안 나간 항목은 null.</summary>
    public string? Date { get; init; }

    /// <summary>
    /// 2.3.0 부터. 기능별로 묶고 항목마다 갈래를 단다.
    ///
    /// 옛 항목은 null 이고, 그때는 화면이 <see cref="Notes"/> 를 그대로 늘어놓는다.
    /// **이미 나간 버전은 뒤늦게 나누지 않는다** — 사용자가 그때 본 것과 달라진다.
    /// </summary>
    public IReadOnlyList<ChangelogGroup>? Groups { get; init; }

    /// <summary>
    /// 평평한 목록.
    ///
    /// **지우면 안 된다.** 2.2.0 이하가 같은 JSON 을 받아보는데 그쪽은 이것만 읽는다.
    /// 묶음을 쓰는 항목에서는 여기서 **만들어 낸다** — 두 곳에 손으로 적으면 어긋난다.
    /// </summary>
    public IReadOnlyList<string> Notes
    {
        get => notes ?? Flatten(Groups);
        init => notes = value;
    }

    private readonly IReadOnlyList<string>? notes;

    private static string[] Flatten(IReadOnlyList<ChangelogGroup>? groups) => groups is null
        ? []
        : [.. groups.SelectMany(group => group.Notes.Select(note => $"[{group.Title}] {note.Text}"))];
}

public sealed record ChangelogFeed
{
    public required IReadOnlyList<ChangelogEntry> Entries { get; init; }
}

/// <summary>
/// 원격 내역을 받아 본 결과. 성공이면 <see cref="Entries"/>, 실패면 <see cref="Failure"/> 만 찬다.
///
/// <see cref="Usage.UsageResult"/> 와 같은 모양이다 — 조회 실패를 다루는 자리가 이미 그렇게
/// 생겼는데 여기만 다른 모양을 쓰면 부르는 쪽이 두 가지를 외워야 한다.
/// </summary>
public sealed record ChangelogFetch(IReadOnlyList<ChangelogEntry>? Entries, string? Failure)
{
    public static ChangelogFetch Ok(IReadOnlyList<ChangelogEntry> entries) => new(entries, null);
    public static ChangelogFetch Failed(string reason) => new(null, reason);
}

/// <summary>
/// 버전별 변경 내역.
///
/// 설정 창의 **버전** 탭이 이걸 그대로 보여준다. 쓰는 방법은 <c>../CLAUDE.md</c> 참고 —
/// 기능 단위로 한 줄씩, "추가 / 수정 / 변경" 처럼 명사형으로 끝맺는다.
///
/// **맥과 따로 센다.** 고쳐야 할 버그가 서로 달라서 번호를 맞추면 한쪽은 고친 게
/// 없는데 번호만 올라간다.
/// </summary>
public static class Changelog
{
    /// <summary>원격 내역 주소. 릴리스 API 대신 이 파일 하나로 최신 버전과 내역을 함께 받는다.</summary>
    public static readonly Uri FeedUrl =
        new("https://raw.githubusercontent.com/ldg030201/dong-csu/main/win/docs/changelog.json");

    public static readonly IReadOnlyList<ChangelogEntry> Entries =
    [
        new ChangelogEntry
        {
            Version = "2.5.0",
            Date = null,
            Groups =
            [
                new ChangelogGroup
                {
                    Title = "측정",
                    Tab = "measure",
                    IsNew = true,
                    Notes =
                    [
                        ChangelogNote.New("시작·중지 사이에 쓴 한도 %p와 토큰 수 측정 (설정 창 측정 탭)"),
                        ChangelogNote.New("모델별 한도·토큰 표시"),
                        ChangelogNote.New("토큰 합계와 캐시 제외 합계 표시 (기본은 캐시 제외)"),
                        ChangelogNote.New("일시정지·계속 (세워 둔 동안의 시간과 사용량은 빼고 셈)"),
                        ChangelogNote.New("측정 기록 목록 (중지하면 남고, 누르면 그때 값을 펼쳐 봄)"),
                        ChangelogNote.New("기록을 하나씩 또는 전부 지우기 (지우기 전에 한 번 물어봄)"),
                        ChangelogNote.New("HUD·펫 모드의 측정 버튼 (설정 버튼 왼쪽, 누르면 측정 화면이 열림)"),
                        ChangelogNote.New("토큰은 Claude Code 기록에서만 셈 (WSL 안에서 쓰면 못 셈)"),
                        ChangelogNote.New("아직 다듬는 중이라 beta 표시 (숫자가 실제와 어긋날 수 있음)"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "마스코트",
                    Notes =
                    [
                        ChangelogNote.Remove("부엉이 아이콘의 베타 딱지 제거"),
                    ],
                },
            ],
        },
        new ChangelogEntry
        {
            Version = "2.4.0",
            Date = "2026-08-20",
            Groups =
            [
                new ChangelogGroup
                {
                    Title = "조회",
                    Tab = "status",
                    Notes =
                    [
                        ChangelogNote.Fix("요청 제한에 걸리면 조회 주기에 따라 최대 30분까지 멈추던 문제 수정 (1분에서 5분까지로 고정)"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "펫 모드",
                    Tab = "pet",
                    Notes =
                    [
                        ChangelogNote.Change("세션 한도를 거의 다 쓰면 혼자 걸어다니지 않도록 변경"),
                        ChangelogNote.Change("커서 피하기 판정을 마스코트 둘레로 좁힘"),
                        ChangelogNote.Fix("어지러운 동안이나 우클릭 메뉴가 떠 있는 동안에도 걸어다니던 문제 수정"),
                        ChangelogNote.Fix("마스코트나 아래 버튼을 누르고만 있어도 매달린 자세가 되던 문제 수정"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "토큰",
                    Tab = "account",
                    Notes =
                    [
                        ChangelogNote.Improve("갱신한 토큰이 사는 폴더에 낯선 권한이 있으면 지우도록 개선"),
                        ChangelogNote.Improve("조회할 때마다 로그인 정보 파일을 다시 읽던 것을 개선 (WSL 이 10분마다 깨어나지 않음)"),
                        ChangelogNote.Fix("잠깐 인터넷이 끊겼을 때 갱신해 둔 토큰까지 버리고 재로그인을 요구하던 문제 수정"),
                        ChangelogNote.Fix("토큰을 갱신한 직후에도 계정 탭이 만료됐다고 표시하던 문제 수정"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "업데이트",
                    Tab = "version",
                    Notes =
                    [
                        ChangelogNote.New("받는 동안 진행 상황 표시"),
                        ChangelogNote.New("갈아끼우다 멈췄을 때 강제 종료"),
                        ChangelogNote.New("새 버전이 있으면 설정 창 버전 탭에 표시"),
                        ChangelogNote.Change("다 받은 뒤에 물어보도록 변경 (\"지금 다시 띄우기\" / \"나중에\")"),
                        ChangelogNote.Change("자동 확인을 켜면 그 자리에서 한 번 확인하도록 변경"),
                        ChangelogNote.Fix("확인에 실패하면 아무 흔적도 남지 않던 문제 수정 (실패 사유와 마지막 확인 시각 표시)"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "마스코트",
                    Notes =
                    [
                        ChangelogNote.Improve("작게 그릴 때 뭉개지던 것을 개선"),
                        ChangelogNote.Improve("사용량 링 진행선 둘레의 번짐이 크기를 따라가도록 개선"),
                        ChangelogNote.Change("새로 그린 그림으로 교체 (옆모습이 통통해지고 눈·표정이 또렷해짐)"),
                        ChangelogNote.Change("캐릭터 애니메이션을 끄면 흔들어도 어지러워하지 않도록 변경"),
                        ChangelogNote.Fix("그림이 가로로 눌리고 맥보다 작게 그려지던 문제 수정 (들고 있을 때 특히 길쭉했음)"),
                        ChangelogNote.Fix("주간 한도를 다 써도 주간 링과 주간 점은 색이 남던 문제 수정"),
                        ChangelogNote.Fix("걸음을 멈출 때마다 눈을 감았다 뜨던 문제 수정"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "설정 창",
                    Notes =
                    [
                        ChangelogNote.Improve("창을 닫았다 열어도 보던 탭과 창 자리가 그대로이도록 개선"),
                        ChangelogNote.Improve("확인 창의 버튼에 무엇이 일어나는지 적도록 개선"),
                        ChangelogNote.Improve("맥판과 어긋나 있던 문구·색·여백을 맞춤"),
                        ChangelogNote.Improve("밝은 테마에서 아이콘 미리보기를 어두운 판 위에 그리도록 개선 (HUD 에서 보이는 대로)"),
                        ChangelogNote.Fix("변경 내역을 내리면 업데이트 버튼과 지금 버전이 밀려 나가던 문제 수정"),
                        ChangelogNote.Fix("창을 좁히면 펫·계정 탭의 오른쪽이 잘려 나가던 문제 수정"),
                        ChangelogNote.Fix("설정을 잠그는 조건이 틀렸던 문제 수정 (HUD 를 꺼 두면 크기·불투명도를 못 고르고, 펫 모드가 아닌데 펫 링은 고를 수 있었음)"),
                        ChangelogNote.Fix("모든 설정 초기화가 일부 설정을 되돌리지 않던 문제 수정"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "메뉴",
                    Notes =
                    [
                        ChangelogNote.Improve("트레이·우클릭 메뉴의 모서리를 둥글게 하고 테마에 맞춰 색을 바꾸도록 개선"),
                    ],
                },
            ],
        },
        new ChangelogEntry
        {
            Version = "2.3.0",
            Date = "2026-08-15",
            Groups =
            [
                new ChangelogGroup
                {
                    Title = "계정",
                    Tab = "account",
                    Notes =
                    [
                        ChangelogNote.New("재로그인이 필요할 때 앱에서 바로 로그인 창 띄우기 (트레이 메뉴·계정 탭)"),
                        ChangelogNote.New("플랜·한도 등급·토큰 만료 표시"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "조회",
                    Tab = "status",
                    Notes =
                    [
                        ChangelogNote.Improve("조회 사이에 최소 10초를 두어 요청 제한에 덜 걸리도록 개선"),
                        ChangelogNote.Improve("새로고침을 지금 할 수 없으면 몇 초 뒤에 되는지 표시"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "마스코트",
                    Notes =
                    [
                        ChangelogNote.Fix("새 부엉이가 눈을 깜빡이지 않던 문제 수정"),
                        ChangelogNote.Fix("주간 한도를 다 써도 눈을 깜빡이고 집어 들면 버둥거리던 문제 수정"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "펫 모드",
                    Tab = "pet",
                    Notes =
                    [
                        ChangelogNote.New("들고 있을 때 링·버튼 줄 감추기 (기본 켜짐, 펫 탭에서 끌 수 있음)"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "HUD",
                    Notes =
                    [
                        ChangelogNote.Fix("버튼에 마우스를 올려도 설명이 한 번도 뜨지 않던 문제 수정"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "설정 창",
                    Notes =
                    [
                        ChangelogNote.New("종료할 때 확인받기 (트레이 메뉴의 종료는 그대로 바로 꺼짐)"),
                    ],
                },
                new ChangelogGroup
                {
                    Title = "변경 내역",
                    Tab = "version",
                    Notes =
                    [
                        ChangelogNote.Improve("기능별로 묶고 항목마다 갈래를 달아 보기 쉽게 개선"),
                    ],
                },
            ],
        },
        new ChangelogEntry
        {
            Version = "2.2.0",
            Date = "2026-08-13",
            Notes =
            [
                "부엉이를 새로 그린 그림으로 변경 (베타 — 설정 창 아이콘 탭에서 '오리지널'로 되돌릴 수 있음)",
                "걷는 모습을 옆모습 두 박자로 개선 (다리를 벌렸다 모으며 걸음, 가는 쪽을 보고 걸음)",
                "졸릴 때 졸린 얼굴로 걷도록 개선",
                "걷던 중에 펫에서 나가면 카드 안에서 계속 걷던 문제 수정",
                "혼자 돌아다니기를 꺼도 걷던 것이 목적지까지 마저 가던 문제 수정",
                "주간 한도를 다 써도 마스코트가 계속 돌아다니던 문제 수정",
                "카운트다운·CPU 메모리 줄·버전 딱지를 잡고 창을 끌거나 두 번 눌러 접지 못하던 문제 수정",
                "버튼을 누른 채 창 밖에서 떼면 펫이 멈춘 채로 굳던 문제 수정",
                "설정 창을 열어 둔 채 조회가 들어오면 읽던 자리가 맨 위로 튀던 문제 수정",
                "트레이 메뉴가 다른 앱을 눌러도 닫히지 않던 문제 수정",
                "새 버전 확인이 실패하면 앱이 꺼질 수 있던 문제 수정",
                "로그인할 때 자동 시작이 켜져 있는데도 꺼짐으로 보이던 문제 수정",
                "메모리 사용량 개선 (작업 집합 약 11%, 개인 메모리 약 17% 감소)",
                "펫이 걷는 동안 트레이 아이콘을 초당 일곱 번씩 다시 그리던 것을 개선",
                "커서가 멀리 있을 때 회피 검사 주기를 늘려 전력 사용 개선",
            ],
        },
        new ChangelogEntry
        {
            Version = "2.1.0",
            Date = "2026-08-07",
            // 펫 모드는 이번에 처음 나간다. **만드는 동안 고친 펫 버그는 여기 적지 않는다** —
            // 사용자는 그 버그를 본 적이 없다. 2.0.1 에서 넘어오는 사람 기준으로 쓴다.
            Notes =
            [
                "펫 모드 추가 (마스코트를 두 번 누르면 들어갑니다)",
                "펫 모드에 혼자 돌아다니기 추가 (글을 쓰는 동안에는 멈춤)",
                "펫 모드에 커서 피하기 추가",
                "펫 모드에 설정·새로고침 버튼과 새 버전 표시 추가",
                "펫 모드에서 사용량 링을 언제 보여줄지 고르기 추가 (올렸을 때 · 항상 · 안 함)",
                "마스코트를 끌면 매달리고 마구 흔들면 어지러워하는 자세 추가",
                "마스코트가 지친 정도에 따라 걷는 모습이 달라지도록 추가",
                "카운트다운·CPU 메모리 줄·버전 딱지에 설명 문구 추가",
                "주간 한도를 다 쓰면 마스코트·세션 링·세션 숫자를 회색으로 표시하도록 변경",
                "펼침과 접힘 사이에서 창 크기가 부드럽게 옮겨가도록 변경",
                "배경 불투명도 기본값을 100%로 변경",
                "숨겨 두거나 화면이 잠긴 동안 마스코트 애니메이션을 멈추도록 개선",
                "설정 창에서 조작부가 설명 글을 가리던 문제 수정",
                "변경 내역이 원격 목록으로 덮여 최신 항목이 사라지던 문제 수정",
                "링과 배경 모서리가 각지게 그려지던 문제 수정",
                "접거나 펼칠 때 창이 늘 오른쪽으로 자라던 문제 수정",
            ],
        },
        new ChangelogEntry
        {
            Version = "2.0.1",
            Date = "2026-08-07",
            Notes =
            [
                "설정 창이 다른 창 뒤에 열리던 문제 수정",
                "WSL 안에서 로그인한 경우를 찾지 못하던 문제 수정",
                "로그인 정보를 못 읽은 이유를 계정 탭과 기록에 표시하도록 추가",
                "위치 초기화를 눌러도 HUD가 그대로이던 문제 수정 (주 모니터 오른쪽 위로 이동)",
            ],
        },
        new ChangelogEntry
        {
            Version = "2.0.0",
            Date = "2026-08-07",
            Notes =
            [
                "만료된 토큰을 스스로 갱신하도록 변경 (Claude Code를 켜 두지 않아도 조회)",
                "테스트판 분리 지원 (설정·자동 시작이 갈리고 자체 업데이트를 걸지 않음)",
                "이 앱이 쓰는 CPU·메모리 표시 추가 (끄기 가능, 기본 꺼짐)",
                "마스코트 움직이기 끄기 추가",
                "가운데 아이콘 고르기 추가 (부엉이 · Clawd · Claude 아이콘 · 버스트 마크)",
                "설정 창을 새로 만듦 (크기 조절 · 다크 테마 · 항목별 설명)",
                "설정 창에 배경 불투명도 조절 추가",
                "설정 창에 위치 초기화 · 모든 설정 초기화 추가",
                "설정 창 상태 탭이 스스로 갱신되도록 변경 (다음 조회까지 남은 시간 포함)",
                "지금 상태에서 쓸 수 없는 설정 항목을 흐리게 표시하도록 변경",
                "HUD에 접기·설정·새로고침 버튼 추가",
                "HUD 우클릭에서 트레이와 같은 메뉴가 뜨도록 변경",
                "세션·주간을 링과 같은 색 점으로 구분해 표시하도록 변경",
                "초기화까지 남은 시간을 줄을 나눠 항상 표시하도록 변경",
                "다음 조회까지 남은 시간 표시 추가",
                "화면 숫자가 오래됐거나 재로그인이 필요할 때 HUD에 알리도록 추가",
                "새 버전 표시를 눌러 버전 화면으로 갈 수 있도록 변경",
                "HUD 각 요소에 설명 풍선 추가",
                "펼침 방향 설정이 화면에 적용되지 않던 문제 수정",
                "새 버전 표시가 HUD 배경 밖에 찍히던 문제 수정",
                "사용률이 0%일 때 링에 아무 표시도 남지 않던 문제 수정",
                "시스템 테마를 바꿔도 HUD 색이 그대로이던 문제 수정",
                "모니터 구성이 바뀌면 HUD를 화면 안으로 되돌리도록 추가",
                "배경 불투명도 기본값을 92%로 변경",
                "조회 주기 기본값을 10분으로 변경",
                "링 굵기와 마스코트 크기를 맥판과 맞춤",
            ],
        },
        new ChangelogEntry
        {
            Version = "1.1.1",
            Date = "2026-08-06",
            Notes =
            [
                "Claude가 켜져 있는데도 토큰 만료로 조회를 포기하던 문제 수정",
                "업데이트를 누르면 앱만 꺼지고 갈아 끼워지지 않던 문제 수정",
                "만료 원인이 파일인지 서버인지 구분해서 표시하도록 변경",
                "만료 시각이 소수·문자열로 저장된 경우를 읽지 못하던 문제 수정",
                "윈도우에서 진단 출력(--probe 등)이 보이지 않던 문제 수정",
                "기록 파일 추가 (--log, 계정 탭의 기록 열기)",
            ],
        },
        new ChangelogEntry
        {
            Version = "1.1.0",
            Date = "2026-08-06",
            Notes =
            [
                "계정 탭에서 로그인 정보를 찾았는지와 그 경로를 표시하도록 변경",
                "계정 탭에 폴더 열기·다시 확인 버튼 추가",
                "새 버전이 나오면 HUD 왼쪽 위에 표시하는 기능 추가",
                "업데이트 버튼을 눌러도 진행 상태가 보이지 않던 문제 수정",
                "설정 창을 열어 둔 채로는 사용량이 갱신되지 않던 문제 수정",
                "배율이 100%가 아닌 화면에서 HUD 위치가 매번 초기화되던 문제 수정",
                "HUD를 옮긴 자리가 종료 전에 저장되지 않던 문제 수정",
                "눈 깜빡일 때마다 트레이 아이콘을 다시 만들던 문제 개선",
            ],
        },
        new ChangelogEntry
        {
            Version = "1.0.0",
            Date = "2026-08-06",
            Notes =
            [
                "윈도우판 첫 배포",
                "이중 링 HUD 추가 (5시간 세션 · 7일 주간)",
                "부엉이 마스코트 추가 (맥판과 같은 그림)",
                "트레이 아이콘과 메뉴 추가",
                "설정 창 추가 (테마 · 크기 · 조회 주기 · 펼침 방향)",
                "로그인할 때 자동 시작 설정 추가",
                "새 버전 자동 확인과 자체 업데이트 지원",
            ],
        },
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // 한글이 \uXXXX 로 escape 되면 사람이 파일을 열어 봐도 못 읽는다.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>이 목록을 JSON 으로 뽑는다. <c>DongCSU.exe --dump-changelog</c> 가 쓴다.</summary>
    public static string Dump() =>
        JsonSerializer.Serialize(new ChangelogFeed { Entries = Entries }, Options);

    /// <summary>
    /// 앱에 박힌 내역과 원격에서 받은 내역을 합친다.
    ///
    /// **원격 것으로 갈아치우지 않는다.** 앱에 박힌 내역은 그 버전까지밖에 모르지만,
    /// 반대로 **방금 올린 버전을 쓰는 앱은 자기보다 뒤처진 목록을 받을 수 있다.**
    /// 갈아치우면 그때 자기 버전 항목이 화면에서 사라진다.
    ///
    /// 같은 버전은 원격 쪽을 택하고(고쳐 적었을 수 있다) 버전 내림차순으로 세운다.
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> Merge(IReadOnlyList<ChangelogEntry>? remote)
    {
        if (remote is not { Count: > 0 }) return Entries;

        var byVersion = new Dictionary<string, ChangelogEntry>();
        foreach (var entry in Entries) byVersion[entry.Version] = entry;
        foreach (var entry in remote) byVersion[entry.Version] = entry;

        return [.. byVersion.Values.OrderByDescending(e => e.Version, VersionOrder.Instance)];
    }

    /// <summary>버전 문자열 순서. 못 읽는 것은 글자 순으로 떨어뜨린다.</summary>
    private sealed class VersionOrder : IComparer<string>
    {
        public static readonly VersionOrder Instance = new();

        public int Compare(string? left, string? right)
        {
            if (AppVersion.TryParse(left, out var a) && AppVersion.TryParse(right, out var b))
            {
                return a.CompareTo(b);
            }
            return string.CompareOrdinal(left, right);
        }
    }

    /// <summary>
    /// 받아 온 응답을 성공·실패로 가른다. 앞에서부터 차례로 걸러 **왜 못 받았는지**를 남긴다.
    ///
    /// 화면과 무관한 순수 계산이라 여기 있다 — `App` 에 두면 맥에서 테스트할 수 없다.
    ///
    /// **사유에 응답 본문이나 헤더를 절대 끼우지 않는다.** 이 문장은 그대로 기록 파일과
    /// 버전 탭에 나가는데, 주소가 바뀌어 엉뚱한 응답(로그인 페이지·프록시 안내)을 받아
    /// 왔다면 그 안에 무엇이 실려 있을지 모른다. 상태 코드와 우리가 쓴 문장만 남긴다 —
    /// <c>win/CLAUDE.md</c>: 토큰이나 자격 증명 내용은 기록에 남기지 않는다.
    /// </summary>
    public static ChangelogFetch Read(int status, string body)
    {
        if (status != 200)
        {
            return ChangelogFetch.Failed($"변경 내역을 받지 못했습니다 (HTTP {status})");
        }
        if (Parse(body) is not { } feed)
        {
            return ChangelogFetch.Failed("변경 내역을 읽지 못했습니다 (형식이 다릅니다)");
        }
        if (feed.Entries is not { Count: > 0 })
        {
            return ChangelogFetch.Failed("받아온 변경 내역이 비어 있습니다");
        }
        return ChangelogFetch.Ok(feed.Entries);
    }

    public static ChangelogFeed? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ChangelogFeed>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
