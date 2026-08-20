using DongCSU.Core.Owl;
using DongCSU.Core.Pet;

namespace DongCSU.Core.Tests;

/// <summary>걷는 자세로 갈아타는 규칙. 그림은 <c>owl.json</c> 에 이미 다 들어 있다.</summary>
public class OwlGaitTests
{
    private static OwlAnimator Animator() => new(OwlDocument.Embedded, new Random(1));

    [Fact]
    public void 걸으면_기분_대신_걷기_그림을_쓴다()
    {
        var animator = Animator();
        Assert.Equal("idle", animator.Animation.Name);

        animator.SetGait(PetGait.Walk);
        Assert.Equal("walk", animator.Animation.Name);

        animator.SetGait(PetGait.Run);
        Assert.Equal("run", animator.Animation.Name);
    }

    [Fact]
    public void 걸음을_끄면_기분으로_돌아간다()
    {
        var animator = Animator();
        animator.SetMood(OwlMood.Tired);
        animator.SetGait(PetGait.Walk);

        animator.SetGait(null);

        Assert.Equal("tired", animator.Animation.Name);
    }

    /// <summary>걷는 중에는 기분보다 걸음이 이긴다 — 걸어가며 서 있는 자세를 하면 미끄러진다.</summary>
    [Fact]
    public void 걷는_중에는_걸음이_기분을_이긴다()
    {
        var animator = Animator();
        animator.SetGait(PetGait.Walk);

        animator.SetMood(OwlMood.Exhausted);

        Assert.Equal("walk", animator.Animation.Name);
    }

    /// <summary>
    /// **걷기 → 달리기는 프레임을 되감지 않는다.** 다리 위치는 같고 박자만 빨라지는
    /// 것이라, 되감으면 발이 튄다.
    /// </summary>
    [Fact]
    public void 걷기에서_달리기로_바뀔_때_프레임을_되감지_않는다()
    {
        var animator = Animator();
        animator.SetGait(PetGait.Walk);
        animator.Advance();
        animator.Advance();
        var before = animator.CurrentGrid;

        animator.SetGait(PetGait.Run);

        // 같은 자리의 프레임이어야 한다(walk 와 run 은 다리 순서가 같다).
        Assert.Equal(before.Length, animator.CurrentGrid.Length);
        Assert.NotEqual("walk", animator.Animation.Name);
    }

    /// <summary>서 있다 걷기 시작할 때는 처음 프레임부터. 중간 자세로 튀어나오면 어색하다.</summary>
    [Fact]
    public void 서_있다_걷기_시작하면_처음부터()
    {
        var animator = Animator();
        var walk = OwlDocument.Embedded.Animations.Single(a => a.Name == "walk");

        animator.SetGait(PetGait.Walk);

        Assert.Equal(walk.Frames[0].Grid, animator.CurrentGrid);
    }

    /// <summary>
    /// **멈출 때도 처음 프레임부터.** 걷는 동안 <c>frameIndex</c> 는 다리 네 칸 주기라
    /// 0~3 을 도는데, 그 값을 그대로 두고 서면 기분 프레임의 중간 — 눈을 감은 칸 —
    /// 에서 다시 선다. 지침은 두 칸뿐이라 0.9초짜리 감은 칸에 걸려 질끈 감았다 뜬다.
    /// </summary>
    [Fact]
    public void 걸음을_멈추면_처음_프레임부터()
    {
        var animator = Animator();
        animator.SetMood(OwlMood.Tired);
        animator.SetGait(PetGait.Walk);
        animator.Advance();   // 걷는 주기에서 frameIndex 가 1이 된다

        animator.SetGait(null);

        // tired 0번은 실눈(3.6초), 1번은 감음(0.9초)이다.
        Assert.Equal(OwlEyes.Half, animator.CurrentFrame.Pose.Eyes);
        Assert.True(animator.CurrentDelay() >= TimeSpan.FromSeconds(3.6));
    }

    /// <summary>평소 기분도 마찬가지다 — idle 2번이 눈을 감은 칸이다.</summary>
    [Fact]
    public void 평소_기분도_멈추면_눈을_뜬_칸에서_선다()
    {
        var animator = Animator();
        animator.SetGait(PetGait.Walk);
        animator.Advance();
        animator.Advance();   // frameIndex 2 — idle 이라면 눈을 감은 칸이다

        animator.SetGait(null);

        Assert.Equal(OwlEyes.Open, animator.CurrentFrame.Pose.Eyes);
    }

    [Fact]
    public void 같은_걸음을_다시_넣으면_아무_일도_없다()
    {
        var animator = Animator();

        Assert.True(animator.SetGait(PetGait.Walk));
        Assert.False(animator.SetGait(PetGait.Walk));
    }

    /// <summary>
    /// 걷기·달리기는 8프레임이다. 다리 주기는 앞 4개이고 뒤 4개는 같은 다리에
    /// 눈 깜빡임이 붙은 것이라, 4개로 줄이면 깜빡임이 사라진다.
    /// </summary>
    [Fact]
    public void 걷기와_달리기는_여덟_프레임이다()
    {
        var document = OwlDocument.Embedded;

        Assert.Equal(8, document.Animations.Single(a => a.Name == "walk").Frames.Count);
        Assert.Equal(8, document.Animations.Single(a => a.Name == "run").Frames.Count);
    }

    /// <summary>다리 주기는 네 칸이다. 여덟 칸을 돌리면 같은 다리를 두 번씩 밟는다.</summary>
    [Fact]
    public void 다리는_네_칸마다_제자리로_온다()
    {
        var animator = Animator();
        animator.SetGait(PetGait.Walk);
        var first = animator.CurrentGrid;

        for (var i = 0; i < 4; i++) animator.Advance();

        Assert.Equal(first, animator.CurrentGrid);
    }

    /// <summary>
    /// **우리가 합성한 걸음이 맥이 뽑아 둔 것과 글자 하나까지 같아야 한다.**
    ///
    /// 걷는 자세는 <c>owl.json</c> 의 그림을 그대로 쓰지 않고 기분이 준 자세 위에 발을
    /// 얹어 만든다(그래야 지친 채로 걷는다). 옮겨 적은 규칙은 언젠가 어긋나므로,
    /// 평소(idle) 기분에서는 맥의 결과와 대조해 굳혀 둔다.
    /// </summary>
    [Theory]
    [InlineData("walk", PetGait.Walk)]
    [InlineData("run", PetGait.Run)]
    public void 합성한_걸음이_맥이_뽑아둔_것과_같다(string name, PetGait gait)
    {
        var frames = OwlDocument.Embedded.Animations.Single(a => a.Name == name).Frames;
        var animator = Animator();
        animator.SetGait(gait);

        for (var phase = 0; phase < 4; phase++)
        {
            Assert.Equal(frames[phase].Grid, animator.CurrentGrid);
            animator.Advance();
        }
    }

    /// <summary>
    /// **지친 채로 걷는다.** 걷기 그림을 통째로 쓰면 걷는 순간 눈이 다시 떠지고 처진
    /// 날개가 올라가서, 사용량이 줄어든 것처럼 읽힌다.
    /// </summary>
    [Theory]
    [InlineData(OwlMood.Tired)]
    [InlineData(OwlMood.Exhausted)]
    public void 지친_부엉이는_걸어도_지친_얼굴이다(OwlMood mood)
    {
        var document = OwlDocument.Embedded;
        var resting = document.Animations.Single(a => a.Name == mood.Name()).Frames[0].Pose;

        var animator = Animator();
        animator.SetMood(mood);
        animator.SetGait(PetGait.Walk);

        // 눈과 날개는 기분이 준 그대로, 발만 걷는 자세여야 한다.
        var expected = OwlComposer.Compose(document, resting with
        {
            Feet = OwlFeet.StepA,
            Lean = -1,
            FaceLean = 0,
            Bob = 0,
        });

        Assert.Equal(expected, animator.CurrentGrid);
        // 평소 기분의 걸음과는 달라야 한다 — 같으면 지친 티가 안 난다.
        Assert.NotEqual(
            document.Animations.Single(a => a.Name == "walk").Frames[0].Grid,
            animator.CurrentGrid);
    }

    /// <summary>
    /// **걸을 때 매 걸음마다 깜빡이지 않는다.** 여덟 칸을 통째로 돌리면 1.1초마다
    /// 깜빡여서 경련하는 것처럼 보인다 — 맥은 22~34틱(3~5초)에 한 번이다.
    /// </summary>
    [Fact]
    public void 걸을_때_깜빡임은_한참에_한_번이다()
    {
        var animator = Animator();
        animator.SetGait(PetGait.Walk);

        var open = 0;
        var shut = 0;
        for (var i = 0; i < 120; i++)
        {
            animator.Advance();
            // 눈 자리(5번째 줄)에 흰자가 남아 있으면 뜬 것이다.
            if (animator.CurrentGrid[4].Contains('w')) open++; else shut++;
        }

        // 120틱이면 서너 번 깜빡이고 한 번에 두어 칸이다. 여덟 칸을 통째로 돌렸다면
        // 열다섯 번이 넘는다.
        Assert.InRange(shut, 1, 14);
        Assert.True(open > shut);
    }
}
