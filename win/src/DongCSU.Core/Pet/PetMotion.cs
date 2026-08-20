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

    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly Random random = random ?? Random.Shared;

    private enum State { Still, Resting, Walking, Dodging }

    private State state = State.Still;
    private DateTimeOffset restUntil;
    private PetPoint target;
    private bool hurried;
    private DateTimeOffset? lastDodgeAt;
    private bool started;

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
        if (state is State.Walking or State.Dodging)
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
        _ => null,
    };

    /// <summary>처음부터 다시. 펫 모드에 들어올 때 부른다.</summary>
    public void Reset()
    {
        state = State.Still;
        started = false;
        hurried = false;
        lastDodgeAt = null;
    }

    /// <summary>
    /// 커서를 피해 비켜서라고 시킨다. 이미 비키는 중이면 무시한다.
    ///
    /// 되돌려주는 값이 true 면 다음 틱을 걸어야 한다.
    /// </summary>
    public bool RequestDodge(IPetStage stage)
    {
        if (!DodgesCursor || state == State.Dodging) return false;
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
        var now = time.GetUtcNow();
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

        if (remaining <= stepLength) return Arrive(now, null, target);

        var next = new PetPoint(
            from.X + (target.X - from.X) / remaining * stepLength,
            from.Y + (target.Y - from.Y) / remaining * stepLength);
        var clamped = Clamp(next, area);

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
            var dx = sign * (90 + random.NextDouble() * 270);
            var dy = -70 + random.NextDouble() * 140;
            var candidate = Clamp(new PetPoint(from.X + dx, from.Y + dy), area);

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
    private static PetPoint? RetreatTarget(IPetStage stage, PetRect area)
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
                new PetPoint(
                    from.X + (dx * cos - dy * sin) * distance,
                    from.Y + (dx * sin + dy * cos) * distance),
                area);

            if (candidate.DistanceTo(from) >= MinimumTravel) return candidate;
        }
        return null;
    }
}
