using System.Text.Json.Serialization;

namespace DongCSU.Core.Usage;

/// <summary>
/// 한도 하나를 따라가는 기록.
///
/// **창이 리셋돼도 <see cref="Accumulated"/> 는 계속 더한다.** 5시간 창은 재는 도중에
/// 반드시 한 번은 새로 열리고 그때 값이 0으로 떨어지는데, 그냥 빼면 그때까지의 기록이
/// 통째로 날아간다.
///
/// 맥은 struct 라 대입만으로 복사되지만 **C# 은 참조 타입이다** — 기록에 남긴 것이
/// 계속 움직이지 않게, 남에게 넘길 때는 반드시 <see cref="Clone"/> 한다.
/// </summary>
public sealed class LimitTrack
{
    /// <summary>화면 이름. 표본마다 새 값으로 옮긴다 — 서버가 이름을 바꿔도 따라간다.</summary>
    public string Title { get; set; } = "";

    /// <summary>여태 쌓인 소모량(%p).</summary>
    public double Accumulated { get; set; }

    /// <summary>직전에 본 값. 다음 표본과 견줘 증가분을 뽑는다.</summary>
    public double LastPercent { get; set; }

    public DateTimeOffset? LastResetsAt { get; set; }

    /// <summary>재는 동안 창이 몇 번 새로 열렸는지.</summary>
    public int Resets { get; set; }

    public LimitTrack Clone() => new()
    {
        Title = Title,
        Accumulated = Accumulated,
        LastPercent = LastPercent,
        LastResetsAt = LastResetsAt,
        Resets = Resets,
    };
}

/// <summary>
/// 끝난 측정 하나.
///
/// **중지하는 그 순간의 값을 통째로 얼려 둔다.** 나중에 다시 계산하지 않으므로 그때
/// 무엇을 봤는지가 그대로 남는다. 만든 뒤에는 아무도 고치지 않는다 — 칸이 열려 있는
/// 것은 System.Text.Json 이 읽고 쓰기 위해서다.
/// </summary>
public sealed class MeterRecord
{
    /// <summary>
    /// 시작 시각이 곧 구분자다(맥의 <c>id</c>). 같은 순간에 두 번 시작할 수 없다.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset StoppedAt { get; set; }

    /// <summary>화면에 늘어놓는 차례 그대로.</summary>
    public List<LimitTrack> Tracks { get; set; } = [];

    public TokenTally Tokens { get; set; }

    public Dictionary<string, TokenTally> TokensByModel { get; set; } = new(StringComparer.Ordinal);

    public int Samples { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => StoppedAt - StartedAt;
}

/// <summary>
/// 지금 재고 있는 것을 가리키는 표식.
///
/// **다시 시작하면 <see cref="StartedAt"/> 이, 계속을 누르면 <see cref="PausedTotal"/> 이
/// 달라진다.** 중지는 둘 다 그대로 두므로 중지 직후의 마지막 훑기는 이 대조를 통과한다 —
/// 표식에 <c>StoppedAt</c> 이나 <c>PausedAt</c> 을 넣으면 그 마지막 몫이 통째로 사라진다.
///
/// 값 비교가 저절로 되는 <c>record struct</c> 라야 <c>!=</c> 가 뜻대로 동작한다.
/// </summary>
public readonly record struct SessionStamp(DateTimeOffset? StartedAt, TimeSpan PausedTotal);

/// <summary>
/// 재는 중인 것과 끝난 기록을 함께 담는 상태 덩어리. 통째로 <c>meter.json</c> 에 오간다.
///
/// **전체가 저장 대상이라 앱을 껐다 켜도 몇 시간짜리 측정이 이어진다.** 다만
/// <c>needsRebaseline</c>·<c>acceptsFinalSample</c> 은 여기 없다 — 버튼을 누른 그 순간에만
/// 유효한 값이라 껐다 켜면 사라지는 것이 맞다.
///
/// 고치는 자리는 <see cref="UsageMeter"/> 하나뿐이고, 거기서도 **복사본을 고쳐 통째로
/// 갈아 끼운다**(<see cref="Copy"/>). 그래서 밖으로 나간 상태는 다시는 안 움직이고,
/// 화면이 배경 훑기와 부딪치지 않는다. 사전·집합 칸도 **그 자리에서 고치는 사람이 없다** —
/// 배경 훑기가 그 참조를 들고 나가도 안전한 것이 그 약속 덕이다.
///
/// <para>
/// <b><c>class</c> 가 아니라 <c>record</c> 인 것은 <see cref="Copy"/> 때문이다.</b> 칸을
/// 손으로 열셋 옮겨 적던 시절에는 칸을 하나 더하면서 <c>Copy</c> 에 적는 것을 잊는 순간
/// 그 값이 **모든 상태 변화에서 조용히 초기화됐다.** <c>with</c> 는 안 적은 칸을 그대로
/// 가져오므로 적어야 하는 것이 "참조를 나눠 쓰면 안 되는 칸" 으로 줄어든다.
/// 직렬화는 달라지지 않는다 — <c>record</c> 가 더하는 <c>EqualityContract</c> 는
/// <c>protected</c> 라 System.Text.Json 이 보지 않는다.
/// </para>
/// </summary>
public sealed record MeterState
{
    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? StoppedAt { get; set; }

    public Dictionary<string, LimitTrack> Tracks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>화면에 늘어놓는 차례. 사전은 순서가 없어서 따로 들고 있는다.</summary>
    public List<string> Order { get; set; } = [];

    public TokenTally Tokens { get; set; }

    public Dictionary<string, TokenTally> TokensByModel { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 파일마다 어디까지 읽었는지(바이트).
    ///
    /// **열쇠가 경로라 비교자가 <see cref="ClaudeCodeUsage.PathComparer"/> 여야 한다.**
    /// 안 그러면 대소문자만 다른 경로가 새 항목이 되어 같은 파일을 처음부터 다시 세고,
    /// 토큰이 두 배가 된다. 직렬화를 건너오면 비교자가 기본값으로 되돌아오므로
    /// <see cref="Copy"/> 가 읽은 직후에 다시 싼다.
    /// </summary>
    public Dictionary<string, long> Offsets { get; set; } = new(ClaudeCodeUsage.PathComparer);

    /// <summary>이미 센 <c>message.id</c>. 경로와 달리 **대소문자를 가린다.**</summary>
    public HashSet<string> SeenIds { get; set; } = new(StringComparer.Ordinal);

    public int Samples { get; set; }

    /// <summary>마지막 표본의 <c>FetchedAt</c>. 우리 시계가 아니라 서버 응답 시각이다.</summary>
    public DateTimeOffset? LastSampledAt { get; set; }

    /// <summary>일시정지한 시각. null 이면 돌고 있다.</summary>
    public DateTimeOffset? PausedAt { get; set; }

    /// <summary>여태 멈춰 있던 시간의 합. 잰 시간에서 뺀다.</summary>
    public TimeSpan PausedTotal { get; set; }

    /// <summary>끝난 측정들. 최신이 앞이다.</summary>
    public List<MeterRecord> History { get; set; } = [];

    [JsonIgnore]
    public bool IsRunning => StartedAt is not null && StoppedAt is null;

    [JsonIgnore]
    public bool IsPaused => IsRunning && PausedAt is not null;

    /// <summary>실제로 세고 있는 중. 일시정지 동안에는 표본도 토큰도 받지 않는다.</summary>
    [JsonIgnore]
    public bool IsCounting => IsRunning && PausedAt is null;

    /// <summary>차례대로 늘어놓은 한도. <see cref="Order"/> 에 있는데 사라진 것은 건너뛴다.</summary>
    [JsonIgnore]
    public IReadOnlyList<LimitTrack> TracksInOrder
    {
        get
        {
            var tracks = new List<LimitTrack>(Order.Count);
            foreach (var id in Order)
            {
                if (Tracks.TryGetValue(id, out var track)) tracks.Add(track);
            }
            return tracks;
        }
    }

    /// <summary>훑기 결과를 얹기 전에 대조할 표식. <see cref="SessionStamp"/> 참고.</summary>
    [JsonIgnore]
    public SessionStamp Stamp => new(StartedAt, PausedTotal);

    /// <summary>
    /// 잰 시간. 재는 중이면 <paramref name="now"/> 까지, 멈췄으면 멈춘 시점까지.
    ///
    /// **멈춰 있던 시간은 뺀다.** 안 그러면 잠깐 세우고 밥 먹고 온 시간이 측정에 들어간다.
    /// 시계를 안에서 읽지 않는 것은 검사가 시간을 밀 수 있어야 하기 때문이다.
    /// </summary>
    public TimeSpan? Elapsed(DateTimeOffset now)
    {
        if (StartedAt is not { } startedAt) return null;

        var end = StoppedAt ?? now;
        var paused = PausedTotal;
        if (PausedAt is { } pausedAt) paused += end - pausedAt;

        var elapsed = end - startedAt - paused;
        return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
    }

    /// <summary>
    /// 깊은 복사.
    ///
    /// 맥이 struct 대입으로 공짜로 얻는 성질을 여기서 손으로 만든다. 고치는 쪽은 늘
    /// 복사본을 고쳐 통째로 갈아 끼우므로, 이미 밖으로 나간 상태는 두 번 다시 안 움직인다.
    ///
    /// **사전 비교자도 여기서 되살린다** — 직렬화를 건너온 사전은 기본 비교자를 달고 온다.
    /// </summary>
    public MeterState Copy()
        => CopyAdopting(CopiedOffsets(Offsets), new HashSet<string>(SeenIds, StringComparer.Ordinal));

    /// <summary>
    /// 훑기 결과를 얹을 때 쓰는 복사. 오프셋과 본 id 는 베끼지 않고 **넘겨받은 것을 그대로
    /// 들인다**(<see cref="UsageMeter.Applying"/>).
    ///
    /// **베껴 봐야 그 자리에서 버려진다.** 얹기는 그 둘을 통째로 갈아 끼우는 일이라
    /// (<see cref="TokenScanResult"/> 가 델타가 아니라 전체를 담는다) 깊은 복사가 곧바로
    /// 쓰레기가 된다 — 파일 200개·id 수천 개면 훑기 한 번에 0.5ms 와 수십 MB 를 그렇게 버렸다.
    ///
    /// <b>넘긴 사전과 집합의 소유권도 함께 넘어간다.</b> 부르는 쪽이 그 뒤로 손대면
    /// 살아 있는 상태를 직접 고치는 셈이 된다. 오프셋 사전의 비교자는
    /// <see cref="ClaudeCodeUsage.PathComparer"/> 여야 한다.
    /// </summary>
    public MeterState CopyAdopting(Dictionary<string, long> offsets, HashSet<string> seenIds)
    {
        var tracks = new Dictionary<string, LimitTrack>(Tracks.Count, StringComparer.Ordinal);
        foreach (var (id, track) in Tracks) tracks[id] = track.Clone();

        // **`with` 는 안 적은 칸을 그대로 가져온다.** 그래서 여기 적을 것은 참조를
        // 나눠 쓰면 안 되는 칸뿐이고, 칸이 새로 늘어도 저절로 따라온다.
        return this with
        {
            Tracks = tracks,
            Order = [.. Order],
            TokensByModel = new Dictionary<string, TokenTally>(TokensByModel, StringComparer.Ordinal),
            Offsets = offsets,
            SeenIds = seenIds,
            // 기록은 만든 뒤 아무도 안 고치므로 목록만 새로 든다. 안쪽까지 베끼면
            // 기록 50개 × 한도 몇 개를 표본마다 다시 만들게 된다.
            History = [.. History],
        };
    }

    /// <summary>
    /// 오프셋 사전을 <see cref="ClaudeCodeUsage.PathComparer"/> 로 다시 싼다.
    ///
    /// **생성자로 한 번에 옮기지 않는다.** 손으로 고친 <c>meter.json</c> 이나 옛 판이 남긴
    /// 파일에 대소문자만 다른 두 경로가 들어 있으면 그 자리에서 던진다. 그때는 **큰 쪽을
    /// 남긴다** — 작은 쪽을 남기면 그 구간을 다시 읽어 토큰이 두 배가 된다.
    ///
    /// **오프셋 사전을 다시 싸는 자리는 여기 하나다.** <see cref="TokenScan.Run"/> 도 이걸
    /// 부른다 — 던지는 판과 안 던지는 판이 나란히 있으면 어느 쪽을 탔느냐로 답이 갈린다.
    /// </summary>
    public static Dictionary<string, long> CopiedOffsets(IReadOnlyDictionary<string, long>? source)
    {
        var copy = new Dictionary<string, long>(ClaudeCodeUsage.PathComparer);
        if (source is null) return copy;

        foreach (var (path, offset) in source)
        {
            copy[path] = Math.Max(copy.GetValueOrDefault(path), Math.Max(0, offset));
        }
        return copy;
    }
}
