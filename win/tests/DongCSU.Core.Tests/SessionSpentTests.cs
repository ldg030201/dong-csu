using DongCSU.Core.Owl;
using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 세션(5시간)을 다 쓰면 주간이 아무리 남았어도 **지금은** 한 글자도 못 보낸다.
///
/// 그래서 마스코트는 주간을 다 썼을 때와 **똑같이 죽는다.** 다른 것은 링·숫자뿐이다 —
/// 주간은 다음 창이 열리면 실제로 쓸 수 있는 양이라 색을 안 뺀다.
///
/// <see cref="WeeklySpentTests"/> 와 합치지 않는다. 저쪽은 "주간이 찼을 때" 를 못 박은
/// 파일이고, 여기는 "세션이 찼을 때" 와 "둘을 어떻게 가르나" 를 못 박는다.
/// </summary>
public class SessionSpentTests
{
    private static UsageWindow Window(double utilization) => SpentFixture.Window(utilization);

    private static UsageStore Empty() => SpentFixture.Empty();

    private static UsageStore Store(double? session, double? weekly) =>
        SpentFixture.Store(session, weekly);

    [Fact]
    public void 세션이_백_퍼센트면_다_쓴_것이다()
    {
        Assert.True(Store(session: 100, weekly: 10).IsSessionSpent);
        Assert.True(Store(session: 120, weekly: 10).IsSessionSpent);
    }

    [Fact]
    public void 세션이_아직_남았으면_다_쓴_것이_아니다()
    {
        Assert.False(Store(session: 99.4, weekly: 10).IsSessionSpent);
    }

    /// <summary>값이 없는 것과 0%는 다르다. 안 받아온 것을 다 썼다고 하면 안 된다.</summary>
    [Fact]
    public void 세션_값이_없으면_다_쓴_것이_아니다()
    {
        Assert.False(Store(session: null, weekly: 50).IsSessionSpent);
        Assert.False(Empty().IsSessionSpent);
    }

    /// <summary>둘 중 하나만 차도 지금은 못 쓴다. 마스코트가 죽는 조건이다.</summary>
    [Fact]
    public void 하나만_차도_다_쓴_것이다()
    {
        Assert.True(Store(session: 100, weekly: 10).IsSpent);
        Assert.True(Store(session: 10, weekly: 100).IsSpent);
        Assert.True(Store(session: 100, weekly: 100).IsSpent);
        Assert.False(Store(session: 99, weekly: 99).IsSpent);
    }

    /// <summary>
    /// **세션만 찼을 때 주간은 살아 있다.** 두 값을 갈라 두는 이유가 이것뿐이라,
    /// 여기가 무너지면 링 두 개를 따로 칠하는 것도 같이 무너진다.
    /// </summary>
    [Fact]
    public void 세션만_찼으면_주간은_다_쓴_것이_아니다()
    {
        var store = Store(session: 100, weekly: 10);

        Assert.True(store.IsSpent);
        Assert.True(store.IsSessionSpent);
        Assert.False(store.IsWeeklySpent);
    }

    /// <summary>세션이 찼으면 곧바로 탈진이다. "천천히 지쳐 간다"가 아니라 "끝났다"다.</summary>
    [Fact]
    public void 세션을_다_쓰면_탈진이다()
    {
        var mood = OwlMoodResolver.Resolve(
            OwlDocument.Embedded, sessionUtilization: 100, isDisconnected: false, isSpent: true);

        Assert.Equal(OwlMood.Exhausted, mood);
    }

    /// <summary>
    /// 자세만 탈진하는 것과 **죽는 것**은 다르다.
    ///
    /// 문턱(<c>owl.json</c> 의 exhausted)만 넘으면 주저앉을 뿐 눈은 깜빡이고 색도 그대로다.
    /// 다 쓰면 프레임이 멎고 색이 빠지고 칸이 <c>Dead</c> 가 된다.
    /// </summary>
    [Fact]
    public void 다_쓴_것은_주저앉은_것과_다르다()
    {
        var tired = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        tired.SetMood(OwlMood.Exhausted);
        Assert.NotNull(tired.Advance());
        Assert.NotEqual("offline", tired.PaletteName);

        var dead = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        dead.SetMood(OwlMood.Exhausted);
        dead.IsUnusable = true;
        Assert.Null(dead.Advance());
        Assert.Equal("offline", dead.PaletteName);
        Assert.Equal(MascotSprite.Dead, dead.MascotFrame);
    }
}
