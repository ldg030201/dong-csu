namespace DongCSU.Core.Pet;

/// <summary>
/// 마구 흔들면 어지러워한다.
///
/// 흔들린 정도를 **점수로 쌓는다.** 방향이 홱 바뀔 때 크게 오르고, 아주 빠르게 끌면
/// 조금씩 오른다. 가만히 두면 내려간다. 문턱을 넘으면 한동안 어지러워한다.
///
/// 천천히 옮기는 것만으로는 절대 안 쌓이게 하는 것이 요령이다 — 창을 자리 잡으려고
/// 조금씩 미는 동안 어지러워지면 성가시다.
///
/// 수치는 맥판(<c>OwlAnimator.swift</c>)과 같다.
/// </summary>
public sealed class PetShake(TimeProvider? time = null)
{
    /// <summary>방향이 뒤집힐 때 한 번에 오르는 값. 셋을 채우면 문턱을 넘는다.</summary>
    private const double ReversalGain = 1.1;

    /// <summary>방향 뒤집힘으로 치려면 이만큼은 빨라야 한다. 손 떨림은 세지 않는다.</summary>
    private const double ReversalSpeed = 320;

    /// <summary>뒤집지 않아도 이보다 빠르면 조금씩 쌓인다.</summary>
    private const double SpinSpeed = 950;

    /// <summary>
    /// **시간당으로 센다.** 맥은 마우스 이벤트가 올 때마다 0.07 씩 더하고 0.06 씩 빼는데,
    /// 그러면 **이벤트가 얼마나 자주 오느냐에 따라 결과가 달라진다.**
    ///
    /// 윈도우의 <c>LocationChanged</c> 는 맥의 <c>mouseDragged</c> 보다 훨씬 자주 온다.
    /// 그대로 옮겼더니 빼는 쪽만 몇 배로 늘어나서, 아무리 흔들어도 점수가 안 쌓였다.
    /// 맥의 60Hz 기준으로 환산해 초당으로 바꿨다 — 이제 이벤트 주기와 무관하다.
    /// </summary>
    private const double SpinGainPerSecond = 0.07 * 60;
    private const double DecayPerSecond = 0.06 * 60;

    private const double Threshold = 3.0;

    /// <summary>문턱을 넘고 나서 어지러워하는 시간.</summary>
    public static readonly TimeSpan DizzyDuration = TimeSpan.FromSeconds(2.4);

    private readonly TimeProvider time = time ?? TimeProvider.System;

    private double score;
    private int lastHorizontal;
    private int lastVertical;
    private DateTimeOffset dizzyUntil = DateTimeOffset.MinValue;
    private PetPoint? previous;
    private DateTimeOffset previousAt;

    public bool IsDizzy => dizzyUntil > time.GetUtcNow();

    /// <summary>마지막으로 잰 속도(pt/s). 끌려가는 자세를 정할 때도 이 값을 쓴다.</summary>
    public PetPoint Velocity { get; private set; }

    /// <summary>
    /// 끌기 시작. **점수를 새로 센다** — 사이를 두고 조금씩 흔든 것이 쌓여서
    /// 엉뚱한 때에 어지러워지면 안 된다.
    /// </summary>
    public void Begin()
    {
        score = 0;
        lastHorizontal = 0;
        lastVertical = 0;
        previous = null;
        Velocity = default;
    }

    /// <summary>
    /// 지금 자리를 넣는다. 이번에 어지러워졌으면 true.
    ///
    /// 방향 뒤집힘은 가로·세로를 따로 보고, 빠르기는 둘을 합쳐 본다 —
    /// **위아래로만 마구 흔들어도 어지러워져야 한다.**
    /// </summary>
    public bool Sample(PetPoint position)
    {
        var now = time.GetUtcNow();

        if (previous is not { } before)
        {
            previous = position;
            previousAt = now;
            return false;
        }

        var seconds = (now - previousAt).TotalSeconds;
        previous = position;
        previousAt = now;
        if (seconds <= 0) return false;

        var dx = (position.X - before.X) / seconds;
        var dy = (position.Y - before.Y) / seconds;
        Velocity = new PetPoint(dx, dy);

        // 아주 긴 간격(다른 창에 갔다 온 뒤 등)에 점수가 통째로 날아가지 않게 막는다.
        score = Math.Max(0, score - DecayPerSecond * Math.Min(seconds, 0.25));

        var horizontal = Math.Abs(dx) > ReversalSpeed ? Math.Sign(dx) : 0;
        if (horizontal != 0)
        {
            if (lastHorizontal != 0 && horizontal != lastHorizontal) score += ReversalGain;
            lastHorizontal = horizontal;
        }

        var vertical = Math.Abs(dy) > ReversalSpeed ? Math.Sign(dy) : 0;
        if (vertical != 0)
        {
            if (lastVertical != 0 && vertical != lastVertical) score += ReversalGain;
            lastVertical = vertical;
        }

        if (Math.Sqrt(dx * dx + dy * dy) > SpinSpeed) score += SpinGainPerSecond * Math.Min(seconds, 0.25);

        if (score < Threshold) return false;

        score = 0;
        lastHorizontal = 0;
        lastVertical = 0;
        dizzyUntil = now + DizzyDuration;
        return true;
    }
}
