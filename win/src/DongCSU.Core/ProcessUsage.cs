using System.Diagnostics;

namespace DongCSU.Core;

/// <summary>이 앱이 지금 쓰고 있는 자원.</summary>
public readonly record struct ProcessUsage(double CpuPercent, long MemoryBytes)
{
    public string CpuText => $"{CpuPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}%";

    public string MemoryText =>
        $"{Math.Round(MemoryBytes / 1048576.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture)}MB";
}

/// <summary>
/// 자기 프로세스의 CPU 시간과 메모리를 어디서 읽을지.
///
/// 갈라 둔 이유는 **테스트 때문**이다. 진짜 프로세스를 재면 값이 매번 달라서
/// 계산이 맞는지 확인할 방법이 없다.
/// </summary>
public interface IProcessSampleSource
{
    /// <summary>프로세스가 시작한 뒤 지금까지 쓴 CPU 시간.</summary>
    TimeSpan CpuTime { get; }

    long MemoryBytes { get; }
}

/// <summary>지금 이 프로세스를 잰다. 외부 명령을 띄우지 않는다.</summary>
public sealed class CurrentProcessSource : IProcessSampleSource
{
    public TimeSpan CpuTime => Process.GetCurrentProcess().TotalProcessorTime;

    /// <summary>
    /// 작업 관리자의 <b>작업 집합</b>과 같은 기준이다.
    ///
    /// 맥판은 <c>phys_footprint</c> 를 쓰는데 기준이 달라서 **두 판의 숫자가 조금
    /// 다르게 나온다.** 각자 자기 OS 의 도구와 맞추는 편이 낫다 — 사용자가 견줄
    /// 상대는 반대편 판이 아니라 자기 컴퓨터의 작업 관리자다.
    /// </summary>
    public long MemoryBytes => Process.GetCurrentProcess().WorkingSet64;
}

/// <summary>
/// 표본 두 개 사이의 CPU 시간 차이로 점유율을 낸다.
///
/// **코어 수로 나눈다.** 맥판은 나누지 않아서 한 코어를 가득 쓰면 100%로 나오지만,
/// 윈도우 사용자가 견줄 상대는 작업 관리자이고 그쪽은 전체 코어 기준이다. 같은 부하를
/// 8코어에서 800%로 보여주면 그대로 버그 신고가 된다.
/// </summary>
public sealed class ProcessUsageSampler(
    IProcessSampleSource source,
    int? processorCount = null,
    TimeProvider? time = null)
{
    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly int processorCount = Math.Max(1, processorCount ?? Environment.ProcessorCount);

    private TimeSpan? previousCpuTime;
    private DateTimeOffset? previousAt;

    /// <summary>
    /// 한 번 잰다. **첫 표본은 0%다** — 견줄 이전 값이 없다.
    ///
    /// 시작부터 지금까지의 평균을 쓰지 않는다. 뜨는 순간이 제일 바쁜데 그 값을
    /// 계속 들고 있으면, 한참 놀고 있어도 높은 숫자가 그대로 남는다.
    /// </summary>
    public ProcessUsage Sample()
    {
        var now = time.GetUtcNow();
        var cpuTime = source.CpuTime;

        var percent = 0.0;
        if (previousCpuTime is { } beforeCpu && previousAt is { } beforeAt)
        {
            var elapsed = (now - beforeAt).TotalSeconds;
            if (elapsed > 0)
            {
                var used = (cpuTime - beforeCpu).TotalSeconds;
                percent = Math.Max(0, used / elapsed / processorCount * 100);
            }
        }

        previousCpuTime = cpuTime;
        previousAt = now;

        return new ProcessUsage(percent, source.MemoryBytes);
    }

    /// <summary>다시 처음부터 센다. 한동안 멈춰 뒀다가 켤 때 부른다.</summary>
    public void Reset()
    {
        previousCpuTime = null;
        previousAt = null;
    }
}
