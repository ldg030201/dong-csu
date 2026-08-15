using DongCSU.Core.Owl;

namespace DongCSU.Core.Tests;

/// <summary>
/// 시트에서 어느 칸을 고르는지. <c>MascotSprite.resolve</c> 를 옮겨 온 것이라
/// 맥이 정해 둔 차례가 그대로 지켜져야 한다.
/// </summary>
public class MascotSheetTests
{
    private static MascotSprite Pick(
        OwlMood mood = OwlMood.Idle,
        OwlEyes eyes = OwlEyes.Open,
        PetGaitKind gait = PetGaitKind.Still,
        int beat = 0,
        OwlEyes resting = OwlEyes.Open) =>
        MascotSheet.Choose(mood, eyes, gait, beat, resting);

    // ── 차례 ────────────────────────────────────────────────────────

    [Fact]
    public void 끊기면_다른_무엇보다_먼저_죽은_칸이다()
    {
        Assert.Equal(MascotSprite.Dead, Pick(OwlMood.Offline, OwlEyes.Dizzy, PetGaitKind.Dragged));
    }

    /// <summary>
    /// 끌고 흔드는 동안에는 **기분이 아니라 눈으로 본다.** 기분은 끌림 그대로이고
    /// 눈만 풀리기 때문에, 기분을 보면 어지러운 칸이 영영 안 나온다.
    /// </summary>
    [Fact]
    public void 어지러움이_끌림보다_먼저다()
    {
        Assert.Equal(MascotSprite.Dizzy, Pick(eyes: OwlEyes.Dizzy, gait: PetGaitKind.Dragged));
    }

    [Fact]
    public void 끌리면_걸음보다_들린_칸이_먼저다()
    {
        Assert.Equal(MascotSprite.Held, Pick(OwlMood.Tired, gait: PetGaitKind.Dragged));
    }

    // ── 걸음 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, MascotSprite.WalkA)]
    [InlineData(1, MascotSprite.WalkB)]
    [InlineData(2, MascotSprite.WalkA)]
    [InlineData(3, MascotSprite.WalkB)]
    public void 걸음은_두_박자를_번갈아_쓴다(int beat, MascotSprite expected)
    {
        Assert.Equal(expected, Pick(gait: PetGaitKind.Walk, beat: beat));
    }

    /// <summary>쫓겨서 뛸 때는 뛰기 그림. 지친 몸으로 도망치는 것이라 졸림보다 앞선다.</summary>
    [Fact]
    public void 뛸_때는_졸려도_뛰기_그림이다()
    {
        Assert.Equal(MascotSprite.RunA, Pick(OwlMood.Tired, gait: PetGaitKind.Run));
    }

    [Fact]
    public void 졸리면_졸린_얼굴로_걷는다()
    {
        Assert.Equal(MascotSprite.WalkSleepyA, Pick(OwlMood.Tired, gait: PetGaitKind.Walk));
        Assert.Equal(MascotSprite.WalkSleepyB, Pick(OwlMood.Exhausted, gait: PetGaitKind.Walk, beat: 1));
    }

    // ── 깜빡임 ──────────────────────────────────────────────────────

    [Fact]
    public void 감은_얼굴이_있는_자세는_깜빡인다()
    {
        Assert.Equal(MascotSprite.Blink, Pick(eyes: OwlEyes.Closed));
        Assert.Equal(MascotSprite.BlinkSleepy, Pick(OwlMood.Tired, eyes: OwlEyes.Closed));
        Assert.Equal(MascotSprite.BlinkHeld, Pick(eyes: OwlEyes.Closed, gait: PetGaitKind.Dragged));
    }

    /// <summary>
    /// **완전히 감았을 때만 친다.** 그림에는 중간 단계가 없어서, 반쯤 감긴 칸까지 세면
    /// 깜빡임이 두 배 넘게 길어진다 — 잠깐 깜빡이는 게 아니라 질끈 감았다 뜨는 것이 된다.
    /// </summary>
    [Fact]
    public void 실눈은_깜빡임으로_안_친다()
    {
        Assert.Equal(MascotSprite.Idle, Pick(eyes: OwlEyes.Half));
    }

    /// <summary>
    /// 평소가 이미 감긴 기분(탈진)은 깜빡일 것이 없다. 거기서 실눈을 뜨는 것은
    /// 감는 게 아니라 뜨는 것이다.
    /// </summary>
    [Fact]
    public void 평소가_감긴_기분은_깜빡이지_않는다()
    {
        Assert.Equal(
            MascotSprite.Exhausted,
            Pick(OwlMood.Exhausted, eyes: OwlEyes.Closed, resting: OwlEyes.Closed));
    }

    /// <summary>
    /// 옆모습이라 눈이 점 하나 크기고, 화면에서는 40pt 남짓으로 줄어서 감았는지
    /// 떴는지 보이지 않는다. 그 넉 장을 안 그리므로 여기서도 안 고른다.
    /// </summary>
    [Fact]
    public void 걸을_때는_깜빡이지_않는다()
    {
        Assert.Equal(MascotSprite.WalkA, Pick(eyes: OwlEyes.Closed, gait: PetGaitKind.Walk));
        Assert.Equal(MascotSprite.RunA, Pick(eyes: OwlEyes.Closed, gait: PetGaitKind.Run));
    }

    [Fact]
    public void 어지러움과_죽음은_눈_자체가_정보라_안_깜빡인다()
    {
        Assert.Equal(MascotSprite.Dizzy, Pick(eyes: OwlEyes.Dizzy));
        Assert.Equal(MascotSprite.Dead, Pick(OwlMood.Offline, eyes: OwlEyes.Closed));
    }

    // ── 애니메이터와 이어져 있는지 ──────────────────────────────────

    /// <summary>
    /// 위의 것들이 다 맞아도 애니메이터가 눈을 안 넘겨주면 화면에서는 영영 안 깜빡인다.
    /// 실제로 프레임을 돌려서 감은 칸이 나오는지 본다.
    /// </summary>
    [Fact]
    public void 프레임을_돌리면_깜빡이는_칸이_나온다()
    {
        var animator = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        animator.SetMood(OwlMood.Idle);

        var seen = new HashSet<MascotSprite>();
        for (var i = 0; i < 24; i++)
        {
            seen.Add(animator.MascotFrame);
            animator.Advance();
        }

        Assert.Contains(MascotSprite.Idle, seen);
        Assert.Contains(MascotSprite.Blink, seen);
    }

    /// <summary>
    /// 탈진한 채로 집어 들면 **매달린 얼굴은 눈을 뜨고 있다.** 기분으로 기준 눈을
    /// 보면 여기서 깜빡임이 막힌다 — 걸러야 할 것은 "탈진해서 감고 있음"이지
    /// "탈진한 적이 있음"이 아니다.
    /// </summary>
    [Fact]
    public void 탈진한_채로_들려_있어도_깜빡인다()
    {
        var animator = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        animator.SetMood(OwlMood.Exhausted);
        animator.IsDragged = true;

        var seen = new HashSet<MascotSprite>();
        for (var i = 0; i < 60; i++)
        {
            seen.Add(animator.MascotFrame);
            animator.Advance();
        }

        Assert.Contains(MascotSprite.BlinkHeld, seen);
    }

    /// <summary>탈진은 평소가 이미 감긴 얼굴이라, 한 바퀴를 다 돌려도 깜빡이는 칸이 없다.</summary>
    [Fact]
    public void 탈진은_프레임을_돌려도_안_깜빡인다()
    {
        var animator = new OwlAnimator(OwlDocument.Embedded, new Random(1));
        animator.SetMood(OwlMood.Exhausted);

        for (var i = 0; i < 24; i++)
        {
            Assert.Equal(MascotSprite.Exhausted, animator.MascotFrame);
            animator.Advance();
        }
    }
}
