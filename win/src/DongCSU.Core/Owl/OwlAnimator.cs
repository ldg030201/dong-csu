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
    ///
    /// 지쳐 가는 정도는 세션(5시간)으로 본다 — 주간은 며칠에 걸쳐 천천히 차서,
    /// 그걸로 지치면 한 주 내내 지친 얼굴로 있게 된다.
    ///
    /// **다만 주간을 다 쓴 것은 다르다.** 그때는 세션이 얼마 남았든 쓸 수 없으므로,
    /// 세션 숫자를 보지 않고 곧바로 탈진이다. "천천히 지쳐 간다"가 아니라 "끝났다"다.
    /// </summary>
    public static OwlMood Resolve(
        OwlDocument document,
        double? sessionUtilization,
        bool isDisconnected,
        bool isWeeklySpent = false)
    {
        if (isDisconnected) return OwlMood.Offline;
        if (isWeeklySpent) return OwlMood.Exhausted;
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
    private DongCSU.Core.Pet.PetGait? gait;
    private int frameIndex;

    public OwlMood Mood => mood;

    /// <summary>
    /// 걷는 자세로 바꾼다. null 이면 기분에 따른 자세로 돌아간다.
    ///
    /// **서 있다 → 걷는다 로 바뀔 때만 처음 프레임으로 되돌린다.** 걷기 → 달리기는
    /// 그대로 이어간다 — 다리 위치는 같고 박자만 빨라지는 것이라, 되감으면 발이 튄다.
    /// </summary>
    public bool SetGait(DongCSU.Core.Pet.PetGait? next)
    {
        if (gait == next) return false;

        var wasStill = gait is null;
        gait = next;
        if (wasStill) frameIndex = 0;
        return true;
    }

    /// <summary>
    /// 지금 보여줄 애니메이션.
    ///
    /// 걷는 중이면 기분보다 걸음이 이긴다 — 걸어가면서 서 있는 자세를 하면 미끄러진다.
    /// 그림은 <c>owl.json</c> 에 이미 다 들어 있다.
    /// </summary>
    public OwlAnimation Animation => document.Animations.Single(a => a.Name == CurrentName);

    /// <summary>손에 잡혀 끌려가는 중인지. 다른 무엇보다 이게 먼저다.</summary>
    public bool IsDragged
    {
        get => dragged;
        set
        {
            if (dragged == value) return;
            dragged = value;
            frameIndex = 0;
        }
    }

    /// <summary>
    /// 마구 흔들린 직후인지. 눈이 풀리고 비틀거린다.
    ///
    /// **손에 들려 있는 동안에는 되감지 않는다** — 그때는 매달린 자세 그대로 두고 눈만
    /// 바뀌므로, 되감으면 흔드는 도중에 몸이 툭 튄다.
    /// </summary>
    public bool IsDizzy
    {
        get => dizzy;
        set
        {
            if (dizzy == value) return;
            dizzy = value;
            if (!dragged) frameIndex = 0;
        }
    }

    private bool dragged;
    private bool dizzy;

    /// <summary>
    /// 지금 보여줄 그림 이름.
    ///
    /// 차례가 정해져 있다 — **끌림 &gt; 어지러움 &gt; 걸음 &gt; 기분.** 손에 들려 있는데
    /// 걷는 자세를 하면 허공에서 발을 놀리는 꼴이고, 어지러운데 기분 자세를 하면
    /// 흔든 보람이 없다.
    /// </summary>
    private string CurrentName
    {
        get
        {
            if (dragged) return "dragged";
            if (dizzy) return "dizzy";
            return gait switch
            {
                DongCSU.Core.Pet.PetGait.Walk => "walk",
                DongCSU.Core.Pet.PetGait.Run => "run",
                _ => mood.Name(),
            };
        }
    }

    /// <summary>
    /// 걷기·달리기 그림은 여덟 칸이지만 **다리 주기는 앞 네 칸**이다. 뒤 네 칸은 같은
    /// 다리에 눈 깜빡임이 얹힌 것이고, 그중 실제로 눈이 감긴 것은 <c>여섯 번째 하나뿐</c>이다.
    ///
    /// 여덟 칸을 통째로 돌리면 **한 걸음마다(1.1초) 깜빡인다.** 맥은 다리와 눈을 따로
    /// 돌려서 22~34틱(3~5초)에 한 번 깜빡인다. 여기서도 그렇게 센다.
    /// </summary>
    private const int GaitLegFrames = 4;
    private const int GaitBlinkFrame = GaitLegFrames + 2;
    private const int GaitBlinkLeg = GaitBlinkFrame - GaitLegFrames;
    private const int BlinkInterval = 22;
    private const int BlinkJitter = 12;

    private int blinkCountdown = BlinkInterval;
    private bool blinkQueued;

    /// <summary>지금 걷는 그림을 쓰고 있는지. 끌림·어지러움이 걸음보다 먼저다.</summary>
    private bool IsWalking => !dragged && !dizzy && gait is not null;

    public OwlFrame CurrentFrame
    {
        get
        {
            var frames = Animation.Frames;
            if (!IsWalking || frames.Count <= GaitBlinkFrame)
            {
                return frames[Math.Min(frameIndex, frames.Count - 1)];
            }

            var leg = frameIndex % GaitLegFrames;
            return frames[blinkQueued && leg == GaitBlinkLeg ? GaitBlinkFrame : leg];
        }
    }

    /// <summary>
    /// 지금 그려야 할 그림. <c>owl.json</c> 이 실어 온 합성 결과를 그대로 쓴다.
    ///
    /// **한 가지만 예외다** — 손에 들린 채로 어지러우면 매달린 자세에 풀린 눈만 얹는다.
    /// </summary>
    public string[] CurrentGrid
    {
        get
        {
            var frame = CurrentFrame;
            if (!dragged || !dizzy) return frame.Grid;

            // 통째로 dizzy 그림으로 갈아타면 **허공에서 비틀거리는 꼴**이라 무엇이
            // 흔들리는 건지 알 수 없다. 맥도 몸은 carried 그대로 두고 눈만 바꾼다.
            if (dizzyDrag.TryGetValue(frame, out var made)) return made;

            made = OwlComposer.Compose(document, frame.Pose with { Eyes = OwlEyes.Dizzy });
            dizzyDrag[frame] = made;
            return made;
        }
    }

    /// <summary>끌린 자세 + 풀린 눈. 프레임마다 한 번만 합성하고 재사용한다.</summary>
    private readonly Dictionary<OwlFrame, string[]> dizzyDrag = [];

    /// <summary>
    /// 다 써서 쓸 수 없는 상태. 켜면 **자세는 그대로 두고 색만 뺀다.**
    ///
    /// 기분을 따로 만들지 않는 이유: <c>owl.json</c> 에 애니메이션이 하나 더 생긴다.
    /// 자세는 탈진과 똑같고 색만 다른 것이라 여기서 팔레트만 바꾸는 편이 싸다.
    /// </summary>
    public bool IsUnusable { get; set; }

    /// <summary>지금 칠할 팔레트 이름. 다 썼으면 색이 빠진다.</summary>
    public string PaletteName =>
        // 끊김의 회색은 색 자체가 정보라 덮어쓰지 않는다 — 이미 회색이다.
        IsUnusable && mood != OwlMood.Offline ? "offline" : Animation.Palette;

    public IReadOnlyDictionary<string, string> CurrentPalette => document.Palettes[PaletteName];

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

        if (IsWalking && frames.Count > GaitBlinkFrame)
        {
            // 깜빡임을 보여준 칸을 지나가면 내려놓는다.
            if (blinkQueued && frameIndex % GaitLegFrames == GaitBlinkLeg) blinkQueued = false;

            frameIndex = (frameIndex + 1) % GaitLegFrames;

            // 깜빡일 차례가 되면 예약해 두고, 눈 감은 그림이 있는 다리 자세에서 쓴다.
            if (!blinkQueued && --blinkCountdown <= 0)
            {
                blinkQueued = true;
                blinkCountdown = BlinkInterval + random.Next(BlinkJitter + 1);
            }

            return DelayFor(CurrentFrame);
        }

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
