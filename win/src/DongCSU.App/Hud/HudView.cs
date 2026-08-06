using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DongCSU.App.Rendering;
using DongCSU.Core;
using DongCSU.Core.Owl;
using DongCSU.Core.Usage;

namespace DongCSU.App.Hud;

/// <summary>어둡게 / 밝게 한 벌.</summary>
public sealed record HudPalette(Color Backdrop, Color Primary, Color Secondary, Color Track)
{
    public static readonly HudPalette Dark = new(
        Backdrop: Color.FromRgb(0x1C, 0x1C, 0x1E),
        Primary: Color.FromRgb(0xF2, 0xF2, 0xF7),
        Secondary: Color.FromRgb(0x9A, 0x9A, 0xA0),
        Track: Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF));

    public static readonly HudPalette Light = new(
        Backdrop: Color.FromRgb(0xF7, 0xF7, 0xF9),
        Primary: Color.FromRgb(0x1C, 0x1C, 0x1E),
        Secondary: Color.FromRgb(0x6C, 0x6C, 0x70),
        Track: Color.FromArgb(0x24, 0x00, 0x00, 0x00));
}

/// <summary>
/// HUD 를 통째로 그린다.
///
/// 배율 1 기준 치수는 **맥판과 같다** — 펼치면 240×88, 접으면 88×88.
/// 두 판이 나란히 놓였을 때 크기가 다르면 같은 앱으로 안 보인다.
/// </summary>
public sealed class HudView : FrameworkElement
{
    public const double BaseExpandedWidth = 240;
    public const double BaseExpandedHeight = 88;
    public const double BaseCollapsedSize = 88;
    private const double BaseRingDiameter = 62;
    private const double BaseRingThickness = 5;

    private static readonly OwlDocument Document = OwlDocument.Embedded;
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Typeface NumberFace = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly Dictionary<string, Dictionary<string, Brush>> owlBrushes = [];

    public HudMode Mode { get; set; } = HudMode.Expanded;
    public double Scale { get; set; } = 1;
    public double BackdropOpacity { get; set; } = 0.72;
    public bool IsDark { get; set; } = true;
    public string? VersionBadge { get; set; }

    public UsageSnapshot? Snapshot { get; set; }
    public bool IsDisconnected { get; set; }
    public string[]? OwlGrid { get; set; }
    public string OwlPaletteName { get; set; } = "normal";

    public HudView()
    {
        // 픽셀 아트라 가장자리를 부드럽게 하면 안 된다. 뭉개진다.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        foreach (var (name, palette) in Document.Palettes)
        {
            owlBrushes[name] = OwlRenderer.Brushes(palette);
        }
    }

    public Size DesiredHudSize => Mode == HudMode.Collapsed
        ? new Size(BaseCollapsedSize * Scale, BaseCollapsedSize * Scale)
        : new Size(BaseExpandedWidth * Scale, BaseExpandedHeight * Scale);

    private HudPalette Palette => IsDark ? HudPalette.Dark : HudPalette.Light;

    protected override void OnRender(DrawingContext context)
    {
        var size = DesiredHudSize;
        var palette = Palette;
        var s = Scale;

        // 배경. 모서리를 깎는다.
        var backdrop = new SolidColorBrush(palette.Backdrop)
        {
            Opacity = Math.Clamp(BackdropOpacity, 0.05, 1),
        };
        backdrop.Freeze();
        var radius = (Mode == HudMode.Collapsed ? 26 : 20) * s;
        context.DrawRoundedRectangle(backdrop, null, new Rect(0, 0, size.Width, size.Height), radius, radius);

        DrawRingAndOwl(context, size, palette, s);

        if (Mode == HudMode.Expanded) DrawText(context, size, palette, s);
        if (VersionBadge is { } badge) DrawVersionBadge(context, badge, palette, s);
    }

    private void DrawRingAndOwl(DrawingContext context, Size size, HudPalette palette, double s)
    {
        var diameter = BaseRingDiameter * s;
        var center = Mode == HudMode.Collapsed
            ? new Point(size.Width / 2, size.Height / 2)
            : new Point(14 * s + diameter / 2, size.Height / 2);

        RingRenderer.Draw(
            context,
            center,
            diameter,
            BaseRingThickness * s,
            Snapshot?.FiveHour?.Utilization,
            Snapshot?.SevenDay?.Utilization,
            palette.Track,
            grayscale: IsDisconnected);

        if (OwlGrid is not { } grid) return;

        // 부엉이는 안쪽 링에 닿지 않게. 안지름에서 조금 더 줄인다.
        var inner = diameter - (BaseRingThickness * s + RingRenderer.Gap) * 4;
        var cell = OwlRenderer.CellSize(inner * 0.92, Document.Grid.Lines);
        var owlSize = OwlRenderer.MeasuredSize(cell, Document.Grid);
        var origin = new Point(
            Math.Round(center.X - owlSize.Width / 2),
            Math.Round(center.Y - owlSize.Height / 2));

        var brushes = owlBrushes.TryGetValue(OwlPaletteName, out var found)
            ? found
            : owlBrushes["normal"];
        OwlRenderer.Draw(context, grid, brushes, origin, cell);
    }

    private void DrawText(DrawingContext context, Size size, HudPalette palette, double s)
    {
        var left = (14 + BaseRingDiameter + 12) * s;
        var right = size.Width - 12 * s;
        var primary = Frozen(palette.Primary);
        var secondary = Frozen(palette.Secondary);
        var now = DateTimeOffset.Now;

        if (Snapshot is not { } snapshot)
        {
            var waiting = Text(IsDisconnected ? "연결 끊김" : "불러오는 중…", 12 * s, Face, secondary);
            context.DrawText(waiting, new Point(left, size.Height / 2 - waiting.Height / 2));
            return;
        }

        var y = 12 * s;

        // 플랜 이름. 없으면(API 사용자) 그 줄을 비우지 않고 위로 당긴다.
        if (snapshot.PlanName is { } plan)
        {
            var planText = Text(plan, 11 * s, Face, secondary);
            context.DrawText(planText, new Point(left, y));
            y += planText.Height + 3 * s;
        }

        y += DrawRow(context, "세션", snapshot.FiveHour, left, right, y, s, primary, secondary, now);
        y += 2 * s;
        DrawRow(context, "주간", snapshot.SevenDay, left, right, y, s, primary, secondary, now);
    }

    private double DrawRow(
        DrawingContext context,
        string label,
        UsageWindow? window,
        double left,
        double right,
        double y,
        double s,
        Brush primary,
        Brush secondary,
        DateTimeOffset now)
    {
        var labelText = Text(label, 11 * s, Face, secondary);
        context.DrawText(labelText, new Point(left, y + 2 * s));

        var percent = window is { } value
            ? $"{Math.Round(value.Utilization):F0}%"
            : "–";
        var percentText = Text(percent, 15 * s, NumberFace, primary);
        context.DrawText(percentText, new Point(left + 32 * s, y));

        // 남은 시간은 오른쪽 끝에 붙인다. 자리가 모자라면 그리지 않는다 —
        // 잘린 글자가 보이는 것보다 없는 편이 낫다.
        var remaining = Text(RemainingTime.Text(window?.ResetsAt, now), 10 * s, Face, secondary);
        var remainingX = right - remaining.Width;
        if (remainingX > left + 32 * s + percentText.Width + 6 * s)
        {
            context.DrawText(remaining, new Point(remainingX, y + 4 * s));
        }

        return percentText.Height;
    }

    /// <summary>테스트판인지 한눈에 알 수 있게 왼쪽 위에 붙인다.</summary>
    private void DrawVersionBadge(DrawingContext context, string badge, HudPalette palette, double s)
    {
        var text = Text(badge, 9 * s, Face, Frozen(palette.Secondary));
        context.DrawText(text, new Point(8 * s, 5 * s));
    }

    private FormattedText Text(string value, double size, Typeface face, Brush brush) =>
        new(value, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, face,
            Math.Max(1, size), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
