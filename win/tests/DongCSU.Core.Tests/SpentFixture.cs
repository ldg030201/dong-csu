using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 한도만 꽂아 둔 <see cref="UsageStore"/>.
///
/// <b>세션·주간을 갈라 본 두 검사가 같은 고정값을 글자까지 똑같이 들고 있었다.</b>
/// <see cref="UsageStore"/> 나 <see cref="CredentialStore"/> 의 생성자가 한 칸 바뀌면
/// 그만큼의 파일을 다 고쳐야 했다.
/// </summary>
internal static class SpentFixture
{
    public static UsageWindow Window(double utilization) =>
        new(utilization, DateTimeOffset.UtcNow.AddHours(1));

    /// <summary>조회를 안 거는 저장소. 자격 증명이 없어도 된다.</summary>
    public static UsageStore Empty() =>
        new(new UsageApi(new HttpClient(), new CredentialStore(new NoCredentials(), null, null)));

    public static UsageStore Store(double? session, double? weekly)
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

    private sealed class NoCredentials : ICredentialSource
    {
        public ClaudeCredentials? Read() => null;
    }
}
