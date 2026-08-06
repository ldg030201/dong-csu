namespace DongCSU.Core.Owl;

/// <summary>
/// 부엉이 기분. <c>owl.json</c> 의 애니메이션 이름과 1:1 이다.
///
/// 끌림·어지러움·걷기·달리기는 **펫 모드 전용**이라 윈도우 첫 배포에는 쓰지 않는다.
/// 데이터에는 들어 있으니, 펫 모드를 만들 때 그림은 그대로 쓰면 된다.
/// </summary>
public enum OwlMood
{
    Idle,
    Tired,
    Exhausted,
    Offline,
}

public static class OwlMoodResolver
{
    /// <summary><c>owl.json</c> 의 애니메이션 이름.</summary>
    public static string Name(this OwlMood mood) => mood switch
    {
        OwlMood.Idle => "idle",
        OwlMood.Tired => "tired",
        OwlMood.Exhausted => "exhausted",
        OwlMood.Offline => "offline",
        _ => "idle",
    };

    /// <summary>
    /// 사용률과 연결 상태로 기분을 정한다.
    ///
    /// **끊김이 지침보다 세다.** 숫자가 지금 값이 아닌데 그 숫자로 지친 표정을 지으면,
    /// 옛 값을 보고 현재 상태인 것처럼 오해하게 된다.
    /// </summary>
    public static OwlMood Resolve(OwlDocument document, double? sessionUtilization, bool isDisconnected)
    {
        if (isDisconnected) return OwlMood.Offline;
        if (sessionUtilization is not { } utilization) return OwlMood.Idle;

        if (utilization >= document.MoodThresholds["exhausted"]) return OwlMood.Exhausted;
        if (utilization >= document.MoodThresholds["tired"]) return OwlMood.Tired;
        return OwlMood.Idle;
    }
}

/// <summary>
/// 프레임을 차례로 넘긴다.
///
/// 시간을 직접 재지 않는다 — <see cref="Advance"/> 가 다음 프레임까지 기다릴 시간을
/// 돌려주고, 타이머는 부르는 쪽이 건다. 그래야 테스트가 시계 없이 돈다.
/// </summary>
public sealed class OwlAnimator(OwlDocument document, Random? random = null)
{
    private readonly Random random = random ?? Random.Shared;
    private OwlMood mood = OwlMood.Idle;
    private int frameIndex;

    public OwlMood Mood => mood;

    public OwlAnimation Animation => document.Animations.Single(a => a.Name == mood.Name());

    public OwlFrame CurrentFrame => Animation.Frames[Math.Min(frameIndex, Animation.Frames.Count - 1)];

    /// <summary>지금 그려야 할 그림. <c>owl.json</c> 이 실어 온 합성 결과를 그대로 쓴다.</summary>
    public string[] CurrentGrid => CurrentFrame.Grid;

    public IReadOnlyDictionary<string, string> CurrentPalette => document.Palettes[Animation.Palette];

    /// <summary>기분이 바뀌면 처음 프레임부터 다시 시작한다. 바뀌었으면 true.</summary>
    public bool SetMood(OwlMood next)
    {
        if (mood == next) return false;
        mood = next;
        frameIndex = 0;
        return true;
    }

    /// <summary>
    /// 다음 프레임으로 넘기고, 그 프레임을 얼마나 보여줄지 돌려준다.
    ///
    /// <c>null</c> 이면 **더 넘길 것이 없다** — 프레임이 하나뿐인 기분(끊김)이다.
    /// 그때는 타이머를 걸지 않는다. 0초 타이머를 걸면 쉬지 않고 도는 루프가 된다.
    /// </summary>
    public TimeSpan? Advance()
    {
        var frames = Animation.Frames;
        if (frames.Count <= 1) return null;

        frameIndex = (frameIndex + 1) % frames.Count;
        return DelayFor(frames[frameIndex]);
    }

    /// <summary>지금 프레임을 얼마나 더 보여줄지. 타이머를 처음 걸 때 쓴다.</summary>
    public TimeSpan? CurrentDelay()
    {
        var frames = Animation.Frames;
        return frames.Count <= 1 ? null : DelayFor(CurrentFrame);
    }

    private TimeSpan DelayFor(OwlFrame frame)
    {
        // 같은 자세가 기계처럼 반복되지 않게 흔든다. 눈 깜빡임이 특히 티가 난다.
        var jitter = frame.Jitter > 0 ? random.NextDouble() * frame.Jitter : 0;
        return TimeSpan.FromSeconds(frame.Duration + jitter);
    }
}
