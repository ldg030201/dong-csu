using System.Windows.Media;
using DongCSU.App.Rendering;

namespace DongCSU.App.Settings;

/// <summary>
/// 설정 창 배색.
///
/// **HUD 테마 설정을 그대로 따른다.** HUD 는 어둡게 해 뒀는데 설정 창만 하얗게 뜨면
/// 같은 앱으로 안 보인다. 색을 여기 한 곳에만 두는 이유도 같다 — 탭마다 색을 적으면
/// 탭 하나를 손볼 때마다 어긋난다.
/// </summary>
public sealed class SettingsPalette
{
    public static SettingsPalette For(bool isDark) => isDark ? Dark : Light;

    public static readonly SettingsPalette Dark = new(isDark: true);
    public static readonly SettingsPalette Light = new(isDark: false);

    private SettingsPalette(bool isDark) => IsDark = isDark;

    public bool IsDark { get; }

    private Color Ink => IsDark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A);

    private Color Fade(double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), Ink.R, Ink.G, Ink.B);

    public Color Window => IsDark ? Color.FromRgb(0x1B, 0x1B, 0x1F) : Color.FromRgb(0xFA, 0xFA, 0xFB);
    public Color Sidebar => IsDark ? Color.FromRgb(0x17, 0x17, 0x1A) : Color.FromRgb(0xF1, 0xF1, 0xF4);

    /// <summary>내용을 담는 판. 배경보다 한 단계 떠 보이게 한다.</summary>
    public Color Card => IsDark ? Color.FromRgb(0x24, 0x24, 0x2A) : Color.FromRgb(0xFF, 0xFF, 0xFF);

    public Color Line => Fade(IsDark ? 0.10 : 0.09);

    public Color Primary => Fade(1);
    public Color Secondary => Fade(IsDark ? 0.68 : 0.60);
    public Color Tertiary => Fade(IsDark ? 0.50 : 0.45);
    public Color Faint => Fade(IsDark ? 0.34 : 0.36);

    /// <summary>새로 생긴 것을 알리는 색. 변경 내역의 "신규" 딱지가 쓴다.</summary>
    public Color Good => IsDark ? Color.FromRgb(0x5A, 0xC8, 0x8B) : Color.FromRgb(0x1E, 0x8E, 0x55);

    /// <summary>마우스를 올렸을 때 깔리는 옅은 면.</summary>
    public Color Hover => Fade(IsDark ? 0.07 : 0.05);
    public Color Pressed => Fade(IsDark ? 0.13 : 0.10);

    /// <summary>고른 것. HUD 의 새 버전 표시와 같은 파랑이다.</summary>
    public Color Accent => IsDark ? Color.FromRgb(0x4A, 0x99, 0xFC) : Color.FromRgb(0x1C, 0x70, 0xE6);

    public Color AccentSoft => Color.FromArgb(IsDark ? (byte)0x2E : (byte)0x24, Accent.R, Accent.G, Accent.B);
    public Color OnAccent => Colors.White;

    public Color Warning => IsDark ? Color.FromRgb(0xF2, 0xB8, 0x45) : Color.FromRgb(0xB8, 0x78, 0x0D);
    public Color Danger => IsDark ? Color.FromRgb(0xF2, 0x6A, 0x6A) : Color.FromRgb(0xC0, 0x35, 0x35);

    /// <summary>테스트판 표시. 마스코트·HUD 딱지와 같은 보라다.</summary>
    public Color Test => IsDark ? Color.FromRgb(0xBD, 0x99, 0xFC) : Color.FromRgb(0x66, 0x38, 0xB8);

    /// <summary>꺼져 있는 토글의 바탕.</summary>
    public Color TrackOff => Fade(IsDark ? 0.18 : 0.16);

    /// <summary>
    /// 색을 브러시로. **색마다 하나씩만 만들어 나눠 쓴다** — 자세히는 <see cref="Paint"/>.
    ///
    /// 설정 창은 탭을 다시 그릴 때마다 컨트롤 하나에 두세 번씩 이걸 부른다.
    /// 매번 새로 만들면 그때마다 수백 개가 생겼다 버려진다.
    /// </summary>
    public SolidColorBrush Brush(Color color) => Paint.Brush(color);
}
