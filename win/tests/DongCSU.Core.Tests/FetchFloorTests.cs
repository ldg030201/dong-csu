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
}
