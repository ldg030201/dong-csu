using DongCSU.Core.Owl;
using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 주간을 다 쓰면 세션이 얼마 남았든 쓸 수 없다.
///
/// **세션 링만 초록으로 남아 있으면 아직 여유가 있는 것처럼 보인다.** 링·숫자·마스코트가
/// 같이 죽은 것으로 보여야 하고, 판단은 <see cref="UsageStore.IsWeeklySpent"/> 한 곳에서 한다.
/// </summary>
public class WeeklySpentTests
{
    private static UsageWindow Window(double utilization) =>
        new(utilization, DateTimeOffset.UtcNow.AddHours(1));

    private static UsageStore Empty() =>
        new(new UsageApi(new HttpClient(), new CredentialStore(new NoCredentials(), null, null)));

    private static UsageStore Store(double? session, double? weekly)
    {
        var store = Empty();
        store.Preview(new UsageSnapshot
        {
            FiveHour = session is { } s ? Window(s) : null,
            SevenDay = weekly is { } w ? Window(w) : null,
            FetchedAt = DateTimeOffset.UtcNow,
        });
        return store;
    }

    /// <summary>여기 테스트는 조회를 걸지 않는다. 자격 증명은 없어도 된다.</summary>
    private sealed class NoCredentials : ICredentialSource
    {
        public ClaudeCredentials? Read() => null;
    }

    [Fact]
    public void 주간이_백_퍼센트면_다_쓴_것이다()
    {
        Assert.True(Store(session: 10, weekly: 100).IsWeeklySpent);
        Assert.True(Store(session: 10, weekly: 120).IsWeeklySpent);
    }

    [Fact]
    public void 주간이_아직_남았으면_다_쓴_것이_아니다()
    {
        Assert.False(Store(session: 99, weekly: 99.4).IsWeeklySpent);
    }

    /// <summary>값이 없는 것과 0%는 다르다. 안 받아온 것을 다 썼다고 하면 안 된다.</summary>
    [Fact]
    public void 주간_값이_없으면_다_쓴_것이_아니다()
    {
        Assert.False(Store(session: 50, weekly: null).IsWeeklySpent);
        Assert.False(Empty().IsWeeklySpent);
    }

    /// <summary>
    /// 주간을 다 썼으면 **세션 숫자를 보지 않고** 곧바로 탈진이다.
    /// "천천히 지쳐 간다"가 아니라 "끝났다"라서 주간으로 판단하는 게 맞다.
    /// </summary>
    [Fact]
    public void 주간을_다_쓰면_세션이_한가해도_탈진이다()
    {
        var mood = OwlMoodResolver.Resolve(
            OwlDocument.Embedded, sessionUtilization: 3, isDisconnected: false, isWeeklySpent: true);

        Assert.Equal(OwlMood.Exhausted, mood);
    }

    /// <summary>끊김이 가장 세다. 지금 값이 아닌데 탈진한 얼굴이면 옛 숫자를 믿게 된다.</summary>
    [Fact]
    public void 끊겼으면_주간을_다_썼어도_끊김이다()
    {
        var mood = OwlMoodResolver.Resolve(
            OwlDocument.Embedded, sessionUtilization: 3, isDisconnected: true, isWeeklySpent: true);

        Assert.Equal(OwlMood.Offline, mood);
    }

    /// <summary>자세는 탈진 그대로 두고 색만 뺀다. 그림을 하나 더 만들지 않는다.</summary>
    [Fact]
    public void 다_쓰면_자세는_그대로고_색만_빠진다()
    {
        var animator = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        animator.SetMood(OwlMood.Exhausted);
        var pose = animator.CurrentGrid;

        animator.IsUnusable = true;

        Assert.Same(pose, animator.CurrentGrid);
        Assert.Equal("offline", animator.PaletteName);
    }

    /// <summary>끊김의 회색은 색 자체가 정보라 덮어쓰지 않는다 — 이미 회색이다.</summary>
    [Fact]
    public void 끊김_팔레트는_그대로_둔다()
    {
        var animator = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        animator.SetMood(OwlMood.Offline);
        animator.IsUnusable = true;

        Assert.Equal("offline", animator.PaletteName);
    }
}
