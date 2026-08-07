using DongCSU.Core.Owl;

namespace DongCSU.Core.Tests;

/// <summary>
/// 손에 들려 있는 동안.
///
/// **자세는 프레임을 돌려서 만들지 않는다.** 끄는 속도로 만든다 — 가만히 잡고만 있으면
/// 매달린 채로 멈춰 있고, 옮기는 방향에 따라 몸이 처지고 날개가 움직인다. 잡기만 해도
/// 계속 흔들리면 무엇 때문에 움직이는 건지 알 수 없다.
/// </summary>
public class DizzyDragTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static OwlAnimator Animator() => new(OwlDocument.Embedded, new Random(1));

    private static OwlAnimation Animation(string name) =>
        OwlDocument.Embedded.Animations.Single(a => a.Name == name);

    /// <summary>매달린 자세 하나를 글자로. 얼굴은 한 틱 전, 발은 두 틱 전 기울기를 따른다.</summary>
    private static string[] Carried(
        int lean, int face, int feet, OwlEyes eyes = OwlEyes.Open, OwlWings wings = OwlWings.Droop) =>
        OwlComposer.Compose(OwlDocument.Embedded, new OwlPose
        {
            Eyes = eyes,
            Wings = wings,
            Feet = OwlFeet.Dangle,
            Lean = lean,
            FaceLean = face - lean,
            FeetLean = feet,
            Bob = 0,
        });

    [Fact]
    public void 끌리는_중에_어지러우면_자세는_끌린_그대로다()
    {
        var animator = Animator();
        animator.IsDragged = true;

        animator.IsDizzy = true;

        Assert.Equal("dragged", animator.Animation.Name);
    }

    /// <summary>몸은 매달린 그대로고 **눈만** 바뀐다.</summary>
    [Fact]
    public void 끌리는_중에_어지러우면_눈만_바뀐다()
    {
        var animator = Animator();
        animator.IsDragged = true;
        Assert.Equal(Carried(0, 0, 0), animator.CurrentGrid);

        animator.IsDizzy = true;

        Assert.Equal(Carried(0, 0, 0, OwlEyes.Dizzy), animator.CurrentGrid);
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
    /// **잡고만 있으면 가만히 매달려 있다.** 사용자가 본 증상이 이것이다 — 잡기만 해도
    /// 계속 흔들렸다.
    /// </summary>
    [Fact]
    public void 잡고만_있으면_자세가_그대로다()
    {
        var animator = Animator();
        animator.IsDragged = true;
        var still = animator.CurrentGrid;

        var now = Start;
        for (var i = 0; i < 12; i++)
        {
            now += OwlAnimator.DragTick;
            animator.Advance(now);
            Assert.Equal(still, animator.CurrentGrid);
        }
    }

    /// <summary>오른쪽으로 빠르게 끌면 몸이 **왼쪽으로** 처진다. 손보다 늦게 따라오기 때문이다.</summary>
    [Fact]
    public void 오른쪽으로_끌면_몸이_왼쪽으로_처진다()
    {
        var animator = Animator();
        animator.IsDragged = true;

        var now = Start;
        animator.SetDragVelocity(400, 0, now);
        now += OwlAnimator.DragTick;
        animator.Advance(now);

        Assert.Equal(Carried(lean: -1, face: 0, feet: 0), animator.CurrentGrid);
    }

    /// <summary>느리게 옮기는 것만으로는 안 처진다. 자리를 잡으려고 미는 동안 흔들리면 성가시다.</summary>
    [Fact]
    public void 느리게_옮기면_그냥_매달려_있다()
    {
        var animator = Animator();
        animator.IsDragged = true;

        var now = Start;
        animator.SetDragVelocity(100, 0, now);
        now += OwlAnimator.DragTick;
        animator.Advance(now);

        Assert.Equal(Carried(0, 0, 0), animator.CurrentGrid);
    }

    /// <summary>들어 올리면 날개를 든다. 세게 내리면 활짝 편다.</summary>
    [Theory]
    [InlineData(300, OwlWings.Lift)]
    [InlineData(-300, OwlWings.Lift)]
    [InlineData(-900, OwlWings.Spread)]
    [InlineData(50, OwlWings.Droop)]
    public void 세로_속도가_날개를_정한다(double vertical, OwlWings expected)
    {
        var animator = Animator();
        animator.IsDragged = true;

        var now = Start;
        animator.SetDragVelocity(0, vertical, now);
        now += OwlAnimator.DragTick;
        animator.Advance(now);

        Assert.Equal(Carried(0, 0, 0, OwlEyes.Open, expected), animator.CurrentGrid);
    }

    /// <summary>
    /// **마우스가 멈추면 매달린 자세로 돌아온다.** 멈추면 이벤트가 끊겨서 속도가 옛 값으로
    /// 남는데, 그걸 그대로 쓰면 손을 세워 둔 채로 영영 처져 있는다.
    /// </summary>
    [Fact]
    public void 마우스가_멈추면_매달린_자세로_돌아온다()
    {
        var animator = Animator();
        animator.IsDragged = true;

        var now = Start;
        animator.SetDragVelocity(400, 0, now);
        now += OwlAnimator.DragTick;
        animator.Advance(now);
        Assert.NotEqual(Carried(0, 0, 0), animator.CurrentGrid);

        // 속도를 더 넣지 않는다. 한 틱만 지나도 0.13초를 넘겨 몸은 바로 선다.
        // 다만 얼굴과 발이 한 틱·두 틱 늦게 따라오므로 다 가라앉는 데 세 틱이 든다.
        for (var i = 0; i < 3; i++)
        {
            now += OwlAnimator.DragTick;
            animator.Advance(now);
        }

        Assert.Equal(Carried(0, 0, 0), animator.CurrentGrid);
    }

    /// <summary>
    /// **우리가 만든 매달린 자세가 맥이 뽑아 둔 것과 글자 하나까지 같아야 한다.**
    ///
    /// <c>owl.json</c> 의 <c>dragged</c> 프레임들은 맥이 같은 규칙으로 만든 결과다.
    /// 기울기 세 개(몸·얼굴·발)에서 그 프레임이 나오면 옮겨 적은 규칙이 맞는 것이다.
    /// </summary>
    [Theory]
    [InlineData(0, -1, 0, 0)]
    [InlineData(1, -1, -1, 0)]
    [InlineData(2, 0, -1, -1)]
    [InlineData(3, 1, 0, -1)]
    public void 매달린_자세가_맥이_뽑아둔_것과_같다(int frame, int lean, int face, int feet)
    {
        Assert.Equal(Animation("dragged").Frames[frame].Grid, Carried(lean, face, feet));
    }

    /// <summary>날개를 든 칸과 편 칸도 대조한다.</summary>
    [Theory]
    [InlineData(6, OwlWings.Lift)]
    [InlineData(7, OwlWings.Spread)]
    public void 날개_자세도_맥이_뽑아둔_것과_같다(int frame, OwlWings wings)
    {
        Assert.Equal(
            Animation("dragged").Frames[frame].Grid,
            Carried(0, 0, 0, OwlEyes.Open, wings));
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
