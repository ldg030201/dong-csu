using System.Runtime.InteropServices;

namespace DongCSU.App;

/// <summary>
/// 창 모서리를 둥글게 깎고 테두리 색을 잡는다.
///
/// **윈도우 11 부터는 창 관리자가 직접 깎아 준다.** 우리가 그리지 않는 이유는,
/// 모서리와 그림자가 창 바깥 영역이라 앱이 손댈 수 없는 자리이기 때문이다.
/// 직접 흉내 내면 배율이 다른 화면에서 계단이 진다.
///
/// 트레이 메뉴(<see cref="Tray.TrayMenuStyle"/>)만 쓰다가 확인 창이 같은 것을 필요로
/// 해서 따로 냈다. **한 앱에서 어떤 창은 둥글고 어떤 창은 각지면 남의 앱처럼 보인다.**
///
/// WPF 창에 걸 때는 <c>AllowsTransparency</c> 를 **반드시 꺼 둔다** — 켜면 레이어드
/// 창이 되어 DWM 이 모서리를 안 깎고, 덤으로 글자 렌더링까지 흐려진다.
/// </summary>
internal static partial class RoundedWindow
{
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int RoundCorner = 2;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// 핸들이 생긴 뒤에 건다 — WinForms 는 <c>HandleCreated</c>, WPF 는
    /// <c>OnSourceInitialized</c> 다. 아직 없으면 조용히 넘어간다.
    /// </summary>
    public static void Round(IntPtr window, bool dark)
    {
        if (window == IntPtr.Zero) return;

        var corner = RoundCorner;
        DwmSetWindowAttribute(window, WindowCornerPreference, ref corner, sizeof(int));

        // COLORREF 는 0x00BBGGRR 이다. 어두운 테마에서 밝은 테두리를 쓰면 떠 보인다.
        var border = dark ? 0x003A3A42 : 0x00E0E0E4;
        DwmSetWindowAttribute(window, BorderColor, ref border, sizeof(int));
    }
}
