using DongCSU.Core.Owl;
using DongCSU.Core.Pet;

namespace DongCSU.Core.Tests;

/// <summary>
/// 어느 창 어느 테두리에 붙을지 고르는 규칙.
///
/// <b>화면 없이 굳힌다</b> — 창 목록을 지어 넣으면 앱을 안 띄우고도 "가려진 테두리에
/// 안 붙는지" 를 확인할 수 있다. 진짜 창을 조사하는 쪽은 <c>App</c> 이라 여기가 안 닿고,
/// 거기는 <c>--probe-perch windows</c> 가 본다.
///
/// <b>맥과 세로가 뒤집혀 있다.</b> 맥은 y 가 위로 크고 우리는 아래로 큰다 — 위 테두리에
/// 앉는 것과 아래 테두리에 매달리는 것이 부호로 갈리므로, 여기가 그 뒤집기를 굳히는
/// 자리이기도 하다.
/// </summary>
public class PerchFinderTests
{
    /// <summary>배율 1의 펫 마스코트와 같은 자리수.</summary>
    private const double Width = 85;
    private const double Height = 84;

    private static readonly PetRect Window = new(400, 300, 800, 600);
    private static readonly PerchWindow Big = new(1, Window, "가짜창");
    private static readonly IReadOnlyList<PerchWindow> One = [Big];

    private static double Limit => PerchFinder.SnapDistance(Width, Height);

    private static PetRect At(double x, double y) => new(x, y, Width, Height);

    [Fact]
    public void 위_테두리에_앉는다()
    {
        // 발이 창 위 테두리에 닿는다 — 그림은 테두리 **위**에 있다.
        var spot = PerchFinder.Snap(At(760, Window.Y - Height), Limit, One);

        Assert.NotNull(spot);
        Assert.Equal(MascotPerch.Top, spot.Value.Edge);
    }

    [Fact]
    public void 아래_테두리에_매달린다()
    {
        // 손이 창 아래 테두리에 닿는다 — 그림은 테두리 **아래**에 있다.
        var spot = PerchFinder.Snap(At(760, Window.Bottom), Limit, One);

        Assert.NotNull(spot);
        Assert.Equal(MascotPerch.Bottom, spot.Value.Edge);
    }

    [Theory]
    [InlineData(true, MascotPerch.Right)]
    [InlineData(false, MascotPerch.Left)]
    public void 좌우_테두리를_껴안는다(bool right, MascotPerch expected)
    {
        var x = right ? Window.Right : Window.X - Width;
        var spot = PerchFinder.Snap(At(x, 560), Limit, One);

        Assert.NotNull(spot);
        Assert.Equal(expected, spot.Value.Edge);
    }

    [Fact]
    public void 멀리_놓으면_안_붙는다()
    {
        Assert.Null(PerchFinder.Snap(At(760, Window.Y - Height - Limit - 10), Limit, One));
    }

    /// <summary>
    /// 가로로 창을 벗어난 자리에는 안 붙는다. <b>그림 한가운데가 기준이다.</b>
    /// </summary>
    [Fact]
    public void 창_옆으로_비껴나면_안_붙는다()
    {
        Assert.Null(PerchFinder.Snap(At(100, Window.Y - Height), Limit, One));
    }

    /// <summary>
    /// <b>모서리 끝을 겨냥해도 안 튄다.</b>
    ///
    /// 오프셋을 여기서 안 가두면 그림 절반이 창 밖으로 나간 자리에 그대로 앉고, 뒤이은
    /// 첫 추적 틱이 안으로 밀어 넣어 **곧바로 옆으로 튄다** — 미리보기와 착지는 맞았는데
    /// 곧 옮겨가는 것으로 보인다.
    /// </summary>
    [Fact]
    public void 모서리_끝을_겨냥해도_안_튄다()
    {
        var spot = PerchFinder.Snap(At(Window.X - Width / 2, Window.Y - Height), Limit, One);

        Assert.NotNull(spot);
        Assert.Equal(Width / 2, spot.Value.Offset, 3);
    }

    /// <summary>
    /// <b>가려진 테두리에는 안 붙는다.</b> 목록이 앞에서 뒤 순서라 자기보다 앞에 있는
    /// 창만 본다 — 놓는 사람은 보이는 것에 겨냥한다.
    /// </summary>
    [Fact]
    public void 가려진_테두리에는_안_붙는다()
    {
        var cover = new PerchWindow(2, new PetRect(600, 100, 400, 400), "덮개");

        Assert.Null(PerchFinder.Snap(At(760, Window.Y - Height), Limit, [cover, Big]));
    }

    /// <summary>
    /// 대조군. <b>이게 없으면 위 검사가 늘 통과한다</b> — 애초에 안 붙는 자리를
    /// "가려서 안 붙었다" 로 읽을 수 있다.
    /// </summary>
    [Fact]
    public void 가리는_창이_없으면_붙는다()
    {
        Assert.NotNull(PerchFinder.Snap(At(760, Window.Y - Height), Limit, One));
    }

    /// <summary>덮개가 뒤에 있으면 안 가린다. 앞뒤 순서를 실제로 보는지 굳힌다.</summary>
    [Fact]
    public void 뒤에_있는_창은_안_가린다()
    {
        var behind = new PerchWindow(2, new PetRect(600, 100, 400, 400), "뒤창");

        Assert.NotNull(PerchFinder.Snap(At(760, Window.Y - Height), Limit, [Big, behind]));
    }

    /// <summary>모서리가 그림보다 짧으면 붙은 게 아니라 걸쳐진 것으로 보인다.</summary>
    [Fact]
    public void 모서리가_그림보다_짧으면_안_붙는다()
    {
        var narrow = new PerchWindow(3, new PetRect(400, 300, 60, 600), "좁은창");

        Assert.Null(PerchFinder.Snap(At(400, 300 - Height), Limit, [narrow]));
    }

    /// <summary>
    /// 놓을 수 없다고 하면 그 변을 건너뛴다. <b>안 물어보면 후보로 골라 놓고 못 놓는다</b> —
    /// 사용자 눈에는 아무 데도 안 붙는 것으로 보인다.
    /// </summary>
    [Fact]
    public void 놓을_수_없는_변은_건너뛴다()
    {
        var spot = PerchFinder.Snap(
            At(760, Window.Y - Height), Limit, One,
            placeable: candidate => candidate.Edge != MascotPerch.Top);

        Assert.Null(spot);
    }

    [Fact]
    public void 붙은_창을_찾고_맨_앞인지_안다()
    {
        var cover = new PerchWindow(2, new PetRect(0, 0, 100, 100), "앞창");

        Assert.True(PerchFinder.Locate(1, One)?.IsFront);
        Assert.False(PerchFinder.Locate(1, [cover, Big])?.IsFront);
        Assert.Null(PerchFinder.Locate(99, One));
    }

    /// <summary>
    /// 창이 좁아지면 오프셋이 안으로 끌려온다. 더 좁아져 모서리가 그림보다 짧아지면 null.
    /// </summary>
    [Fact]
    public void 창이_좁아지면_안으로_끌려온다()
    {
        var spot = new PerchSpot(1, MascotPerch.Top, 700, Window);

        var narrowed = spot.Clamped(new PetRect(400, 300, 200, 600), Width, Height);
        Assert.NotNull(narrowed);
        Assert.Equal(200 - Width / 2, narrowed.Value.Offset, 3);

        Assert.Null(spot.Clamped(new PetRect(400, 300, 60, 600), Width, Height));
    }

    /// <summary>
    /// 붙는 지점. <b>맥과 세로가 뒤집혀 있다</b> — 위 테두리가 <c>Y</c> 이고 아래가
    /// <c>Bottom</c> 이다. 여기가 뒤집히면 앉기와 매달리기가 통째로 바뀐다.
    /// </summary>
    [Fact]
    public void 붙는_지점이_변마다_맞다()
    {
        var window = new PetRect(400, 300, 800, 600);

        Assert.Equal(new PetPoint(500, 300), new PerchSpot(1, MascotPerch.Top, 100, window).Contact(window));
        Assert.Equal(new PetPoint(500, 900), new PerchSpot(1, MascotPerch.Bottom, 100, window).Contact(window));
        Assert.Equal(new PetPoint(400, 400), new PerchSpot(1, MascotPerch.Left, 100, window).Contact(window));
        Assert.Equal(new PetPoint(1200, 400), new PerchSpot(1, MascotPerch.Right, 100, window).Contact(window));
    }
}
