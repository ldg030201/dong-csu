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

        Assert.Same(first, animator.CurrentGrid);
    }

    /// <summary>
    /// **걸을 때 매 걸음마다 깜빡이지 않는다.** 여덟 칸을 통째로 돌리면 1.1초마다
    /// 깜빡여서 경련하는 것처럼 보인다 — 맥은 22~34틱(3~5초)에 한 번이다.
    /// </summary>
    [Fact]
    public void 걸을_때_깜빡임은_한참에_한_번이다()
    {
        var animator = Animator();
        var blink = OwlDocument.Embedded.Animations.Single(a => a.Name == "walk").Frames[6].Grid;
        animator.SetGait(PetGait.Walk);

        var blinks = 0;
        for (var i = 0; i < 100; i++)
        {
            animator.Advance();
            if (ReferenceEquals(animator.CurrentGrid, blink)) blinks++;
        }

        // 100틱이면 서너 번. 여덟 칸을 통째로 돌렸다면 열두 번이 넘는다.
        Assert.InRange(blinks, 2, 5);
    }
}
