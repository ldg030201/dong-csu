using DongCSU.Core.Owl;

namespace DongCSU.Core.Pet;

/// <summary>
/// 붙었을 때 창을 어디에 놓을지. <b>그림을 모른다</b> — 잉크 상자를 숫자로 받는다.
///
/// 맥은 이 계산이 <c>UsageHUDView</c> 안에 있지만 거기는 SwiftUI 뷰가 아니라 치수
/// 계산기다. 우리 <c>HudView</c> 는 진짜 WPF 컨트롤이라 같이 둘 수 없어서 여기로 뺐다.
///
/// **여기에 상수를 새로 만들지 않는다.** 보태 주는 몫의 한계(12%)는
/// <see cref="AppSettings.MaxAutoPerchExtra"/> 에 있고, 사람이 맞추는 한계
/// (<see cref="AppSettings.MaxPerchDepth"/> = 60%)와 왜 다른 값인지도 거기 적혀 있다.
/// 같은 숫자를 두 곳에 두면 반드시 어긋난다.
/// </summary>
public static class PerchLayout
{
    /// <summary>
    /// 붙잡는 부위가 창 안으로 넘어가는 깊이.
    ///
    /// <b>잉크를 재서 비율을 곱한다.</b> 상수로 박아 두면 배율을 키우거나 남의 그림을
    /// 넣었을 때 몸통까지 잠긴다 — 잠기는 양은 그림 크기를 따라가야 한다.
    /// </summary>
    /// <param name="ink">그림이 실제로 덮는 자리(창 안 좌표, y 아래로).</param>
    /// <param name="depth">
    /// 잉크에 대한 비율. 사용자가 맞춘 값(<c>AppSettings.PerchDepth</c>)이 온다 —
    /// 규격(<c>MascotSheet.GripDepth</c>)은 그 설정의 기본값일 뿐이다.
    /// </param>
    /// <param name="work">작업 영역. 창 밖에 남는 몫이 이 안에 들어가야 한다.</param>
    public static double Sink(
        MascotPerch perch, PetPoint contact, PetRect ink, double depth, PetRect? work)
    {
        // 그 변에서 창 밖으로 뻗는 축의 길이.
        var span = perch.IsHorizontal() ? ink.Height : ink.Width;
        var baseSink = span * depth;
        if (work is not { } visible) return baseSink;

        // **자리가 모자라면 그만큼 더 깊이 앉는다.**
        //
        // 창의 위 테두리가 화면 꼭대기에 가까우면 그 위에 설 자리가 없다 — 맥에서
        // 실제로 8pt 모자라 안 붙는 창이 있었고, 그건 사용자 눈에 고장으로 보인다.
        // 모자란 만큼만 창 안으로 더 넣으면 붙는다.
        //
        // **맥과 세로가 뒤집혀 있다** — 저쪽의 `visible.maxY - contact.y` 가 여기서는
        // `contact.Y - visible.Y` 다(창 위 테두리 **위쪽**에 남은 자리).
        var room = perch switch
        {
            MascotPerch.Top => contact.Y - visible.Y,
            MascotPerch.Bottom => visible.Bottom - contact.Y,
            MascotPerch.Right => visible.Right - contact.X,
            _ => contact.X - visible.X,
        };

        var outside = span - baseSink;
        if (outside <= room) return baseSink;

        // **조금 모자랄 때만 더 넣는다.** 많이 모자란데 억지로 넣으면 다리가 아니라
        // 몸통이 잠겨서, 붙은 것이 아니라 창에 박힌 것으로 보인다 — 그때는 차라리
        // 안 붙는 편이 낫다(더 넣어도 안 들어가면 <see cref="Origin"/> 이 null 을 낸다).
        return baseSink + Math.Min(span * AppSettings.MaxAutoPerchExtra, outside - room);
    }

    /// <summary>
    /// 그 자리에 붙었을 때 <b>창이 놓일 원점</b>. 붙을 수 없으면 null.
    ///
    /// <b>상자가 아니라 그림이 닿아야 한다.</b> 매달린 칸은 손이 묶음 상자 위쪽에,
    /// 앉은 칸은 발이 아래쪽에 그려져 있어서, 상자를 그대로 테두리에 대면 자세마다
    /// 수십 pt 씩 뜬다 — 매달린 것이 아니라 공중에 뜬 것으로 보인다.
    ///
    /// <b>붙잡는 부위만 창 안으로 넣는다.</b> 한때 그림 절반을 넣어 봤고 버렸다 —
    /// 그때 틀린 것은 겹친다는 것이 아니라 <b>몸까지 겹쳤다</b>는 것이다. 몸이 창에
    /// 잠기면 무엇에 붙어 있는지가 흐려진다. 지금 넘어가는 것은 다리 · 발 · 붙잡는
    /// 앞다리뿐이다.
    ///
    /// <b>그림이 화면 밖으로 나가면 붙지 않는다.</b> 화면 안으로 밀어 넣지 않는 이유는
    /// 밀어 넣으면 테두리에서 떨어진 자리에서 붙은 척을 하기 때문이다.
    ///
    /// <b>상자가 아니라 알맹이로 잰다.</b> 상자에는 자세마다 빈 여백이 붙어 있어서,
    /// 그것까지 화면 안을 요구하면 실제로는 다 보이는 자리에서 안 붙는다.
    /// </summary>
    /// <param name="ink">그림이 창 안에서 덮는 자리(창 왼쪽 위가 원점, y 아래로).</param>
    public static PetPoint? Origin(
        MascotPerch perch, PetPoint contact, PetRect ink, double sink, PetRect? work)
    {
        var origin = perch switch
        {
            // 창 위에 앉는다 → 그림 **바닥**이 접점 아래 sink 에 온다.
            MascotPerch.Top => new PetPoint(
                contact.X - ink.Center.X, contact.Y + sink - ink.Bottom),
            // 창 아래에 매달린다 → 그림 **머리**가 접점 위 sink 에 온다.
            MascotPerch.Bottom => new PetPoint(
                contact.X - ink.Center.X, contact.Y - sink - ink.Y),
            // 창 오른쪽 테두리 → 그림 **왼쪽**이 닿는다.
            MascotPerch.Right => new PetPoint(
                contact.X - sink - ink.X, contact.Y - ink.Center.Y),
            _ => new PetPoint(
                contact.X + sink - ink.Right, contact.Y - ink.Center.Y),
        };

        if (work is not { } area) return origin;

        var visual = new PetRect(origin.X + ink.X, origin.Y + ink.Y, ink.Width, ink.Height);
        return area.ContainsRect(visual) ? origin : null;
    }

    /// <summary>
    /// 붙었을 때 <b>남의 창 테두리 선이 지나는 자리</b>. 그림 끝이 아니라 거기서
    /// <paramref name="sink"/> 만큼 안쪽이다 — 붙잡는 부위가 그 선을 넘어가 창 면 위에
    /// 얹히기 때문이다. 가로 테두리면 y, 세로 테두리면 x 다.
    ///
    /// <b>화면 쪽 두 자리가 이걸 따로 적고 있었다.</b> 끌 때 보여주는 막대와, 붙어 있는
    /// 동안 마우스를 흘려보내는 판정이다. 둘은 <b>같은 선이어야</b> 하는데 —
    /// 사용자가 눈으로 맞대 볼 수 있는 유일한 짝이다 — <c>App</c> 에 있어서
    /// <see cref="PerchLayoutTests"/> 가 닿지 못했다. 네 변의 부호가 맥과 뒤집혀 있어서
    /// 제일 틀리기 쉬운 자리인데도 다섯 곳 중 셋만 굳어 있던 셈이다.
    /// </summary>
    /// <param name="ink">그림이 덮는 자리. 재는 좌표계는 부르는 쪽이 정한다.</param>
    public static double BorderLine(MascotPerch perch, PetRect ink, double sink) => perch switch
    {
        // 창 위에 앉는다 → 그림 **바닥**이 닿는다 → 거기서 sink 만큼 위.
        MascotPerch.Top => ink.Bottom - sink,
        // 창 아래에 매달린다 → 그림 **머리**가 닿는다 → 거기서 sink 만큼 아래.
        MascotPerch.Bottom => ink.Y + sink,
        // 창 오른쪽 테두리 → 그림 **왼쪽**이 닿는다.
        MascotPerch.Right => ink.X + sink,
        _ => ink.Right - sink,
    };

    /// <summary>
    /// 그 자리가 <see cref="BorderLine"/> 을 넘어 <b>남의 창 위</b>인지.
    ///
    /// 넘어간 자리는 대개 남의 창 제목 표시줄이라, 거기서는 마우스를 우리가 안 받아야
    /// 그 창을 끌 수 있다. <b>변마다 넘어간 쪽이 반대다</b> — 위에 앉으면 선 아래가,
    /// 아래에 매달리면 선 위가 남의 창이다.
    /// </summary>
    public static bool CrossesBorder(
        MascotPerch perch, PetRect ink, double sink, PetPoint point)
    {
        var line = BorderLine(perch, ink, sink);
        return perch switch
        {
            MascotPerch.Top => point.Y > line,
            MascotPerch.Bottom => point.Y < line,
            MascotPerch.Right => point.X < line,
            _ => point.X > line,
        };
    }
}
