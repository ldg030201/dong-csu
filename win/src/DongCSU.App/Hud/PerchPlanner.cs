using System.Windows;
using DongCSU.Core;
using DongCSU.Core.Owl;
using DongCSU.Core.Pet;

namespace DongCSU.App.Hud;

/// <summary>
/// 그림 사정과 화면 사정을 이어 붙여 <b>어디에 붙고 창을 어디에 놓을지</b> 정한다.
///
/// <c>Core</c> 의 <see cref="PerchFinder"/>·<see cref="PerchLayout"/> 은 잉크 상자를
/// 숫자로만 받는다 — 그림에서 그 숫자를 재는 것은 <c>App</c> 의 일이라 여기가 그 자리다.
///
/// <b>앱과 진단이 같은 것을 쓴다.</b> 맥 <c>ProbePerch</c> 가 앱과 같은
/// <c>snap</c>·<c>petPerchOrigin</c> 을 부르는 것과 같은 이유다 — 따로 셈하면 표에는
/// "가능" 이 뜨는데 실제로는 안 붙는 자리가 생긴다. 맥에서 실제로 그랬다.
/// </summary>
internal sealed class PerchPlanner(HudView view, AppSettings settings, PetRect? work)
{
    /// <summary>
    /// 놓은 자리에서 붙을 자리를 고른다. 붙을 데가 없으면 null.
    /// </summary>
    /// <param name="origin">지금 창의 왼쪽 위(화면 좌표).</param>
    /// <param name="windows">창 목록. <b>앞에 있는 것이 먼저 와야 한다.</b></param>
    public PerchSpot? Snap(Point origin, IReadOnlyList<PerchWindow> windows)
    {
        if (MascotRect(origin) is not { } mascot) return null;

        return PerchFinder.Snap(
            mascot,
            PerchFinder.SnapDistance(mascot.Width, mascot.Height),
            windows,
            // **자리와 가림이 같은 깊이를 봐야 한다.** 두 곳에서 따로 고르면 미리보기와
            // 실제 착지가 갈린다.
            sink: spot => Sink(spot),
            placeable: spot => Origin(spot) is not null);
    }

    /// <summary>
    /// <see cref="Snap"/> 이 <b>어디서 무엇을 걸러냈는지</b> 한 줄씩. 붙을 데를 못 찾은
    /// 뒤에 부른다.
    ///
    /// <b>고르는 것과 같은 것을 엮어서 넘긴다.</b> 거리 문턱도 깊이도 놓을 수 있는지도
    /// <see cref="Snap"/> 이 쓰는 그대로다 — 부르는 쪽마다 이 한 벌을 다시 적으면,
    /// 새 걸러내기가 하나 늘 때 표와 기록만 옛 규칙으로 "가능" 이라고 말한다.
    /// </summary>
    public IReadOnlyList<string> Explain(Point origin, IReadOnlyList<PerchWindow> windows)
    {
        if (MascotRect(origin) is not { } mascot) return ["그림을 못 읽었다"];

        return PerchFinder.Explain(
            mascot,
            PerchFinder.SnapDistance(mascot.Width, mascot.Height),
            windows,
            sink: Sink,
            placeable: spot => Origin(spot) is not null);
    }

    /// <summary>
    /// 그림이 덮는 자리(화면 좌표). 시트 그림이 아니면 null.
    ///
    /// <b>자세를 안 본다.</b> 모든 칸을 묶은 상자 하나로 잰다 — 맥
    /// <c>mascotScreenRect</c> 가 <c>petMascotRect</c>(자세와 무관한 상자)를 쓰는 것과
    /// 같다.
    ///
    /// <b>자세마다 다르게 재면 미리보기가 떨린다.</b> 끌고 가다 닿으면 자세가 붙는 칸으로
    /// 바뀌는데, 그 칸이 더 좁으면 그 자리에서 다시 "안 닿음" 이 되고, 그러면 자세가
    /// 되돌아가 또 닿는다 — 30Hz 로 깜빡인다. 잉크는 <b>어디에 놓을지</b>(<see cref="Origin"/>)
    /// 에만 쓴다.
    /// </summary>
    public PetRect? MascotRect(Point origin)
    {
        if (view.PetMascotBoxRect() is not { } box) return null;
        return new PetRect(origin.X + box.X, origin.Y + box.Y, box.Width, box.Height);
    }

    /// <summary>그 자리에 붙었을 때 붙잡는 부위가 창 안으로 넘어가는 깊이.</summary>
    public double Sink(PerchSpot spot)
    {
        if (Ink(spot.Edge) is not { } ink) return 0;
        return PerchLayout.Sink(
            spot.Edge, spot.Contact(spot.WindowFrame), ink, Depth(spot.Edge), work);
    }

    /// <summary>그 자리에 붙었을 때 <b>창이 놓일 원점</b>. 화면 밖이면 null.</summary>
    public PetPoint? Origin(PerchSpot spot)
    {
        if (Ink(spot.Edge) is not { } ink) return null;
        var contact = spot.Contact(spot.WindowFrame);
        var sink = PerchLayout.Sink(spot.Edge, contact, ink, Depth(spot.Edge), work);
        return PerchLayout.Origin(spot.Edge, contact, ink, sink, work);
    }

    /// <summary>그 자리가 지금 남의 창에 묻혔는지.</summary>
    public bool IsBuried(PerchSpot spot, IReadOnlyList<PerchWindow> windows)
    {
        if (Ink(spot.Edge) is not { } ink) return false;
        return PerchFinder.IsBuried(spot, ink.Width, ink.Height, Sink(spot), windows);
    }

    /// <summary>
    /// 그 자세의 그림이 <b>창 안에서</b> 덮는 자리. 시트 그림이 아니면 null.
    ///
    /// <b>그림을 숫자로 바꾸는 곳이 여기 하나다.</b> 부르는 쪽이 뷰에서 직접 재면,
    /// 없는 자세를 대신할 규칙 같은 것이 나중에 붙었을 때 놓을 때만 걸리고 따라갈
    /// 때는 안 걸리는 꼴이 된다.
    /// </summary>
    public PetRect? Ink(MascotPerch edge) => view.PetMascotInkRect(edge) is { } ink
        ? new PetRect(ink.X, ink.Y, ink.Width, ink.Height)
        : null;

    /// <summary>
    /// 그 변에 붙을 때 쓸 깊이. <b>사용자가 맞춘 값이 이긴다.</b>
    ///
    /// 맥도 같다(<c>HUDSettings.storedGripDepth(perch) ?? drawn.gripDepth</c>) — 규격은
    /// 기본값일 뿐이고, 눈으로 보고 정한 값이 있으면 그것이 답이다.
    ///
    /// <b>시트에 그 자세가 없어도 마찬가지다.</b> 그때 잉크는 대체 칸(선 자세)에서
    /// 나오는데, 여기서 규격값으로 되돌려 버리면 <b>슬라이더를 아무리 움직여도 그 변만
    /// 아무 일이 없다</b> — 붙잡는 부위가 없는 그림을 얼마나 밀어 넣을지는 사람이
    /// 정하는 것이 맞다. 0 으로 두면 테두리에 딱 붙는다.
    /// </summary>
    private double Depth(MascotPerch edge) => settings.PerchDepth(edge);
}
