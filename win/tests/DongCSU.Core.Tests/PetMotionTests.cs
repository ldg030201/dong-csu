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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(1)) { Wanders = false };

        pet.Tick(Desk());
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Null(pet.Tick(Desk()).NextWakeup);
    }

    [Fact]
    public void 쉬고_나면_걷기_시작한다()
    {
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var pet = new PetMotion(new FakeTime(Start), new Random(1)) { DodgesCursor = false };
        var stage = Desk();
        stage.Cursor = new PetPoint(900, 580);

        Assert.False(pet.RequestDodge(stage));
    }

    // ── 글 쓰는 동안 ────────────────────────────────────────────────

    [Fact]
    public void 글을_쓰는_동안에는_비키지_않는다()
    {
        var pet = new PetMotion(new FakeTime(Start), new Random(1));
        var stage = Desk();
        stage.Cursor = new PetPoint(900, 580);
        stage.SinceLastKey = TimeSpan.FromSeconds(1);

        Assert.False(pet.RequestDodge(stage));
    }

    [Fact]
    public void 글을_쓰는_동안에는_걷기_시작하지_않는다()
    {
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
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
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(1));
        var desk = Desk();

        pet.Tick(desk);
        Assert.True(pet.RequestDodge(desk));
        Assert.NotNull(pet.Gait);

        pet.Halt();

        Assert.Null(pet.Gait);
    }

    // ── 탈진 · 어지러움 ────────────────────────────────────────────

    /// <summary>탈진하면 제 발로 산책 나갈 기운이 없다. 깨울 이유도 없다.</summary>
    [Fact]
    public void 탈진하면_걷기_시작하지_않는다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(7)) { IsDrained = true };

        pet.Tick(Desk());
        clock.Advance(TimeSpan.FromSeconds(4));
        var tick = pet.Tick(Desk());

        Assert.Null(tick.Gait);
        Assert.Null(tick.NextWakeup);
    }

    /// <summary>
    /// **지친 것이지 멎은 것이 아니다.** 안 비키면 화면을 가린 채로 굳는다 —
    /// 주간 소진(아예 멈춘다)과 갈리는 자리가 여기다.
    /// </summary>
    [Fact]
    public void 탈진해도_커서는_피한다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(7)) { IsDrained = true };
        var stage = Desk();
        stage.Cursor = new PetPoint(900, 580);

        Assert.True(pet.RequestDodge(stage));

        var moved = pet.Tick(stage);
        Assert.NotNull(moved.MoveTo);
        Assert.True(moved.MoveTo!.Value.X > stage.Window.X, "탈진해도 커서 반대쪽으로 가야 한다");
    }

    /// <summary>어지러운 동안에는 전부 멈춘다. 비틀거리는 정지 그림이 옆으로 미끄러지면 안 된다.</summary>
    [Fact]
    public void 어지러운_동안에는_걷지도_비키지도_않는다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(7)) { IsDizzy = true };
        var stage = Desk();

        pet.Tick(stage);
        clock.Advance(TimeSpan.FromSeconds(4));
        var tick = pet.Tick(stage);

        Assert.Null(tick.Gait);
        Assert.Null(tick.NextWakeup);

        stage.Cursor = new PetPoint(900, 580);
        Assert.False(pet.RequestDodge(stage));
    }

    /// <summary>2.4초가 지나 어지러움이 풀리면 다시 걸어야 한다. 안 그러면 흔든 뒤로 영영 안 움직인다.</summary>
    [Fact]
    public void 어지러움이_풀리면_다시_걷는다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(7));

        pet.Tick(Desk());
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(PetGait.Walk, pet.Tick(Desk()).Gait);

        // 걷던 중에 흔들렸다 → 그 자리에 선다.
        pet.IsDizzy = true;
        Assert.Null(pet.Tick(Desk()).Gait);

        pet.IsDizzy = false;
        // 쉬는 시간(최대 11초)을 넉넉히 넘긴다.
        clock.Advance(TimeSpan.FromSeconds(12));

        Assert.Equal(PetGait.Walk, pet.Tick(Desk()).Gait);
    }

    // ── 바라보는 쪽 ────────────────────────────────────────────────

    /// <summary>커서에게서 물러나는 쪽을 보고 걷는다. 시트의 옆모습이 이걸로 뒤집힌다.</summary>
    [Theory]
    [InlineData(100, true)]    // 왼쪽에서 쫓기면 오른쪽으로 물러난다
    [InlineData(1800, false)]  // 오른쪽에서 쫓기면 왼쪽으로
    public void 가는_쪽을_본다(double cursorX, bool facingRight)
    {
        var pet = new PetMotion(new FakeTime(Start), new Random(1));
        var desk = Desk();
        desk.Cursor = new PetPoint(cursorX, 580);

        Assert.True(pet.RequestDodge(desk));

        Assert.Equal(facingRight, pet.Tick(desk).FacingRight);
    }

    /// <summary>
    /// 세로로만 움직이면 보던 쪽 그대로다.
    ///
    /// 여기서 한쪽으로 덮으면 화면 가장자리에 붙어 위아래로만 걷는 동안 내내
    /// 그쪽을 보게 된다. 목표가 잘려 가로 이동이 0 이 되는 일은 드물지 않다.
    /// </summary>
    [Fact]
    public void 세로로만_움직이면_보던_쪽_그대로다()
    {
        var pet = new PetMotion(new FakeTime(Start), new Random(1));
        var desk = Desk();
        // 커서가 마스코트 한가운데 바로 아래. 물러나는 쪽이 정확히 위다.
        desk.Cursor = new PetPoint(964, 900);

        Assert.True(pet.RequestDodge(desk));

        var tick = pet.Tick(desk);
        Assert.NotNull(tick.MoveTo);
        Assert.Null(tick.FacingRight);
    }

    private sealed class Stage : IPetStage
    {
        public PetRect Window { get; set; }
        public PetRect? WorkArea { get; set; }
        public PetPoint Cursor { get; set; }
        public TimeSpan SinceLastKey { get; set; }

        /// <summary>
        /// 화면 목록. 안 넣으면 <see cref="WorkArea"/> 하나짜리로 본다.
        ///
        /// **기본값을 이렇게 두는 것이 중요하다** — 안 그러면 여기 있는 검사 스물몇
        /// 개가 저마다 화면 목록을 다시 적어야 한다.
        /// </summary>
        public IReadOnlyList<PetRect> WorkAreas
        {
            get => screens ?? (WorkArea is { } one ? [one] : []);
            set => screens = value;
        }

        private IReadOnlyList<PetRect>? screens;

        /// <summary>눈금. 배율이 다른 화면으로 넘어가는 것을 흉내 낼 때만 만진다.</summary>
        public double Scale { get; set; } = 1;
    }

    // ── 다른 화면으로 넘어가기 ──────────────────────────────────────

    /// <summary>
    /// 1920 짜리 화면 둘이 나란히 붙어 있다. 오른쪽 것이 조금 더 높다 — 이음매를
    /// 넘은 뒤에도 갈 자리가 있어야 한다.
    /// </summary>
    private static Stage TwoScreens() => new()
    {
        Window = new PetRect(1700, 500, 128, 160),
        WorkArea = new PetRect(0, 0, 1920, 1040),
        WorkAreas = [new PetRect(0, 0, 1920, 1040), new PetRect(1920, 0, 1920, 1080)],
        Cursor = new PetPoint(100, 100),
        SinceLastKey = TimeSpan.MaxValue,
    };

    /// <summary>목적지를 정할 때까지 틱을 돌리고, 걷는 동안 창을 따라 옮겨 준다.</summary>
    private static double WalkFor(PetMotion pet, Stage stage, FakeTime clock, int ticks)
    {
        var farthest = stage.Window.X;
        for (var i = 0; i < ticks; i++)
        {
            var tick = pet.Tick(stage);
            if (tick.MoveTo is { } to)
            {
                stage.Window = new PetRect(to.X, to.Y, stage.Window.Width, stage.Window.Height);
                farthest = Math.Max(farthest, to.X);
            }
            clock.Advance(tick.NextWakeup ?? PetMotion.TickInterval);
        }
        return farthest;
    }

    /// <summary>
    /// **꺼 두면 아무것도 안 바뀐다.** 이 검사가 대조군이다 — 넘어가기를 넣으면서
    /// 화면 하나짜리 사람의 동작을 건드리면 여기가 먼저 빨개진다.
    /// </summary>
    [Fact]
    public void 꺼_두면_지금_화면_안에서만_돈다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(3)) { Wanders = true };
        var stage = TwoScreens();

        var farthest = WalkFor(pet, stage, clock, 40000);

        // 왼쪽 화면에서 창 왼쪽 위가 갈 수 있는 오른쪽 끝: 1920 - 8 - 128 = 1784.
        Assert.True(farthest <= 1784 + 0.001, $"화면을 넘었다: {farthest}");
    }

    /// <summary>켜면 이음매를 넘어 오른쪽 화면까지 걸어간다.</summary>
    [Fact]
    public void 켜면_옆_화면_자리도_받는다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(3)) { Wanders = true, CrossesScreens = true };
        var stage = TwoScreens();

        var farthest = WalkFor(pet, stage, clock, 40000);

        Assert.True(farthest > 1920, $"이음매를 못 넘었다: {farthest}");
    }

    /// <summary>
    /// **이음매 위에서 멈추지 않는다.**
    ///
    /// 맥처럼 "어느 한 화면에 온전히 들어가야 한다" 로 재면 화면 사이에 창 폭 + 여백
    /// 16 만큼의 죽은 띠가 생기는데, 한 걸음이 2.6 이라 거기 발을 들일 수가 없다 —
    /// 경계 앞에 서서 목적지만 새로 고르고 영영 안 넘어간다.
    /// </summary>
    [Fact]
    public void 이음매_위에서_멈추지_않는다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(3)) { Wanders = true, CrossesScreens = true };
        var stage = TwoScreens();
        // 왼쪽 화면의 오른쪽 끝. 여기서 한 걸음이라도 더 가야 넘어간다.
        stage.Window = new PetRect(1784, 500, 128, 160);

        var farthest = WalkFor(pet, stage, clock, 40000);

        Assert.True(farthest > 1784, $"끝에 붙어서 안 움직였다: {farthest}");
    }

    /// <summary>
    /// 화면이 떨어져 있으면 그 사이 빈 자리에는 안 선다. 거기 서면 마스코트가 통째로
    /// 안 보인다.
    /// </summary>
    [Fact]
    public void 화면_사이_빈_자리에는_안_선다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(3)) { Wanders = true, CrossesScreens = true };
        var stage = TwoScreens();
        stage.WorkAreas = [new PetRect(0, 0, 1920, 1040), new PetRect(2400, 0, 1600, 1040)];

        var farthest = WalkFor(pet, stage, clock, 40000);

        // 왼쪽 화면 끝(1784)까지는 가도, 빈 띠(1920~2400)에는 서면 안 된다.
        Assert.True(
            farthest <= 1784 + 0.001 || farthest >= 2400 + 8,
            $"빈 자리에 섰다: {farthest}");
    }

    /// <summary>
    /// **넘어갈 때만 크게 걷는다.** 같은 씨앗으로 뽑으면 켜짐 쪽이 더 멀리 간다 —
    /// 화면 경계까지 한 걸음이 닿아야 넘어갈 수 있다.
    /// </summary>
    [Fact]
    public void 켜면_한_걸음이_커진다()
    {
        // **첫 산책 하나만 잰다.** 오래 돌리면 둘 다 벽까지 가서 걸음 폭이 아니라
        // 화면 크기를 재게 된다.
        static double FirstWalk(bool crosses)
        {
            var clock = new FakeTime(Start);
            var pet = new PetMotion(clock, new Random(7))
            {
                Wanders = true,
                CrossesScreens = crosses,
            };
            // 아주 넓은 화면 한가운데 — 어느 쪽으로 얼마를 걸어도 안 막힌다.
            var desk = new PetRect(0, 0, 20000, 1440);
            var stage = new Stage
            {
                Window = new PetRect(10000, 500, 128, 160),
                WorkArea = desk,
                WorkAreas = [desk],
                Cursor = new PetPoint(0, 0),
                SinceLastKey = TimeSpan.MaxValue,
            };

            var start = stage.Window.X;
            var farthest = 0.0;
            for (var i = 0; i < 40000; i++)
            {
                var tick = pet.Tick(stage);
                if (tick.MoveTo is { } to)
                {
                    stage.Window = new PetRect(to.X, to.Y, stage.Window.Width, stage.Window.Height);
                    farthest = Math.Max(farthest, Math.Abs(to.X - start));
                }
                // 한 번 걷고 멈추면 거기까지가 한 걸음이다.
                if (tick.Settled && farthest > 0) break;
                clock.Advance(tick.NextWakeup ?? PetMotion.TickInterval);
            }
            return farthest;
        }

        Assert.True(FirstWalk(true) > FirstWalk(false), "켜도 걸음 폭이 그대로다");
    }

    /// <summary>
    /// 배율이 다른 화면으로 넘어가면 좌표계 전체가 다시 늘어난다. **들고 있던 목적지도
    /// 같은 비율로 옮겨 줘야** 넘어간 그 순간부터 엉뚱한 자리로 걷지 않는다.
    /// </summary>
    [Fact]
    public void 배율이_바뀌면_목적지도_같이_늘어난다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(3)) { Wanders = true, CrossesScreens = true };
        var stage = TwoScreens();

        // 걷기 시작할 때까지 돌린다.
        for (var i = 0; i < 200 && pet.Gait is null; i++)
        {
            var warm = pet.Tick(stage);
            if (warm.MoveTo is { } to)
            {
                stage.Window = new PetRect(to.X, to.Y, stage.Window.Width, stage.Window.Height);
            }
            clock.Advance(warm.NextWakeup ?? PetMotion.TickInterval);
        }
        Assert.NotNull(pet.Gait);

        var before = pet.Tick(stage).MoveTo;
        Assert.NotNull(before);

        // 배율 150% 화면으로 넘어갔다 — 눈금이 1 에서 2/3 으로 줄어든다.
        stage.Scale = 2.0 / 3.0;
        stage.Window = new PetRect(
            stage.Window.X * stage.Scale, stage.Window.Y * stage.Scale,
            stage.Window.Width, stage.Window.Height);

        var after = pet.Tick(stage).MoveTo;

        // 목적지가 같이 줄어들지 않으면 한 걸음이 옛 좌표계의 먼 자리를 향해 튄다.
        Assert.NotNull(after);
        Assert.True(
            Math.Abs(after.Value.X - stage.Window.X) < 400,
            $"목적지가 옛 좌표계에 남았다: {after.Value.X} (창 {stage.Window.X})");
    }

    /// <summary>
    /// **커서를 피할 때도 넘어간다.** 몰아붙이면 옆 모니터로 달아나는 것이 이 설정을
    /// 켠 사람이 기대하는 모습이다 (맥 2.5.2.1 이 같은 것을 고쳤다).
    /// </summary>
    [Fact]
    public void 커서를_피할_때도_넘어간다()
    {
        var clock = new FakeTime(Start);
        var pet = new PetMotion(clock, new Random(3)) { CrossesScreens = true };
        var stage = TwoScreens();
        // 왼쪽 화면 끝에 서 있고 커서가 왼쪽에서 밀고 들어온다.
        stage.Window = new PetRect(1784, 500, 128, 160);
        stage.Cursor = new PetPoint(1780, 580);

        Assert.True(pet.RequestDodge(stage));
        var tick = pet.Tick(stage);

        Assert.NotNull(tick.MoveTo);
        Assert.True(tick.MoveTo.Value.X > 1784, $"이음매에 막혔다: {tick.MoveTo.Value.X}");
    }

}
