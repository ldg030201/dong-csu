using System.Text.Json;
using System.Text.Json.Serialization;

namespace DongCSU.Core;

public sealed record ChangelogEntry
{
    public required string Version { get; init; }
    /// <summary>아직 안 나간 항목은 null.</summary>
    public string? Date { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
}

public sealed record ChangelogFeed
{
    public required IReadOnlyList<ChangelogEntry> Entries { get; init; }
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
