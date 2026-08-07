using DongCSU.Core.Owl;

namespace DongCSU.Core.Tests;

/// <summary>
/// 손에 들린 채로 어지러울 때.
///
/// **흔드는 그 자리에서 눈이 풀려야 한다** — 놓아야 보이면 흔든 보람이 없다.
/// 다만 몸은 매달린 자세 그대로다. 통째로 비틀거리는 그림으로 갈아타면 허공에서
/// 휘청이는 꼴이라 무엇이 흔들리는 건지 알 수 없다.
/// </summary>
public class DizzyDragTests
{
    private static OwlAnimator Animator() => new(OwlDocument.Embedded, new Random(1));

    private static OwlAnimation Animation(string name) =>
        OwlDocument.Embedded.Animations.Single(a => a.Name == name);

    [Fact]
    public void 끌리는_중에_어지러우면_자세는_끌린_그대로다()
    {
        var animator = Animator();
        animator.IsDragged = true;

        animator.IsDizzy = true;

        Assert.Equal("dragged", animator.Animation.Name);
    }

    /// <summary>몸은 그대로고 **눈만** 바뀐다. 그림이 달라지긴 해야 한다.</summary>
    [Fact]
    public void 끌리는_중에_어지러우면_눈만_바뀐다()
    {
        var animator = Animator();
        animator.IsDragged = true;
        var calm = animator.CurrentGrid;

        animator.IsDizzy = true;
        var woozy = animator.CurrentGrid;

        Assert.NotEqual(calm, woozy);

        // 같은 자세를 어지러운 눈으로 합성한 것과 글자 단위로 같아야 한다.
        var pose = Animation("dragged").Frames[0].Pose with { Eyes = OwlEyes.Dizzy };
        Assert.Equal(OwlComposer.Compose(OwlDocument.Embedded, pose), woozy);
    }

    /// <summary>손에서 놓으면 그때는 통째로 비틀거리는 그림으로 간다.</summary>
    [Fact]
    public void 놓으면_비틀거리는_그림으로_간다()
    {
        var animator = Animator();
        animator.IsDragged = true;
        animator.IsDizzy = true;

        animator.IsDragged = false;

        Assert.Equal("dizzy", animator.Animation.Name);
        Assert.Equal(Animation("dizzy").Frames[0].Grid, animator.CurrentGrid);
    }

    /// <summary>
    /// **끌리는 중에 어지러워져도 프레임을 되감지 않는다.** 되감으면 흔드는 도중에
    /// 몸이 툭 튄다 — 자세가 바뀌는 게 아니라 눈만 바뀌는 것이라 이어져야 한다.
    /// </summary>
    [Fact]
    public void 끌리는_중에는_어지러워져도_되감지_않는다()
    {
        var animator = Animator();
        animator.IsDragged = true;
        animator.Advance();
        animator.Advance();
        var poseBefore = Animation("dragged").Frames[2].Pose;

        animator.IsDizzy = true;

        Assert.Equal(
            OwlComposer.Compose(OwlDocument.Embedded, poseBefore with { Eyes = OwlEyes.Dizzy }),
            animator.CurrentGrid);
    }

    /// <summary>끌리지 않을 때는 그대로다 — 어지러움이 기분·걸음을 이긴다.</summary>
    [Fact]
    public void 끌리지_않으면_어지러움이_기분을_이긴다()
    {
        var animator = Animator();
        animator.SetMood(OwlMood.Exhausted);

        animator.IsDizzy = true;

        Assert.Equal("dizzy", animator.Animation.Name);
    }
}
