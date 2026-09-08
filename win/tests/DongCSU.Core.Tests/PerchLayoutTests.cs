using DongCSU.Core.Owl;
using DongCSU.Core.Pet;

namespace DongCSU.Core.Tests;

/// <summary>
/// 붙었을 때 창을 어디에 놓을지.
///
/// <b>여기가 뒤집히면 절반이 반대로 나온다.</b> 네 변마다 부호가 다르고, 맥과는 세로가
/// 뒤집혀 있어서 옮겨 적을 때 제일 틀리기 쉬운 자리다 — 그래서 변마다 하나씩 굳힌다.
/// </summary>
public class PerchLayoutTests
{
    /// <summary>
    /// 그림이 창 안에서 덮는 자리. 창(128×160) 안에 84 높이 그림이 위쪽 가운데에 있는 꼴이다.
    /// </summary>
    private static readonly PetRect Ink = new(21, 22, 85, 84);

    /// <summary>깊이를 안 먹인 자리를 재는 검사에서 쓴다.</summary>
    private const double NoDepth = 0;

    [Fact]
    public void 창_위에_앉으면_그림_바닥이_테두리에_온다()
    {
        var contact = new PetPoint(800, 300);

        var origin = PerchLayout.Origin(MascotPerch.Top, contact, Ink, NoDepth, work: null);

        Assert.NotNull(origin);
        // 그림 바닥(Ink.Bottom = 106)이 접점 y 에 오도록 창을 위로 민다.
        Assert.Equal(300 - 106, origin.Value.Y, 3);
        // 가로는 그림 한가운데가 접점에 온다.
        Assert.Equal(800 - Ink.Center.X, origin.Value.X, 3);
    }

    [Fact]
    public void 창_아래에_매달리면_그림_머리가_테두리에_온다()
    {
        var contact = new PetPoint(800, 900);

        var origin = PerchLayout.Origin(MascotPerch.Bottom, contact, Ink, NoDepth, work: null);

        Assert.NotNull(origin);
        // 그림 머리(Ink.Y = 22)가 접점 y 에 오도록 창을 아래로 민다.
        Assert.Equal(900 - 22, origin.Value.Y, 3);
    }

    [Fact]
    public void 오른쪽_테두리에서는_그림_왼쪽이_닿는다()
    {
        var contact = new PetPoint(1200, 560);

        var origin = PerchLayout.Origin(MascotPerch.Right, contact, Ink, NoDepth, work: null);

        Assert.NotNull(origin);
        Assert.Equal(1200 - Ink.X, origin.Value.X, 3);
        Assert.Equal(560 - Ink.Center.Y, origin.Value.Y, 3);
    }

    [Fact]
    public void 왼쪽_테두리에서는_그림_오른쪽이_닿는다()
    {
        var contact = new PetPoint(400, 560);

        var origin = PerchLayout.Origin(MascotPerch.Left, contact, Ink, NoDepth, work: null);

        Assert.NotNull(origin);
        Assert.Equal(400 - Ink.Right, origin.Value.X, 3);
    }

    /// <summary>
    /// <b>붙잡는 부위만 창 안으로 넣는다.</b> 깊이를 주면 그만큼 더 창 쪽으로 들어간다 —
    /// 0 이면 붙잡는 부위가 선에 닿기만 하고 넘어가질 않아서, 옆에 붙었을 때 껴안은 것이
    /// 아니라 벽에 부딪친 것으로 보인다.
    /// </summary>
    [Fact]
    public void 깊이만큼_창_안으로_들어간다()
    {
        var contact = new PetPoint(800, 300);

        var flat = PerchLayout.Origin(MascotPerch.Top, contact, Ink, 0, work: null);
        var sunk = PerchLayout.Origin(MascotPerch.Top, contact, Ink, 12, work: null);

        Assert.NotNull(flat);
        Assert.NotNull(sunk);
        // 창 위에 앉는 것은 y 가 커지는 쪽(아래)으로 12 만큼 내려간다.
        Assert.Equal(flat.Value.Y + 12, sunk.Value.Y, 3);
    }

    /// <summary>
    /// <b>그림이 화면 밖으로 나가면 붙지 않는다.</b> 화면 안으로 밀어 넣지 않는 이유는
    /// 밀어 넣으면 테두리에서 떨어진 자리에서 붙은 척을 하기 때문이다.
    /// </summary>
    [Fact]
    public void 화면_밖으로_나가면_안_붙는다()
    {
        var work = new PetRect(0, 0, 1920, 1032);

        // 창 위 테두리가 화면 꼭대기에 딱 붙어 있으면 그 위에 설 자리가 없다.
        Assert.Null(PerchLayout.Origin(MascotPerch.Top, new PetPoint(800, 0), Ink, 0, work));
        // 넉넉하면 붙는다 — 대조군.
        Assert.NotNull(PerchLayout.Origin(MascotPerch.Top, new PetPoint(800, 400), Ink, 0, work));
    }

    /// <summary>깊이 비율은 잉크에 곱해진다. 배율을 키우면 잠기는 양도 같이 커져야 한다.</summary>
    [Fact]
    public void 깊이는_잉크에_대한_비율이다()
    {
        var contact = new PetPoint(800, 400);

        // 가로 테두리는 잉크 **세로**(84)를 잰다.
        Assert.Equal(84 * 0.15, PerchLayout.Sink(MascotPerch.Top, contact, Ink, 0.15, null), 3);
        // 세로 테두리는 잉크 **가로**(85)를 잰다.
        Assert.Equal(85 * 0.25, PerchLayout.Sink(MascotPerch.Left, contact, Ink, 0.25, null), 3);
    }

    /// <summary>
    /// <b>자리가 모자라면 그만큼 더 깊이 앉는다.</b> 창의 테두리가 화면 끝에 가까우면
    /// 그 밖에 설 자리가 없다 — 모자란 만큼만 창 안으로 더 넣으면 붙는다.
    ///
    /// <b>여기가 부호를 제일 뒤집기 쉬운 자리다</b> — 맥은 <c>visible.maxY - contact.y</c>
    /// 인데 우리는 <c>contact.Y - visible.Y</c> 라, 한 줄만 그대로 옮겨 적으면 그 변에서만
    /// 조용히 틀린다. 그래서 네 변을 다 돈다.
    /// </summary>
    [Theory]
    // 위 테두리 — 화면 **꼭대기**. 창 밖에 남는 몫이 84 × 0.85 = 71.4 인데 65 밖에 없다.
    [InlineData(MascotPerch.Top, 800.0, 65.0, 84.0)]
    // 아래 테두리 — 화면 **바닥** 쪽에 자리가 모자라다.
    [InlineData(MascotPerch.Bottom, 800.0, 967.0, 84.0)]
    // 오른쪽 테두리 — 화면 **오른쪽**. 세로 테두리는 잉크 **가로**(85)를 잰다.
    [InlineData(MascotPerch.Right, 1855.0, 500.0, 85.0)]
    // 왼쪽 테두리 — 화면 **왼쪽**.
    [InlineData(MascotPerch.Left, 65.0, 500.0, 85.0)]
    public void 네_변_다_자리가_모자라면_조금_더_넣는다(
        MascotPerch perch, double x, double y, double span)
    {
        var work = new PetRect(0, 0, 1920, 1032);

        var sink = PerchLayout.Sink(perch, new PetPoint(x, y), Ink, 0.15, work);

        Assert.True(sink > span * 0.15, $"{perch}: 모자란데 기본값 그대로다 ({sink})");
        Assert.True(
            sink <= span * (0.15 + AppSettings.MaxAutoPerchExtra) + 0.001,
            $"{perch}: 한계보다 많이 넣었다 ({sink})");
    }

    /// <summary>
    /// 네 변 모두 <b>화면 한가운데에서는</b> 기본값 그대로다. 위 검사의 대조군이다 —
    /// 부호가 뒤집혀 있으면 "늘 모자라다" 가 되어 위 검사만으로는 통과해 버린다.
    /// </summary>
    [Theory]
    [InlineData(MascotPerch.Top, 84.0)]
    [InlineData(MascotPerch.Bottom, 84.0)]
    [InlineData(MascotPerch.Right, 85.0)]
    [InlineData(MascotPerch.Left, 85.0)]
    public void 네_변_다_한가운데서는_기본값이다(MascotPerch perch, double span)
    {
        var work = new PetRect(0, 0, 1920, 1032);

        Assert.Equal(
            span * 0.15,
            PerchLayout.Sink(perch, new PetPoint(960, 516), Ink, 0.15, work),
            3);
    }

    /// <summary>
    /// 그림이 덮을 자리. <b>가림 판정이 이걸로 잰다</b> — 실제 자리와 같은 깊이를 봐야
    /// 미리보기와 착지가 안 갈린다.
    /// </summary>
    [Fact]
    public void 덮을_자리가_실제_자리와_맞는다()
    {
        var window = new PetRect(400, 300, 800, 600);
        var spot = new PerchSpot(1, MascotPerch.Top, 400, window);

        var landing = PerchFinder.LandingArea(spot, 85, 84, sink: 12);

        // 접점 (800, 300) 에서 바닥이 12 만큼 창 안으로.
        Assert.Equal(800 - 85 / 2.0, landing.X, 3);
        Assert.Equal(300 + 12 - 84, landing.Y, 3);
    }

    /// <summary>
    /// 창 테두리 선이 지나는 자리. <b>화면 쪽 두 곳이 이걸 따로 적고 있었다</b> —
    /// 끌 때 보여주는 막대(<c>PerchHint</c>)와 붙어 있는 동안 마우스를 흘려보내는
    /// 판정(<c>HudWindow.OnHitTest</c>)이다. 사용자가 눈으로 맞대 보는 유일한 짝이라
    /// 갈리면 바로 보인다 — 막대가 있는 자리에서 클릭이 안 넘어간다.
    /// </summary>
    [Theory]
    // 위에 앉으면 그림 **바닥**(106)에서 sink 만큼 위.
    [InlineData(MascotPerch.Top, 106.0 - 12)]
    // 아래에 매달리면 그림 **머리**(22)에서 sink 만큼 아래.
    [InlineData(MascotPerch.Bottom, 22.0 + 12)]
    // 오른쪽 테두리는 그림 **왼쪽**(21)에서 안으로.
    [InlineData(MascotPerch.Right, 21.0 + 12)]
    // 왼쪽 테두리는 그림 **오른쪽**(106)에서 안으로.
    [InlineData(MascotPerch.Left, 106.0 - 12)]
    public void 테두리_선이_그림_끝에서_깊이만큼_안쪽이다(MascotPerch perch, double expected)
    {
        Assert.Equal(expected, PerchLayout.BorderLine(perch, Ink, sink: 12), 3);
    }

    /// <summary>
    /// 선을 넘은 쪽이 남의 창이다. <b>변마다 넘어간 쪽이 반대다</b> — 위에 앉으면 선
    /// 아래가, 아래에 매달리면 선 위가 남의 창이라, 부호가 뒤집히면 <b>정확히 반대쪽</b>
    /// 마우스를 흘려보낸다. 그러면 몸통을 못 잡고 남의 창 제목 줄만 잡힌다.
    /// </summary>
    [Theory]
    // 위에 앉는다 → 선(94) 아래가 남의 창.
    [InlineData(MascotPerch.Top, 60.0, 100.0, true)]
    [InlineData(MascotPerch.Top, 60.0, 80.0, false)]
    // 아래에 매달린다 → 선(34) 위가 남의 창.
    [InlineData(MascotPerch.Bottom, 60.0, 20.0, true)]
    [InlineData(MascotPerch.Bottom, 60.0, 50.0, false)]
    // 오른쪽 테두리 → 선(33) 왼쪽이 남의 창.
    [InlineData(MascotPerch.Right, 20.0, 60.0, true)]
    [InlineData(MascotPerch.Right, 50.0, 60.0, false)]
    // 왼쪽 테두리 → 선(94) 오른쪽이 남의 창.
    [InlineData(MascotPerch.Left, 100.0, 60.0, true)]
    [InlineData(MascotPerch.Left, 80.0, 60.0, false)]
    public void 선_너머는_남의_창이다(MascotPerch perch, double x, double y, bool crosses)
    {
        Assert.Equal(
            crosses,
            PerchLayout.CrossesBorder(perch, Ink, sink: 12, new PetPoint(x, y)));
    }
}
