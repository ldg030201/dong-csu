using DongCSU.Core.Owl;

namespace DongCSU.Core.Pet;

/// <summary>
/// 놓은 자리에서 어느 창 어느 테두리에 붙을지 고른다.
///
/// 맥 <c>WindowSurvey</c> 의 <b>화면 없는 절반</b>이다 — 창 목록을 받아 계산만 한다.
/// 목록을 뜨는 것은 <c>App</c> 의 <c>WindowSurvey</c> 가 한다(Win32 를 쓴다).
///
/// <b>놓은 자리에서만 찾는다.</b> 화면을 통째로 뒤져 "제일 그럴듯한" 창을 고르는 길은
/// 맥에서 버렸다 — 사용자가 여기 놓겠다고 끌어다 놓은 것이라, 우리가 다른 자리를 더
/// 좋게 볼 이유가 없다.
/// </summary>
public static class PerchFinder
{
    /// <summary>
    /// 놓은 자리가 테두리에서 이만큼 안이면 붙는다. <b>그림 크기를 따라간다.</b>
    ///
    /// 맥은 한동안 40 으로 못 박아 뒀는데 조준을 너무 잘해야 했다 — 기록을 보니 42 ·
    /// 46 처럼 몇 칸 차이로 놓치는 일이 잦았다. 게다가 붙잡는 부위가 창 안으로
    /// 넘어가면서 실제 착지 지점이 12~15 안쪽으로 옮겨졌는데 문턱은 그대로여서,
    /// 조준 범위가 한쪽으로 쏠려 있었다.
    ///
    /// 그림 높이의 4/5 다. 배율을 키우면 그림이 커지는 만큼 조준도 편해져야 한다.
    /// </summary>
    public static double SnapDistance(double width, double height) =>
        Math.Max(width, height) * 0.8;

    /// <summary>이보다 작은 창에는 안 붙는다. 띠·조각 창을 막는다.</summary>
    public static readonly PetRect MinimumWindow = new(0, 0, 120, 80);

    /// <summary>테두리 안쪽으로 이만큼까지 남의 창이 덮고 있으면 가려진 것으로 본다.</summary>
    private const double EdgePeek = 6;

    /// <summary>붙을 수 있는 네 변. 차례가 곧 검사 차례다.</summary>
    private static readonly MascotPerch[] Edges =
        [MascotPerch.Top, MascotPerch.Bottom, MascotPerch.Left, MascotPerch.Right];

    /// <summary>
    /// 마스코트를 놓은 자리에서 가장 가까운 창 테두리. 닿는 것이 없으면 null.
    /// </summary>
    /// <param name="mascot">
    /// 그림이 실제로 덮는 자리(화면 좌표). <b>창이 아니다</b> — 펫의 창은 링만큼 커서
    /// 그것으로 재면 아직 한참 떨어져 있는데도 붙는다.
    /// </param>
    /// <param name="limit">이 거리 안이어야 붙는다(<see cref="SnapDistance"/>).</param>
    /// <param name="windows">창 목록. <b>앞에 있는 것이 먼저 와야 한다.</b></param>
    /// <param name="sink">
    /// 그 변에 붙었을 때 붙잡는 부위가 <b>창 안으로 넘어가는 깊이</b>. 그림 사정을
    /// 여기서 알 수 없어서 뷰 쪽이 재서 숫자로 건네준다. 안 주면 0 —
    /// 테두리에 딱 맞춘다. <b>변이 아니라 자리를 받는다</b> — 화면 가장자리에서는
    /// 자리마다 더 깊이 들어가야 해서 깊이가 접점에 따라 달라진다.
    /// </param>
    /// <param name="placeable">
    /// 그 자리에 <b>실제로 놓을 수 있는지.</b> 자리 계산이 화면 밖이라 거절하는 변이
    /// 있는데, 그걸 여기서 안 물어보면 후보로 골라 놓고 나중에 못 놓는다 — 사용자
    /// 눈에는 <b>아무 데도 안 붙는 것</b>으로 보인다. 걸러 내면 그 다음으로 가까운
    /// 변으로 넘어간다.
    /// </param>
    public static PerchSpot? Snap(
        PetRect mascot,
        double limit,
        IReadOnlyList<PerchWindow> windows,
        Func<PerchSpot, double>? sink = null,
        Func<PerchSpot, bool>? placeable = null)
    {
        PerchSpot? best = null;
        var bestDistance = double.MaxValue;

        for (var rank = 0; rank < windows.Count; rank++)
        {
            var window = windows[rank];
            foreach (var edge in Edges)
            {
                if (Gap(mascot, window.Frame, edge) is not { } distance) continue;
                if (distance > limit) continue;

                // **더 가까운 것만 이긴다.** 같으면 먼저 본 것 — 목록이 앞에 있는 창부터
                // 오므로, 겹쳐 있는 두 창의 같은 자리에서는 사용자가 보고 있는 쪽이 된다.
                if (best is not null && distance >= bestDistance) continue;

                // **여기서 오프셋을 가둔다.** 안 가두면 모서리 맨 끝에 놓았을 때 그림
                // 절반이 창 밖으로 나간 자리에 그대로 앉고, 뒤이은 첫 추적 틱이 안으로
                // 밀어 넣어 **곧바로 옆으로 튄다** — 미리보기와 착지는 맞았는데 곧
                // 옮겨가는 것으로 보인다. 미리보기·착지·추적이 한 셈에서 나오게
                // 이 자리에서 한 번만 가둔다.
                var candidate = new PerchSpot(
                    window.Handle, edge,
                    Offset(mascot, window.Frame, edge),
                    window.Frame);
                if (candidate.Clamped(window.Frame, mascot.Width, mascot.Height)
                    is not { } spot) continue;

                // **가려진 테두리에는 안 붙는다.** 목록이 앞에서 뒤 순서라 자기보다
                // 앞에 있는 창만 보면 된다. 이걸 안 보면 다른 창에 덮여 **보이지도 않는
                // 창**의 테두리에 붙어서, 사용자 눈에는 아무것도 없는 자리에 매달린
                // 것으로 보인다.
                //
                // 맥에서 한동안 이 판정을 뺐다 — "사용자가 직접 끌어다 놓는 것이라
                // 우리가 자리를 고를 이유가 없다"고 봤는데 거꾸로였다. **놓는 사람은
                // 보이는 것에 겨냥한다.**
                if (IsCovered(spot, mascot.Width, mascot.Height, sink?.Invoke(spot) ?? 0,
                        windows, rank)) continue;

                // **놓을 수 있는지 마지막에 묻는다.** 앞의 걸러내기를 다 통과해도 화면
                // 가장자리라 자리가 안 나오는 변이 있다 — 창 위 테두리가 화면 꼭대기에
                // 가까우면 그 위에 앉을 자리가 없다.
                if (placeable is not null && !placeable(spot)) continue;

                best = spot;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// 그 창이 아직 목록에 있으면 자리와 <b>맨 앞인지.</b> 없으면 null.
    ///
    /// **맨 앞인지가 같이 필요하다.** 붙어 있는 펫은 그 창과 같은 층에서 보여야 하는데,
    /// 그 창이 앞으로 왔는지는 목록 순서로만 알 수 있다.
    /// </summary>
    public static (PetRect Frame, bool IsFront)? Locate(
        long window, IReadOnlyList<PerchWindow> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Handle == window) return (list[i].Frame, i == 0);
        }
        return null;
    }

    /// <summary>
    /// 붙어 있는 자리가 지금 <b>남의 창에 묻혔는지.</b>
    ///
    /// **붙일 때만 보던 것을 붙어 있는 동안에도 본다.** 붙은 창이 다른 창에 가려지면
    /// 펫도 같이 가려져야 하는데, 앞으로 끌어올리는 코드가 그걸 뒤집어 놓는다 —
    /// **아무것도 없는 자리에 매달린 것으로 보인다.**
    /// </summary>
    public static bool IsBuried(
        PerchSpot spot, double width, double height, double sink,
        IReadOnlyList<PerchWindow> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Handle != spot.Window) continue;
            return IsCovered(spot, width, height, sink, list, i);
        }
        return false;
    }

    /// <summary>
    /// 그 자리에 붙었을 때 그림이 덮을 자리(대략).
    ///
    /// **알맹이가 아니라 상자로 잰다.** 가림을 보려는 것이라 넉넉한 쪽이 안전하고,
    /// 자세별 알맹이는 그리는 쪽만 아는 값이라 여기로 끌어올 수 없다.
    ///
    /// <paramref name="sink"/> 만큼 <b>창 안쪽으로 밀어 놓는다.</b> 실제 자리
    /// (<see cref="PerchLayout.Origin"/>)가 그만큼 들어가 있어서, 여기만 바깥에 두면
    /// 진단 통로가 앱과 다른 답을 낸다.
    ///
    /// **맥과 세로가 뒤집혀 있다** — 저쪽은 창 위 테두리가 <c>maxY</c> 지만 여기서는
    /// <c>Y</c> 다.
    /// </summary>
    public static PetRect LandingArea(PerchSpot spot, double width, double height, double sink = 0)
    {
        var contact = spot.Contact(spot.WindowFrame);
        return spot.Edge switch
        {
            // 창 위에 앉는다 → 그림은 테두리 **위**에 있고 바닥이 sink 만큼 창 안(아래)으로.
            MascotPerch.Top => new PetRect(contact.X - width / 2, contact.Y + sink - height, width, height),
            // 창 아래에 매달린다 → 그림은 테두리 **아래**에 있고 머리가 sink 만큼 창 안(위)으로.
            MascotPerch.Bottom => new PetRect(contact.X - width / 2, contact.Y - sink, width, height),
            MascotPerch.Right => new PetRect(contact.X - sink, contact.Y - height / 2, width, height),
            _ => new PetRect(contact.X - width + sink, contact.Y - height / 2, width, height),
        };
    }

    /// <summary>
    /// 왜 안 붙었는지 창·변마다 한 줄씩. <b>진단에만 쓴다.</b>
    ///
    /// <see cref="Snap"/> 은 되는 자리 하나만 돌려주고 나머지는 조용히 버린다. 안
    /// 붙는다는 말을 들었을 때 그 "조용히" 가 문제라, 같은 걸러내기를 순서대로 다시
    /// 걸으면서 어디서 걸렸는지 남긴다.
    /// </summary>
    public static IReadOnlyList<string> Explain(
        PetRect mascot,
        double limit,
        IReadOnlyList<PerchWindow> windows,
        Func<PerchSpot, double> sink,
        Func<PerchSpot, bool> placeable)
    {
        if (windows.Count == 0) return ["창이 하나도 안 잡혔다"];

        var lines = new List<string>(windows.Count);
        for (var rank = 0; rank < windows.Count; rank++)
        {
            var window = windows[rank];
            var parts = new List<string>(4);

            foreach (var edge in Edges)
            {
                var name = Label(edge);
                if (Gap(mascot, window.Frame, edge) is not { } distance)
                {
                    parts.Add($"{name} 빗나감");
                    continue;
                }
                if (distance > limit)
                {
                    parts.Add($"{name} {distance:0}pt 떨어짐");
                    continue;
                }

                var candidate = new PerchSpot(
                    window.Handle, edge, Offset(mascot, window.Frame, edge), window.Frame);
                if (candidate.Clamped(window.Frame, mascot.Width, mascot.Height) is not { } spot)
                {
                    parts.Add($"{name} 모서리가 짧음");
                    continue;
                }

                if (IsCovered(spot, mascot.Width, mascot.Height, sink(spot), windows, rank))
                {
                    parts.Add($"{name} 가려짐");
                }
                else if (!placeable(spot))
                {
                    parts.Add($"{name} 화면 밖");
                }
                else
                {
                    parts.Add($"{name} **가능({distance:0}pt)**");
                }
            }

            lines.Add($"  {window.Owner} [{window.Handle}] {Box(window.Frame)} — "
                + string.Join(" · ", parts));
        }
        return lines;
    }

    private static string Label(MascotPerch edge) => edge switch
    {
        MascotPerch.Top => "위",
        MascotPerch.Bottom => "아래",
        MascotPerch.Left => "왼쪽",
        _ => "오른쪽",
    };

    /// <summary>기록과 진단이 자리를 같은 꼴로 찍게 한다. <c>(x,y) WxH</c>.</summary>
    public static string Box(PetRect rect) =>
        $"({rect.X:0},{rect.Y:0}) {rect.Width:0}x{rect.Height:0}";

    /// <summary>
    /// 그 모서리까지 얼마나 떨어져 있는지. 나란한 방향으로 아예 빗나가 있으면 null.
    ///
    /// 재는 것은 <b>닿아야 할 변까지의 거리</b>다. 위 테두리에 앉으려면 그림의 발이,
    /// 아래 테두리에 매달리려면 손이 닿아야 해서 변마다 보는 쪽이 다르다.
    ///
    /// <b>맥과 세로만 뒤집혀 있다.</b> 저쪽은 창 위 테두리가 <c>maxY</c> 지만 여기서는
    /// <c>Y</c> 다 — 부호를 그대로 옮겨 적으면 위아래가 통째로 바뀐다.
    /// </summary>
    private static double? Gap(PetRect mascot, PetRect window, MascotPerch edge)
    {
        if (edge.IsHorizontal())
        {
            // 모서리가 그림보다 짧으면 붙은 게 아니라 걸쳐진 것으로 보인다.
            if (window.Width < mascot.Width) return null;
            // 가로로 창을 벗어난 자리에는 안 붙는다. 그림 한가운데가 기준이다.
            var midX = mascot.Center.X;
            if (midX < window.X || midX > window.Right) return null;

            return edge == MascotPerch.Top
                ? Math.Abs(mascot.Bottom - window.Y)   // 발이 창 위 테두리에 닿는다
                : Math.Abs(mascot.Y - window.Bottom);  // 손이 창 아래 테두리에 닿는다
        }

        if (window.Height < mascot.Height) return null;
        var midY = mascot.Center.Y;
        if (midY < window.Y || midY > window.Bottom) return null;

        return edge == MascotPerch.Right
            ? Math.Abs(mascot.X - window.Right)
            : Math.Abs(mascot.Right - window.X);
    }

    /// <summary>모서리 시작점에서 얼마나 떨어진 자리에 붙었는지.</summary>
    private static double Offset(PetRect mascot, PetRect window, MascotPerch edge) =>
        edge.IsHorizontal() ? mascot.Center.X - window.X : mascot.Center.Y - window.Y;

    /// <summary>
    /// 붙을 자리가 <b>더 앞에 있는 창</b>에 덮여 있는지.
    ///
    /// 재는 것은 그림이 놓일 자리에 테두리 안쪽 몇 pt 를 더한 것이다. 안쪽을 같이 봐야
    /// <b>붙을 테두리 자체가 가려진 것</b>이 걸린다 — 그림이 놓일 자리만 보면, 테두리는
    /// 남의 창에 덮였는데 그 바깥은 비어 있는 자리가 통과한다.
    /// </summary>
    private static bool IsCovered(
        PerchSpot spot, double width, double height, double sink,
        IReadOnlyList<PerchWindow> list, int rank)
    {
        if (rank == 0) return false;

        var landing = LandingArea(spot, width, height, sink).Inflate(EdgePeek);
        for (var i = 0; i < rank; i++)
        {
            if (list[i].Frame.Intersects(landing)) return true;
        }
        return false;
    }
}
