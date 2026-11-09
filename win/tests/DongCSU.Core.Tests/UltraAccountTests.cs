using DongCSU.Core.Owl;
using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 5시간 한도가 **없는** 계정(인증된 Ultra).
///
/// 서버는 그때 <c>five_hour</c> 를 빼지 않고 <c>null</c> 로 준다. **키가 아예 없는
/// 것(옛 응답)과 가르는 것이 핵심이다** — 그쪽은 값을 모르는 것이라 예전처럼 "—" 로
/// 남겨야 하고, 이쪽은 줄째 빼야 한다.
///
/// **요금제 이름으로 가르지 않는다.** 인증 전인 Ultra 는 임시 5시간 한도가 걸려 있어서
/// 이름만 보고 숨기면 있는 한도를 가린다. 조회할 때마다 응답을 본다.
/// </summary>
public class UltraAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static ClaudeCredentials Cred() => new() { AccessToken = "t" };

    private static UsageSnapshot Parse(string body) =>
        UsageApi.Parse(body, Cred(), Now).Snapshot!;

    [Fact]
    public void 서버가_창을_null_로_주면_한도가_없는_것이다()
    {
        var snapshot = Parse("""{"five_hour":null,"seven_day":{"utilization":61}}""");

        Assert.False(snapshot.HasSessionLimit);
        Assert.Null(snapshot.FiveHour);
        Assert.Equal(61, snapshot.SevenDay!.Value.Utilization);
    }

    /// <summary>창은 있는데 <c>utilization</c> 만 <c>null</c> 인 꼴도 같은 뜻이다.</summary>
    [Fact]
    public void 사용률만_null_인_것도_없는_것이다()
    {
        Assert.False(Parse("""{"five_hour":{"utilization":null}}""").HasSessionLimit);
    }

    /// <summary>
    /// **키가 없는 것은 "모름" 이다.** 옛 응답이 그렇게 생겼는데, 없다고 보면 세션 줄이
    /// 통째로 사라져서 조회가 되는지 안 되는지도 알 수 없게 된다.
    /// </summary>
    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"seven_day":{"utilization":61}}""")]
    [InlineData("""{"five_hour":{},"seven_day":{"utilization":61}}""")]
    public void 키가_없으면_있는_것으로_둔다(string body)
    {
        var snapshot = Parse(body);

        Assert.True(snapshot.HasSessionLimit);
        Assert.Null(snapshot.FiveHour);
    }

    /// <summary>
    /// 한쪽만 <c>null</c> 인 응답에서는 <c>limits</c> 를 믿는다.
    ///
    /// 있는 한도를 숨기느니 "—" 를 띄우는 편이 낫다 — 숨기면 다 쓴 줄도 모르고 쓰다가
    /// 갑자기 막힌다.
    /// </summary>
    [Fact]
    public void limits_에_세션이_있으면_있는_것이다()
    {
        var snapshot = Parse("""
            {
              "five_hour": null,
              "limits": [{ "kind": "session", "percent": 12 }]
            }
            """);

        Assert.True(snapshot.HasSessionLimit);
    }

    /// <summary>
    /// **첫 조회 전에는 있다고 본다.** 거의 모든 계정에 있어서, 빼 두면 대부분의 사람에게
    /// 링 하나가 뒤늦게 생겨나는 것으로 보인다.
    /// </summary>
    [Fact]
    public void 아직_못_읽었으면_있는_것으로_둔다()
    {
        Assert.True(SpentFixture.Empty().HasSessionLimit);
        Assert.True(new UsageSnapshot { FetchedAt = Now }.HasSessionLimit);
    }

    /// <summary>
    /// 지쳐 가는 정도는 보통 세션으로 재지만, **5시간 한도가 없으면 주간으로 잰다.**
    /// 거기서는 주간이 곧 제일 먼저 막히는 한도다.
    /// </summary>
    [Fact]
    public void 한도가_없으면_주간으로_지친다()
    {
        var ultra = SpentFixture.Store(session: null, weekly: 80, hasSessionLimit: false);
        var normal = SpentFixture.Store(session: 10, weekly: 80);

        Assert.Equal(80, ultra.TiringUtilization);
        Assert.Equal(10, normal.TiringUtilization);
    }

    /// <summary>
    /// 문턱은 그대로다(<c>owl.json</c> 의 tired · exhausted). 보는 값만 바뀐다 —
    /// 세션만 보고 있으면 주간을 다 쓰는 순간까지 멀쩡하다가 갑자기 쓰러진다.
    /// </summary>
    [Fact]
    public void 주간으로_지쳐도_문턱은_같다()
    {
        var document = OwlDocument.Embedded;
        var tired = document.MoodThresholds["tired"];

        var store = SpentFixture.Store(session: null, weekly: tired, hasSessionLimit: false);
        var mood = OwlMoodResolver.Resolve(
            document, store.TiringUtilization, store.IsDisconnected, store.IsSpent);

        Assert.Equal(OwlMood.Tired, mood);
    }

    /// <summary>
    /// **세션 값이 없어도 다 쓴 것은 아니다.** 한도가 없는 계정에서 세션을 "0% 로 꽉
    /// 찼다" 로 세면 마스코트가 뜨는 즉시 죽는다.
    /// </summary>
    [Fact]
    public void 한도가_없으면_세션은_다_쓴_것이_아니다()
    {
        var store = SpentFixture.Store(session: null, weekly: 10, hasSessionLimit: false);

        Assert.False(store.IsSessionSpent);
        Assert.False(store.IsSpent);
    }
}
