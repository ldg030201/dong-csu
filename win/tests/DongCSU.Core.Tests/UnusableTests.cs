using DongCSU.Core.Owl;

namespace DongCSU.Core.Tests;

/// <summary>
/// 주간 한도를 다 썼을 때.
///
/// **색만 빼는 게 아니라 통째로 멈춘다.** 죽은 것으로 보이게 해 놓고 걷거나 버둥거리거나
/// 눈을 깜빡이면 아직 살아 있는 것으로 읽힌다.
/// </summary>
public class UnusableTests
{
    private static OwlAnimator Spent()
    {
        var animator = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        animator.SetMood(OwlMood.Exhausted);
        animator.IsUnusable = true;
        return animator;
    }

    /// <summary>다음 틱이 없다. null 을 돌려주면 부르는 쪽이 타이머를 안 건다.</summary>
    [Fact]
    public void 프레임을_더_넘기지_않는다()
    {
        Assert.Null(Spent().Advance());
    }

    [Fact]
    public void 눈을_깜빡이지_않는다()
    {
        var animator = Spent();

        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(MascotSprite.Dead, animator.MascotFrame);
            animator.Advance();
        }
    }

    /// <summary>집어 들어도 버둥거리지 않는다. 죽은 부엉이는 매달려도 가만히 있다.</summary>
    [Fact]
    public void 집어_들어도_자세가_안_바뀐다()
    {
        var animator = Spent();
        var before = animator.CurrentGrid;

        animator.IsDragged = true;

        Assert.Equal(before, animator.CurrentGrid);
        Assert.Equal(MascotSprite.Dead, animator.MascotFrame);
        Assert.Null(animator.Advance());
    }

    /// <summary>흔들어도 어지러워하지 않는다. 죽은 것에는 아무 반응이 없다.</summary>
    [Fact]
    public void 흔들어도_어지러워하지_않는다()
    {
        var animator = Spent();
        var before = animator.CurrentGrid;

        animator.IsDizzy = true;

        Assert.Equal(before, animator.CurrentGrid);
        Assert.Equal(MascotSprite.Dead, animator.MascotFrame);
    }

    /// <summary>
    /// 다 쓰기 전에 걷던 중이었어도 멈춘다. 걸음을 켜 둔 채로 이 상태가 되는 일이
    /// 실제로 있다 — 주간 한도는 걷는 도중에 넘어간다.
    /// </summary>
    [Fact]
    public void 걷던_중에_다_써도_멈춘다()
    {
        var animator = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        animator.SetMood(OwlMood.Tired);
        animator.SetGait(DongCSU.Core.Pet.PetGait.Walk);

        animator.IsUnusable = true;

        Assert.Null(animator.Advance());
        Assert.Equal(MascotSprite.Dead, animator.MascotFrame);
    }

    /// <summary>끄면 다시 돈다. 다음 주가 되면 살아나야 한다.</summary>
    [Fact]
    public void 다시_쓸_수_있게_되면_되살아난다()
    {
        var animator = Spent();
        Assert.Null(animator.Advance());

        animator.IsUnusable = false;

        Assert.NotNull(animator.Advance());
        Assert.NotEqual(MascotSprite.Dead, animator.MascotFrame);
    }

    /// <summary>색은 그대로 회색이다. 자세가 굳었다고 색이 돌아오면 다시 쓸 수 있게 된 것으로 읽힌다.</summary>
    [Fact]
    public void 색은_회색_그대로다()
    {
        Assert.Equal("offline", Spent().PaletteName);
    }
}
