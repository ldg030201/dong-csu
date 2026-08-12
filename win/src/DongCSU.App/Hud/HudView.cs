using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DongCSU.App.Rendering;
using DongCSU.Core;
using DongCSU.Core.Owl;
using DongCSU.Core.Usage;

namespace DongCSU.App.Hud;

/// <summary>HUD 위에서 마우스를 받는 자리.</summary>
public enum HudHit
{
    None,
    /// <summary>접기·펼치기.</summary>
    Collapse,
    Settings,
    Refresh,
    /// <summary>새 버전 표시. 누르면 버전 화면이 열린다.</summary>
    UpdateBadge,
    /// <summary>
    /// 마스코트. 더블클릭으로 펫 모드를 드나든다.
    ///
    /// **다른 자리와 달리 여기서는 드래그가 살아 있어야 한다** — 펫 모드에서는
    /// 마스코트가 창의 거의 전부라, 여기서 못 끌면 창을 옮길 방법이 없다.
    /// </summary>
    Mascot,
    /// <summary>다음 조회 카운트다운. 누를 것은 없고 무슨 상태인지 알려만 준다.</summary>
    Countdown,
    /// <summary>이 앱 자신이 쓰는 CPU·메모리 줄.</summary>
    StatsRow,
    /// <summary>왼쪽 위 버전 딱지.</summary>
    VersionBadge,
    /// <summary>
    /// 펫의 버튼 줄 — 버튼이 아닌 빈 자리.
    ///
    /// 여기서도 링과 버튼이 떠 있어야 한다. 안 그러면 버튼을 누르러 내려오는 도중에
    /// 눈앞에서 사라진다. 맥도 줄 전체를 한 영역으로 잡는다.
    /// </summary>
    PetRow,
}

public static class HudHitExtensions
{
    /// <summary>
    /// **눌러서 무언가 일어나는 자리인가.**
    ///
    /// 나머지(빈 자리·마스코트·설명만 붙은 자리)는 창이 다 같이 다룬다 — 끌 수 있고,
    /// 두 번 누르면 접힌다. 설명 문구를 붙이겠다고 끌 수 있는 자리를 뺏으면 안 된다.
    /// </summary>
    public static bool IsButton(this HudHit hit) =>
        hit is HudHit.Collapse or HudHit.Settings or HudHit.Refresh or HudHit.UpdateBadge;
}

/// <summary>
/// 어둡게 / 밝게 한 벌.
///
/// **맥과 같은 방식이다** — 잉크색 하나를 정하고 알파만 바꿔 계층을 만든다. 색을 따로
/// 여러 개 두면 두 판이 조금씩 어긋나고, 반투명 배경 위에서 계층이 뭉개진다.
/// </summary>
public sealed class HudPalette
{
    public static readonly HudPalette Dark = new(isDark: true);
    public static readonly HudPalette Light = new(isDark: false);

    private HudPalette(bool isDark) => IsDark = isDark;

    public bool IsDark { get; }

    private Color Ink => IsDark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A);

    private Color Fade(double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), Ink.R, Ink.G, Ink.B);

    public Color Backdrop => IsDark ? Color.FromRgb(0x17, 0x17, 0x17) : Color.FromRgb(0xF7, 0xF7, 0xF7);
    public Color Border => IsDark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0, 0, 0);

    public Color Primary => Fade(1);
    public Color Secondary => Fade(IsDark ? 0.68 : 0.60);
    public Color Tertiary => Fade(IsDark ? 0.62 : 0.55);
    public Color Faint => Fade(IsDark ? 0.38 : 0.40);

    /// <summary>값이 없을 때의 색점. 사용률 색과 헷갈리지 않게 무채색으로 둔다.</summary>
    public Color MutedDot => Fade(0.28);

    public Color RingTrack => Fade(IsDark ? 0.15 : 0.13);

    /// <summary>주간을 다 써서 세션도 못 쓸 때의 링·색점. 사용률 색을 대신한다.</summary>
    public Color RingSpent => Fade(IsDark ? 0.38 : 0.32);

    public Color ControlIdle => Fade(0.45);
    public Color ControlActive => Fade(0.95);
    public Color ControlHoverFill => Fade(0.13);

    /// <summary>밝은 배경에서 검은 그림자를 진하게 쓰면 지저분해진다.</summary>
    public Color TextShadow => IsDark ? Color.FromArgb(0x8C, 0, 0, 0) : Color.FromArgb(0x1F, 0, 0, 0);

    /// <summary>갱신 실패·재로그인 경고색. 밝은 배경에서는 더 어둡게 잡아야 읽힌다.</summary>
    public Color Warning => IsDark ? Color.FromRgb(0xF2, 0xB8, 0x45) : Color.FromRgb(0xB8, 0x78, 0x0D);

    /// <summary>새 버전 알림. 링 색(초록·노랑·빨강)과 겹치지 않는 파랑이다.</summary>
    public Color UpdateBadge => IsDark ? Color.FromRgb(0x4A, 0x99, 0xFC) : Color.FromRgb(0x1C, 0x70, 0xE6);

    /// <summary>
    /// 테스트판 버전 딱지. 마스코트의 테스트 팔레트와 같은 보라 계열이다.
    ///
    /// 곁눈으로도 걸려야 한다 — 두 판을 나란히 띄워 놓고 비교하는 중에 어느 쪽을
    /// 보고 있는지 헷갈리면 검증이 통째로 무의미해진다.
    /// </summary>
    public Color TestBadge => IsDark ? Color.FromRgb(0xBD, 0x99, 0xFC) : Color.FromRgb(0x66, 0x38, 0xB8);
}

/// <summary>
/// HUD 를 통째로 그린다.
///
/// 배율 1 기준 치수는 **맥판과 같다** — 펼치면 240×88, 접으면 108×88.
/// 두 판이 나란히 놓였을 때 크기가 다르면 같은 앱으로 안 보인다.
///
/// **자식 컨트롤을 두지 않는다.** 창이 <c>WS_EX_NOACTIVATE</c> 라 포커스를 받지 않아서
/// 진짜 Button 을 얹으면 클릭·호버가 제대로 안 온다. 대신 <see cref="HitTest"/> 로
/// 직접 자리를 재고, 그린 자리와 재는 자리를 **같은 함수**에서 뽑는다 — 따로 두면
/// 창 높이가 바뀌는 순간(자원 줄) 눌리지 않는 버튼이 생긴다.
/// </summary>
public sealed class HudView : FrameworkElement
{
    public const double BaseExpandedWidth = 240;
    public const double BaseExpandedHeight = 88;
    /// <summary>접은 모습: 링 + 버튼 세 칸이 세로로. 맥과 같은 108 이다.</summary>
    public const double BaseCollapsedWidth = 108;
    public const double BaseCollapsedHeight = 88;

    /// <summary>자원 사용량 줄을 붙일 때 늘어나는 높이.</summary>
    public const double BaseStatsRowHeight = 17;

    /// <summary>
    /// 펫 모습: 마스코트만. 창은 뒤에 두를 링을 담을 만큼이다.
    ///
    /// **마우스를 올렸다고 창을 키우지 않는다** — 커서가 창 밖으로 밀려나 호버가 끊기고
    /// 그 자리에서 켜졌다 꺼졌다 한다.
    /// </summary>
    public const double BasePetWidth = 128;

    /// <summary>
    /// 링 아래에 붙는 버튼 줄의 높이.
    ///
    /// **링 영역과 자리를 갈라 둔다.** 커서 피하기를 켜 두면 마스코트가 커서를 피해
    /// 달아나는데, 버튼이 그 안에 있으면 **누르러 다가갈 때마다 도망가서 영영 못 누른다.**
    /// 자리를 나눠 두면 예외 규칙 없이도 그 일이 안 생긴다.
    /// </summary>
    public const double BasePetButtonRow = 32;

    private const double BasePetRingDiameter = 124;
    private const double BasePetOuterThickness = 5;
    private const double BasePetInnerThickness = 4;
    private const double BasePetOwlHeight = 84;
    private const double BasePetButton = 24;

    private const double BaseRingDiameter = 62;
    private const double BaseOuterThickness = 6;
    private const double BaseInnerThickness = 5;
    private const double BaseRingGap = 7;
    private const double BaseInset = 4;
    private const double BaseButton = 20;
    private const double BaseCollapsedTrailing = 6;
    private const double BaseUpdateBadge = 18;

    /// <summary>
    /// Segoe MDL2 Assets 글리프. 윈도우 10 1809 부터 들어 있어 따로 챙길 것이 없다.
    ///
    /// **글자 그대로 적지 않고 코드로 적는다.** 사설 영역 문자라 편집기마다 다르게 보이고,
    /// 인코딩이 한 번 어긋나면 조용히 빈칸이나 네모로 바뀐다.
    /// </summary>
    private const string GlyphChevronRight = "\uE76C";
    private const string GlyphChevronLeft = "\uE76B";
    private const string GlyphSettings = "\uE713";
    private const string GlyphRefresh = "\uE72C";

    private static readonly OwlDocument Document = OwlDocument.Embedded;
    private static readonly Typeface Regular = new("Segoe UI");
    private static readonly Typeface Semibold = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Bold = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface Icons = new("Segoe MDL2 Assets");

    private readonly Dictionary<string, Dictionary<string, Brush>> owlBrushes = [];

    public HudMode Mode { get; set; } = HudMode.Expanded;
    public HudExpandSide ExpandSide { get; set; } = HudExpandSide.Right;
    public double Scale { get; set; } = 1;
    public double BackdropOpacity { get; set; } = AppSettings.DefaultBackdropOpacity;
    public bool IsDark { get; set; } = true;
    public string? VersionBadge { get; set; }

    /// <summary>그 딱지를 테스트판 색으로 그릴지. 렌더 통로가 실제 빌드와 무관하게 넘길 수 있게 받는다.</summary>
    public bool VersionBadgeIsTest { get; set; }

    /// <summary>새 버전이 있으면 버튼 반대편 모서리에 표시한다. 없으면 아무도 모른다.</summary>
    public bool HasUpdate { get; set; }

    public UsageSnapshot? Snapshot { get; set; }
    public bool IsDisconnected { get; set; }

    /// <summary>
    /// 주간을 다 썼다. 세션 링과 세션 숫자의 색을 뺀다 — 세션만 초록으로 남아 있으면
    /// 아직 여유가 있는 것처럼 보인다.
    /// </summary>
    public bool IsWeeklySpent { get; set; }

    /// <summary>마지막 성공값을 보여주는 중. 링과 숫자를 흐리게 한다.</summary>
    public bool IsStale { get; set; }

    public bool NeedsReauth { get; set; }
    public bool IsRefreshing { get; set; }
    public string? ErrorText { get; set; }

    /// <summary>다음 조회 예정 시각. null 이면 조회가 멈춘 상태다.</summary>
    public DateTimeOffset? NextPollAt { get; set; }

    /// <summary>이 앱 자신의 CPU·메모리를 아래 줄에 붙일지. 접힌 모습에는 자리가 없다.</summary>
    public bool ShowsProcessStats { get; set; }

    public ProcessUsage? Stats { get; set; }

    public string[]? OwlGrid { get; set; }
    public string OwlPaletteName { get; set; } = "normal";

    /// <summary>가운데에 무엇을 그릴지. 부엉이 말고는 전부 정지 그림이다.</summary>
    public IconStyle IconStyle { get; set; } = IconStyle.Owl;

    /// <summary>지금 마우스가 올라가 있는 자리. 창이 넣어 준다.</summary>
    public HudHit Hover { get; set; } = HudHit.None;

    public HudView()
    {
        // **뷰 전체에 EdgeMode.Aliased 를 걸지 않는다.**
        //
        // 예전에는 그렇게 했다. 픽셀 아트(부엉이·Clawd)를 부드럽게 하면 뭉개지기
        // 때문인데, 그 설정이 **링과 배경 모서리에까지 걸려서** 원이 계단처럼 각지게
        // 그려졌다. 이 화면에서 곡선이 대부분이라 잃는 쪽이 훨씬 컸다.
        //
        // 그래서 기본은 부드럽게 두고, 픽셀 아트만 <see cref="DrawPixelated"/> 안에서
        // 각지게 그린다. 지켜야 할 것이 좁은 쪽이니 그쪽을 감싸는 것이 맞다.
        foreach (var (name, palette) in Document.Palettes)
        {
            owlBrushes[name] = OwlRenderer.Brushes(palette);
        }
    }

    /// <summary>접힌 카드와 펫에는 자원 줄을 붙일 자리가 없다.</summary>
    private bool HasStatsRow => StatsRowIn(Mode);

    private bool StatsRowIn(HudMode mode) => ShowsProcessStats && mode == HudMode.Expanded;

    public Size DesiredHudSize => SizeFor(Mode);

    /// <summary>
    /// 어떤 보기의 창 크기. 아직 갈아타지 않은 보기의 크기도 물어볼 수 있어야 한다.
    ///
    /// **자원 줄이 붙는지는 <paramref name="mode"/> 로 따진다.** 지금 보기(<see cref="Mode"/>)로
    /// 따지면 아직 갈아타기 전이라 답이 틀린다 — 접힘에서 펼침으로 갈 때 17 이 모자란
    /// 크기로 옮겨가다 마지막 프레임에 툭 튄다.
    /// </summary>
    public Size SizeFor(HudMode mode) => mode switch
    {
        HudMode.Pet => new Size(BasePetWidth * Scale, (BasePetWidth + BasePetButtonRow) * Scale),
        HudMode.Collapsed => new Size(BaseCollapsedWidth * Scale, BaseCollapsedHeight * Scale),
        _ => new Size(
            BaseExpandedWidth * Scale,
            (BaseExpandedHeight + (StatsRowIn(mode) ? BaseStatsRowHeight : 0)) * Scale),
    };

    /// <summary>펫에서 마우스가 마스코트 위에 있는지. 창이 넣어 준다.</summary>
    public bool IsHovered { get; set; }

    public PetRingDisplay PetRingDisplay { get; set; } = PetRingDisplay.Hover;

    public bool ShowsPetRing => PetRingDisplay switch
    {
        PetRingDisplay.Always => true,
        PetRingDisplay.Never => false,
        _ => IsHovered,
    };

    /// <summary>
    /// 펫 링이 얼마나 떠올랐는지(0~1). **창이 애니메이션으로 밀어 준다.**
    ///
    /// 곧바로 켜고 끄면 마우스가 스칠 때마다 링이 번쩍인다. 맥은 0.18초에 걸쳐
    /// 떠오르게 한다. <c>AffectsRender</c> 라 값이 바뀔 때마다 알아서 다시 그린다.
    /// </summary>
    public static readonly DependencyProperty PetRingFadeProperty = DependencyProperty.Register(
        nameof(PetRingFade), typeof(double), typeof(HudView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double PetRingFade
    {
        get => (double)GetValue(PetRingFadeProperty);
        set => SetValue(PetRingFadeProperty, value);
    }

    /// <summary>맥과 같은 시간. 더 길면 굼떠 보이고 짧으면 곧바로 켜는 것과 다름없다.</summary>
    public static readonly TimeSpan PetRingFadeDuration = TimeSpan.FromSeconds(0.18);

    /// <summary>
    /// 그림을 가로로 이만큼 밀어서 그린다. **창이 넣어 준다.**
    ///
    /// 왼쪽으로 펼치는 설정에서 보기를 갈아탈 때만 0 이 아니다 — 그때는 오른쪽 변이
    /// 고정이라 내용도 오른쪽에 붙어 있어야 미끄러지지 않는다.
    /// </summary>
    public static readonly DependencyProperty RenderOffsetXProperty = DependencyProperty.Register(
        nameof(RenderOffsetX), typeof(double), typeof(HudView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double RenderOffsetX
    {
        get => (double)GetValue(RenderOffsetXProperty);
        set => SetValue(RenderOffsetXProperty, value);
    }

    private HudPalette Palette => IsDark ? HudPalette.Dark : HudPalette.Light;

    private bool ToRight => ExpandSide == HudExpandSide.Right;

    // ── 자리 재기 (그리는 쪽과 누르는 쪽이 같은 것을 본다) ──────────────

    /// <summary>버튼 세 칸. 차례는 접기 · 설정 · 새로고침이다.</summary>
    private Rect[] ButtonRects()
    {
        var size = DesiredHudSize;
        var button = BaseButton * Scale;

        // 펫은 링 **아래 줄**에 설정·새로고침 둘만 둔다. 접기 자리는 없다 —
        // 나가는 길은 마스코트를 두 번 누르는 것이다.
        if (Mode == HudMode.Pet)
        {
            var petButton = BasePetButton * Scale;
            var gap = 8 * Scale;
            var row = BasePetButtonRow * Scale;
            var petLeft = (size.Width - (petButton * 2 + gap)) / 2;
            var petTop = size.Height - row + (row - petButton) / 2;

            return
            [
                Rect.Empty,
                new Rect(petLeft, petTop, petButton, petButton),
                new Rect(petLeft + petButton + gap, petTop, petButton, petButton),
            ];
        }

        if (Mode == HudMode.Collapsed)
        {
            var trailing = BaseCollapsedTrailing * Scale;
            var x = ToRight ? size.Width - trailing - button : trailing;
            var top = (size.Height - button * 3) / 2;
            return
            [
                new Rect(x, top, button, button),
                new Rect(x, top + button, button, button),
                new Rect(x, top + button * 2, button, button),
            ];
        }

        var inset = BaseInset * Scale;
        var left = ToRight ? size.Width - inset - button * 3 : inset;
        return
        [
            new Rect(left, inset, button, button),
            new Rect(left + button, inset, button, button),
            new Rect(left + button * 2, inset, button, button),
        ];
    }

    /// <summary>새 버전 표시. 버튼 묶음 **반대편** 위 모서리다.</summary>
    private Rect UpdateBadgeRect()
    {
        var size = DesiredHudSize;
        var badge = BaseUpdateBadge * Scale;

        // 펫에서는 **오른쪽 위**다. 링(원)의 바깥 모서리라 마스코트를 안 가린다.
        if (Mode == HudMode.Pet)
        {
            var petInset = 2 * Scale;
            return new Rect(size.Width - badge - petInset, petInset, badge, badge);
        }

        var inset = BaseInset * Scale;
        var x = ToRight ? inset : size.Width - inset - badge;
        return new Rect(x, inset, badge, badge);
    }

    /// <summary>펫의 버튼 줄이 차지하는 자리. 링 영역과 겹치지 않는다.</summary>
    private Rect PetButtonRowRect()
    {
        var size = DesiredHudSize;
        var row = BasePetButtonRow * Scale;
        return new Rect(0, size.Height - row, size.Width, row);
    }

    /// <summary>링이 놓인 자리. 펫에서는 버튼 줄을 뺀 위쪽 가운데다.</summary>
    private Rect RingRect()
    {
        var size = DesiredHudSize;

        // 펫의 링은 버튼 줄을 뺀 **위쪽 영역**의 가운데다.
        if (Mode == HudMode.Pet)
        {
            var pet = BasePetRingDiameter * Scale;
            var area = BasePetWidth * Scale;
            return new Rect((size.Width - pet) / 2, (area - pet) / 2, pet, pet);
        }

        var ring = BaseRingDiameter * Scale;
        var rowHeight = Mode == HudMode.Collapsed ? size.Height : BaseExpandedHeight * Scale;
        var top = (rowHeight - ring) / 2;

        // 접힌 상태에서 왼쪽으로 펼치는 설정이면 버튼 열이 링 앞에 온다.
        var leading = (Mode, ToRight) switch
        {
            (HudMode.Collapsed, true) => 12 * Scale,
            (HudMode.Collapsed, false) =>
                (BaseCollapsedTrailing + BaseButton + 8) * Scale,
            (_, true) => 13 * Scale,
            _ => size.Width - 13 * Scale - ring,
        };
        return new Rect(leading, top, ring, ring);
    }

    /// <summary>
    /// 펫에서 **커서를 피해 비켜야 할 자리.**
    ///
    /// <see cref="HitTest"/> 를 쓰지 않는다. 두 가지 이유가 있다.
    ///
    /// 하나는 **넓이**다. 히트 테스트에서 마스코트로 치는 것은 링(위 128)뿐인데, 창은
    /// 아래로 32 가 더 있고 그 대부분이 빈 자리다. 아래에서 다가오는 커서는 영영
    /// 안 잡혀서 "안 도망간다"가 된다. 여기서는 **창 전체**를 본다.
    ///
    /// 다른 하나는 **좌표로 본다**는 것이다. 히트 테스트 결과(<c>Hover</c>)는 WPF 의
    /// 마우스 이벤트로만 바뀌는데, 비켜선 뒤 커서가 그 자리에 그대로 있으면 새 이벤트가
    /// 오지 않아 한 번 비키고 굳는다. 창이 움직여도 커서 좌표는 늘 지금 값이다.
    ///
    /// **누를 것 위에서는 비키지 않는다** — 누르러 온 손에서 달아나면 영영 못 누른다.
    /// </summary>
    public bool PetDodgeZoneContains(Point point)
    {
        if (Mode != HudMode.Pet) return false;

        var size = DesiredHudSize;
        if (point.X < 0 || point.Y < 0 || point.X >= size.Width || point.Y >= size.Height) return false;

        if (HasUpdate && UpdateBadgeRect().Contains(point)) return false;

        // 버튼 자리는 **보이든 안 보이든** 뺀다. 보이는지로 가르면 그 판단이 다시
        // 호버 상태에 매이는데, 그게 늦게 따라오면 누르려는 순간 달아난다.
        // 24×24 두 개뿐이라 빼도 아쉬울 것이 없다.
        var buttons = ButtonRects();
        return !buttons[1].Contains(point) && !buttons[2].Contains(point);
    }

    /// <summary>이 자리에 무엇이 있나. 창이 클릭·호버를 나눠 줄 때 쓴다.</summary>
    public HudHit HitTest(Point point)
    {
        if (HasUpdate && UpdateBadgeRect().Contains(point)) return HudHit.UpdateBadge;

        var buttons = ButtonRects();
        if (buttons[0].Contains(point)) return HudHit.Collapse;
        if (buttons[1].Contains(point)) return HudHit.Settings;
        if (buttons[2].Contains(point)) return HudHit.Refresh;

        // 마스코트는 **버튼 다음**이다. 펫에서는 링이 창의 거의 전부라 먼저 보면
        // 다른 것을 다 덮는다. 링과 마스코트가 겹쳐 있으므로 링 전체를 잡는다.
        if (RingRect().Contains(point)) return HudHit.Mascot;

        // 누를 것이 없는 자리들. 무엇인지 알려만 주므로 **맨 뒤**에 본다.
        if (VersionBadgeStrip() is { } version && version.Contains(point)) return HudHit.VersionBadge;
        if (StatsStrip() is { } stats && stats.Contains(point)) return HudHit.StatsRow;
        if (CountdownStrip() is { } countdown && countdown.Contains(point)) return HudHit.Countdown;

        // 펫 버튼 줄의 빈 자리. 누를 것은 없지만 **링과 버튼은 떠 있어야 한다.**
        if (Mode == HudMode.Pet && PetButtonRowRect().Contains(point)) return HudHit.PetRow;

        return HudHit.None;
    }

    /// <summary>
    /// 글로만 된 자리들의 대략적인 범위.
    ///
    /// 글자 폭을 정확히 재지 않는다 — 재려면 <see cref="FormattedText"/> 를 만들어야 하는데,
    /// 그건 마우스가 움직일 때마다 도는 자리에 두기엔 비싸다. **누를 것이 없는 자리**라
    /// 조금 넉넉해도 탈이 없다.
    /// </summary>
    private Rect? CountdownStrip()
    {
        if (Mode is HudMode.Collapsed or HudMode.Pet) return null;

        var s = Scale;
        var size = DesiredHudSize;
        var inset = HasStatsRow ? 13 * s : 10 * s;
        var bottom = HasStatsRow ? size.Height - 4 * s : BaseExpandedHeight * s - 7 * s;
        var width = 76 * s;
        var top = bottom - 13 * s;

        return new Rect(ToRight ? size.Width - inset - width : inset, top, width, 13 * s);
    }

    private Rect? StatsStrip()
    {
        if (!HasStatsRow) return null;

        var s = Scale;
        var size = DesiredHudSize;
        var inset = 13 * s;
        var width = 108 * s;
        var bottom = size.Height - 4 * s;

        return new Rect(ToRight ? inset : size.Width - inset - width, bottom - 13 * s, width, 13 * s);
    }

    private Rect? VersionBadgeStrip()
    {
        if (Mode == HudMode.Collapsed || VersionBadge is null) return null;

        var s = Scale;
        var rect = UpdateBadgeRect();
        var width = 46 * s;
        var left = ToRight
            ? (HasUpdate ? rect.Right + 3 * s : rect.Left)
            : (HasUpdate ? rect.Left - 3 * s : rect.Right) - width;

        return new Rect(left, rect.Top, width, rect.Height);
    }

    /// <summary>그 자리가 무엇인지 알려주는 문구. 작은 그림뿐이라 이게 없으면 물어볼 곳이 없다.</summary>
    public string? TooltipFor(HudHit hit) => hit switch
    {
        HudHit.Collapse => Mode == HudMode.Collapsed ? "펼치기" : "접기",
        HudHit.Settings => "설정",
        HudHit.Refresh => ErrorText is { } error ? $"갱신 실패: {error} — 눌러서 다시 시도" : "새로고침",
        HudHit.UpdateBadge => "새 버전이 나왔다 — 눌러서 확인",
        // 펫에는 숫자를 안 그린다. 올려서 읽는 것이 그 자리를 대신한다.
        HudHit.Mascot => Mode == HudMode.Pet
            ? SummaryText ?? "두 번 눌러 원래 보기로"
            : "두 번 눌러 마스코트만 보기",
        HudHit.Countdown => CountdownHelp,
        HudHit.StatsRow => "dong-csu 자신이 쓰는 CPU와 메모리",
        HudHit.VersionBadge => VersionBadgeIsTest ? "테스트 빌드다 — 배포본이 아니다" : "지금 버전",
        _ => null,
    };

    /// <summary>카운트다운 자리가 지금 무슨 뜻인지. 숫자만으로는 멈춘 건지 도는 건지 모른다.</summary>
    private string CountdownHelp
    {
        get
        {
            if (NeedsReauth) return $"Claude Code 재로그인이 필요하다 — {ErrorText}";
            if (ErrorText is { } error) return $"갱신 실패로 마지막 성공값을 보여주는 중 — {error}";
            if (NextPollAt is null) return "화면이 꺼져 있어 조회를 멈춘 상태";
            return "다음 사용량 조회까지 남은 시간";
        }
    }

    /// <summary>펫에서 마스코트에 올렸을 때 띄울 요약. 창이 넣어 준다.</summary>
    public string? SummaryText { get; set; }

    // ── 그리기 ──────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext context)
    {
        var size = DesiredHudSize;
        var palette = Palette;
        var s = Scale;

        // 보기를 갈아타는 동안 왼쪽으로 펼치는 설정이면 내용을 오른쪽 변에 붙여 둔다.
        var shifted = Math.Abs(RenderOffsetX) > 0.01;
        if (shifted) context.PushTransform(new TranslateTransform(RenderOffsetX, 0));

        if (Mode.ShowsBackdrop())
        {
            // 불투명도를 브러시 속성으로 두면 색마다 캐시가 갈리지 않는다. 알파에 섞는다.
            var opacity = Math.Clamp(BackdropOpacity, AppSettings.MinBackdropOpacity, 1);
            var card = palette.Backdrop;
            var backdrop = Frozen(Color.FromArgb(
                (byte)Math.Round(opacity * 255), card.R, card.G, card.B));
            var border = Paint.Pen(palette.Border, 1);

            var radius = (Mode == HudMode.Collapsed ? 26 : 20) * s;
            context.DrawRoundedRectangle(
                backdrop, border, new Rect(0.5, 0.5, size.Width - 1, size.Height - 1), radius, radius);
        }

        // **보이지 않지만 마우스를 받는 바닥.**
        //
        // WPF 는 *그린 자리*에만 마우스를 준다. 펫에는 배경이 없어서, 버튼 줄에 아무것도
        // 안 그려진 동안에는 그 위로 커서를 옮겨도 호버가 안 잡힌다 — 그런데 버튼은
        // 호버해야 나타난다. 닭과 달걀이 되어 **영영 안 나타난다.**
        //
        // **알파 0 으로는 안 된다.** 이 창은 `AllowsTransparency` 라 레이어드 창이고,
        // 히트테스트를 WPF 가 아니라 **OS 가 픽셀 알파로** 한다. 완전 투명하게 그리면
        // 그려도 마우스가 그냥 통과한다. 알파를 1 만 줘도 잡히고, 눈에는 안 보인다.
        //
        // 맥이 `liveRects` 로 하는 일을 여기서는 이렇게 한다. 아무것도 안 그린 나머지
        // 자리는 그대로 클릭이 통과하므로, 뒤에 있는 창을 누를 수 있다.
        if (Mode == HudMode.Pet)
        {
            var live = Frozen(Color.FromArgb(1, 0, 0, 0));
            context.DrawRectangle(live, null, RingRect());
            context.DrawRectangle(live, null, PetButtonRowRect());
        }

        // 마지막 성공값을 보여주는 중이면 링과 숫자를 흐리게 해 지금 값이 아님을 드러낸다.
        var dim = IsStale;
        if (dim) context.PushOpacity(0.45);
        DrawRingAndOwl(context, palette, s);
        if (Mode == HudMode.Expanded) DrawMetrics(context, palette, s);
        if (dim) context.Pop();

        // 펫에는 숫자도 버전 딱지도 없다. 링 아래 버튼 둘과 새 버전 표시만 얹는다.
        if (Mode == HudMode.Pet)
        {
            DrawPetButtons(context, palette, s);
            if (HasUpdate) DrawUpdateBadge(context, UpdateBadgeRect(), palette, s);
            if (shifted) context.Pop();
            return;
        }

        DrawStatsRow(context, size, palette, s);
        DrawCountdown(context, size, palette, s);
        DrawCornerBadges(context, palette, s);
        DrawButtons(context, palette, s);

        if (shifted) context.Pop();
    }

    private void DrawRingAndOwl(DrawingContext context, HudPalette palette, double s)
    {
        var frame = RingRect();
        var center = new Point(frame.Left + frame.Width / 2, frame.Top + frame.Height / 2);
        var isPet = Mode == HudMode.Pet;

        // 펫의 링은 카드보다 얇다. 마스코트가 주인공이라 테두리처럼만 두른다.
        var outer = (isPet ? BasePetOuterThickness : BaseOuterThickness) * s;
        var innerThickness = (isPet ? BasePetInnerThickness : BaseInnerThickness) * s;

        // 펫에서는 링을 감췄다 보였다 한다. 감춰도 창 크기는 그대로다.
        var ringOpacity = isPet
            ? PetRingFade * (IsDisconnected ? 0.4 : 0.95)
            : 1;

        if (ringOpacity > 0)
        {
            if (ringOpacity < 1) context.PushOpacity(ringOpacity);
            RingRenderer.Draw(
                context,
                center,
                frame.Width,
                outer,
                innerThickness,
                BaseRingGap * s,
                Snapshot?.FiveHour?.Utilization,
                Snapshot?.SevenDay?.Utilization,
                palette.RingTrack,
                grayscale: IsDisconnected,
                // 주간을 다 썼으면 세션은 쓸 수 없다. 초록으로 남겨 두면 여유가
                // 있는 것처럼 보이므로 색을 빼서 죽은 것으로 그린다.
                sessionSpentColor: IsWeeklySpent && !IsDisconnected ? palette.RingSpent : null);
            if (ringOpacity < 1) context.Pop();
        }

        if (isPet)
        {
            // 움직이는 캐릭터는 팔레트가 회색으로 바뀌어 스스로 드러낸다. 정지 그림은
            // 그럴 수 없는데, 펫에는 링도 안 보여서 조회가 끊긴 걸 알 방법이 없어진다.
            // 링에 이미 쓰는 방식대로 흐리게 해서 지금 값이 아님을 알린다.
            var dim = IsDisconnected && !IconStyle.IsAnimated();
            if (dim) context.PushOpacity(0.4);
            // 마스코트가 주인공이라 링 안지름과 무관하게 크게 잡는다.
            DrawIcon(context, center, BasePetOwlHeight * s);
            if (dim) context.Pop();
            return;
        }

        // 맥과 같은 산식 — 안지름에서 안쪽 링 두께 두 겹과 여유 4 를 뺀다.
        var inner = frame.Width - outer * 2 - BaseRingGap * s;
        var available = inner - innerThickness * 2 - 4 * s;
        if (available <= 0) return;

        DrawIcon(context, center, available);
    }

    private void DrawIcon(DrawingContext context, Point center, double available)
    {
        if (available <= 0) return;

        switch (IconStyle)
        {
            case IconStyle.OwlSheet:
                // 시트를 못 읽으면 격자로 떨어진다 — 가운데가 비면 안 된다.
                if (!MascotRenderer.Draw(context, MascotFrame, Square(center, available), MascotFlipped))
                {
                    DrawPixelated(context, ctx => DrawOwl(ctx, center, available));
                }
                break;

            case IconStyle.Owl:
                DrawPixelated(context, ctx => DrawOwl(ctx, center, available));
                break;

            case IconStyle.Clawd:
                // 11×8 이라 **폭을 기준으로** 맞춘다. 높이로 맞추면 옆으로 삐져나온다.
                DrawPixelated(context, ctx => DrawClawdCentered(ctx, center, available));
                break;

            case IconStyle.AppIcon:
                var box = Square(center, available);
                // 그림이 없으면(뽑기 실패) 마크로 떨어진다 — 가운데가 비면 안 된다.
                if (!IconRenderer.DrawAppIcon(context, box)) IconRenderer.DrawMark(context, box);
                break;

            default:
                IconRenderer.DrawMark(context, Square(center, available));
                break;
        }
    }

    /// <summary>지금 그릴 그림 칸. 창이 넣어 준다 — 격자와 **같은 신호**에서 나온다.</summary>
    public MascotSprite MascotFrame { get; set; } = MascotSprite.Idle;

    /// <summary>
    /// 그림을 좌우로 뒤집을지. 걷기·달리기 칸이 왼쪽을 보고 그려져 있어서, 오른쪽으로
    /// 갈 때 켜진다. **격자 부엉이에는 안 걸린다** — 정면 대칭이라 뒤집어도 같다.
    /// </summary>
    public bool MascotFlipped { get; set; }

    private void DrawOwl(DrawingContext context, Point center, double available)
    {
        if (OwlGrid is not { } grid) return;

        var cell = OwlRenderer.CellSize(available, Document.Grid.Lines);
        var size = OwlRenderer.MeasuredSize(cell, Document.Grid);
        var origin = new Point(
            Math.Round(center.X - size.Width / 2),
            Math.Round(center.Y - size.Height / 2));

        var brushes = owlBrushes.TryGetValue(OwlPaletteName, out var found)
            ? found
            : owlBrushes["normal"];
        OwlRenderer.Draw(context, grid, brushes, origin, cell);
    }

    private void DrawClawdCentered(DrawingContext context, Point center, double available)
    {
        var width = available;
        var height = width * ClawdMark.Lines / ClawdMark.Columns;
        var bounds = new Rect(
            Math.Round(center.X - width / 2), Math.Round(center.Y - height / 2), width, height);

        var eye = Color.FromArgb((byte)(IsDark ? 0xE0 : 0xBF), 0, 0, 0);
        IconRenderer.DrawClawd(context, bounds, eye);
    }

    private static Rect Square(Point center, double side) =>
        new(center.X - side / 2, center.Y - side / 2, side, side);

    /// <summary>
    /// 이 안에서만 각지게 그린다.
    ///
    /// 픽셀 아트(부엉이·Clawd)는 가장자리를 부드럽게 하면 뭉개진다. **딱 그것만**
    /// 감싼다 — 뷰 전체에 걸면 링과 배경 모서리까지 계단처럼 각져 버린다.
    /// </summary>
    private static void DrawPixelated(DrawingContext context, Action<DrawingContext> body)
    {
        var group = new DrawingGroup();
        RenderOptions.SetEdgeMode(group, EdgeMode.Aliased);
        using (var ctx = group.Open()) body(ctx);
        group.Freeze();
        context.DrawDrawing(group);
    }

    /// <summary>세션 · 주간 두 블록. 각 블록은 두 줄이고, 앞의 색점이 링과 짝을 지어 준다.</summary>
    private void DrawMetrics(DrawingContext context, HudPalette palette, double s)
    {
        var now = DateTimeOffset.Now;
        // 링과 같은 규칙이다. 링만 회색이고 점은 초록이면 앞뒤가 안 맞는다.
        var session = MeasureBlock("세션", Snapshot?.FiveHour, now, palette, s, IsWeeklySpent);
        var weekly = MeasureBlock("주간", Snapshot?.SevenDay, now, palette, s);

        var totalHeight = session.Height + 8 * s + weekly.Height;
        var top = (BaseExpandedHeight * s - totalHeight) / 2;

        var ring = RingRect();
        var width = Math.Max(session.Width, weekly.Width);
        // 왼쪽으로 펼치면 링이 오른쪽에 있으므로 블록을 그 앞에 붙인다.
        var left = ToRight ? ring.Right + 13 * s : ring.Left - 13 * s - width;

        DrawBlock(context, session, new Point(left, top), palette, s);
        DrawBlock(context, weekly, new Point(left, top + session.Height + 8 * s), palette, s);
    }

    /// <summary>
    /// 한 블록을 재어 둔 것.
    ///
    /// 재는 것과 그리는 것을 갈라야 한다 — 왼쪽으로 펼치는 설정에서는 두 블록 중
    /// 넓은 쪽에 맞춰 오른쪽 정렬해야 하는데, 그러려면 그리기 전에 폭을 알아야 한다.
    /// 색을 따로 들고 있는 이유는 <see cref="FormattedText"/> 에서 칠한 색을 도로
    /// 꺼내올 방법이 없어서다(그림자를 깐 뒤 원래 색으로 되돌려야 한다).
    /// </summary>
    private readonly record struct Block(
        FormattedText Title,
        FormattedText Percent,
        FormattedText Remaining,
        Color TitleColor,
        Color PercentColor,
        Color RemainingColor,
        Color Dot,
        double Width,
        double Height);

    private Block MeasureBlock(
        string title, UsageWindow? window, DateTimeOffset now, HudPalette palette, double s,
        bool isSpent = false)
    {
        var titleText = Text(title, 10 * s, Semibold, palette.Secondary);
        var percentText = Text(
            window is { } value ? $"{Math.Round(value.Utilization):F0}%" : "—",
            14 * s, Bold, palette.Primary);
        var remainingText = Text(
            RemainingTime.Text(window?.ResetsAt, now), 9.5 * s, Regular, palette.Tertiary);

        // 점 색이 곧 그 창의 링 색이다. 이게 바깥 링 = 세션, 안쪽 링 = 주간을 이어 준다.
        var dot = window is not { } filled ? palette.MutedDot
            : isSpent ? palette.RingSpent
            : UsageColor.For(filled.Utilization).ToColor();

        var firstLine = 5 * s + 5 * s + titleText.Width + 5 * s + percentText.Width;
        var firstHeight = Math.Max(titleText.Height, percentText.Height);

        return new Block(
            titleText, percentText, remainingText,
            palette.Secondary, palette.Primary, palette.Tertiary, dot,
            Width: Math.Max(firstLine, remainingText.Width),
            Height: firstHeight + 1 * s + remainingText.Height);
    }

    private void DrawBlock(DrawingContext context, Block block, Point origin, HudPalette palette, double s)
    {
        var firstHeight = Math.Max(block.Title.Height, block.Percent.Height);

        var dotRadius = 2.5 * s;
        var dotBrush = Frozen(block.Dot);
        context.DrawEllipse(
            dotBrush, null,
            new Point(origin.X + dotRadius, origin.Y + firstHeight / 2),
            dotRadius, dotRadius);

        var titleX = origin.X + 5 * s + 5 * s;
        var titleY = origin.Y + (firstHeight - block.Title.Height) / 2;
        var percentX = titleX + block.Title.Width + 5 * s;
        var percentY = origin.Y + (firstHeight - block.Percent.Height) / 2;
        var remainingY = origin.Y + firstHeight + 1 * s;

        // **그림자를 먼저 깐다.** WPF 의 DrawingContext 에는 글자 그림자가 없어서
        // 한 픽셀 내린 어두운 사본을 밑에 깔아 대신한다. 흐림은 없지만, 반투명 배경
        // 위에서 글자가 묻히는 것은 이것만으로도 크게 나아진다.
        var shadow = Frozen(palette.TextShadow);
        DrawShadowed(context, block.Title, new Point(titleX, titleY), block.TitleColor, shadow, s);
        DrawShadowed(context, block.Percent, new Point(percentX, percentY), block.PercentColor, shadow, s);
        DrawShadowed(context, block.Remaining, new Point(origin.X, remainingY), block.RemainingColor, shadow, s);
    }

    private static void DrawShadowed(
        DrawingContext context, FormattedText text, Point at, Color color, Brush shadow, double s)
    {
        text.SetForegroundBrush(shadow);
        context.DrawText(text, new Point(at.X, at.Y + Math.Max(0.5, s)));
        text.SetForegroundBrush(Frozen(color));
        context.DrawText(text, at);
    }

    /// <summary>
    /// 아래 모서리. 평소에는 다음 조회까지 남은 시간, 값이 낡았으면 그 사실을 대신 알린다.
    ///
    /// 낡은 숫자를 지금 값으로 믿게 두는 것이 제일 나쁘다. 그래서 카운트다운보다
    /// 경고가 이긴다.
    /// </summary>
    private void DrawCountdown(DrawingContext context, Size size, HudPalette palette, double s)
    {
        if (Mode == HudMode.Collapsed) return;

        var now = DateTimeOffset.Now;

        // 자원 줄이 붙으면 카운트다운도 거기로 내려가 같은 높이에 놓인다.
        var inset = HasStatsRow ? 13 * s : 10 * s;
        var bottom = HasStatsRow
            ? size.Height - 4 * s
            : BaseExpandedHeight * s - 7 * s;
        var right = size.Width - inset;

        if (StaleLabel(now) is { } warning)
        {
            var text = Text(warning, 9.5 * s, Semibold, palette.Warning);
            var x = ToRight ? right - text.Width : inset;
            context.DrawText(text, new Point(x, bottom - text.Height));
            return;
        }

        var label = Text("조회", 8.5 * s, Semibold, palette.Faint);
        var clockColor = palette.Tertiary;
        if (IsRefreshing) clockColor = Color.FromArgb((byte)(clockColor.A * 0.55), clockColor.R, clockColor.G, clockColor.B);
        var clock = Text(RemainingTime.CountdownText(NextPollAt, now), 9.5 * s, Regular, clockColor);

        var width = label.Width + 4 * s + clock.Width;
        var startX = ToRight ? right - width : inset;
        var lineHeight = Math.Max(label.Height, clock.Height);

        context.DrawText(label, new Point(startX, bottom - lineHeight + (lineHeight - label.Height) / 2));
        context.DrawText(clock, new Point(
            startX + label.Width + 4 * s, bottom - lineHeight + (lineHeight - clock.Height) / 2));
    }

    /// <summary>
    /// 카드 맨 아래 — 이 앱 자신이 쓰는 CPU 와 메모리.
    ///
    /// 사용량 API 와는 아무 상관이 없다. **항상 떠 있는 앱이 컴퓨터를 얼마나 먹는지**를
    /// 사용자가 직접 확인할 수 있어야 해서 둔다. 카운트다운 반대편에 놓는다.
    /// </summary>
    private void DrawStatsRow(DrawingContext context, Size size, HudPalette palette, double s)
    {
        if (!HasStatsRow) return;

        var stats = Stats ?? new ProcessUsage(0, 0);
        var inset = 13 * s;
        var bottom = size.Height - 4 * s;

        var parts = new (FormattedText Title, FormattedText Value)[]
        {
            (Text("CPU", 8 * s, Semibold, palette.Faint), Text(stats.CpuText, 9 * s, Regular, palette.Tertiary)),
            (Text("MEM", 8 * s, Semibold, palette.Faint), Text(stats.MemoryText, 9 * s, Regular, palette.Tertiary)),
        };

        var width = 0.0;
        foreach (var (title, value) in parts) width += title.Width + 3 * s + value.Width;
        width += 6 * s;   // 두 묶음 사이

        var x = ToRight ? inset : size.Width - inset - width;
        foreach (var (title, value) in parts)
        {
            var lineHeight = Math.Max(title.Height, value.Height);
            context.DrawText(title, new Point(x, bottom - lineHeight + (lineHeight - title.Height) / 2));
            x += title.Width + 3 * s;
            context.DrawText(value, new Point(x, bottom - lineHeight + (lineHeight - value.Height) / 2));
            x += value.Width + 6 * s;
        }
    }

    private string? StaleLabel(DateTimeOffset now)
    {
        if (NeedsReauth) return "재로그인 필요";
        if (!IsStale || Snapshot is not { } snapshot) return null;
        return RemainingTime.AgeText(snapshot.FetchedAt, now);
    }

    /// <summary>버튼 묶음 반대편 위 모서리 — 새 버전 표시와 버전 딱지.</summary>
    private void DrawCornerBadges(DrawingContext context, HudPalette palette, double s)
    {
        // 접은 카드는 108 뿐이라 버전 딱지를 붙이면 링 위에 겹친다.
        var badge = Mode == HudMode.Collapsed ? null : VersionBadge;
        if (!HasUpdate && badge is null) return;

        var rect = UpdateBadgeRect();
        if (HasUpdate) DrawUpdateBadge(context, rect, palette, s);

        if (badge is null) return;

        var color = VersionBadgeIsTest ? palette.TestBadge : palette.Faint;
        var text = Text(badge, 9 * s, Semibold, color);

        // 표시가 없으면 그 자리부터, 있으면 그 옆에서 시작한다.
        var x = ToRight
            ? (HasUpdate ? rect.Right + 3 * s : rect.Left) + 5 * s
            : (HasUpdate ? rect.Left - 3 * s : rect.Right) - 5 * s - text.Width;
        var y = rect.Top + (rect.Height - text.Height) / 2;

        // 테스트판은 알약 배경을 깔아 곁눈으로도 걸리게 한다.
        if (VersionBadgeIsTest)
        {
            var pill = new Rect(
                x - 5 * s, y - 1 * s, text.Width + 10 * s, text.Height + 2 * s);
            var radius = pill.Height / 2;
            context.DrawRoundedRectangle(
                Frozen(Color.FromArgb(0x2E, color.R, color.G, color.B)), null, pill, radius, radius);
        }

        // **그림자 없이는 읽히지 않는다.** 새 버전 표시가 붙으면 딱지가 그만큼 밀려서
        // 링 위로 올라앉는데, 옅은 회색 글자가 링 트랙과 겹치면 그대로 묻힌다.
        DrawShadowed(context, text, new Point(x, y), color, Frozen(palette.TextShadow), s);
    }

    /// <summary>
    /// 아래를 가리키는 화살표가 든 동그라미.
    ///
    /// 글꼴 글리프를 쓰지 않고 직접 그린다 — 배지가 18 밖에 안 되는데 글리프는 자간과
    /// 기준선이 크기마다 달라져서, 배율을 바꾸면 동그라미 안에서 화살표가 떠다닌다.
    /// </summary>
    private static void DrawUpdateBadge(DrawingContext context, Rect rect, HudPalette palette, double s)
    {
        var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

        // **18 은 누르는 자리이지 그림 크기가 아니다.** 맥도 18짜리 프레임 안에 13짜리
        // 글리프를 넣는다. 18을 꽉 채워 그리면 배경의 둥근 모서리(반지름 20) 바깥으로
        // 삐져나가서, 점이 카드 밖 허공에 떠 있는 것처럼 보인다.
        var radius = 6.5 * s;
        context.DrawEllipse(Frozen(palette.UpdateBadge), null, center, radius, radius);

        var white = Frozen(Colors.White);
        var stem = 1.4 * s;
        var head = 3.1 * s;
        var top = center.Y - 3.4 * s;
        var tip = center.Y + 3.4 * s;

        context.DrawRectangle(white, null, new Rect(center.X - stem / 2, top, stem, tip - top - head));

        var arrow = new StreamGeometry();
        using (var ctx = arrow.Open())
        {
            ctx.BeginFigure(new Point(center.X, tip), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(center.X - head, tip - head), true, false);
            ctx.LineTo(new Point(center.X + head, tip - head), true, false);
        }
        arrow.Freeze();
        context.DrawGeometry(white, null, arrow);
    }

    /// <summary>
    /// 링 밖 아래에 붙는 동그란 버튼 둘.
    ///
    /// **링과 같은 조건으로만 보인다** — 펫은 마스코트만 띄우는 보기라, 버튼이 늘
    /// 떠 있으면 그 뜻이 사라진다. 다만 자리는 늘 살아 있어서, 다가가면 뜨고 눌린다.
    /// </summary>
    private void DrawPetButtons(DrawingContext context, HudPalette palette, double s)
    {
        // 링과 같은 박자로 떠오른다. 버튼만 곧바로 켜지면 따로 노는 것처럼 보인다.
        if (PetRingFade <= 0) return;
        if (PetRingFade < 1) context.PushOpacity(PetRingFade);

        var rects = ButtonRects();
        DrawPetButton(context, rects[1], GlyphSettings, HudHit.Settings, palette, s);
        DrawPetButton(context, rects[2], GlyphRefresh, HudHit.Refresh, palette, s);

        if (PetRingFade < 1) context.Pop();
    }

    private void DrawPetButton(
        DrawingContext context, Rect rect, string glyph, HudHit target, HudPalette palette, double s)
    {
        var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

        // 투명한 배경 위에 뜨는 버튼이라 **제 바탕이 있어야 읽힌다.**
        var backdrop = palette.Backdrop;
        var fill = Frozen(Color.FromArgb(0xEB, backdrop.R, backdrop.G, backdrop.B));
        var border = Paint.Pen(palette.RingTrack, Math.Max(1, s));
        context.DrawEllipse(fill, border, center, rect.Width / 2, rect.Height / 2);

        var color = Hover == target ? palette.ControlActive : palette.ControlIdle;
        if (target == HudHit.Refresh && IsRefreshing)
        {
            color = Color.FromArgb((byte)(color.A * 0.35), color.R, color.G, color.B);
        }

        var text = Text(glyph, 11 * s, Icons, color);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private void DrawButtons(DrawingContext context, HudPalette palette, double s)
    {
        var rects = ButtonRects();

        // 눌렀을 때 창이 움직일 방향을 가리킨다.
        var chevron = (Mode == HudMode.Collapsed) == ToRight ? GlyphChevronRight : GlyphChevronLeft;

        DrawButton(context, rects[0], chevron, HudHit.Collapse, palette.ControlIdle, palette, s);
        DrawButton(context, rects[1], GlyphSettings, HudHit.Settings, palette.ControlIdle, palette, s);

        // 갱신에 실패해 화면 숫자가 낡았으면 버튼 자체를 경고색으로 물들인다.
        var refreshTint = ErrorText is null ? palette.ControlIdle : palette.Warning;
        DrawButton(context, rects[2], GlyphRefresh, HudHit.Refresh, refreshTint, palette, s);
    }

    private void DrawButton(
        DrawingContext context,
        Rect rect,
        string glyph,
        HudHit target,
        Color idle,
        HudPalette palette,
        double s)
    {
        var hovering = Hover == target;
        if (hovering)
        {
            context.DrawEllipse(
                Frozen(palette.ControlHoverFill), null,
                new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2),
                rect.Width / 2, rect.Height / 2);
        }

        var color = hovering && idle != palette.Warning ? palette.ControlActive : idle;
        // 갱신 중에는 흐리게. 돌아가는 애니메이션은 유휴 상태에서 계속 도는 위험이 있어 쓰지 않는다.
        if (target == HudHit.Refresh && IsRefreshing)
        {
            color = Color.FromArgb((byte)(color.A * 0.35), color.R, color.G, color.B);
        }

        var text = Text(glyph, 9.5 * s, Icons, color);
        context.DrawText(text, new Point(
            rect.Left + (rect.Width - text.Width) / 2,
            rect.Top + (rect.Height - text.Height) / 2));
    }

    private FormattedText Text(string value, double size, Typeface face, Color color) =>
        new(value, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, face,
            Math.Max(1, size), Frozen(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);


    /// <summary>색마다 하나씩만 만들어 나눠 쓴다. 자세히는 <see cref="Paint"/>.</summary>
    private static SolidColorBrush Frozen(Color color) => Paint.Brush(color);
}
