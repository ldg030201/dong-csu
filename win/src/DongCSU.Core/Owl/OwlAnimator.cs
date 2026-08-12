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

        // **없는 값을 대괄호로 꺼내지 않는다.** owl.json 은 맥에서 뽑혀 오는 파일이라
        // 언젠가 열쇠 이름이 바뀔 수 있는데, 그때 조회할 때마다 예외가 나면 사용량이
        // 통째로 안 들어온다.
        if (utilization >= Threshold(document, "exhausted")) return OwlMood.Exhausted;
        if (utilization >= Threshold(document, "tired")) return OwlMood.Tired;
        return OwlMood.Idle;
    }

    /// <summary>
    /// 문턱 하나. 없으면 **닿지 않는 값**으로 본다.
    ///
    /// 여기에 숫자를 대신 적어 두지 않는다 — 맥이 기준을 낮추면 그 숫자만 옛것으로
    /// 남아서, 파일이 멀쩡한 날에는 안 보이다가 깨진 날에만 틀린 얼굴을 짓는다.
    /// 차라리 그 기분에 안 걸리는 편이 낫다.
    /// </summary>
    private static double Threshold(OwlDocument document, string name) =>
        document.MoodThresholds.TryGetValue(name, out var value) ? value : double.PositiveInfinity;
}

/// <summary>
/// 프레임을 차례로 넘긴다.
///
/// 시간을 직접 재지 않는다 — <see cref="Advance"/> 가 다음 프레임까지 기다릴 시간을
/// 돌려주고, 타이머는 부르는 쪽이 건다. 그래야 테스트가 시계 없이 돈다.
/// </summary>
public sealed class OwlAnimator(OwlDocument document, Random? random = null, TimeProvider? time = null)
{
    private readonly Random random = random ?? Random.Shared;

    /// <summary>끌리는 동안 "마우스가 멈췄나"를 재는 데만 쓴다. 나머지는 시계 없이 돈다.</summary>
    private readonly TimeProvider time = time ?? TimeProvider.System;
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
        if (wasStill)
        {
            frameIndex = 0;
            // 걷기 시작할 때는 기분이 준 눈 그대로. 첫 칸부터 깜빡이면 어색하다.
            eyes = MoodPose.Eyes;
        }
        Recompose();
        return true;
    }

    /// <summary>
    /// 지금 보여줄 애니메이션.
    ///
    /// 걷는 중이면 기분보다 걸음이 이긴다 — 걸어가면서 서 있는 자세를 하면 미끄러진다.
    /// 그림은 <c>owl.json</c> 에 이미 다 들어 있다.
    /// </summary>
    public OwlAnimation Animation => Named(CurrentName);

    /// <summary>
    /// 이름으로 애니메이션을 찾는다.
    ///
    /// **목록을 훑지 않는다.** 이 값은 한 틱에 서너 번 읽히는데(프레임 넘기기·눈 고르기·
    /// 합성·팔레트), 그때마다 <c>Single</c> 로 훑으면 delegate 까지 새로 만든다.
    /// </summary>
    private OwlAnimation Named(string name) => byName[name];

    private readonly Dictionary<string, OwlAnimation> byName =
        document.Animations.ToDictionary(a => a.Name);

    /// <summary>손에 잡혀 끌려가는 중인지. 다른 무엇보다 이게 먼저다.</summary>
    public bool IsDragged
    {
        get => dragged;
        set
        {
            if (dragged == value) return;
            dragged = value;
            frameIndex = 0;
            if (dragged)
            {
                // 잡은 순간에는 아직 속도가 없다. 가만히 매달린 자세로 시작한다.
                previousLean = 0;
                olderLean = 0;
                dragVelocityAt = DateTimeOffset.MinValue;
                carried = CarriedPose(0, 0, 0, dizzy ? OwlEyes.Dizzy : OwlEyes.Open, OwlWings.Droop);
            }
            Recompose();
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
            // `IsWalking` 은 `!dizzy` 를 포함하므로 여기 닿으면 방금 풀린 것이다.
            // 어지러운 동안의 눈은 `BlinkingEyes` 가 따로 준다.
            if (IsWalking) eyes = MoodPose.Eyes;
            // 들려 있는 동안에는 다음 틱을 기다리지 않고 바로 눈을 푼다.
            if (dragged) carried = carried with { Eyes = dizzy ? OwlEyes.Dizzy : OwlEyes.Open };
            Recompose();
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
    /// 눈을 몇 틱마다 깜빡일지. 붙박아 두기만 하면 노려보는 것처럼 보인다.
    /// 지터가 없으면 시계처럼 정확한 박자로 깜빡인다.
    /// </summary>
    private const int BlinkInterval = 22;
    private const int BlinkJitter = 12;

    private int blinkCountdown = BlinkInterval;

    /// <summary>지금 걷는 자세를 쓰고 있는지. 끌림·어지러움이 걸음보다 먼저다.</summary>
    private bool IsWalking => !dragged && !dizzy && gait is not null;

    public OwlFrame CurrentFrame
    {
        get
        {
            var frames = Animation.Frames;
            return frames[Math.Min(frameIndex, frames.Count - 1)];
        }
    }

    /// <summary>
    /// 지금 그려야 할 그림. 대개는 <c>owl.json</c> 이 실어 온 합성 결과를 그대로 쓴다.
    ///
    /// **스스로 도는 상태(걸음·끌림)만 예외다.** 그때는 자세를 여기서 합성한다 —
    /// 자세히는 <see cref="Recompose"/> 를 보라.
    /// </summary>
    public string[] CurrentGrid => made ?? CurrentFrame.Grid;

    /// <summary>합성해 둔 그림. 합성할 것이 없으면 null 이고 프레임 그대로를 쓴다.</summary>
    private string[]? made;

    /// <summary>이번 칸의 눈. 깜빡임은 <see cref="Advance"/> 에서만 새로 고른다.</summary>
    private OwlEyes eyes;

    /// <summary>
    /// 지금 상태로 그림을 다시 만든다.
    ///
    /// 두 가지를 여기서 합성한다.
    ///
    /// **걸음** — 걷기 그림을 통째로 쓰면 **지친 부엉이가 걷는 순간 말짱해진다.** 감긴
    /// 눈이 떠지고 처진 날개가 올라가서, 사용량이 줄어든 것처럼 읽힌다. 맥처럼 기분이
    /// 준 자세 위에 **발과 몸 기울임만** 얹는다.
    ///
    /// **끌림 + 어지러움** — 몸은 매달린 자세 그대로 두고 눈만 푼다. 통째로 비틀거리는
    /// 그림으로 갈아타면 허공에서 휘청이는 꼴이라 무엇이 흔들리는 건지 알 수 없다.
    /// </summary>
    private void Recompose()
    {
        if (dragged)
        {
            made = OwlComposer.Compose(document, carried);
            return;
        }

        if (IsWalking && gait is { } moving)
        {
            var baseline = MoodPose;
            made = OwlComposer.Compose(document, GaitPose(baseline, frameIndex, moving) with
            {
                Eyes = eyes,
            });
            return;
        }

        made = null;
    }

    // ── 끌려가는 동안 ────────────────────────────────────────────────

    /// <summary>끌리는 동안 자세를 다시 잡는 주기.</summary>
    public static readonly TimeSpan DragTick = TimeSpan.FromSeconds(0.09);

    /// <summary>마우스가 멈추면 이벤트가 끊긴다. 이만큼 지난 속도는 0으로 본다.</summary>
    private static readonly TimeSpan DragIdle = TimeSpan.FromSeconds(0.13);

    /// <summary>이 속도(pt/s)를 넘어야 몸이 처진다. 느리게 옮기면 그냥 매달려 있다.</summary>
    private const double DragLeanSpeed = 140;

    /// <summary>위아래로 이만큼 빠르면 날개가 한 단계 움직인다(pt/s).</summary>
    private const double WingLiftSpeed = 200;
    private const double WingSpreadSpeed = 620;

    private double dragDx;
    private double dragDy;
    private DateTimeOffset dragVelocityAt = DateTimeOffset.MinValue;

    /// <summary>한 틱 전과 두 틱 전의 몸 기울기. 얼굴과 발이 각각 여기에 남는다.</summary>
    private int previousLean;
    private int olderLean;

    private OwlPose carried = CarriedPose(0, 0, 0, OwlEyes.Open, OwlWings.Droop);

    /// <summary>
    /// 끌려가는 동안 마우스의 속도(pt/s). 부호는 **마우스가 가는 쪽**이다.
    ///
    /// 가로는 몸이 처지는 방향을 정한다 — 오른쪽으로 가면 몸이 왼쪽으로 뒤처진다.
    /// 세로는 날개 높이를 정한다 — 들어 올리면 날개를 들고, 세게 내리면 활짝 편다.
    ///
    /// **위가 양수다.** 화면 좌표는 아래로 커지므로 넣는 쪽에서 뒤집어 준다.
    /// </summary>
    public void SetDragVelocity(double dx, double dy, DateTimeOffset now)
    {
        dragDx = dx;
        dragDy = dy;
        dragVelocityAt = now;
    }

    /// <summary>
    /// 끌리는 자세 한 칸.
    ///
    /// **마우스가 멈추면 가만히 매달려 있어야 한다.** 잡고만 있어도 계속 흔들리면
    /// 무엇 때문에 움직이는 건지 알 수 없다. 이벤트가 끊긴 지 <see cref="DragIdle"/> 이
    /// 지나면 속도를 0으로 본다.
    ///
    /// 얼굴과 발은 몸보다 **한 틱·두 틱 늦게** 따라온다. 매달린 것은 손보다 늦게
    /// 움직이기 때문이다.
    /// </summary>
    private void AdvanceDrag(DateTimeOffset now)
    {
        var moving = now - dragVelocityAt < DragIdle;
        var vx = moving ? dragDx : 0;
        var vy = moving ? dragDy : 0;

        var lean = Math.Abs(vx) > DragLeanSpeed ? (vx > 0 ? -1 : 1) : 0;
        carried = CarriedPose(lean, previousLean, olderLean, BlinkingEyes(OwlEyes.Open), Wings(vy));

        olderLean = previousLean;
        previousLean = lean;
        Recompose();
    }

    /// <summary>마우스가 가는 반대쪽으로 처진다. 매달린 것은 손보다 늦게 따라오기 때문이다.</summary>
    private static OwlPose CarriedPose(int lean, int face, int feet, OwlEyes eyes, OwlWings wings) => new()
    {
        Eyes = eyes,
        Wings = wings,
        Feet = OwlFeet.Dangle,
        Lean = lean,
        FaceLean = face - lean,
        FeetLean = feet,
        Bob = 0,
    };

    private static OwlWings Wings(double vertical)
    {
        if (vertical > WingLiftSpeed) return OwlWings.Lift;
        if (vertical < -WingSpreadSpeed) return OwlWings.Spread;
        if (vertical < -WingLiftSpeed) return OwlWings.Lift;
        return OwlWings.Droop;
    }

    /// <summary>기분이 정한 기본 자세. 걸을 때 여기에 발을 얹는다.</summary>
    private OwlPose MoodPose => Named(mood.Name()).Frames[0].Pose;

    /// <summary>
    /// 걷는 자세 한 칸. 기분이 준 자세에서 <b>발·기울임·날개만</b> 바꾼다.
    ///
    /// **뛸 때는 발을 모으는 칸마다 날개를 펼친다.** 부엉이는 다리가 짧아서 발만 빨리
    /// 놀리면 종종거리는 것으로 보인다. 기울기가 0인 칸에서만 펴는 이유는, 기운 칸에서
    /// 펴면 바깥쪽 한 칸이 캔버스 밖으로 잘려 한쪽 날개만 짧아지기 때문이다.
    /// </summary>
    private static OwlPose GaitPose(OwlPose baseline, int phase, DongCSU.Core.Pet.PetGait gait)
    {
        (OwlFeet Feet, int Lean, bool Planted) step = (phase % 4) switch
        {
            0 => (OwlFeet.StepA, -1, true),
            2 => (OwlFeet.StepB, 1, true),
            _ => (OwlFeet.Stand, 0, false),
        };

        return baseline with
        {
            Feet = step.Feet,
            Lean = step.Lean,
            Wings = gait == DongCSU.Core.Pet.PetGait.Run
                ? (step.Planted ? OwlWings.Folded : OwlWings.Spread)
                : baseline.Wings,
            FaceLean = 0,
            // 주저앉은 채로 걸으면 다리가 몸에 가려져서 미끄러지는 것으로 보인다.
            Bob = 0,
        };
    }

    /// <summary>
    /// 스스로 도는 상태의 눈. 흔들려 놨으면 풀린 채로, 아니면 이따금 깜빡인다.
    ///
    /// **지친 눈을 억지로 뜨게 하지 않는다.** 걷는다고 눈이 다시 떠지면 사용량이 줄어든
    /// 것처럼 읽힌다. 이미 감고 있는 부엉이(탈진)는 반대로 이따금 실눈을 뜨는 것으로 대신한다.
    /// </summary>
    private OwlEyes BlinkingEyes(OwlEyes baseline)
    {
        if (dizzy) return OwlEyes.Dizzy;

        blinkCountdown--;
        var next = blinkCountdown switch
        {
            1 => baseline == OwlEyes.Closed ? OwlEyes.Half : OwlEyes.Closed,
            0 or 2 => OwlEyes.Half,
            _ => baseline,
        };

        if (blinkCountdown <= 0) blinkCountdown = BlinkInterval + random.Next(BlinkJitter + 1);
        return next;
    }

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
        // 걷는 중에 지쳐도 눈은 바로 따라간다. 걸음이 끝나야 지친 얼굴이 되면 늦다.
        if (IsWalking) eyes = MoodPose.Eyes;
        Recompose();
        return true;
    }

    /// <summary>
    /// 다음 프레임으로 넘기고, 그 프레임을 얼마나 보여줄지 돌려준다.
    ///
    /// <c>null</c> 이면 **더 넘길 것이 없다** — 프레임이 하나뿐인 기분(끊김)이다.
    /// 그때는 타이머를 걸지 않는다. 0초 타이머를 걸면 쉬지 않고 도는 루프가 된다.
    /// </summary>
    public TimeSpan? Advance() => Advance(time.GetUtcNow());

    /// <summary>시각을 받는 판. 테스트가 시계 없이 돈다.</summary>
    public TimeSpan? Advance(DateTimeOffset now)
    {
        // 끌리는 동안에는 프레임을 넘기지 않고 **속도로 자세를 만든다.**
        if (dragged)
        {
            AdvanceDrag(now);
            return DragTick;
        }

        var frames = Animation.Frames;
        if (frames.Count <= 1) return null;

        // **다리 주기는 네 칸이다.** 걷기 그림이 여덟 칸인 것은 뒤 네 개에 눈 깜빡임이
        // 얹혀 있어서인데, 우리는 눈을 따로 돌리므로 앞 네 칸만 쓴다. 여덟 칸을 통째로
        // 돌리면 한 걸음마다(1.1초) 깜빡여서 경련하는 것처럼 보인다.
        if (IsWalking)
        {
            frameIndex = (frameIndex + 1) % 4;
            eyes = BlinkingEyes(MoodPose.Eyes);
            Recompose();
            return DelayFor(frames[0]);
        }

        frameIndex = (frameIndex + 1) % frames.Count;
        Recompose();
        return DelayFor(frames[frameIndex]);
    }

    /// <summary>지금 프레임을 얼마나 더 보여줄지. 타이머를 처음 걸 때 쓴다.</summary>
    public TimeSpan? CurrentDelay()
    {
        if (dragged) return DragTick;

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
