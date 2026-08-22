using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 측정 엔진은 **화면 없이 검사로만 굳힐 수 있다.**
///
/// 5시간 창이 실제로 새로 열리기를 기다리면 확인에 다섯 시간이 걸리고, 훑는 도중에
/// 다시 시작을 누르는 상황은 손으로 재현할 수 없다. 그래서 시계(<see cref="FakeTime"/>)와
/// 훑기(<see cref="UsageMeter"/> 생성자의 <c>scanRunner</c>)를 밖에서 꽂아 그 순간들을
/// 직접 만든다.
/// </summary>
internal static class Meters
{
    /// <summary>기준 시각. 아무 값이나 좋지만 고정이어야 답이 안 흔들린다.</summary>
    public static readonly DateTimeOffset Origin =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>검사가 시간을 민다. 안 그러면 "10분 뒤"를 실제로 기다려야 한다.</summary>
    public sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    /// <summary>
    /// 있는 임시 기록 폴더. 훑기 검사만 이걸 쓴다 — 안 꽂으면 검사가 **사용자의 진짜
    /// <c>~/.claude/projects</c>** 를 훑어서 기계마다 다른 답이 나온다.
    /// </summary>
    public sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "dongcsu-meter-" + Guid.NewGuid().ToString("N"));

        public TempRoot() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>없는 폴더. <c>IsAvailable</c> 이 false 라 훑기가 아예 안 돈다.</summary>
    public static string MissingRoot()
        => Path.Combine(Path.GetTempPath(), "dongcsu-없음-" + Guid.NewGuid().ToString("N"));

    /// <param name="scan">
    /// 훑기를 대신 돌릴 것. **생성자로만 꽂을 수 있다** — 살아 있는 미터의 훑기를 도중에
    /// 갈아 끼우지 못하게 막아 둔 자리다.
    /// </param>
    public static UsageMeter Meter(
        FakeTime time,
        MeterStore? store = null,
        string? root = null,
        Func<TokenScan, CancellationToken, Task<TokenScanResult>>? scan = null)
        => new(store, time, root ?? MissingRoot(), scan);

    public static UsageLimit Limit(
        string kind, double percent, DateTimeOffset? resetsAt = null, string? model = null)
        => new() { Kind = kind, ModelName = model, Percent = percent, ResetsAt = resetsAt };

    public static UsageSnapshot Sample(DateTimeOffset at, params UsageLimit[] limits)
        => new() { FetchedAt = at, Limits = limits };
}

/// <summary>잰 시간과 파생 값. 시계를 밀어 가며 본다.</summary>
public class MeterStateTests
{
    [Fact]
    public void 안_멈추면_시작부터_지금까지다()
    {
        var state = new MeterState { StartedAt = Meters.Origin };

        Assert.Equal(TimeSpan.FromHours(1), state.Elapsed(Meters.Origin.AddHours(1)));
    }

    /// <summary>20분 재고 10분 멈춘 뒤 계속해서 20분 → 40분.</summary>
    [Fact]
    public void 멈춰_있던_시간은_빠진다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        time.Advance(TimeSpan.FromMinutes(20));
        meter.Pause();
        time.Advance(TimeSpan.FromMinutes(10));
        meter.Resume();
        time.Advance(TimeSpan.FromMinutes(20));

        Assert.Equal(TimeSpan.FromMinutes(40), meter.Elapsed(time.GetUtcNow()));
    }

    [Fact]
    public void 멈춰_있는_동안에는_안_늘어난다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        time.Advance(TimeSpan.FromMinutes(5));
        meter.Pause();

        var atPause = meter.Elapsed(time.GetUtcNow());
        time.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(5), atPause);
        Assert.Equal(atPause, meter.Elapsed(time.GetUtcNow()));
    }

    [Fact]
    public void 중지한_뒤에는_시계를_돌려도_고정이다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        time.Advance(TimeSpan.FromMinutes(7));
        meter.Stop();
        time.Advance(TimeSpan.FromHours(3));

        Assert.Equal(TimeSpan.FromMinutes(7), meter.Elapsed(time.GetUtcNow()));
    }

    [Fact]
    public void 한_번도_안_쟀으면_잰_시간이_없다()
    {
        Assert.Null(new MeterState().Elapsed(Meters.Origin));
    }

    [Fact]
    public void 재는_중인지_세는_중인지_진리표()
    {
        var idle = new MeterState();
        Assert.False(idle.IsRunning);
        Assert.False(idle.IsPaused);
        Assert.False(idle.IsCounting);

        var counting = new MeterState { StartedAt = Meters.Origin };
        Assert.True(counting.IsRunning);
        Assert.False(counting.IsPaused);
        Assert.True(counting.IsCounting);

        var paused = new MeterState { StartedAt = Meters.Origin, PausedAt = Meters.Origin };
        Assert.True(paused.IsRunning);
        Assert.True(paused.IsPaused);
        Assert.False(paused.IsCounting);

        var stopped = new MeterState { StartedAt = Meters.Origin, StoppedAt = Meters.Origin };
        Assert.False(stopped.IsRunning);
        Assert.False(stopped.IsPaused);
        Assert.False(stopped.IsCounting);
    }

    /// <summary>
    /// 대소문자만 다른 경로는 **같은 파일이다.** 두 항목으로 갈리면 한쪽 오프셋이 0 인 채로
    /// 남아 그 파일을 통째로 다시 읽는다.
    /// </summary>
    [Fact]
    public void 오프셋_사전은_경로_대소문자를_안_가린다()
    {
        // 직렬화를 건너온 사전(기본 비교자)을 흉내 낸다.
        var state = new MeterState
        {
            Offsets = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                [@"C:\Users\me\a.jsonl"] = 10,
                [@"C:\users\ME\A.jsonl"] = 40,
            },
        };

        var copy = state.Copy();

        if (OperatingSystem.IsWindows())
        {
            Assert.Single(copy.Offsets);
            // 합쳐질 때는 **큰 쪽**이 남아야 한다. 작은 쪽을 남기면 그 구간을 다시 읽는다.
            Assert.Equal(40, copy.Offsets[@"C:\USERS\ME\A.JSONL"]);
        }
        else
        {
            Assert.Equal(2, copy.Offsets.Count);
        }
    }

    /// <summary>
    /// **칸을 하나 더하면서 복사에 적는 것을 잊으면 그 값이 모든 상태 변화에서 조용히
    /// 초기화된다.** 참조 칸만 원본 것으로 되돌려 견주면, 나머지가 하나도 안 빠졌을 때에만
    /// 값이 같아진다 — <c>record</c> 의 값 비교가 그걸 대신 세어 준다.
    /// </summary>
    [Fact]
    public void 복사가_칸을_하나도_안_빠뜨린다()
    {
        var state = new MeterState
        {
            StartedAt = Meters.Origin,
            StoppedAt = Meters.Origin.AddHours(2),
            Tracks = new Dictionary<string, LimitTrack>(StringComparer.Ordinal)
            {
                ["session"] = new() { Title = "세션 (5시간)", Accumulated = 12 },
            },
            Order = ["session"],
            Tokens = new TokenTally(1, 2, 3, 4, 5),
            TokensByModel = new Dictionary<string, TokenTally>(StringComparer.Ordinal)
            {
                ["Opus 5"] = new(1, 2, 3, 4, 5),
            },
            Offsets = new Dictionary<string, long>(ClaudeCodeUsage.PathComparer) { [@"C:\a.jsonl"] = 7 },
            SeenIds = new HashSet<string>(StringComparer.Ordinal) { "msg_1" },
            Samples = 9,
            LastSampledAt = Meters.Origin.AddMinutes(-2),
            PausedAt = Meters.Origin.AddMinutes(-1),
            PausedTotal = TimeSpan.FromMinutes(3),
            History = [new MeterRecord { StartedAt = Meters.Origin, StoppedAt = Meters.Origin }],
        };

        var copy = state.Copy();
        var scalarsOnly = copy with
        {
            Tracks = state.Tracks,
            Order = state.Order,
            TokensByModel = state.TokensByModel,
            Offsets = state.Offsets,
            SeenIds = state.SeenIds,
            History = state.History,
        };

        Assert.Equal(state, scalarsOnly);

        // 그러면서도 참조는 나눠 쓰지 않는다 — 복사본을 고쳐도 원본이 안 움직여야 한다.
        Assert.NotSame(state.Tracks, copy.Tracks);
        Assert.NotSame(state.Tracks["session"], copy.Tracks["session"]);
        Assert.NotSame(state.Order, copy.Order);
        Assert.NotSame(state.TokensByModel, copy.TokensByModel);
        Assert.NotSame(state.Offsets, copy.Offsets);
        Assert.NotSame(state.SeenIds, copy.SeenIds);
        Assert.NotSame(state.History, copy.History);
    }

    /// <summary>
    /// 훑기 결과를 얹을 때 쓰는 복사. 오프셋·본 id 는 **넘긴 것을 그대로 들이고**
    /// (바로 갈아 끼울 것이라 베끼면 그 자리에서 버려진다) 나머지는 깊은 복사다.
    /// </summary>
    [Fact]
    public void 얹기용_복사는_오프셋과_id_를_그대로_들인다()
    {
        var state = new MeterState
        {
            Tracks = new Dictionary<string, LimitTrack>(StringComparer.Ordinal)
            {
                ["session"] = new() { Accumulated = 5 },
            },
            Offsets = new Dictionary<string, long>(ClaudeCodeUsage.PathComparer) { [@"C:\a.jsonl"] = 7 },
            SeenIds = new HashSet<string>(StringComparer.Ordinal) { "msg_0" },
        };
        var offsets = new Dictionary<string, long>(ClaudeCodeUsage.PathComparer) { [@"C:\a.jsonl"] = 90 };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "msg_1" };

        var copy = state.CopyAdopting(offsets, seen);

        Assert.Same(offsets, copy.Offsets);
        Assert.Same(seen, copy.SeenIds);
        Assert.NotSame(state.Tracks["session"], copy.Tracks["session"]);
        // 원본은 그대로다.
        Assert.Equal(7, state.Offsets[@"C:\a.jsonl"]);
        Assert.Equal(new[] { "msg_0" }, state.SeenIds);
    }

    [Fact]
    public void 훑기_주기와_상수들이_맥과_같다()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), UsageMeter.ScanInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), UsageMeter.MinSampleInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), UsageMeter.WindowJitterTolerance);
        Assert.Equal(50, UsageMeter.HistoryLimit);
    }

    [Fact]
    public void 세는_중일_때만_훑기_타이머를_켠다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);
        Assert.False(meter.WantsScanning);

        meter.Start();
        Assert.True(meter.WantsScanning);

        meter.Pause();
        Assert.False(meter.WantsScanning);

        meter.Resume();
        Assert.True(meter.WantsScanning);

        meter.Stop();
        Assert.False(meter.WantsScanning);
    }
}

/// <summary>
/// 리셋을 넘겨서도 쌓는 순수 계산.
///
/// 맥 <c>--probe-meter selftest</c> 의 여섯 단계를 그대로 옮겼다 — 5시간 창이 실제로
/// 열리기를 기다리지 않고 이 계산이 맞는지 본다.
/// </summary>
public class UsageMeterAdvanceTests
{
    private static readonly DateTimeOffset First = Meters.Origin.AddHours(5);
    private static readonly DateTimeOffset Second = Meters.Origin.AddHours(10);

    [Fact]
    public void 맥_selftest_표를_그대로_통과한다()
    {
        var track = new LimitTrack { Title = "세션", LastPercent = 20, LastResetsAt = First };

        // (퍼센트, 창, 단계가 끝난 뒤의 누적)
        (double Percent, DateTimeOffset ResetsAt, double Accumulated)[] steps =
        [
            (55, First, 35),                        // 그냥 늘었다
            (92, First, 72),                        // 그냥 늘었다
            (4, Second, 76),                        // 창이 새로 열렸다 — 새 값을 통째로 더한다
            (30, Second, 102),                      // 그냥 늘었다
            (28, Second, 102),                      // 서버 보정 — 더하지 않는다
            (30, Second.AddSeconds(5), 104),        // resets_at 지터 — 리셋이 아니다
        ];

        foreach (var (percent, resetsAt, accumulated) in steps)
        {
            track = UsageMeter.Advance(track, Meters.Limit("session", percent, resetsAt));
            Assert.Equal(accumulated, track.Accumulated, 6);
        }

        Assert.Equal(104d, track.Accumulated, 6);
        Assert.Equal(1, track.Resets);
    }

    [Fact]
    public void 값이_내려가면_기준만_옮긴다()
    {
        var track = new LimitTrack { Accumulated = 30, LastPercent = 50, LastResetsAt = First };

        var next = UsageMeter.Advance(track, Meters.Limit("session", 42, First));

        Assert.Equal(30d, next.Accumulated, 6);
        Assert.Equal(42d, next.LastPercent, 6);
        Assert.Equal(0, next.Resets);
    }

    /// <summary>지터를 리셋으로 세면 표본마다 소모량이 통째로 더해져 값이 터진다.</summary>
    [Fact]
    public void 오십구초는_리셋이_아니고_육십일초는_리셋이다()
    {
        var track = new LimitTrack { LastPercent = 40, LastResetsAt = First };

        var jitter = UsageMeter.Advance(track, Meters.Limit("session", 40, First.AddSeconds(59)));
        Assert.Equal(0, jitter.Resets);
        Assert.Equal(0d, jitter.Accumulated, 6);

        var moved = UsageMeter.Advance(track, Meters.Limit("session", 40, First.AddSeconds(61)));
        Assert.Equal(1, moved.Resets);
        Assert.Equal(40d, moved.Accumulated, 6);

        // 뒤로 움직인 것도 마찬가지다.
        var back = UsageMeter.Advance(track, Meters.Limit("session", 40, First.AddSeconds(-61)));
        Assert.Equal(1, back.Resets);
    }

    [Fact]
    public void 초기화_시각이_없으면_리셋으로_안_센다()
    {
        var noOld = new LimitTrack { LastPercent = 10, LastResetsAt = null };
        var fromNull = UsageMeter.Advance(noOld, Meters.Limit("session", 3, First));
        Assert.Equal(0, fromNull.Resets);
        Assert.Equal(0d, fromNull.Accumulated, 6);   // 3 < 10 이라 더할 것도 없다

        var track = new LimitTrack { LastPercent = 10, LastResetsAt = First };
        var toNull = UsageMeter.Advance(track, Meters.Limit("session", 3, null));
        Assert.Equal(0, toNull.Resets);
        Assert.Equal(0d, toNull.Accumulated, 6);
    }

    /// <summary>
    /// 재기준은 **누적도 리셋도 안 건드리고 기준만 옮긴다.** 이름까지 옮기는 것이
    /// <see cref="UsageMeter.Advance"/> 와 같아야 한다 — 여기만 빼놓으면 계속을 누른
    /// 측정에서 한도 이름 하나가 옛것으로 남는다.
    /// </summary>
    [Fact]
    public void 재기준은_기준만_옮기고_인자를_안_고친다()
    {
        var track = new LimitTrack
        {
            Title = "옛 이름",
            Accumulated = 30,
            LastPercent = 20,
            LastResetsAt = First,
            Resets = 2,
        };

        var next = UsageMeter.Baseline(track, Meters.Limit("weekly_all", 88, Second));

        Assert.Equal(30d, next.Accumulated, 6);
        Assert.Equal(2, next.Resets);
        Assert.Equal(88d, next.LastPercent, 6);
        Assert.Equal(Second, next.LastResetsAt);
        Assert.Equal("주간 (7일)", next.Title);

        Assert.Equal("옛 이름", track.Title);
        Assert.Equal(20d, track.LastPercent, 6);
    }

    /// <summary>맥은 struct 라 저절로 지켜지는 것이라, C# 에서는 이 검사가 그 자리를 대신한다.</summary>
    [Fact]
    public void 인자로_준_트랙을_고치지_않는다()
    {
        var track = new LimitTrack { Title = "옛 이름", LastPercent = 20, LastResetsAt = First };

        var next = UsageMeter.Advance(track, Meters.Limit("weekly_all", 80, Second));

        Assert.Equal("옛 이름", track.Title);
        Assert.Equal(20d, track.LastPercent, 6);
        Assert.Equal(0d, track.Accumulated, 6);
        Assert.Equal(0, track.Resets);
        Assert.Equal("주간 (7일)", next.Title);
    }
}

/// <summary>서버가 <c>limits</c> 를 안 주면 지어낸다. 이게 없으면 옛 서버에서 측정이 통째로 빈다.</summary>
public class UsageMeterLimitsTests
{
    [Fact]
    public void 서버가_준_한도가_있으면_그대로_쓴다()
    {
        var snapshot = new UsageSnapshot
        {
            FetchedAt = Meters.Origin,
            FiveHour = new UsageWindow(11, Meters.Origin),
            Limits = [Meters.Limit("session", 40), Meters.Limit("weekly_scoped", 7, model: "Opus 5")],
        };

        var limits = UsageMeter.LimitsOf(snapshot);

        Assert.Equal(2, limits.Count);
        Assert.Equal("session", limits[0].Id);
        Assert.Equal("weekly_scoped/Opus 5", limits[1].Id);
    }

    [Fact]
    public void 옛_응답이면_다섯시간과_이레로_두_개를_지어낸다()
    {
        var resets = Meters.Origin.AddHours(3);
        var snapshot = new UsageSnapshot
        {
            FetchedAt = Meters.Origin,
            FiveHour = new UsageWindow(41, resets),
            SevenDay = new UsageWindow(62, resets.AddDays(2)),
        };

        var limits = UsageMeter.LimitsOf(snapshot);

        Assert.Equal(2, limits.Count);
        Assert.Equal("session", limits[0].Id);
        Assert.Equal("세션 (5시간)", limits[0].Title);
        Assert.Equal(41d, limits[0].Percent, 6);
        Assert.Equal(resets, limits[0].ResetsAt);
        Assert.Equal("weekly_all", limits[1].Id);
        Assert.Equal("주간 (7일)", limits[1].Title);
        Assert.Equal(62d, limits[1].Percent, 6);
    }

    [Fact]
    public void 아무것도_없으면_빈_목록이다()
    {
        Assert.Empty(UsageMeter.LimitsOf(new UsageSnapshot { FetchedAt = Meters.Origin }));
    }
}

/// <summary>표본을 받아 한도를 전진시키는 자리 · 조회를 조르지 않는 30초 바닥.</summary>
public class UsageMeterSampleTests
{
    [Fact]
    public void 첫_표본은_기준점일_뿐이다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 63, Meters.Origin.AddHours(2))));

        var track = meter.State.Tracks["session"];
        Assert.Equal(0d, track.Accumulated, 6);
        Assert.Equal(63d, track.LastPercent, 6);
        Assert.Equal(new[] { "session" }, meter.State.Order);
        Assert.Equal(1, meter.State.Samples);
    }

    [Fact]
    public void 두_번째_표본부터_더한다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);
        var window = Meters.Origin.AddHours(2);

        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 63, window)));
        time.Advance(TimeSpan.FromMinutes(10));
        var at = time.GetUtcNow();
        meter.Record(Meters.Sample(at, Meters.Limit("session", 71, window)));

        Assert.Equal(8d, meter.State.Tracks["session"].Accumulated, 6);
        Assert.Equal(2, meter.State.Samples);
        // 우리 시계가 아니라 **서버 응답 시각**을 따라간다.
        Assert.Equal(at, meter.State.LastSampledAt);
    }

    [Fact]
    public void 시작하면_조회를_한_번_부탁하고_삼십초_안에는_또_안_부탁한다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);
        var asked = 0;
        meter.SampleWanted += () => asked++;

        meter.Start();
        Assert.Equal(1, asked);

        time.Advance(TimeSpan.FromSeconds(29));
        meter.Stop();
        Assert.Equal(1, asked);
    }

    [Fact]
    public void 삼십초가_지났으면_다시_부탁한다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);
        var asked = 0;
        meter.SampleWanted += () => asked++;

        meter.Start();
        time.Advance(TimeSpan.FromSeconds(31));
        meter.Stop();

        Assert.Equal(2, asked);
    }

    [Fact]
    public void 안_재는_중에_온_표본은_아무_일도_안_한다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 20)));

        Assert.Equal(0, meter.State.Samples);
        Assert.Empty(meter.State.Tracks);
    }
}

/// <summary>일시정지·계속 — 멈춰 있던 **시간**과 그동안 쓴 **몫**을 둘 다 뺀다.</summary>
public class UsageMeterPauseTests
{
    private static readonly DateTimeOffset Window = Meters.Origin.AddHours(4);

    [Fact]
    public void 세워_둔_동안_온_표본은_안_받는다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 30, Window)));
        meter.Pause();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 90, Window)));

        Assert.Equal(1, meter.State.Samples);
        Assert.Equal(0d, meter.State.Tracks["session"].Accumulated, 6);
        Assert.Equal(30d, meter.State.Tracks["session"].LastPercent, 6);
    }

    [Fact]
    public void 계속_뒤_첫_표본은_기준만_옮기고_그다음부터_더한다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 30, Window)));
        meter.Pause();
        time.Advance(TimeSpan.FromMinutes(30));
        meter.Resume();

        // 세워 둔 동안 서버 값이 크게 뛰었다 — 그건 이번 측정이 쓴 것이 아니다.
        meter.Record(Meters.Sample(time.GetUtcNow(), Meters.Limit("session", 88, Window)));
        Assert.Equal(0d, meter.State.Tracks["session"].Accumulated, 6);
        Assert.Equal(88d, meter.State.Tracks["session"].LastPercent, 6);

        meter.Record(Meters.Sample(time.GetUtcNow(), Meters.Limit("session", 91, Window)));
        Assert.Equal(3d, meter.State.Tracks["session"].Accumulated, 6);
    }

    /// <summary>깃발을 루프 안에서 내리면 첫 한도만 재기준된다.</summary>
    [Fact]
    public void 한도가_셋이면_계속_뒤_첫_표본에서_셋_다_재기준된다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        UsageSnapshot Three(double a, double b, double c) => Meters.Sample(
            time.GetUtcNow(),
            Meters.Limit("session", a, Window),
            Meters.Limit("weekly_all", b, Window),
            Meters.Limit("weekly_scoped", c, Window, model: "Opus 5"));

        meter.Start();
        meter.Record(Three(10, 20, 30));
        meter.Pause();
        meter.Resume();
        meter.Record(Three(60, 70, 80));

        Assert.All(meter.State.TracksInOrder, track => Assert.Equal(0d, track.Accumulated, 6));
        Assert.Equal(new[] { 60d, 70d, 80d }, meter.State.TracksInOrder.Select(track => track.LastPercent));
    }

    [Fact]
    public void 안_재는_중에는_일시정지도_계속도_아무_일이_없다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Pause();
        meter.Resume();
        Assert.False(meter.IsRunning);
        Assert.Null(meter.State.StartedAt);

        meter.Start();
        meter.Resume();   // 세운 적이 없다
        Assert.True(meter.IsCounting);
        Assert.Equal(TimeSpan.Zero, meter.State.PausedTotal);
    }
}

/// <summary>중지 — 그 자리에서 남기고, 늦게 오는 마지막 표본을 딱 한 번 통과시킨다.</summary>
public class UsageMeterStopTests
{
    private static readonly DateTimeOffset Window = Meters.Origin.AddHours(4);

    [Fact]
    public void 조회가_한_번도_안_돌아와도_중지하면_기록이_남는다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        time.Advance(TimeSpan.FromMinutes(3));
        meter.Stop();

        var record = Assert.Single(meter.State.History);
        Assert.Equal(TimeSpan.FromMinutes(3), record.Duration);
        Assert.Equal(0, record.Samples);
    }

    [Fact]
    public void 중지_뒤_늦게_온_표본_하나는_기록에_들어간다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 30, Window)));
        time.Advance(TimeSpan.FromMinutes(5));
        meter.Stop();

        meter.Record(Meters.Sample(time.GetUtcNow(), Meters.Limit("session", 37, Window)));

        Assert.Equal(7d, meter.State.History[0].Tracks[0].Accumulated, 6);
        Assert.Equal(2, meter.State.History[0].Samples);
    }

    [Fact]
    public void 두_번째_표본은_통과하지_못한다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 30, Window)));
        meter.Stop();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 37, Window)));
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 99, Window)));

        Assert.Equal(7d, meter.State.Tracks["session"].Accumulated, 6);
        Assert.Equal(2, meter.State.Samples);
    }

    /// <summary>앞 측정에서 남은 티켓이 새 측정의 첫 표본을 삼키면 안 된다.</summary>
    [Fact]
    public void 다시_시작하면_마지막_표본_티켓이_내려간다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Stop();
        time.Advance(TimeSpan.FromMinutes(1));
        meter.Start();

        meter.Record(Meters.Sample(time.GetUtcNow(), Meters.Limit("session", 55, Window)));

        Assert.Equal(1, meter.State.Samples);
        Assert.Equal(0d, meter.State.Tracks["session"].Accumulated, 6);
    }

    [Fact]
    public void 재는_중이_아닐_때_중지를_눌러도_기록이_안_는다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Stop();
        Assert.Empty(meter.State.History);

        meter.Start();
        meter.Stop();
        meter.Stop();
        Assert.Single(meter.State.History);
    }

    [Fact]
    public void 멈춰_있는_채로_중지하면_멈춘_몫이_잰_시간에서_빠진다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        time.Advance(TimeSpan.FromMinutes(10));
        meter.Pause();
        time.Advance(TimeSpan.FromMinutes(50));
        meter.Stop();

        Assert.Null(meter.State.PausedAt);
        Assert.Equal(TimeSpan.FromMinutes(50), meter.State.PausedTotal);
        Assert.Equal(TimeSpan.FromMinutes(10), meter.State.History[0].Duration - meter.State.PausedTotal);
    }
}

/// <summary>끝난 측정을 얼려 두는 자리. 상한 50개.</summary>
public class UsageMeterHistoryTests
{
    private static readonly DateTimeOffset Window = Meters.Origin.AddHours(4);

    [Fact]
    public void 오십일번_재면_가장_오래된_것이_빠진다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        var first = time.GetUtcNow();
        for (var i = 0; i < 51; i++)
        {
            meter.Start();
            time.Advance(TimeSpan.FromMinutes(1));
            meter.Stop();
            time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(UsageMeter.HistoryLimit, meter.State.History.Count);
        Assert.DoesNotContain(meter.State.History, record => record.StartedAt == first);
        // 최신이 앞이다.
        Assert.True(meter.State.History[0].StartedAt > meter.State.History[^1].StartedAt);
    }

    [Fact]
    public void 다시_시작해도_지난_기록은_그대로다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Stop();
        time.Advance(TimeSpan.FromMinutes(1));
        meter.Start();

        Assert.Single(meter.State.History);
    }

    [Fact]
    public void 재는_중에_온_표본은_목록을_안_건드린다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Stop();
        time.Advance(TimeSpan.FromMinutes(1));

        meter.Start();
        meter.Record(Meters.Sample(time.GetUtcNow(), Meters.Limit("session", 20, Window)));
        meter.Record(Meters.Sample(time.GetUtcNow(), Meters.Limit("session", 44, Window)));

        Assert.Single(meter.State.History);
        Assert.Equal(0, meter.State.History[0].Samples);
        Assert.Empty(meter.State.History[0].Tracks);
    }

    [Fact]
    public void 기록_하나만_지운다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        var starts = new List<DateTimeOffset>();
        for (var i = 0; i < 3; i++)
        {
            meter.Start();
            starts.Add(meter.State.StartedAt!.Value);
            time.Advance(TimeSpan.FromMinutes(1));
            meter.Stop();
            time.Advance(TimeSpan.FromMinutes(1));
        }

        meter.DeleteRecord(starts[1]);

        Assert.Equal(2, meter.State.History.Count);
        Assert.DoesNotContain(meter.State.History, record => record.StartedAt == starts[1]);
    }

    [Fact]
    public void 목록을_비워도_재던_것은_그대로다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Stop();
        time.Advance(TimeSpan.FromMinutes(1));
        meter.Start();
        meter.Record(Meters.Sample(time.GetUtcNow(), Meters.Limit("session", 20, Window)));

        meter.ClearHistory();

        Assert.Empty(meter.State.History);
        Assert.True(meter.IsCounting);
        Assert.Equal(1, meter.State.Samples);
    }

    /// <summary>
    /// **여기가 맥과 가장 크게 갈리는 자리다.** 기록에 살아 있는 트랙 참조를 담으면
    /// 중지 뒤 늦게 온 표본이 목록 안의 값까지 함께 바꾼다.
    /// </summary>
    [Fact]
    public void 목록에_담긴_트랙은_뒤이은_표본에_안_흔들린다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 30, Window)));
        meter.Stop();

        var frozen = meter.State.History[0];
        Assert.Equal(0d, frozen.Tracks[0].Accumulated, 6);

        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 46, Window)));

        Assert.Equal(0d, frozen.Tracks[0].Accumulated, 6);
        Assert.Equal(16d, meter.State.History[0].Tracks[0].Accumulated, 6);
    }
}

/// <summary>
/// 비동기 훑기와 표식 대조.
///
/// 훑는 사이에 다시 시작·계속이 눌리면 **결과를 통째로 버려야 한다** — 안 버리면 오프셋이
/// 옛 자리로 되감기고, 그 뒤 모든 훑기가 같은 구간을 다시 읽어 값이 계속 커진다.
/// </summary>
public class UsageMeterScanTests
{
    /// <summary>
    /// <c>Moved</c> 를 세워 둔다 — 손으로 지어낸 결과의 기본값은 "아무것도 안 움직였다"
    /// 라서, 안 세우면 얹는 쪽이 통째로 건너뛴다.
    /// </summary>
    private static TokenScanResult Result(long output = 100, string path = @"C:\a.jsonl", long offset = 500)
        => new()
        {
            Added = new TokenTally(Responses: 1, Input: 10, Output: output, CacheCreation: 0, CacheRead: 0),
            AddedByModel = new Dictionary<string, TokenTally>(StringComparer.Ordinal)
            {
                ["Opus 5"] = new(1, 10, output, 0, 0),
            },
            Offsets = new Dictionary<string, long>(ClaudeCodeUsage.PathComparer) { [path] = offset },
            SeenIds = new HashSet<string>(StringComparer.Ordinal) { "msg_1" },
            Moved = true,
        };

    [Fact]
    public async Task 훑는_도중_다시_시작하면_결과를_버린다()
    {
        using var root = new Meters.TempRoot();
        var time = new Meters.FakeTime(Meters.Origin);
        var release = new TaskCompletionSource<TokenScanResult>();
        var meter = Meters.Meter(time, root: root.Path, scan: (_, _) => release.Task);

        meter.Start();
        var scanning = meter.ScanTokensAsync();
        Assert.True(meter.IsScanning);

        time.Advance(TimeSpan.FromMinutes(1));
        meter.Start();                       // 표식이 달라진다
        release.SetResult(Result());
        await scanning;

        Assert.True(meter.State.Tokens.IsEmpty);
        Assert.Empty(meter.State.SeenIds);
        // 새로 잡은 기준이 옛 자리로 되감기지 않았다.
        Assert.Empty(meter.State.Offsets);
    }

    [Fact]
    public async Task 훑는_도중_계속을_눌러도_버린다()
    {
        using var root = new Meters.TempRoot();
        var time = new Meters.FakeTime(Meters.Origin);
        var release = new TaskCompletionSource<TokenScanResult>();
        var meter = Meters.Meter(time, root: root.Path, scan: (_, _) => release.Task);

        meter.Start();
        var scanning = meter.ScanTokensAsync();
        Assert.True(meter.IsScanning);

        meter.Pause();
        time.Advance(TimeSpan.FromMinutes(5));
        meter.Resume();                      // PausedTotal 이 달라진다
        release.SetResult(Result());
        await scanning;

        Assert.True(meter.State.Tokens.IsEmpty);
    }

    /// <summary>중지는 표식을 안 바꾼다 — 마지막 몇십 초 몫이 사라지면 안 된다.</summary>
    [Fact]
    public async Task 훑는_도중_중지하면_결과가_반영된다()
    {
        using var root = new Meters.TempRoot();
        var time = new Meters.FakeTime(Meters.Origin);
        var release = new TaskCompletionSource<TokenScanResult>();
        var meter = Meters.Meter(time, root: root.Path, scan: (_, _) => release.Task);

        meter.Start();
        var scanning = meter.ScanTokensAsync();
        meter.Stop();
        release.SetResult(Result(output: 250));
        await scanning;

        Assert.Equal(250, meter.State.Tokens.Output);
        Assert.Equal(250, meter.State.TokensByModel["Opus 5"].Output);
        // 중지 뒤 훑기는 목록에 남긴 기록까지 갱신한다.
        Assert.Equal(250, meter.State.History[0].Tokens.Output);
    }

    [Fact]
    public async Task 훑는_중에_또_부르면_두_번_돌지_않는다()
    {
        using var root = new Meters.TempRoot();
        var time = new Meters.FakeTime(Meters.Origin);
        var release = new TaskCompletionSource<TokenScanResult>();
        var runs = 0;
        var meter = Meters.Meter(time, root: root.Path, scan: (_, _) => { runs++; return release.Task; });

        meter.Start();
        var scanning = meter.ScanTokensAsync();   // 하나가 돌기 시작한다
        await meter.ScanTokensAsync();            // 겹쳐 부른 것은 그 자리에서 돌아선다
        await meter.ScanTokensAsync();

        Assert.Equal(1, runs);
        release.SetResult(Result());
        await scanning;
        Assert.Equal(100, meter.State.Tokens.Output);
    }

    /// <summary>
    /// **시작은 훑지 않는다.** 방금 모든 오프셋을 파일 끝으로 못 박아 놓고 훑으면 볼 것이
    /// 있을 수 없다 — 파일 200개를 열어 보고 0을 더할 뿐이다.
    /// </summary>
    [Fact]
    public void 시작이_훑기를_걸지_않는다()
    {
        using var root = new Meters.TempRoot();
        var time = new Meters.FakeTime(Meters.Origin);
        var runs = 0;
        var meter = Meters.Meter(time, root: root.Path,
            scan: (_, _) => { runs++; return Task.FromResult(Result()); });

        meter.Start();

        Assert.Equal(0, runs);
        Assert.False(meter.IsScanning);
    }

    /// <summary>
    /// 놀고 있으면 훑기가 빈손으로 돌아온다. 그때마다 새 상태를 만들면 **바이트까지 같은
    /// <c>meter.json</c>** 을 1분에 열두 번 다시 쓰고, 알림이 설정 창 탭을 그만큼 다시 만든다.
    /// </summary>
    [Fact]
    public async Task 아무것도_안_움직인_훑기는_상태를_안_건드린다()
    {
        using var root = new Meters.TempRoot();
        var time = new Meters.FakeTime(Meters.Origin);
        var idle = new TokenScanResult();     // Moved 가 false 다
        var meter = Meters.Meter(time, root: root.Path, scan: (_, _) => Task.FromResult(idle));

        meter.Start();
        var changed = 0;
        meter.Changed += () => changed++;
        var before = meter.State;

        await meter.ScanTokensAsync();

        Assert.Same(before, meter.State);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task 세워_둔_동안에는_안_훑는다()
    {
        using var root = new Meters.TempRoot();
        var time = new Meters.FakeTime(Meters.Origin);
        var runs = 0;
        var meter = Meters.Meter(time, root: root.Path,
            scan: (_, _) => { runs++; return Task.FromResult(Result()); });

        meter.Start();
        await meter.ScanTokensAsync();
        Assert.Equal(1, runs);

        meter.Pause();                       // 세우기 직전 한 번은 돈다
        Assert.Equal(2, runs);

        await meter.ScanTokensAsync();
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task 훑기가_던져도_표시가_내려간다()
    {
        using var root = new Meters.TempRoot();
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time, root: root.Path,
            scan: (_, _) => throw new IOException("파일이 사라졌다"));

        meter.Start();
        await meter.ScanTokensAsync();

        Assert.False(meter.IsScanning);
        Assert.True(meter.IsCounting);
    }

    [Fact]
    public async Task 기록_폴더가_없으면_아예_안_훑는다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var runs = 0;
        // 없는 폴더
        var meter = Meters.Meter(time, scan: (_, _) => { runs++; return Task.FromResult(Result()); });

        meter.Start();
        await meter.ScanTokensAsync();

        Assert.Equal(0, runs);
    }

    /// <summary>진단 통로가 그대로 쓰는 순수 함수다. 인자를 고치면 두 곳의 답이 갈린다.</summary>
    [Fact]
    public void Applying_은_인자를_고치지_않는다()
    {
        var state = new MeterState
        {
            StartedAt = Meters.Origin,
            Tokens = new TokenTally(2, 20, 30, 0, 0),
            TokensByModel = new Dictionary<string, TokenTally>(StringComparer.Ordinal)
            {
                ["Opus 5"] = new(2, 20, 30, 0, 0),
            },
            Offsets = new Dictionary<string, long>(ClaudeCodeUsage.PathComparer) { [@"C:\a.jsonl"] = 10 },
            SeenIds = new HashSet<string>(StringComparer.Ordinal) { "msg_0" },
        };

        var next = UsageMeter.Applying(Result(), state);

        Assert.Equal(30, state.Tokens.Output);
        Assert.Equal(10, state.Offsets[@"C:\a.jsonl"]);
        Assert.Single(state.SeenIds);

        Assert.Equal(130, next.Tokens.Output);
        Assert.Equal(130, next.TokensByModel["Opus 5"].Output);
        // 오프셋과 본 id 는 **통째로 갈아 끼운다** — 델타가 아니다.
        Assert.Equal(500, next.Offsets[@"C:\a.jsonl"]);
        Assert.Equal(new[] { "msg_1" }, next.SeenIds);
    }
}


/// <summary><c>meter.json</c>. 설정 파일에 섞지 않고, 판마다 갈린다.</summary>
public class MeterStoreTests
{
    private static string FileIn(Meters.TempRoot folder) => Path.Combine(folder.Path, "meter.json");

    [Fact]
    public void 쓰고_다시_읽으면_모든_칸이_같다()
    {
        using var folder = new Meters.TempRoot();
        var path = FileIn(folder);

        var state = new MeterState
        {
            StartedAt = Meters.Origin,
            StoppedAt = Meters.Origin.AddHours(2),
            Tracks = new Dictionary<string, LimitTrack>(StringComparer.Ordinal)
            {
                ["session"] = new()
                {
                    Title = "세션 (5시간)",
                    Accumulated = 104,
                    LastPercent = 30,
                    LastResetsAt = Meters.Origin.AddHours(5),
                    Resets = 1,
                },
            },
            Order = ["session"],
            Tokens = new TokenTally(536, 1824, 1_145_375, 16_885_030, 452_846_994),
            TokensByModel = new Dictionary<string, TokenTally>(StringComparer.Ordinal)
            {
                ["Opus 5"] = new(412, 1500, 980_000, 14_000_000, 400_000_000),
            },
            Offsets = new Dictionary<string, long>(ClaudeCodeUsage.PathComparer)
            {
                [@"C:\Users\me\a.jsonl"] = 4096,
            },
            SeenIds = new HashSet<string>(StringComparer.Ordinal) { "msg_1", "msg_2" },
            Samples = 34,
            LastSampledAt = Meters.Origin.AddMinutes(-3),
            PausedTotal = TimeSpan.FromMinutes(12.5),
            History =
            [
                new MeterRecord
                {
                    StartedAt = Meters.Origin.AddDays(-1),
                    StoppedAt = Meters.Origin.AddDays(-1).AddMinutes(40),
                    Tracks = [new LimitTrack { Title = "세션 (5시간)", Accumulated = 7 }],
                    Tokens = new TokenTally(40, 900, 120_000, 1_400_000, 32_000_000),
                    Samples = 3,
                },
            ],
        };

        new MeterStore(path).Write(state);
        var read = new MeterStore(path).Read();

        Assert.NotNull(read);
        Assert.Equal(state.StartedAt, read.StartedAt);
        Assert.Equal(state.StoppedAt, read.StoppedAt);
        Assert.Equal(104d, read.Tracks["session"].Accumulated, 6);
        Assert.Equal(1, read.Tracks["session"].Resets);
        Assert.Equal(state.Tracks["session"].LastResetsAt, read.Tracks["session"].LastResetsAt);
        Assert.Equal(new[] { "session" }, read.Order);
        Assert.Equal(452_846_994, read.Tokens.CacheRead);
        Assert.Equal(400_000_000, read.TokensByModel["Opus 5"].CacheRead);
        Assert.Equal(34, read.Samples);
        Assert.Equal(state.LastSampledAt, read.LastSampledAt);
        Assert.Equal(TimeSpan.FromMinutes(12.5), read.PausedTotal);
        Assert.Equal(2, read.SeenIds.Count);
        Assert.Single(read.History);
        Assert.Equal(TimeSpan.FromMinutes(40), read.History[0].Duration);

        // **비교자가 되살아나야 한다.** 직렬화를 건너온 사전은 기본 비교자를 달고 오는데,
        // 그대로 두면 대소문자만 다른 경로가 새 항목이 되어 그 파일을 처음부터 다시 센다.
        var lookup = OperatingSystem.IsWindows() ? @"C:\USERS\ME\A.JSONL" : @"C:\Users\me\a.jsonl";
        Assert.Equal(4096, read.Offsets[lookup]);
    }

    /// <summary>계산 속성이 끼면 파일만 부풀고 사람이 읽기 나빠진다.</summary>
    [Fact]
    public void 파일이_PascalCase_한_줄이고_계산_속성이_없다()
    {
        using var folder = new Meters.TempRoot();
        var path = FileIn(folder);

        new MeterStore(path).Write(new MeterState
        {
            StartedAt = Meters.Origin,
            StoppedAt = Meters.Origin.AddHours(1),
            Tokens = new TokenTally(1, 2, 3, 4, 5),
            History = [new MeterRecord { StartedAt = Meters.Origin, StoppedAt = Meters.Origin }],
        });

        var text = File.ReadAllText(path);

        Assert.Contains("\"StartedAt\"", text);
        Assert.DoesNotContain("\"startedAt\"", text);
        Assert.DoesNotContain("\n", text);                 // 들여쓰기 없음
        Assert.DoesNotContain("\"Total\"", text);
        Assert.DoesNotContain("\"WithoutCache\"", text);
        Assert.DoesNotContain("\"IsEmpty\"", text);
        Assert.DoesNotContain("\"Duration\"", text);
        Assert.DoesNotContain("\"IsRunning\"", text);
        Assert.DoesNotContain("\"TracksInOrder\"", text);
        Assert.DoesNotContain("\"Stamp\"", text);
    }

    [Fact]
    public void 파일이_없으면_null_이다()
    {
        using var folder = new Meters.TempRoot();
        Assert.Null(new MeterStore(FileIn(folder)).Read());
    }

    [Fact]
    public void 깨진_파일이면_던지지_않고_null_이다()
    {
        using var folder = new Meters.TempRoot();
        var path = FileIn(folder);
        File.WriteAllText(path, "{ 이건 JSON 이 아니다");

        Assert.Null(new MeterStore(path).Read());
    }

    /// <summary>쓰는 도중에 죽으면 <c>.tmp</c> 가 남는데, 본 파일은 멀쩡해야 한다.</summary>
    [Fact]
    public void tmp_가_남아_있어도_본_파일을_읽는다()
    {
        using var folder = new Meters.TempRoot();
        var path = FileIn(folder);

        new MeterStore(path).Write(new MeterState { Samples = 7 });
        File.WriteAllText(path + ".tmp", "{ 반쯤 쓰이다 말았다");

        Assert.Equal(7, new MeterStore(path).Read()?.Samples);
    }

    /// <summary>
    /// 측정 기록은 사용자의 자격 증명이 아니라 앱의 데이터라 **판마다 갈린다** —
    /// <c>AppPaths.SharedFile</c>(token.json 자리)이 아니라 <c>AppPaths.File</c> 이어야 한다.
    ///
    /// <c>AppPaths.UseFolder</c> 를 여기서 부르지 않는다 — 프로세스 전체가 바뀌어 나란히
    /// 도는 다른 검사가 흔들린다. 대신 <c>Root</c> 를 따라가는지로 못 박는다.
    /// </summary>
    [Fact]
    public void 기본_자리는_판마다_갈리는_폴더다()
    {
        Assert.Equal("meter.json", Path.GetFileName(MeterStore.DefaultPath));
        Assert.Equal(AppPaths.Root, Path.GetDirectoryName(MeterStore.DefaultPath));
    }

    [Fact]
    public void 저장소를_안_줘도_값은_그대로_돈다()
    {
        var time = new Meters.FakeTime(Meters.Origin);
        var meter = Meters.Meter(time);

        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 20)));
        time.Advance(TimeSpan.FromMinutes(2));
        meter.Stop();

        Assert.Single(meter.State.History);
    }

    [Fact]
    public void 저장소를_주면_바뀔_때마다_곧바로_쓴다()
    {
        using var folder = new Meters.TempRoot();
        var path = FileIn(folder);
        var time = new Meters.FakeTime(Meters.Origin);

        var meter = new UsageMeter(new MeterStore(path), time, Meters.MissingRoot());
        meter.Start();
        Assert.True(File.Exists(path));

        time.Advance(TimeSpan.FromMinutes(4));
        meter.Stop();

        var restored = new UsageMeter(new MeterStore(path), time, Meters.MissingRoot());
        Assert.Single(restored.State.History);
        Assert.Equal(TimeSpan.FromMinutes(4), restored.State.History[0].Duration);
    }

    /// <summary>몇 시간짜리 측정이 재시작 한 번에 날아가면 쓸모가 없다.</summary>
    [Fact]
    public void 껐다_켜도_재던_측정이_이어진다()
    {
        using var folder = new Meters.TempRoot();
        var path = FileIn(folder);
        var window = Meters.Origin.AddHours(3);
        var time = new Meters.FakeTime(Meters.Origin);

        var meter = new UsageMeter(new MeterStore(path), time, Meters.MissingRoot());
        meter.Start();
        meter.Record(Meters.Sample(Meters.Origin, Meters.Limit("session", 20, window)));

        time.Advance(TimeSpan.FromHours(2));
        var restored = new UsageMeter(new MeterStore(path), time, Meters.MissingRoot());

        Assert.True(restored.IsCounting);
        Assert.Equal(TimeSpan.FromHours(2), restored.Elapsed(time.GetUtcNow()));
        Assert.Equal(20d, restored.State.Tracks["session"].LastPercent, 6);

        // 이어받은 측정도 그대로 더한다.
        restored.Record(Meters.Sample(time.GetUtcNow(), Meters.Limit("session", 26, window)));
        Assert.Equal(6d, restored.State.Tracks["session"].Accumulated, 6);
    }
}
