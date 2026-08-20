using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 렌더로 뽑은 설정 창의 상태 탭이 **조회 카운트다운까지** 실제와 같아야 한다.
///
/// 조회를 한 번도 안 걸면 <see cref="UsageStore.NextPollAt"/> 계산은 늘 null 이라
/// 그 줄만 통째로 빈다. 그래서 <c>Preview(nextPoll:)</c> 로 예정 시각을 꽂는다.
///
/// **꽂은 값은 실제 조회가 시작되면 사라져야 한다.** 안 그러면 평소에 도는 앱에서도
/// 화면이 영영 같은 시간을 가리킬 수 있다.
/// </summary>
public class PreviewNextPollTests
{
    private static UsageStore Empty() =>
        new(new UsageApi(new HttpClient(), new CredentialStore(new NoCredentials(), null, null)));

    private static UsageSnapshot Snapshot() =>
        new() { FetchedAt = DateTimeOffset.UtcNow };

    /// <summary>여기 테스트는 조회를 걸지 않는다. 자격 증명은 없어도 된다.</summary>
    private sealed class NoCredentials : ICredentialSource
    {
        public ClaudeCredentials? Read() => null;
    }

    [Fact]
    public void 꽂아_넣은_예정_시각이_그대로_나온다()
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(7).AddSeconds(12);
        var store = Empty();

        store.Preview(Snapshot(), nextPoll: at);

        Assert.Equal(at, store.NextPollAt);
    }

    /// <summary>물러나는 중도 아니고 조회를 건 적도 없어서, 꽂지 않으면 그 줄이 빈다.</summary>
    [Fact]
    public void 조회를_안_걸었으면_꽂은_값이_유일한_예정_시각이다()
    {
        var store = Empty();
        store.Preview(Snapshot());

        Assert.Null(store.NextPollAt);

        var at = DateTimeOffset.UtcNow.AddMinutes(5);
        store.Preview(Snapshot(), nextPoll: at);

        Assert.Equal(at, store.NextPollAt);
    }

    [Fact]
    public void 실제_조회_결과가_들어오면_꽂은_값이_사라진다()
    {
        var at = DateTimeOffset.UtcNow.AddHours(3);
        var store = Empty();
        store.Preview(Snapshot(), nextPoll: at);

        store.Apply(UsageResult.Ok(Snapshot()));

        Assert.NotEqual(at, store.NextPollAt);
        // 꽂은 값을 버렸어도 줄이 비면 안 된다 — 이제는 방금 건 조회에서 센다.
        Assert.NotNull(store.NextPollAt);
    }
}
