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
