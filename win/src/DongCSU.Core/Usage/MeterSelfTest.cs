namespace DongCSU.Core.Usage;

/// <summary>
/// 리셋을 넘겨서도 계속 쌓는 계산이 맞는지 스스로 검사한다.
///
/// **5시간 창이 실제로 열리기를 기다리면 확인에 다섯 시간이 걸린다.** 그래서 창이
/// 한 번 열리는 상황까지 지어낸 여섯 단계를 <see cref="UsageMeter.Advance"/> 에
/// 통과시키고, 마지막 누적과 리셋 횟수를 못 박아 둔다.
///
/// **표는 여기 한 벌뿐이다.** 맥은 테스트 프로젝트가 없어 <c>main.swift</c> 한 곳이면
/// 됐지만, 우리는 CI 가 <c>dotnet test</c> 와 exe 진단 통로를 **둘 다** 돌린다 —
/// 표를 두 곳에 적으면 한쪽만 고친 채로 둘 다 초록인 날이 온다.
/// <c>--probe-meter selftest</c> 는 여기서 나온 것을 찍기만 한다.
/// </summary>
public static class MeterSelfTest
{
    /// <summary>
    /// 여섯 단계를 다 지났을 때의 누적.
    ///
    /// **정수 %p 만 더하므로 부동소수 등호로 견줘도 정확히 떨어진다**(맥도 그렇게 견준다).
    /// 표에 소수 %p 를 넣게 되면 그때 허용 오차를 들인다.
    /// </summary>
    public const double ExpectedAccumulated = 104;

    /// <summary>여섯 단계 중 창이 실제로 새로 열리는 것은 한 번뿐이다.</summary>
    public const int ExpectedResets = 1;

    /// <param name="Percent">이 단계에서 서버가 준 값.</param>
    /// <param name="Accumulated">단계가 끝난 뒤의 누적 %p.</param>
    /// <param name="Resets">단계가 끝난 뒤까지 센 리셋 횟수.</param>
    /// <param name="Note">왜 그 값이 나오는지. 사람이 읽을 자리다.</param>
    public sealed record Step(double Percent, double Accumulated, int Resets, string Note);

    /// <param name="Steps">여섯 단계. 중간값이 틀리면 어느 규칙이 깨졌는지 바로 짚인다.</param>
    /// <param name="Accumulated">마지막 단계까지 쌓인 %p.</param>
    /// <param name="Resets">그동안 센 리셋 횟수.</param>
    public sealed record Report(IReadOnlyList<Step> Steps, double Accumulated, int Resets)
    {
        public bool Passed => Accumulated == ExpectedAccumulated && Resets == ExpectedResets;
    }

    /// <param name="at">
    /// 기준 시각. **답에는 안 섞인다** — 계산이 보는 것은 창이 움직였는지 하나뿐이라
    /// 어느 시각에서 출발하든 같은 표가 나온다. 검사가 시계와 무관하게 돌라고 열어 둔다.
    /// </param>
    public static Report Run(DateTimeOffset? at = null)
    {
        var origin = at ?? DateTimeOffset.UtcNow;
        var first = origin.AddHours(5);
        var second = origin.AddHours(10);

        (double Percent, DateTimeOffset ResetsAt, string Note)[] table =
        [
            (55, first, "그냥 늘었다"),
            (92, first, "그냥 늘었다"),
            (4, second, "창이 새로 열렸다 — 새 값을 통째로 더한다"),
            (30, second, "그냥 늘었다"),
            (28, second, "서버 보정 — 더하지 않는다"),
            // 서버가 resets_at 을 마이크로초까지 조금씩 다르게 준다. 이걸 리셋으로 세면
            // 표본마다 소모량이 통째로 더해져 값이 터진다.
            (30, second.AddSeconds(5), "resets_at 지터 — 리셋이 아니다"),
        ];

        // 20%를 이미 쓴 첫 창에서 출발한다 — 첫 단계(55%)가 35%p 만 더해야 맞다.
        var track = new LimitTrack { Title = "세션", LastPercent = 20, LastResetsAt = first };

        var steps = new List<Step>(table.Length);
        foreach (var (percent, resetsAt, note) in table)
        {
            track = UsageMeter.Advance(
                track,
                new UsageLimit { Kind = "session", Percent = percent, ResetsAt = resetsAt });
            steps.Add(new Step(percent, track.Accumulated, track.Resets, note));
        }

        return new Report(steps, track.Accumulated, track.Resets);
    }
}
