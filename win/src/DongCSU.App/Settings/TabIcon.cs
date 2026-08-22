using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DongCSU.App.Settings;

/// <summary>
/// 설정 탭 아이콘.
///
/// **여기가 유일한 자리다.** 사이드바와 변경 내역 묶음이 같은 표를 본다 — 탭 아이콘을
/// 바꾸면 변경 내역의 그림도 같이 바뀌어야, 어느 메뉴 이야기인지 눈으로 맞춰 볼 수 있다.
///
/// 그림을 그리지 않고 **윈도우가 들고 있는 아이콘 글꼴**을 쓴다. 11 은 Segoe Fluent
/// Icons, 10 은 Segoe MDL2 Assets 이고 여기 쓰는 글리프는 두 글꼴에 다 있다.
/// </summary>
internal static class TabIcon
{
    /// <summary>
    /// 글리프. 맥의 SF Symbol 과 짝이 맞는 것으로 골랐고 **하나하나 그려 보고 확인했다** —
    /// 코드포인트를 눈대중으로 적으면 빈 네모가 나온다(실제로 <c>F1EE</c> 가 그랬다).
    /// </summary>
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["status"] = "\uEB05",   // 그래프 — gauge
        ["measure"] = "\uE916",  // 스톱워치 — stopwatch
        ["display"] = "\uE9E9",  // 슬라이더 — slider.horizontal.3
        ["icon"] = "\uE76E",     // 웃는 얼굴 — face.smiling
        ["pet"] = "\uE805",      // 걸어가는 사람 — pawprint (Segoe 에는 발자국이 없다)
        ["account"] = "\uE77B",  // 사람 — person.crop.circle
        ["version"] = "\uE896",  // 내려받기 화살표 — arrow.down.circle
    };

    /// <summary>탭에 없는 묶음(마스코트·HUD·설치)이 함께 쓰는 아이콘. 맥과 같은 렌치+드라이버.</summary>
    private const string Other = "\uEC7A";

    /// <summary>
    /// **아이콘마다 폭이 달라서 자리를 못 박는다.** 안 그러면 제목 시작점이 줄마다 어긋난다.
    /// 변경 내역의 세로줄도 이 폭에서 나온다.
    /// </summary>
    public const double Width = 16;

    /// <summary>
    /// 아이콘 글꼴 사슬. **탭 아이콘이 아닌 글리프도 이걸 쓴다**(측정 기록 줄의 화살표) —
    /// 사슬을 다른 자리에 옮겨 적으면 대체 글꼴을 고칠 때 한쪽만 고치게 되고, 옛 윈도우
    /// 에서 한쪽만 빈 네모가 된다.
    /// </summary>
    internal static readonly FontFamily Font =
        new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");

    /// <summary>탭 키에 붙은 글리프. 모르는 키(또는 null)면 공통 아이콘.</summary>
    public static string GlyphFor(string? tab) =>
        tab is not null && Glyphs.TryGetValue(tab, out var found) ? found : Other;

    public static TextBlock Make(string? tab, double size, Brush color) => new()
    {
        Text = GlyphFor(tab),
        FontFamily = Font,
        FontSize = size,
        Foreground = color,
        Width = Width,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
