using DongCSU.Core;

namespace DongCSU.Core.Tests;

public class ProcessUsageTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 첫_표본은_0퍼센트다()
    {
        var source = new FakeSource { CpuTime = TimeSpan.FromSeconds(9999), MemoryBytes = 1 };
        var sampler = new ProcessUsageSampler(source, processorCount: 4, new MovingTime(Start));

        // 시작부터의 평균을 쓰면 뜨는 순간의 바쁜 값이 계속 남는다.
        Assert.Equal(0, sampler.Sample().CpuPercent);
    }

    [Fact]
    public void 표본_사이의_점유율을_낸다()
    {
        var source = new FakeSource();
        var clock = new MovingTime(Start);
        var sampler = new ProcessUsageSampler(source, processorCount: 4, clock);

        sampler.Sample();

        // 4초 동안 코어 하나를 가득(4초) 썼다 → 4코어 기준 25%.
        clock.Advance(TimeSpan.FromSeconds(4));
        source.CpuTime = TimeSpan.FromSeconds(4);

        Assert.Equal(25, sampler.Sample().CpuPercent, 3);
    }

    /// <summary>
    /// 맥판은 코어 수로 나누지 않아 같은 부하가 100%로 나온다. 윈도우는 작업 관리자와
    /// 맞춰야 해서 나눈다 — 8코어에서 800%가 뜨면 그대로 버그 신고가 된다.
    /// </summary>
    [Fact]
    public void 코어_수로_나눈다()
    {
        var source = new FakeSource();
        var clock = new MovingTime(Start);
        var sampler = new ProcessUsageSampler(source, processorCount: 8, clock);

        sampler.Sample();
        clock.Advance(TimeSpan.FromSeconds(1));
        source.CpuTime = TimeSpan.FromSeconds(1);

        Assert.Equal(12.5, sampler.Sample().CpuPercent, 3);
    }

    /// <summary>시계가 뒤로 가거나 CPU 시간이 줄어들면 음수가 된다. 0 으로 막는다.</summary>
    [Fact]
    public void 음수가_되지_않는다()
    {
        var source = new FakeSource { CpuTime = TimeSpan.FromSeconds(10) };
        var clock = new MovingTime(Start);
        var sampler = new ProcessUsageSampler(source, processorCount: 4, clock);

        sampler.Sample();
        clock.Advance(TimeSpan.FromSeconds(2));
        source.CpuTime = TimeSpan.FromSeconds(3);

        Assert.Equal(0, sampler.Sample().CpuPercent);
    }

    [Fact]
    public void 시간이_흐르지_않으면_이전_값을_유지한다()
    {
        var source = new FakeSource();
        var clock = new MovingTime(Start);
        var sampler = new ProcessUsageSampler(source, processorCount: 4, clock);

        sampler.Sample();
        source.CpuTime = TimeSpan.FromSeconds(1);

        // 0 으로 나누지 않는다.
        Assert.Equal(0, sampler.Sample().CpuPercent);
    }

    [Fact]
    public void 다시_처음부터_셀_수_있다()
    {
        var source = new FakeSource();
        var clock = new MovingTime(Start);
        var sampler = new ProcessUsageSampler(source, processorCount: 4, clock);

        sampler.Sample();
        sampler.Reset();

        // 멈춰 둔 사이에 쌓인 CPU 시간이 한꺼번에 튀어 보이면 안 된다.
        clock.Advance(TimeSpan.FromHours(1));
        source.CpuTime = TimeSpan.FromMinutes(30);

        Assert.Equal(0, sampler.Sample().CpuPercent);
    }

    [Theory]
    [InlineData(0, "0.0%")]
    [InlineData(1.44, "1.4%")]
    [InlineData(12.35, "12.4%")]
    public void 점유율_문구(double percent, string expected)
    {
        Assert.Equal(expected, new ProcessUsage(percent, 0).CpuText);
    }

    [Theory]
    [InlineData(0, "0MB")]
    [InlineData(65_011_712, "62MB")]
    [InlineData(1_073_741_824, "1024MB")]
    public void 메모리_문구(long bytes, string expected)
    {
        Assert.Equal(expected, new ProcessUsage(0, bytes).MemoryText);
    }

    private sealed class FakeSource : IProcessSampleSource
    {
        public TimeSpan CpuTime { get; set; }
        public long MemoryBytes { get; set; }
    }

    private sealed class MovingTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }
}
