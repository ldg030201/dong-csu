using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 측정 화면이 내놓는 글자.
///
/// **화면 코드가 아니라 여기서 만드는 이유가 검사다.** 측정 탭과 기록 상세 창이 같은
/// 목록을 받아 줄로 옮기기만 하므로, 이 표를 못 박아 두면 두 화면이 갈릴 수 없다.
/// </summary>
public class MeasureTextTests
{
    // ── 한도 값 ─────────────────────────────────────────────────────

    /// <summary>
    /// 서버가 정수 %로 주므로 소수점을 안 찍는다. 소수 한 자리를 붙이면 없는 정밀도가
    /// 있는 것처럼 보인다.
    /// </summary>
    [Theory]
    [InlineData(0.0, "0%p")]
    [InlineData(0.4, "0%p")]
    [InlineData(21.0, "21%p")]
    [InlineData(104.0, "104%p")]
    // 리셋을 넘겨서 계속 쌓으면 100 을 넘는다. 자르지 않는다.
    [InlineData(118.4, "118%p")]
    public void 한도_값은_소수점_없이_적는다(double accumulated, string expected)
    {
        Assert.Equal(expected, MeasureText.LimitValue(new LimitTrack { Accumulated = accumulated }));
    }

    // ── 합계 ────────────────────────────────────────────────────────

    private static TokenTally Sample => new(536, 1_824, 20_000, 30_000, 40_000);

    /// <summary>
    /// 캐시를 넣을지 **한 곳에서만 정한다.** 두 곳에서 판단하면 줄과 합계가 어긋난다.
    /// </summary>
    [Fact]
    public void 합계는_캐시_포함_여부를_따른다()
    {
        Assert.Equal(Sample.WithoutCache, MeasureText.Total(Sample, includesCache: false));
        Assert.Equal(Sample.Total, MeasureText.Total(Sample, includesCache: true));
    }

    // ── 토큰 줄 ─────────────────────────────────────────────────────

    [Fact]
    public void 캐시를_빼면_네_줄이다()
    {
        var rows = MeasureText.TokenRows(Sample, includesCache: false);

        Assert.Equal(
            [("응답", "536건"), ("입력", "1,824 토큰"), ("출력", "2만 토큰"), ("합계", "2.2만 토큰")],
            rows);
    }

    [Fact]
    public void 캐시를_넣으면_두_줄이_늘고_합계도_바뀐다()
    {
        var rows = MeasureText.TokenRows(Sample, includesCache: true);

        Assert.Equal(
            [
                ("응답", "536건"),
                ("입력", "1,824 토큰"),
                ("출력", "2만 토큰"),
                ("캐시 생성", "3만 토큰"),
                ("캐시 읽기", "4만 토큰"),
                ("합계", "9.2만 토큰"),
            ],
            rows);
    }

    /// <summary>
    /// **단위를 반드시 적는다.** `입력 4` 만 있으면 네 번 물었다는 뜻으로 읽힌다.
    /// 횟수인 것은 응답 하나뿐이고, 그것만 축약하지 않는다.
    /// </summary>
    [Fact]
    public void 응답만_횟수이고_나머지는_토큰이다()
    {
        var rows = MeasureText.TokenRows(new TokenTally(120_000, 1, 1, 0, 0), includesCache: false);

        Assert.Equal("120,000건", rows[0].Value);
        Assert.All(rows.Skip(1), row => Assert.EndsWith(" 토큰", row.Value));
    }

    /// <summary>마지막 줄이 늘 합계다 — 화면이 그 자리에만 선을 긋는다.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 마지막_줄은_늘_합계다(bool includesCache)
    {
        Assert.Equal("합계", MeasureText.TokenRows(Sample, includesCache)[^1].Label);
    }

    // ── 모델별 ──────────────────────────────────────────────────────

    private static Dictionary<string, TokenTally> TwoModels => new(StringComparer.Ordinal)
    {
        // 캐시를 빼면 Opus 가 크고, 넣으면 Haiku 가 커진다.
        ["claude-opus-5"] = new TokenTally(1, 10, 10, 0, 0),
        ["claude-haiku-4-5"] = new TokenTally(1, 5, 5, 1_000, 0),
    };

    /// <summary>
    /// **모델이 하나면 표를 안 그린다.** 안 막으면 합계와 똑같은 줄이 한 번 더 나온다.
    /// </summary>
    [Fact]
    public void 모델이_하나면_빈_목록이다()
    {
        var one = new Dictionary<string, TokenTally>(StringComparer.Ordinal)
        {
            ["claude-opus-5"] = new TokenTally(1, 10, 10, 0, 0),
        };

        Assert.Empty(MeasureText.ModelRows(one, includesCache: false));
        Assert.Empty(MeasureText.ModelRows(new Dictionary<string, TokenTally>(), includesCache: false));
    }

    /// <summary>이름은 <c>ClaudeCodeUsage.DisplayName</c> 이 줄인 것을 쓴다.</summary>
    [Fact]
    public void 모델별은_합계_내림차순이다()
    {
        Assert.Equal(
            [("Opus 5", "20 토큰"), ("Haiku 4.5", "10 토큰")],
            MeasureText.ModelRows(TwoModels, includesCache: false));
    }

    [Fact]
    public void 캐시를_넣으면_모델_차례도_바뀐다()
    {
        Assert.Equal(
            [("Haiku 4.5", "1,010 토큰"), ("Opus 5", "20 토큰")],
            MeasureText.ModelRows(TwoModels, includesCache: true));
    }

    // ── 기록 한 줄 요약 ─────────────────────────────────────────────

    [Fact]
    public void 요약은_첫_한도를_쓴다()
    {
        var record = new MeterRecord
        {
            Tracks =
            [
                new LimitTrack { Title = "세션 (5시간)", Accumulated = 21 },
                new LimitTrack { Title = "주간 (7일)", Accumulated = 3 },
            ],
        };

        Assert.Equal("세션 (5시간) 21%p", MeasureText.Headline(record));
    }

    /// <summary>
    /// 한 표본도 못 잡은 측정은 <c>—</c> 다. 0%p 로 적으면 **안 쓴 것**과
    /// **못 잰 것**이 같은 글자가 된다.
    /// </summary>
    [Fact]
    public void 한도가_없으면_줄표다()
    {
        Assert.Equal("—", MeasureText.Headline(new MeterRecord()));
    }

    // ── 표본 문구 ───────────────────────────────────────────────────

    [Fact]
    public void 표본이_없으면_횟수를_안_적는다()
    {
        var now = new DateTimeOffset(2026, 8, 22, 14, 3, 0, TimeSpan.Zero);

        Assert.Equal("표본 없음", MeasureText.SampleText(0, null, now));
    }

    [Fact]
    public void 표본은_횟수와_나이를_같이_적는다()
    {
        var now = new DateTimeOffset(2026, 8, 22, 14, 3, 0, TimeSpan.Zero);

        Assert.Equal("표본 34회 · 3분 전 값", MeasureText.SampleText(34, now.AddMinutes(-3), now));
    }

    // ── 안내 ────────────────────────────────────────────────────────

    /// <summary>
    /// 세 가지를 한 문구가 다 말해야 한다 — 언제 갱신되는지, 왜 잔돈이 안 잡히는지,
    /// 중지하면 어디로 가는지.
    /// </summary>
    [Fact]
    public void 안내는_주기와_눈금과_기록을_다_말한다()
    {
        var guide = MeasureText.Guide("10분마다");

        Assert.Contains("10분마다", guide);
        Assert.Contains("1%p", guide);
        Assert.Contains("기록", guide);
    }
}
