using DongCSU.Core.Pet;

namespace DongCSU.Core.Tests;

/// <summary>
/// 펫이 스스로 움직이는 규칙. **화면 없이 굳힌다** — 시계와 무대를 넣어 주면
/// 앱을 안 띄우고도 "커서를 피해 가는지"를 확인할 수 있다.
/// </summary>
public class PetMotionTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>1920×1080 화면 한가운데에 128×160 펫이 서 있다.</summary>
    private static Stage Desk() => new()
    {
        Window = new PetRect(900, 500, 128, 160),
        WorkArea = new PetRect(0, 0, 1920, 1040),
        Cursor = new PetPoint(100, 100),
        SinceLastKey = TimeSpan.MaxValue,
    };

    [Fact]
    public void 켜자마자_걷지_않고_잠깐_선다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(1));

        var first = pet.Tick(Desk());

        Assert.Null(first.MoveTo);
        Assert.NotNull(first.NextWakeup);
        // 1~3초.
        Assert.InRange(first.NextWakeup!.Value.TotalSeconds, 1, 3);
    }

    [Fact]
    public void 혼자_돌아다니기를_끄면_깨어나지_않는다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(1)) { Wanders = false };

        pet.Tick(Desk());
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Null(pet.Tick(Desk()).NextWakeup);
    }

    [Fact]
    public void 쉬고_나면_걷기_시작한다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(7));

        pet.Tick(Desk());
        clock.Advance(TimeSpan.FromSeconds(4));
        var going = pet.Tick(Desk());

        Assert.Equal(PetGait.Walk, going.Gait);
        Assert.Equal(PetMotion.TickInterval, going.NextWakeup);
    }

    /// <summary>한 틱에 걷기 속도 26 의 1/10 만큼 간다.</summary>
    [Fact]
    public void 한_틱에_한_걸음씩_옮긴다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(7));
        var stage = Desk();

        pet.Tick(stage);
        clock.Advance(TimeSpan.FromSeconds(4));
        pet.Tick(stage);

        var moved = pet.Tick(stage);

        Assert.NotNull(moved.MoveTo);
        var from = new PetPoint(stage.Window.X, stage.Window.Y);
        Assert.Equal(2.6, moved.MoveTo!.Value.DistanceTo(from), 1);
    }

    [Fact]
    public void 화면_밖으로_나가지_않는다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(3));
        // 오른쪽 끝에 붙여 둔다.
        var stage = Desk();
        stage.Window = new PetRect(1780, 500, 128, 160);

        pet.Tick(stage);
        clock.Advance(TimeSpan.FromSeconds(4));

        for (var i = 0; i < 60; i++)
        {
            var tick = pet.Tick(stage);
            if (tick.MoveTo is { } to) stage.Window = stage.Window with { X = to.X, Y = to.Y };
            clock.Advance(PetMotion.TickInterval);
        }

        // 여백 8 을 지킨다.
        Assert.InRange(stage.Window.X, 8, 1920 - 8 - 128);
        Assert.InRange(stage.Window.Y, 8, 1040 - 8 - 160);
    }

    [Fact]
    public void 자리가_없으면_움직이지_않는다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(1));
        var stage = Desk();
        // 창이 화면보다 크다.
        stage.WorkArea = new PetRect(0, 0, 100, 100);

        pet.Tick(stage);
        clock.Advance(TimeSpan.FromSeconds(4));

        var tick = pet.Tick(stage);
        Assert.Null(tick.MoveTo);
        Assert.Null(tick.NextWakeup);
    }

    // ── 커서 피하기 ─────────────────────────────────────────────────

    [Fact]
    public void 커서에서_멀어지는_쪽으로_비킨다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(1));
        var stage = Desk();
        // 커서가 왼쪽에 있다 → 오른쪽으로 물러나야 한다.
        stage.Cursor = new PetPoint(900, 580);

        Assert.True(pet.RequestDodge(stage));

        var moved = pet.Tick(stage);
        Assert.NotNull(moved.MoveTo);
        Assert.True(moved.MoveTo!.Value.X > stage.Window.X, "커서 반대쪽으로 가야 한다");
    }

    [Fact]
    public void 이미_비키는_중이면_무시한다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(1));
        var stage = Desk();
        stage.Cursor = new PetPoint(900, 580);

        Assert.True(pet.RequestDodge(stage));
        Assert.False(pet.RequestDodge(stage));
    }

    /// <summary>4초 안에 또 쫓기면 걷지 않고 뛴다.</summary>
    [Fact]
    public void 두_번째_회피부터_뛴다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(1));
        var stage = Desk();
        stage.Cursor = new PetPoint(900, 580);

        pet.RequestDodge(stage);
        Assert.Equal(PetGait.Walk, pet.Gait);

        // 도착시킨 뒤 곧바로 다시 쫓는다.
        for (var i = 0; i < 60; i++)
        {
            var tick = pet.Tick(stage);
            if (tick.MoveTo is { } to) stage.Window = stage.Window with { X = to.X, Y = to.Y };
            if (tick.Settled) break;
            clock.Advance(PetMotion.TickInterval);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        pet.RequestDodge(stage);

        Assert.Equal(PetGait.Run, pet.Gait);
    }

    [Fact]
    public void 커서_피하기를_끄면_안_비킨다()
    {
        var pet = new PetMotion(new MovingTime(Start), new Random(1)) { DodgesCursor = false };
        var stage = Desk();
        stage.Cursor = new PetPoint(900, 580);

        Assert.False(pet.RequestDodge(stage));
    }

    // ── 글 쓰는 동안 ────────────────────────────────────────────────

    [Fact]
    public void 글을_쓰는_동안에는_비키지_않는다()
    {
        var pet = new PetMotion(new MovingTime(Start), new Random(1));
        var stage = Desk();
        stage.Cursor = new PetPoint(900, 580);
        stage.SinceLastKey = TimeSpan.FromSeconds(1);

        Assert.False(pet.RequestDodge(stage));
    }

    [Fact]
    public void 글을_쓰는_동안에는_걷기_시작하지_않는다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(7));
        var stage = Desk();

        pet.Tick(stage);
        clock.Advance(TimeSpan.FromSeconds(4));
        stage.SinceLastKey = TimeSpan.FromSeconds(2);

        var tick = pet.Tick(stage);

        Assert.Null(tick.MoveTo);
        Assert.Null(tick.Gait);
        // 조용해질 때까지 기다린다.
        Assert.Equal(TimeSpan.FromSeconds(3), tick.NextWakeup);
    }

    /// <summary>걷던 중에 타자가 시작되면 그 자리에 서고, 그 자리를 저장하라고 알린다.</summary>
    [Fact]
    public void 걷다가_타자가_시작되면_그_자리에_선다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(7));
        var stage = Desk();

        pet.Tick(stage);
        clock.Advance(TimeSpan.FromSeconds(4));
        pet.Tick(stage);
        pet.Tick(stage);

        stage.SinceLastKey = TimeSpan.FromSeconds(0.5);
        var stopped = pet.Tick(stage);

        Assert.True(stopped.Settled);
        Assert.Null(stopped.Gait);
    }

    [Fact]
    public void 도착하면_한_번만_알린다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(7));
        var stage = Desk();

        pet.Tick(stage);
        clock.Advance(TimeSpan.FromSeconds(4));

        var settledCount = 0;
        for (var i = 0; i < 200; i++)
        {
            var tick = pet.Tick(stage);
            if (tick.MoveTo is { } to) stage.Window = stage.Window with { X = to.X, Y = to.Y };
            if (tick.Settled) settledCount++;
            clock.Advance(PetMotion.TickInterval);
            if (settledCount > 0 && tick.NextWakeup > PetMotion.TickInterval) break;
        }

        Assert.Equal(1, settledCount);
    }

    /// <summary>
    /// **멈추면 걸음도 내려놓는다.**
    ///
    /// 걷던 중에 펫에서 나가면 <c>Halt</c> 로 세우는데, 여기서 걸음이 안 풀리면
    /// 카드 안의 부엉이가 영영 걷는다.
    /// </summary>
    [Fact]
    public void 세우면_걸음이_풀린다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(7));

        pet.Tick(Desk());
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(PetGait.Walk, pet.Tick(Desk()).Gait);

        pet.Halt();

        Assert.Null(pet.Gait);
    }

    /// <summary>배회를 끄면 걷던 것도 그 자리에 멈춘다. 목적지까지 마저 가면 안 먹은 것처럼 보인다.</summary>
    [Fact]
    public void 배회를_끄면_걷던_것도_그_자리에_멈춘다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(7));

        pet.Tick(Desk());
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(PetGait.Walk, pet.Tick(Desk()).Gait);

        pet.Wanders = false;

        Assert.Null(pet.Gait);
    }

    /// <summary>비키는 중에 세워도 풀린다 — 커서를 피하다 펫에서 나갈 수 있다.</summary>
    [Fact]
    public void 비키는_중에_세워도_걸음이_풀린다()
    {
        var clock = new MovingTime(Start);
        var pet = new PetMotion(clock, new Random(1));
        var desk = Desk();

        pet.Tick(desk);
        Assert.True(pet.RequestDodge(desk));
        Assert.NotNull(pet.Gait);

        pet.Halt();

        Assert.Null(pet.Gait);
    }

    private sealed class Stage : IPetStage
    {
        public PetRect Window { get; set; }
        public PetRect? WorkArea { get; set; }
        public PetPoint Cursor { get; set; }
        public TimeSpan SinceLastKey { get; set; }
    }

    private sealed class MovingTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }
}
