using System.Globalization;

namespace DongCSU.Core.Usage;

/// <summary>
/// 측정 화면이 내놓는 글자.
///
/// **두 화면이 같은 값을 봐야 해서 여기 한 곳에서 만든다.** 측정 탭과 기록 상세 창이
/// 같은 목록을 받아 줄로 옮기기만 한다 — 캐시를 넣을지 같은 판단을 두 곳에서 하면
/// 합계와 줄이 어긋난다(맥이 <c>tokenTotal</c> 을 한 곳에 둔 이유가 그것이다).
///
/// 화면이 아니라 <c>Core</c> 에 있는 것은 <b>검사로 굳혀 두려고</b>다. 문구가 바뀌면
/// 두 화면이 함께 바뀌고, 바뀐 것을 <c>MeasureTextTests</c> 가 알아챈다.
/// </summary>
public static class MeasureText
{
    /// <summary>
    /// 한도 소모량. <b>소수점을 안 찍는다</b> — 서버가 정수 %로 주므로 1%p 아래는
    /// 애초에 못 잡고, 소수 한 자리를 붙이면 없는 정밀도가 있는 것처럼 보인다.
    /// </summary>
    public static string LimitValue(LimitTrack track) =>
        track.Accumulated.ToString("F0", CultureInfo.InvariantCulture) + "%p";

    /// <summary>
    /// 화면에 쓰는 합계. **캐시를 넣을지는 여기서만 정한다.**
    /// </summary>
    public static long Total(TokenTally tokens, bool includesCache) =>
        includesCache ? tokens.Total : tokens.WithoutCache;

    /// <summary>
    /// 토큰 카드에 늘어놓을 줄. **마지막 줄이 늘 합계다** — 화면은 그 자리에만 선을
    /// 긋고 굵게 찍는다.
    ///
    /// <b>단위를 반드시 붙인다.</b> `입력 4` 만 있으면 네 번 물었다는 뜻으로 읽힌다.
    /// 횟수인 것은 응답 하나뿐이라 그것만 자릿점 그대로 찍고, 나머지는 억·만으로 줄인다.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> TokenRows(TokenTally tokens, bool includesCache)
    {
        var rows = new List<(string Label, string Value)>
        {
            ("응답", $"{TokenFormat.Exact(tokens.Responses)}건"),
            ("입력", $"{TokenFormat.Short(tokens.Input)} 토큰"),
            ("출력", $"{TokenFormat.Short(tokens.Output)} 토큰"),
        };

        if (includesCache)
        {
            rows.Add(("캐시 생성", $"{TokenFormat.Short(tokens.CacheCreation)} 토큰"));
            rows.Add(("캐시 읽기", $"{TokenFormat.Short(tokens.CacheRead)} 토큰"));
        }

        // **단가가 서로 달라서 이 숫자가 곧 요금이나 한도 소모량은 아니다** — 그건
        // 위의 %p 가 답한다.
        rows.Add(("합계", $"{TokenFormat.Short(Total(tokens, includesCache))} 토큰"));
        return rows;
    }

    /// <summary>
    /// 모델별 합계. 큰 것부터.
    ///
    /// **모델이 하나뿐이면 빈 목록이다.** 안 막으면 합계와 똑같은 줄이 한 번 더 나와서,
    /// 뭔가 더 있는 줄 알고 읽었다가 같은 숫자를 두 번 보게 된다.
    ///
    /// 같은 값일 때 이름으로 한 번 더 가르는 것은 **차례가 흔들리지 않게** 하려는
    /// 것이다 — 사전을 도는 차례는 보장이 없어서, 훑을 때마다 다시 그리는 화면에서
    /// 줄이 자리를 바꿔 가며 깜빡인다.
    /// </summary>
    public static IReadOnlyList<(string Model, string Value)> ModelRows(
        IReadOnlyDictionary<string, TokenTally> byModel, bool includesCache)
    {
        if (byModel.Count <= 1) return [];

        return
        [
            .. byModel
                .OrderByDescending(pair => Total(pair.Value, includesCache))
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => (
                    ClaudeCodeUsage.DisplayName(pair.Key),
                    $"{TokenFormat.Short(Total(pair.Value, includesCache))} 토큰")),
        ];
    }

    /// <summary>
    /// 기록 목록에 한 줄로 요약할 값. 화면 차례의 첫 한도(대개 세션)를 쓴다.
    ///
    /// 한 표본도 못 잡은 측정은 <c>—</c> 다. 0%p 로 적으면 **안 쓴 것**과
    /// **못 잰 것**이 같은 글자가 된다.
    /// </summary>
    public static string Headline(MeterRecord record) =>
        record.Tracks.Count == 0 ? "—" : $"{record.Tracks[0].Title} {LimitValue(record.Tracks[0])}";

    /// <summary>
    /// 기록 목록 한 줄의 토큰. **목록에는 캐시를 절대 안 넣는다** — 캐시가 다 먹어서
    /// 어느 기록이나 억 단위로 보이면 서로 견줄 수가 없다. 캐시까지는 눌러서 펼쳐 본다.
    ///
    /// 그 판단을 화면에 두지 않는 이유는 <see cref="Total"/> 과 같다. 목록만 밖에서
    /// 만들면 <b>캐시를 넣을지 정하는 자리가 둘</b>이 되고, 그때부터 목록과 상세가
    /// 소리 없이 갈린다.
    /// </summary>
    public static string RecordTokens(MeterRecord record) =>
        $"{TokenFormat.Short(Total(record.Tokens, includesCache: false))} 토큰";

    /// <summary>
    /// 기록의 날짜. **목록·상세·확인 창 셋이 같은 글자를 써야 한다** — 지운다고 물을 때
    /// 나오는 날짜가 목록에 보이던 것과 다르면 엉뚱한 것을 지우는 줄 안다.
    /// </summary>
    public static string RecordDate(MeterRecord record) =>
        record.StoppedAt.ToString("M월 d일 (ddd) HH:mm", RecordCulture);

    /// <summary>
    /// 요일 이름은 **한국어로 못 박는다.** 기계 로캘을 따르면 영어 윈도우에서 요일만
    /// <c>Fri</c> 로 나와 한 줄 안에 두 나라 말이 섞인다.
    /// </summary>
    private static readonly CultureInfo RecordCulture = new("ko-KR");

    /// <summary>
    /// 표본을 몇 번 잡았고 마지막 것이 언제 것인지.
    ///
    /// <paramref name="lastSampledAt"/> 로 판단한다 — 횟수만 보면 표본이 0인데도
    /// "0회 · 방금 값" 같은 말이 나올 수 있다.
    /// </summary>
    public static string SampleText(int samples, DateTimeOffset? lastSampledAt, DateTimeOffset now) =>
        lastSampledAt is { } last
            ? $"표본 {samples}회 · {RemainingTime.AgeText(last, now)}"
            : "표본 없음";

    /// <summary>
    /// 조작 버튼 아래 안내.
    ///
    /// <b>주기 문구를 인자로 받는다.</b> 고를 수 있는 조회 주기 목록은 설정 창이 들고
    /// 있는 것이라, <c>Core</c> 가 그것까지 알면 화면 쪽 표를 두 곳에 두게 된다.
    /// </summary>
    public static string Guide(string pollTitle) =>
        $"한도는 조회할 때({pollTitle}) 갱신된다. "
        + "서버가 정수 %로 줘서 1%p 아래는 안 잡힌다. "
        + "중지하면 아래 기록에 남는다.";
}
