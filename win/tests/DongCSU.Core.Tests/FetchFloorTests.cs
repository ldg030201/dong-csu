using System.Net;
using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 조회 사이의 바닥과 계정 표시.
/// </summary>
public class FetchFloorTests
{
    // ── 한도 등급 표기 ──────────────────────────────────────────────

    [Theory]
    [InlineData("default_claude_max_5x", "Max 5x")]
    [InlineData("default_claude_max_20x", "Max 20x")]
    [InlineData("claude_pro_1x", "Pro 1x")]
    public void 한도_등급을_읽기_좋게_줄인다(string raw, string expected)
    {
        Assert.Equal(expected, UsageSnapshot.TierText(raw));
    }

    /// <summary>
    /// **못 알아보는 값이면 원문을 그대로 둔다.** 서버가 형태를 바꿨을 때 빈칸이 되는
    /// 것보다, 낯설어도 실제 값이 보이는 편이 낫다.
    /// </summary>
    [Theory]
    [InlineData("enterprise", "enterprise")]
    [InlineData("default_claude_max", "default_claude_max")]
    [InlineData("weird_xx", "weird_xx")]
    public void 모르는_값은_원문을_그대로_둔다(string raw, string expected)
    {
        Assert.Equal(expected, UsageSnapshot.TierText(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 비었으면_null(string? raw)
    {
        Assert.Null(UsageSnapshot.TierText(raw));
    }

    // ── 조회 바닥 ───────────────────────────────────────────────────

    private const string Body = """{"five_hour":{"utilization":12}}""";

    private static (UsageStore Store, FakeTime Clock, Func<int> Calls) Ready()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body),
            };
            return response;
        });
        var http = new HttpClient(handler);
        var clock = new FakeTime(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero));
        var credentials = new CredentialStore(new FixedSource(LiveFile), null, null);
        var store = new UsageStore(new UsageApi(http, credentials, clock), clock);
        return (store, clock, () => calls);
    }

    private const string LiveFile = """
        {"claudeAiOauth":{"accessToken":"live","subscriptionType":"max",
        "rateLimitTier":"default_claude_max_5x"}}
        """;

    /// <summary>
    /// **`force` 로도 못 뚫는다.** 429 백오프와 다른 물건이다 — 저쪽은 맞고 나서
    /// 물러서는 것이고 이건 맞기 전에 막는 것이다.
    /// </summary>
    [Fact]
    public async Task 바닥에_걸린_동안에는_force_로도_안_나간다()
    {
        var (store, _, calls) = Ready();

        await store.RefreshAsync(force: true);
        await store.RefreshAsync(force: true);

        Assert.Equal(1, calls());
    }

    [Fact]
    public async Task 바닥이_지나면_다시_나간다()
    {
        var (store, clock, calls) = Ready();

        await store.RefreshAsync(force: true);
        clock.Advance(UsageStore.MinFetchInterval);
        await store.RefreshAsync(force: true);

        Assert.Equal(2, calls());
    }

    /// <summary>눌렀는데 아무 일도 안 일어나면 고장으로 보인다. 몇 초 남았는지 알려 준다.</summary>
    [Fact]
    public async Task 남은_초를_알려준다()
    {
        var (store, clock, _) = Ready();

        Assert.True(store.CanFetchNow);
        Assert.Equal(TimeSpan.Zero, store.FetchCooldown());

        await store.RefreshAsync(force: true);

        Assert.False(store.CanFetchNow);
        Assert.Equal(UsageStore.MinFetchInterval, store.FetchCooldown());

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(6), store.FetchCooldown());
    }

    /// <summary>등급과 만료는 서버가 아니라 자격 증명에서 온다. 조회 결과에 실려 나온다.</summary>
    [Fact]
    public async Task 조회_결과에_등급이_실려_온다()
    {
        var (store, _, _) = Ready();

        await store.RefreshAsync(force: true);

        Assert.Equal("default_claude_max_5x", store.Snapshot?.RateLimitTier);
    }

    // ── 429 물러나기 ────────────────────────────────────────────────

    private static UsageStore Backoff(TimeSpan? pollInterval = null)
    {
        var store = new UsageStore(
            new UsageApi(new HttpClient(), new CredentialStore(new NoCredentials(), null, null)),
            new FakeTime());
        if (pollInterval is { } interval) store.PollInterval = interval;
        return store;
    }

    private static void RateLimit(UsageStore store, TimeSpan? retryAfter = null, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            store.Apply(UsageResult.Fail(UsageError.RateLimited(retryAfter)));
        }
    }

    /// <summary>여기 테스트는 조회를 걸지 않는다. 자격 증명은 없어도 된다.</summary>
    private sealed class NoCredentials : ICredentialSource
    {
        public ClaudeCredentials? Read() => null;
    }

    /// <summary>
    /// **물러나는 시간은 조회 주기와 무관하다.**
    ///
    /// 옛 식은 밑값이 사용자가 고른 주기였다. 그래서 자주 보도록 설정한 사람만 빨리
    /// 돌아오고, 드물게 보는 사람은 첫 429 에 곧바로 상한까지 잠들었다. 막힌 정도는
    /// 서버 사정이라 우리 주기가 끼어들 자리가 아니다.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    public void 첫_429_는_주기와_무관하게_60초다(int pollMinutes)
    {
        var store = Backoff(TimeSpan.FromMinutes(pollMinutes));

        RateLimit(store);

        Assert.Equal(UsageStore.RateLimitBackoffBase, store.NextPollDelay());
    }

    /// <summary>60 → 120 → 240 → 300초. 상한이 없으면 한 번 막혔을 때 영영 안 돌아온다.</summary>
    [Fact]
    public void 잇달아_막히면_사다리를_타고_상한에서_멈춘다()
    {
        var store = Backoff();

        RateLimit(store);
        Assert.Equal(TimeSpan.FromSeconds(60), store.NextPollDelay());

        RateLimit(store);
        Assert.Equal(TimeSpan.FromSeconds(120), store.NextPollDelay());

        RateLimit(store);
        Assert.Equal(TimeSpan.FromSeconds(240), store.NextPollDelay());

        RateLimit(store);
        Assert.Equal(UsageStore.MaxRateLimitBackoff, store.NextPollDelay());

        RateLimit(store, times: 20);
        Assert.Equal(UsageStore.MaxRateLimitBackoff, store.NextPollDelay());
    }

    /// <summary>서버가 알려준 시간은 **더 길 때만** 따른다. 짧으면 우리 바닥이 이긴다.</summary>
    [Fact]
    public void 서버가_알려준_시간은_더_길_때만_따른다()
    {
        var longer = Backoff();
        RateLimit(longer, TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(3), longer.NextPollDelay());

        var shorter = Backoff();
        RateLimit(shorter, TimeSpan.FromSeconds(5));
        Assert.Equal(UsageStore.RateLimitBackoffBase, shorter.NextPollDelay());
    }

    /// <summary>
    /// <c>Retry-After</c> 가 HTTP-date 인데 이미 지난 시각이면 음수가 나온다.
    /// 그걸 그대로 더하면 물러나기가 시작하자마자 풀린다.
    /// </summary>
    [Fact]
    public void 지난_시각을_알려줘도_바닥은_지킨다()
    {
        var store = Backoff();

        RateLimit(store, TimeSpan.FromSeconds(-30));

        Assert.Equal(UsageStore.RateLimitBackoffBase, store.NextPollDelay());
    }
}
