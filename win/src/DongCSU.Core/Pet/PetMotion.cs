namespace DongCSU.Core.Pet;

/// <summary>화면 좌표 한 점. <c>System.Windows</c> 를 쓰지 않으려고 직접 둔다.</summary>
public readonly record struct PetPoint(double X, double Y)
{
    public double DistanceTo(PetPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public readonly record struct PetRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PetPoint Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(PetPoint point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>겹치는 데가 조금이라도 있는지. 창에 붙을 때 가림을 본다.</summary>
    public bool Intersects(PetRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    /// <summary>저 사각형이 <b>통째로</b> 이 안에 들어가는지. 겹치기만 하면 거짓이다.</summary>
    public bool ContainsRect(PetRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>사방으로 <paramref name="amount"/> 만큼 부풀린 것. 음수면 줄어든다.</summary>
    public PetRect Inflate(double amount) =>
        new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);
}

/// <summary>걸음걸이. <c>owl.json</c> 의 애니메이션 이름과 1:1 이다.</summary>
public enum PetGait { Walk, Run }

/// <summary>
/// 펫이 지금 무엇을 보고 있는지.
///
/// **읽기만 한다.** 창을 옮기는 것은 부르는 쪽이고, 여기는 값만 넘겨준다 — 그래야
/// 테스트가 진짜 화면 없이 돈다.
/// </summary>
public interface IPetStage
{
    /// <summary>창의 지금 자리와 크기.</summary>
    PetRect Window { get; }

    /// <summary>창이 놓인 모니터의 작업 영역. 못 알아내면 null — 그때는 움직이지 않는다.</summary>
    PetRect? WorkArea { get; }

    /// <summary>
    /// 붙어 있는 <b>모든</b> 화면의 작업 영역. <see cref="WorkArea"/> 도 이 안에 있다.
    ///
    /// 다른 화면으로 넘어가기를 켰을 때만 본다 — 꺼 두면 아예 묻지 않으므로, 이걸
    /// 만드는 값이 비싸도 대부분의 사람에게는 공짜다.
    ///
    /// **모두 <see cref="WorkArea"/> 와 같은 계수로 환산해야 한다.** 화면마다 제
    /// 배율로 나누면 붙어 있는 화면이 겹치거나 벌어져서 이음매가 사라진다.
    /// </summary>
    IReadOnlyList<PetRect> WorkAreas { get; }

    /// <summary>
    /// 지금 무대의 눈금 — 물리 픽셀 하나가 몇 단위인지(WPF 의 <c>TransformFromDevice.M11</c>).
    ///
    /// **배율이 다른 모니터로 넘어가면 값이 바뀌고, 그 순간 좌표계 전체가 다시 늘어난다.**
    /// 그때 들고 있던 목적지를 같은 비율로 옮겨 주려고 받는다. 화면 하나짜리에서는
    /// 영원히 안 바뀌므로 아무 일도 하지 않는다.
    /// </summary>
    double Scale { get; }

    PetPoint Cursor { get; }

    /// <summary>마지막 키 입력 이후 지난 시간. 글을 쓰는 동안에는 가만히 있는다.</summary>
    TimeSpan SinceLastKey { get; }
}

/// <summary>한 틱의 결과. 부르는 쪽이 이대로 창을 옮기고 타이머를 건다.</summary>
/// <param name="FacingRight">
/// 바라보는 쪽. **null 이면 보던 쪽 그대로다** — 멈출 때와 세로로만 걸을 때가 그렇다.
/// 그림 마스코트가 좌우를 뒤집는 데 쓰고, 격자 부엉이는 정면 대칭이라 아무 일도 안 한다.
/// </param>
public readonly record struct PetTick(
    TimeSpan? NextWakeup,
    PetPoint? MoveTo,
    PetGait? Gait,
    bool Settled,
    bool? FacingRight = null);

/// <summary>
/// 붙어 있는 동안의 한 틱. 부르는 쪽이 이대로 창을 옮기고 층을 맞추고 타이머를 건다.
/// </summary>
/// <param name="MoveTo">창이 놓일 새 원점. 안 움직였으면 그 자리 값이 그대로 온다.</param>
/// <param name="Front">
/// 붙은 창이 맨 앞이고 묻히지도 않았다. 그러면 펫도 같이 올린다 — <b>묻혔으면 올리지
/// 않는다.</b> 올리면 그 창을 덮은 창 위에 펫만 떠서 아무것도 없는 자리에 매달린
/// 것으로 보인다.
/// </param>
/// <param name="Dropped">떨어졌다. 부르는 쪽이 자세와 층을 되돌린다.</param>
public readonly record struct PerchTick(
    PetPoint? MoveTo,
    bool Front,
    bool Dropped,
    TimeSpan NextWakeup);

/// <summary>
/// 펫이 스스로 움직이는 규칙.
///
/// **타이머를 갖지 않는다.** <see cref="Tick"/> 이 "다음에 언제 깨워 달라"를 돌려주고
/// 타이머는 부르는 쪽이 건다 — <c>OwlAnimator</c> 와 같은 관례이고, 그래야 테스트가
/// 시계 없이 돈다.
///
/// 수치는 맥판(<c>PetMotion.swift</c>)과 같다. **y 부호만 뒤집혀 있다** — 윈도우는
/// y 가 아래로 커진다.
/// </summary>
public sealed class PetMotion(TimeProvider? time = null, Random? random = null)
{
    /// <summary>한 걸음 간격. 걷기 한 칸(0.14초)보다 짧아야 움직임이 끊겨 보이지 않는다.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(0.1);

    // 논리단위/초. 한 틱(0.1초)에 이만큼의 1/10 을 간다.
    private const double WalkSpeed = 26;
    private const double DodgeSpeed = 210;
    private const double RunSpeed = 300;

    /// <summary>화면 가장자리에서 이만큼 띄우고 선다.</summary>
    private const double EdgeMargin = 8;

    /// <summary>이보다 가까운 목적지는 고르지 않는다. 찔끔거려 보인다.</summary>
    private const double MinimumTravel = 24;

    /// <summary>글을 쓰는 동안에는 움직이지 않는다. 문장 사이 생각하는 틈까지 덮는 값이다.</summary>
    public static readonly TimeSpan TypingQuiet = TimeSpan.FromSeconds(5);

    /// <summary>이 안에 두 번째로 비키면 걷지 않고 뛴다.</summary>
    private static readonly TimeSpan ChaseWindow = TimeSpan.FromSeconds(4);

    /// <summary>
    /// 붙어 있는 동안 붙은 창을 얼마나 자주 다시 보나.
    ///
    /// **창을 끌지 않을 때도 반드시 본다.** 단축키로 창을 옮기는 도구나 스냅(Win+←) ·
    /// 가상 데스크톱 전환은 마우스 이벤트를 하나도 안 내서, 끄는 중인지만 보고 폴링을
    /// 끄면 그때 통째로 놓친다.
    /// </summary>
    public static readonly TimeSpan PerchTick = TimeSpan.FromSeconds(0.25);

    /// <summary>
    /// 창을 끄는 중일 때. 20Hz 면 50ms 뒤처지는데, 창을 빠르게 끌어도 그 정도는
    /// 붙어 있는 것으로 읽힌다. 목록 한 번이 1ms 도 안 돼서 부담이 아니다.
    /// </summary>
    public static readonly TimeSpan PerchChaseTick = TimeSpan.FromSeconds(0.05);

    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly Random random = random ?? Random.Shared;

    private enum State { Still, Resting, Walking, Dodging, Perched }

    private State state = State.Still;
    private PerchSpot perched;
    private DateTimeOffset restUntil;
    private PetPoint target;
    private bool hurried;
    private DateTimeOffset? lastDodgeAt;
    private bool started;

    /// <summary>
    /// 이번 부름에 본 화면 목록. **한 번만 묻는다** — 한 틱 안에서 설 자리를 여러 번
    /// 재는데(목적지 두 번, 물러날 자리 네 번) 그때마다 모니터를 다시 세면 같은 답을
    /// 여섯 번 산다.
    /// </summary>
    private IReadOnlyList<PetRect> screens = [];

    /// <summary>지난번에 본 눈금. 0 이면 아직 모른다.</summary>
    private double lastScale;

    /// <summary>혼자 돌아다닐지. 꺼도 커서 피하기는 따로 돈다.</summary>
    public bool Wanders
    {
        get => wanders;
        set
        {
            if (wanders == value) return;
            wanders = value;
            HaltIfCannotWander();
        }
    }

    private bool wanders = true;

    /// <summary>
    /// 탈진했는지(세션 90%).
    ///
    /// **배회만 끊는다.** 지쳐서 제 발로 산책 나갈 기운은 없어도, 커서가 밀고 들어오면
    /// 비켜야 한다 — 안 비키면 지친 게 아니라 멎은 것으로 보이고 화면도 가린다.
    /// 그래서 <see cref="RequestDodge"/> 에는 이 값이 들어가지 않는다.
    ///
    /// 넣어 주는 쪽은 **기분을 그대로 쓴다**(<c>Program.SyncMotion</c>). 거기서 사용률을
    /// 다시 견주면 마스코트는 주저앉았는데 산책은 계속 나가는 어긋남이 생긴다.
    /// </summary>
    public bool IsDrained
    {
        get => isDrained;
        set
        {
            if (isDrained == value) return;
            isDrained = value;
            HaltIfCannotWander();
        }
    }

    private bool isDrained;

    /// <summary>
    /// 흔들려서 눈이 풀렸는지.
    ///
    /// **탈진과 달리 전부 멈춘다.** 비틀거리면서 산책을 나가면 어지러운 것이 아니라
    /// 그냥 걷는 것으로 보인다 — 자리에 서서 비틀거려야 흔들린 결과로 읽힌다.
    /// 2.4초짜리라(<c>PetShake.DizzyDuration</c>) 그동안 안 비켜도 화면을 오래 가리지 않는다.
    ///
    /// **커서 피하기까지 여기서 끊는다.** 맥은 이걸 부르는 쪽에서 걸렀다 — 거기서는
    /// 회피를 그냥 막으면 예약이 사라져서, 어지러움이 풀린 뒤 커서가 그대로 있어도
    /// 영영 안 비킨다. 윈도우는 여기서 막아도 안전하다: 커서를 지켜보는
    /// <c>Program.OnDodgeTick</c> 이 <see cref="RequestDodge"/> 의 성패와 무관하게
    /// 언제나 <c>hover.Restart</c> 로 다시 세기 시작하므로, 풀린 뒤 커서가 그 자리면
    /// 0.5초 뒤에 다시 시도한다. **이걸 부르는 쪽으로 옮기지 마라** — 그 예약이 여기
    /// 가드를 대신하고 있다.
    /// </summary>
    public bool IsDizzy
    {
        get => isDizzy;
        set
        {
            if (isDizzy == value) return;
            isDizzy = value;
            HaltIfCannotWander();
        }
    }

    private bool isDizzy;

    public bool DodgesCursor { get; set; } = true;

    /// <summary>
    /// 다른 화면으로 걸어 넘어갈지. **기본은 꺼짐이다.**
    ///
    /// 켜 두면 마스코트가 옆 화면으로 사라져서, 보려고 켜 둔 사람이 어디 갔는지 찾게
    /// 된다. 화면이 하나뿐인 사람에게는 아무 일도 하지 않는다.
    ///
    /// **스스로 걸어가는 것은 다 넘어간다** — 배회도, 커서를 피해 물러나는 것도.
    /// 몰아붙이면 옆 모니터로 달아나는 것이 이 설정을 켠 사람이 기대하는 모습이다.
    /// </summary>
    public bool CrossesScreens { get; set; }

    /// <summary>끌어다 놓으면 다른 앱 창 테두리에 붙일지.</summary>
    public bool Perches { get; set; } = true;

    /// <summary>
    /// 지금 <b>붙어 있어도 되는</b> 상황인지.
    ///
    /// <b>"움직여도 되는지" 와 갈라 둔다.</b> 부르는 쪽의 그 판단에는 눌림·메뉴가 들어
    /// 있는데 그건 <b>잠깐 멈추는 이유</b>일 뿐 떨어질 이유가 아니다. 하나로 묶어 두면
    /// 붙여 놓은 것을 한 번 누르거나 우클릭 메뉴를 열기만 해도 자세만 매달린 채 배회가
    /// 시작된다 — 맥에서 실제로 그랬다.
    /// </summary>
    public bool CanStayPerched { get; set; }

    /// <summary>지금 붙어 있는 자리. 안 붙어 있으면 null.</summary>
    public PerchSpot? PerchedSpot => state == State.Perched ? perched : null;

    /// <summary>지금 혼자 걸어다녀도 되는지. 배회를 끊는 세 가지가 한 식에 모여 있다.</summary>
    private bool CanWander => wanders && !IsDrained && !IsDizzy;

    /// <summary>
    /// 배회를 끊는 스위치가 하나라도 걸리면 **걷던 것도 그 자리에 멈춘다.**
    /// 목적지까지 마저 가면 방금 끈 설정이 안 먹은 것처럼 보인다.
    ///
    /// 세 스위치가 같은 규칙을 저마다 적으면 반드시 어긋나므로 한 곳에 둔다.
    /// </summary>
    private void HaltIfCannotWander()
    {
        if (!CanWander && state == State.Walking) Halt();
    }

    /// <summary>
    /// 지금 움직이던 것을 멈추고 그 자리에 선다.
    ///
    /// 펫 모드에서 나가거나 혼자 돌아다니기를 끌 때 부른다. 부르고 나면
    /// <see cref="Gait"/> 가 null 이 되므로 **자세도 같이 되돌려야 한다** —
    /// 안 그러면 카드 안에서 부엉이가 영영 걷는다.
    /// </summary>
    public void Halt()
    {
        // **여기까지 왔으면 뗀다.** 붙은 것을 지킬지는 부르는 쪽이
        // <see cref="CanStayPerched"/> 로 이미 판단했고, 그걸 통과해 여기 온 것은
        // 정말 멈춰야 하는 것이다.
        if (state is State.Walking or State.Dodging or State.Perched)
        {
            state = State.Resting;
            restUntil = time.GetUtcNow() + RestSpan();
        }
        hurried = false;
    }

    /// <summary>지금 걸음걸이. 서 있으면 null.</summary>
    public PetGait? Gait => state switch
    {
        State.Walking => PetGait.Walk,
        State.Dodging => hurried ? PetGait.Run : PetGait.Walk,
        // 붙어 있으면 걸음이 없다. 자세는 붙은 면이 정한다(`MascotPerch.Sprite`).
        _ => null,
    };

    /// <summary>처음부터 다시. 펫 모드에 들어올 때 부른다.</summary>
    public void Reset()
    {
        state = State.Still;
        started = false;
        hurried = false;
        lastDodgeAt = null;
        screens = [];
        lastScale = 0;
        perched = default;
    }

    /// <summary>
    /// 이번 부름에 쓸 무대 값을 한 번에 받아 둔다. <see cref="Tick"/> 과
    /// <see cref="RequestDodge"/> 맨 앞에서 부른다.
    /// </summary>
    private void Observe(IPetStage stage)
    {
        // **꺼져 있으면 아예 묻지 않는다.** 모니터를 세는 값이 여기 들어 있다.
        screens = CrossesScreens ? stage.WorkAreas : [];
        Rescale(stage.Scale);
    }

    /// <summary>
    /// 배율이 다른 화면으로 넘어가면 좌표계 전체가 다시 늘어난다. 들고 있던 목적지도
    /// 같은 비율로 옮겨 준다 — 안 그러면 넘어간 그 순간부터 엉뚱한 자리를 향해 걷는다.
    ///
    /// **더하기가 아니라 곱하기다.** 무대 좌표는 물리 픽셀에 눈금 하나를 곱한 것이라
    /// 원점이 같다. 비율만 곱하면 정확히 같은 물리 지점을 가리킨다.
    /// </summary>
    private void Rescale(double scale)
    {
        if (scale <= 0) return;
        if (lastScale > 0 && Math.Abs(scale - lastScale) > 1e-9
            && state is State.Walking or State.Dodging)
        {
            var ratio = scale / lastScale;
            target = new PetPoint(target.X * ratio, target.Y * ratio);
        }
        lastScale = scale;
    }

    /// <summary>
    /// 커서를 피해 비켜서라고 시킨다. 이미 비키는 중이면 무시한다.
    ///
    /// 되돌려주는 값이 true 면 다음 틱을 걸어야 한다.
    /// </summary>
    public bool RequestDodge(IPetStage stage)
    {
        Observe(stage);
        if (!DodgesCursor || state == State.Dodging) return false;
        // **붙어 있으면 안 비킨다.** 붙는 자리는 창 바깥이라 글을 가리지 않는데,
        // 제목 표시줄에 손이 갈 때마다 떨어지면 붙여 둔 뜻이 없어진다.
        if (state == State.Perched) return false;
        // 어지러운 동안에는 비키지도 않는다. 비틀거리는 정지 그림 그대로 옆으로
        // 미끄러지면 흔들린 것이 아니라 그림이 깨진 것으로 보인다.
        if (IsDizzy) return false;
        if (stage.SinceLastKey < TypingQuiet) return false;
        if (Area(stage) is not { } area) return false;

        var now = time.GetUtcNow();
        // 4초 안에 또 쫓기면 걷지 말고 뛴다.
        hurried = lastDodgeAt is { } last && now - last < ChaseWindow;
        lastDodgeAt = now;

        var away = RetreatTarget(stage, area);
        if (away is not { } destination) return false;

        target = destination;
        state = State.Dodging;
        return true;
    }

    /// <summary>한 틱. 무엇을 할지 돌려준다.</summary>
    public PetTick Tick(IPetStage stage)
    {
        Observe(stage);
        var now = time.GetUtcNow();

        // **붙어 있으면 자리를 지킨다.** 따라가는 것은 `Follow` 가 따로 하고, 여기서는
        // 배회로 넘어가지 않게 막기만 한다 — 안 막으면 붙여 놓은 것이 걸어나간다.
        if (state == State.Perched) return new PetTick(null, null, null, false);

        if (Area(stage) is not { } area) return new PetTick(null, null, null, false);

        // **움직이는 중이면 그것부터.** 아래의 "켤 때 뜸들이기"보다 앞이어야 한다 —
        // 뒤에 두면 켜자마자 커서에 쫓겼을 때 회피가 뜸들이기에 덮여서 안 비킨다.
        if (state is State.Walking or State.Dodging) return Step(stage, area, now);

        // 켜자마자 걸어가면 놀란다. 1~3초 서 있다 시작한다.
        if (!started)
        {
            started = true;
            state = State.Resting;
            restUntil = now + TimeSpan.FromSeconds(1 + random.NextDouble() * 2);
            return new PetTick(restUntil - now, null, null, false);
        }

        // **쉬는 동안 걸어나갈 이유가 없으면 아예 안 깨운다.** 깨워 봐야 시계만 보고
        // 도로 눕는다. 위의 비키는 갈래보다 **뒤**여야 한다 — 앞에 두면 탈진했을 때
        // 비키던 도중에 멈춰 선다.
        if (!CanWander) return new PetTick(null, null, null, false);

        if (state == State.Resting && now < restUntil)
        {
            return new PetTick(Later(restUntil - now, QuietLeft(stage)), null, null, false);
        }

        return StartWandering(stage, area, now);
    }

    // ── 창에 붙기 ───────────────────────────────────────────────────

    /// <summary>
    /// 끌어다 놓은 자리에서 창 테두리에 붙인다. 붙었으면 true.
    ///
    /// <b>부르는 쪽이 곧바로 자리를 저장해야 한다.</b> 붙는 순간은 걷는 중이 아니라
    /// <see cref="Tick"/> 의 <c>Settled</c> 를 못 탄다 — 안 저장하면 껐다 켜면 붙기 전
    /// 자리로 돌아간다. 맥은 여기서 <c>didSettle()</c> 을 직접 부른다.
    /// </summary>
    public bool Perch(PerchSpot spot)
    {
        if (!Perches) return false;
        perched = spot;
        state = State.Perched;
        hurried = false;
        return true;
    }

    /// <summary>
    /// 붙어 있는 동안의 한 틱. 창이 움직였으면 따라가고, 없어졌으면 떨어진다.
    ///
    /// <b>창 목록을 인자로 받는다.</b> 맥은 여기서 직접 조사하지만 <c>Core</c> 는 Win32 를
    /// 모른다 — 부르는 쪽이 한 번 떠서 넣어 준다. <b>한 틱에 목록 한 번뿐이어야 한다</b> —
    /// 자리와 묻힘이 같은 목록을 봐야 하고, 각자 뜨면 같은 답을 두 번 사면서 붙어 있는
    /// 내내 그 값을 낸다.
    /// </summary>
    /// <param name="mascot">그림이 실제로 덮는 크기. 오프셋을 가두는 데 쓴다.</param>
    /// <param name="origin">
    /// 그 자리에 붙었을 때 창이 놓일 원점을 재 주는 함수. 화면 밖이면 null 을 내고,
    /// 그때는 떨어진다. 그림 사정을 아는 쪽(<c>App</c>)이 꽂아 준다.
    /// </param>
    /// <param name="buried">그 자리가 지금 남의 창에 묻혔는지.</param>
    public PerchTick Follow(
        IReadOnlyList<PerchWindow> windows,
        PetRect mascot,
        Func<PerchSpot, PetPoint?> origin,
        Func<PerchSpot, bool> buried,
        bool dragging)
    {
        if (state != State.Perched) return new PerchTick(null, false, Dropped: true, PerchTick_Wake(dragging));

        // 창이 닫혔거나 최소화됐거나 다른 가상 데스크톱으로 갔다.
        if (PerchFinder.Locate(perched.Window, windows) is not { } found) return Unperch();
        // 창을 좁혀서 붙어 있을 자리가 없어졌다.
        if (perched.Clamped(found.Frame, mascot.Width, mascot.Height) is not { } moved)
        {
            return Unperch();
        }
        // 붙은 자리가 화면 밖으로 나갔다.
        if (origin(moved) is not { } at) return Unperch();

        perched = moved;

        // **묻혔으면 앞으로 끌어올리지 않는다.** 올리면 그 창을 덮은 창 위에 펫만
        // 떠서, 아무것도 없는 자리에 매달린 것으로 보인다.
        var front = found.IsFront && !buried(moved);
        return new PerchTick(at, front, Dropped: false, PerchTick_Wake(dragging));
    }

    /// <summary>창을 끄는 동안에는 촘촘히 따라간다.</summary>
    private static TimeSpan PerchTick_Wake(bool dragging) =>
        dragging ? PerchChaseTick : PerchTick;

    /// <summary>
    /// 붙어 있던 것을 놓고 그 자리에 선다.
    ///
    /// <b>떨어지는 연출은 없다.</b> 떨어지는 칸이 시트에 없어서 그리는 사람이 새로
    /// 그려야 하는데, <c>MascotSheet</c> 의 배치는 순서를 바꾸면 이미 그려진 사용자
    /// 시트가 전부 깨진다. 연출 하나에 치를 값이 아니다.
    /// </summary>
    private PerchTick Unperch()
    {
        state = State.Resting;
        restUntil = time.GetUtcNow() + RestSpan();
        perched = default;
        return new PerchTick(null, false, Dropped: true, PerchTick);
    }

    /// <summary>설정에서 붙기를 껐거나 붙어 있을 수 없게 됐으면 놓는다. 놓았으면 true.</summary>
    public bool ReleasePerchIfNeeded()
    {
        if (state != State.Perched) return false;
        if (Perches && CanStayPerched) return false;
        Unperch();
        return true;
    }

    /// <summary>
    /// 조건을 안 보고 그냥 놓는다. 놓았으면 true.
    ///
    /// <b>붙일 자리를 새로 못 찾았을 때 쓴다.</b> 그때 자세만 되돌리고 여기를 안 부르면
    /// 그림은 선 자세인데 창은 계속 테두리를 따라다닌다 — 아무것도 안 잡고 모서리에
    /// 붙어 미끄러지는 꼴이다.
    /// </summary>
    public bool ReleasePerch()
    {
        if (state != State.Perched) return false;
        Unperch();
        return true;
    }

    // ── 걷기 ────────────────────────────────────────────────────────

    private PetTick StartWandering(IPetStage stage, PetRect area, DateTimeOffset now)
    {
        if (!CanWander) return new PetTick(null, null, null, false);

        // 글을 쓰는 동안에는 새로 걷기 시작하지 않는다.
        if (QuietLeft(stage) is { } wait) return new PetTick(wait, null, null, false);

        if (PickDestination(stage, area) is not { } destination)
        {
            // 갈 곳이 없다(구석에 몰렸다). 조금 쉬었다 다시 본다.
            state = State.Resting;
            restUntil = now + RestSpan();
            return new PetTick(restUntil - now, null, null, false);
        }

        target = destination;
        state = State.Walking;
        hurried = false;
        return new PetTick(TickInterval, null, PetGait.Walk, false);
    }

    /// <summary>한 걸음 옮긴다. 도착했으면 그 자리에서 쉰다.</summary>
    private PetTick Step(IPetStage stage, PetRect area, DateTimeOffset now)
    {
        // 걷던 중에 글을 쓰기 시작하면 **그 자리에 선다.**
        if (state == State.Walking && stage.SinceLastKey < TypingQuiet)
        {
            return Arrive(now, QuietLeft(stage));
        }

        var speed = state == State.Dodging ? (hurried ? RunSpeed : DodgeSpeed) : WalkSpeed;
        var stepLength = speed * TickInterval.TotalSeconds;

        var from = new PetPoint(stage.Window.X, stage.Window.Y);
        var remaining = from.DistanceTo(target);

        // 목표가 오른쪽이면 오른쪽을 본다. **가로로 안 움직이면 보던 쪽 그대로다** —
        // `dx >= 0` 으로 두면 세로로만 걷는 동안 내내 오른쪽으로 덮여서, 옆모습
        // 캐릭터가 왼쪽을 보고 있다가 반대로 돌아버린다. 화면 가장자리에 붙어 있으면
        // 목표가 잘려 dx == 0 이 되므로 드물지도 않다.
        var dx = target.X - from.X;
        var facing = dx == 0 ? (bool?)null : dx > 0;

        // **다 왔을 때도 가둔다.** 걷는 도중에 모니터를 뽑으면 화면 밖 목적지에
        // 그대로 서 버린다.
        if (remaining <= stepLength) return Arrive(now, null, Clamp(stage, target, area));

        var next = new PetPoint(
            from.X + (target.X - from.X) / remaining * stepLength,
            from.Y + (target.Y - from.Y) / remaining * stepLength);
        var clamped = Clamp(stage, next, area);

        // 가둬서 제자리면 도착으로 친다. 안 그러면 벽에 붙어 영원히 떤다.
        if (clamped.DistanceTo(from) <= 0.5) return Arrive(now, null, clamped);

        return new PetTick(TickInterval, clamped, Gait, false, facing);
    }

    private PetTick Arrive(DateTimeOffset now, TimeSpan? wait, PetPoint? at = null)
    {
        state = State.Resting;
        restUntil = now + RestSpan();
        hurried = false;
        return new PetTick(Later(restUntil - now, wait), at, null, Settled: true);
    }

    private TimeSpan RestSpan() => TimeSpan.FromSeconds(3 + random.NextDouble() * 8);

    /// <summary>글 쓰는 중이면 얼마나 더 기다려야 하는지. 조용하면 null.</summary>
    private static TimeSpan? QuietLeft(IPetStage stage) =>
        stage.SinceLastKey < TypingQuiet ? TypingQuiet - stage.SinceLastKey : null;

    private static TimeSpan? Later(TimeSpan a, TimeSpan? b) => b is { } other && other > a ? other : a;

    // ── 목적지 ──────────────────────────────────────────────────────

    /// <summary>창이 돌아다닐 수 있는 범위(창의 왼쪽 위 기준). 자리가 없으면 null.</summary>
    private static PetRect? Area(IPetStage stage)
    {
        if (stage.WorkArea is not { } work) return null;

        var width = work.Width - EdgeMargin * 2 - stage.Window.Width;
        var height = work.Height - EdgeMargin * 2 - stage.Window.Height;
        if (width < 0 || height < 0) return null;

        return new PetRect(work.X + EdgeMargin, work.Y + EdgeMargin, width, height);
    }

    private static PetPoint Clamp(PetPoint point, PetRect area) => new(
        Math.Clamp(point.X, area.X, area.X + area.Width),
        Math.Clamp(point.Y, area.Y, area.Y + area.Height));

    /// <summary>화면 경계에서 반올림 오차만큼은 봐준다. 맥과 같은 여유다.</summary>
    private const double Slack = 0.5;

    /// <summary>
    /// 그 자리에 서도 되는지. **여백만큼 부풀린 창의 네 귀퉁이가 저마다 어느 화면 안에
    /// 있으면 된다.**
    ///
    /// 화면이 하나일 때는 <see cref="Area"/> 로 가두는 것과 **정확히 같은 답**이 나온다
    /// (여백 8 을 안팎으로 똑같이 셈한다). 늘어난 것은 **두 화면에 걸치는 자리를
    /// 허락한다**는 것 하나뿐이다.
    ///
    /// **맥처럼 "어느 한 화면에 온전히 들어가야 한다"로 재면 못 넘어간다.** 화면과 화면
    /// 사이에 창 폭 + 여백 16 만큼의 죽은 띠가 생기는데, 한 걸음이 2.6(뛰어도 30)이라
    /// 그 띠에 발을 들일 수가 없다 — 경계 앞에 서서 목적지만 새로 고르고 영영 안 넘어간다.
    ///
    /// 가운데 한 점이 아니라 **네 귀퉁이**로 재는 이유는 빈 자리를 막기 위해서다.
    /// 화면을 계단처럼 배치하면 어디에도 안 닿는 자리가 생기는데, 거기 서면 마스코트가
    /// 통째로 안 보인다. 부풀려서 재므로 바깥 테두리에서는 지금처럼 8 을 띄우고 멈춘다.
    /// </summary>
    private bool Standable(IPetStage stage, PetPoint origin)
    {
        if (screens.Count == 0) return false;

        var box = new PetRect(
            origin.X - EdgeMargin,
            origin.Y - EdgeMargin,
            stage.Window.Width + EdgeMargin * 2,
            stage.Window.Height + EdgeMargin * 2);

        return Covered(new PetPoint(box.X, box.Y))
            && Covered(new PetPoint(box.Right, box.Y))
            && Covered(new PetPoint(box.X, box.Bottom))
            && Covered(new PetPoint(box.Right, box.Bottom));
    }

    private bool Covered(PetPoint point)
    {
        foreach (var work in screens)
        {
            if (point.X >= work.X - Slack && point.X <= work.Right + Slack
                && point.Y >= work.Y - Slack && point.Y <= work.Bottom + Slack) return true;
        }
        return false;
    }

    /// <summary>
    /// 창 왼쪽 위를 설 수 있는 자리로 되당긴다.
    ///
    /// 넘어가기를 켰으면 **다른 화면 자리도 그대로 받는다.** 아니면 지금 화면 안으로
    /// 되당긴다 — 그때는 <see cref="CrossesScreens"/> 가 꺼져 있어 <see cref="screens"/>
    /// 가 비어 있으므로 검사 한 번이 헛돌지도 않는다.
    /// </summary>
    private PetPoint Clamp(IPetStage stage, PetPoint point, PetRect area) =>
        CrossesScreens && Standable(stage, point) ? point : Clamp(point, area);

    /// <summary>
    /// 어디로 걸어갈지. 가로로 크게, 세로로 조금 움직인다.
    ///
    /// 방향을 두 번 시도한다 — 한쪽이 벽이면 반대로 가 본다. 둘 다 너무 가까우면
    /// 포기하고 쉰다(찔끔거리는 것보다 서 있는 편이 낫다).
    /// </summary>
    private PetPoint? PickDestination(IPetStage stage, PetRect area)
    {
        var from = new PetPoint(stage.Window.X, stage.Window.Y);

        foreach (var sign in random.Next(2) == 0 ? new[] { 1.0, -1.0 } : [-1.0, 1.0])
        {
            // **넘어갈 때만 크게 걷는다.** 화면 하나 안에서는 지금 폭이 맞는데,
            // 화면 경계를 넘으려면 한 걸음이 그 경계까지 닿아야 한다. 맥과 같은
            // 90~900 / 90~360 이다.
            var reach = CrossesScreens ? 810.0 : 270.0;
            var dx = sign * (90 + random.NextDouble() * reach);
            var dy = -70 + random.NextDouble() * 140;
            var candidate = Clamp(stage, new PetPoint(from.X + dx, from.Y + dy), area);

            if (candidate.DistanceTo(from) >= MinimumTravel) return candidate;
        }
        return null;
    }

    /// <summary>
    /// 커서에서 물러날 자리.
    ///
    /// 곧장 반대로 가 보고, 벽이면 90도씩 돌려 가며 네 방향을 시도한다.
    /// 다 막혔으면 null — 구석에서는 찔끔거리지 않고 가만히 있는다.
    /// </summary>
    private PetPoint? RetreatTarget(IPetStage stage, PetRect area)
    {
        var window = stage.Window;
        var from = new PetPoint(window.X, window.Y);
        var center = window.Center;

        var dx = center.X - stage.Cursor.X;
        var dy = center.Y - stage.Cursor.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);

        // 중심이 정확히 겹치면 방향이 없다. 오른쪽 아래로 물러난다
        // (맥은 y 가 위로 커져서 오른쪽 위였다 — 같은 방향이다).
        if (length < 0.001)
        {
            dx = 0.7071;
            dy = 0.7071;
        }
        else
        {
            dx /= length;
            dy /= length;
        }

        var distance = Math.Max(window.Width, window.Height) * 1.15;

        foreach (var angle in new[] { 0.0, Math.PI / 2, -Math.PI / 2, Math.PI })
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var candidate = Clamp(
                stage,
                new PetPoint(
                    from.X + (dx * cos - dy * sin) * distance,
                    from.Y + (dx * sin + dy * cos) * distance),
                area);

            if (candidate.DistanceTo(from) >= MinimumTravel) return candidate;
        }
        return null;
    }
}
