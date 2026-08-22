namespace DongCSU.Core.Usage;

/// <summary>
/// 화면 없이 측정 탭을 그려 볼 때 꽂는 <b>고정값</b>.
///
/// <b>진짜 조회도 진짜 훑기도 하지 않는다.</b> <c>--render-settings measure</c> 와
/// <c>--probe-layout</c> 이 이걸 쓴다 — 그 통로들은 값이 들어온 화면을 봐야 하는데,
/// 실제로 재려면 몇 시간이 걸리고 기계마다 답도 달라진다. 맥 <c>PreviewRender</c> 가
/// <c>UsageMeter(preview:)</c> 로 하는 것과 같은 자리다.
///
/// <b>시각을 인자로 받는다.</b> 안에서 <c>DateTimeOffset.Now</c> 를 부르면 뽑을 때마다
/// 잰 시간이 달라져서, 문서 그림 두 장을 견줄 때 어디가 진짜 바뀐 것인지 알 수 없다.
/// </summary>
public static class MeterPreview
{
    /// <summary>고정값 한 벌. <paramref name="running"/> 이 거짓이면 <b>멈춘 뒤</b>의 모습이다.</summary>
    /// <param name="running">
    /// 재는 중인 모습인지. <b>둘 다 봐야 한다</b> — 멈추면 머리·한도·토큰 카드가 통째로
    /// 빠지고 기록 목록만 남아서, 재는 중만 재 보면 멈춘 쪽이 잘리는 것을 영영 못 본다.
    /// </param>
    /// <param name="now">기준 시각. 안 주면 지금이다.</param>
    public static MeterState State(bool running = true, DateTimeOffset? now = null)
    {
        // **지금을 기준으로 잡는다. 날짜를 못 박지 않는다.**
        //
        // 못 박아 봤고 틀렸다 — 잰 시간은 `UsageMeter.Elapsed(now)` 가 **진짜 시계**로
        // 계산하는데 시작 시각만 옛 날짜로 박혀 있어서, 그림을 뽑는 날마다 늘어나
        // "1일 9시간" 같은 값이 찍혔다. 상태 탭 고정값(`40분 전 확인`)도 지금 기준이다.
        var at = now ?? DateTimeOffset.Now;
        var started = at.AddMinutes(-42).AddSeconds(-17);

        var tracks = new List<LimitTrack>
        {
            // 리셋을 한 번 넘긴 세션. **넘김이 있는 줄을 반드시 하나 둔다** —
            // "리셋 1회 넘김" 이 붙는 자리라 그게 안 잘리는지 봐야 한다.
            new() { Title = "세션 (5시간)", Accumulated = 118.0, LastPercent = 22, Resets = 1 },
            new() { Title = "주간 (7일)", Accumulated = 9.0, LastPercent = 61 },
            // 모델별 한도. 제목이 제일 길어서 최소 폭에서 먼저 잘린다.
            new() { Title = "주간 · Opus 5", Accumulated = 7.0, LastPercent = 44 },
        };

        var state = new MeterState
        {
            StartedAt = started,
            StoppedAt = running ? null : at.AddMinutes(-3),
            Order = [.. tracks.Select(t => t.Title)],
            Tracks = tracks.ToDictionary(t => t.Title, t => t, StringComparer.Ordinal),
            Tokens = new TokenTally(1_284, 331_443, 6_949_752, 61_472_373, 2_463_494_784),
            TokensByModel = new Dictionary<string, TokenTally>(StringComparer.Ordinal)
            {
                // 캐시 읽기가 24억이다. **여기를 작은 수로 두면 안 된다** — 자릿수가
                // 밀려 잘리는 것이 실제 값에서만 드러나면 진단 통로가 제 일을 못 한다.
                ["Opus 5"] = new(1_106, 298_120, 6_402_310, 55_930_004, 2_240_118_770),
                ["Opus 4.8"] = new(166, 31_201, 540_984, 5_402_369, 219_876_014),
                ["Haiku 4.5"] = new(12, 2_122, 6_458, 140_000, 3_500_000),
            },
            Samples = 37,
            LastSampledAt = at.AddSeconds(-24),
            History = [.. Enumerable.Range(1, 4).Select(i => Record(at, i))],
        };

        return state;
    }

    /// <summary>지난 측정 하나. 목록이 여러 줄일 때의 배치를 보려고 넷을 만든다.</summary>
    private static MeterRecord Record(DateTimeOffset at, int index)
    {
        var stopped = at.AddDays(-index).AddHours(-1);
        return new MeterRecord
        {
            StartedAt = stopped.AddMinutes(-(20 + index * 13)),
            StoppedAt = stopped,
            Tracks =
            [
                new() { Title = "세션 (5시간)", Accumulated = 12.0 * index, LastPercent = 8 * index },
                new() { Title = "주간 (7일)", Accumulated = 1.0 * index },
            ],
            Tokens = new TokenTally(
                40L * index, 8_000L * index, 120_000L * index, 900_000L * index, 42_000_000L * index),
            TokensByModel = new Dictionary<string, TokenTally>(StringComparer.Ordinal)
            {
                ["Opus 5"] = new(40L * index, 8_000L * index, 120_000L * index, 900_000L * index, 42_000_000L * index),
            },
            Samples = 6 * index,
        };
    }
}
