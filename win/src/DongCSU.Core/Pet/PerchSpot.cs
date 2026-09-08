using DongCSU.Core.Owl;

namespace DongCSU.Core.Pet;

/// <summary>
/// 창 목록 한 줄. <b>앞에 있는 것이 먼저 온다.</b>
///
/// <c>Handle</c> 은 <c>HWND</c> 지만 <c>Core</c> 는 Win32 를 모르므로 <see cref="long"/>
/// 으로 받는다 — 조사하는 쪽(<c>App</c> 의 <c>WindowSurvey</c>)이 넣어 준다.
///
/// <c>Owner</c> 는 그 창을 띄운 <b>프로세스 이름</b>이다. 붙는 데 안 쓰고
/// <c>--probe-perch</c> 가 사람에게 보여줄 뿐이다 — 맥의 <c>kCGWindowOwnerName</c> 과
/// 같은 자리다. <b>창 제목이 아니다</b>(맥은 그것에 권한이 걸려서 못 본다).
/// </summary>
public readonly record struct PerchWindow(long Handle, PetRect Frame, string Owner);

/// <summary>창 테두리 어디에 붙어 있는지.</summary>
/// <param name="Window">
/// 붙은 창. <b>핸들로 따라간다</b> — 프로세스로 잡으면 창을 여럿 띄운 앱에서 어느
/// 것인지 알 수 없고, 자리로 잡으면 창을 옮기는 순간 다른 창이 된다.
/// </param>
/// <param name="Offset">
/// 모서리 시작점(창의 <b>왼쪽 또는 위</b>)에서 떨어진 거리.
///
/// <b>비율(0~1)로 기억하는 길은 버렸다.</b> 창을 옆으로 넓히면 가만히 있어야 할 펫이
/// 스르륵 미끄러진다. 절대 거리로 두면 창을 넓혀도 제자리고, 좁혀서 모서리가 짧아졌을
/// 때만 끌려온다.
///
/// <b>맥은 세로 오프셋을 창 아래에서 잰다</b>(AppKit 은 y 가 위로 큰다). 여기서는 창
/// 위에서 잰다 — 부호가 다를 뿐 같은 값이고, <see cref="Contact"/> · <see cref="Clamped"/> ·
/// <c>PerchFinder.Offset</c> 셋이 같은 규약을 쓰기만 하면 된다.
/// </param>
/// <param name="WindowFrame">마지막으로 본 창 자리.</param>
public readonly record struct PerchSpot(
    long Window,
    MascotPerch Edge,
    double Offset,
    PetRect WindowFrame)
{
    /// <summary>그 창에서 붙는 지점. 그림의 <b>닿는 변</b>이 여기에 온다.</summary>
    public PetPoint Contact(PetRect window) => Edge switch
    {
        MascotPerch.Top => new PetPoint(window.X + Offset, window.Y),
        MascotPerch.Bottom => new PetPoint(window.X + Offset, window.Bottom),
        MascotPerch.Left => new PetPoint(window.X, window.Y + Offset),
        _ => new PetPoint(window.Right, window.Y + Offset),
    };

    /// <summary>
    /// 창이 좁아져서 모서리 밖으로 나간 오프셋을 안으로 끌어온다.
    /// 모서리가 그림보다 짧아졌으면 null — 그때는 붙어 있을 자리가 없다.
    /// </summary>
    public PerchSpot? Clamped(PetRect window, double mascotWidth, double mascotHeight)
    {
        if (Edge.IsHorizontal())
        {
            if (window.Width < mascotWidth) return null;
            var half = mascotWidth / 2;
            return this with
            {
                WindowFrame = window,
                Offset = Math.Clamp(Offset, half, window.Width - half),
            };
        }

        if (window.Height < mascotHeight) return null;
        var halfHeight = mascotHeight / 2;
        return this with
        {
            WindowFrame = window,
            Offset = Math.Clamp(Offset, halfHeight, window.Height - halfHeight),
        };
    }
}
