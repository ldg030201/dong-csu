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

    /// <summary>
    /// 고쳐진 것. 변경 내역의 "오류" 딱지가 쓴다.
    ///
    /// **<see cref="Warning"/> 을 돌려쓰지 않는다.** 저쪽은 호박색이고 "재로그인 필요 ·
    /// 오래된 값 · 만료" 처럼 **지금 손봐야 하는 것**에 붙는다. 고쳐진 오류는 경고가
    /// 아니라 이미 끝난 일이라, 같은 색으로 내면 한 목록 안에서 뜻이 뒤집혀 읽힌다.
    /// 맥이 <c>ChangeKind.fix</c> 에 주황을 쓰는 것도 같은 이유다.
    /// </summary>
    public Color Fixed => IsDark ? Color.FromRgb(0xF2, 0x8B, 0x3C) : Color.FromRgb(0xBE, 0x58, 0x10);

    /// <summary>
    /// 달라진 것. 변경 내역의 "변경" 딱지가 쓴다.
    ///
    /// **<see cref="Test"/> 의 보라를 피한다.** 변경 내역 탭에는 "테스트 빌드" 딱지가
    /// 같이 떠 있어서, 둘이 같은 색이면 한 화면에서 두 가지 뜻이 겹친다. 보라는
    /// 마스코트·HUD 딱지까지 걸린 **테스트판의 표시색**이라 그쪽이 눈에 띄어야 하고,
    /// 자리를 옮길 수 있는 것은 갈래 쪽이다. 그래서 자홍으로 갈랐다 — 초록(신규) ·
    /// 파랑(개선) · 주황(오류) · 회색(제거) 어디와도 안 붙는 남은 색이다.
    /// </summary>
    public Color Changed => IsDark ? Color.FromRgb(0xF0, 0x82, 0xC4) : Color.FromRgb(0xB8, 0x36, 0x86);

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
