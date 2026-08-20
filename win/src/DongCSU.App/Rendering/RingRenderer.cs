using System.Windows;
using System.Windows.Media;
using DongCSU.Core.Usage;

namespace DongCSU.App.Rendering;

/// <summary>
/// 사용률 링. 바깥이 5시간 세션, 안쪽이 7일 주간이다.
///
/// **두께가 안팎이 다르다** — 맥과 같이 바깥 6, 간격 7, 안쪽 5 다. 하나로 맞추면
/// 두 판을 나란히 놓았을 때 굵기가 눈에 띄게 달라진다.
///
/// WPF 에는 호를 바로 그리는 것이 없어서 <see cref="StreamGeometry"/> 로 만든다.
/// 100%는 <c>ArcTo</c> 한 번으로 그릴 수 없다(시작점과 끝점이 같아서 아무것도 안 그려진다).
/// 반 바퀴씩 두 번 그린다.
/// </summary>
public static class RingRenderer
{
    /// <summary>
    /// 값이 0%여도 이만큼은 남긴다.
    ///
    /// 아무것도 안 그리면 **0%와 "값 없음"이 화면에서 똑같아 보인다.** 점만 한 자국이
    /// 남아 있으면 적어도 값이 왔다는 것은 알 수 있다. 맥과 같은 값이다.
    /// </summary>
    public const double MinimumFraction = 0.004;

    /// <summary>
    /// 진행선 둘레 후광의 번짐 반경. **배율 1 기준이고 실제로는 배율을 곱해 쓴다.**
    /// 맥의 <c>.shadow(color: color.opacity(0.30), radius: s(1.5))</c> 에서 온 값이다.
    /// </summary>
    private const double GlowRadius = 1.5;

    /// <summary>후광이 가장 진한 곳(진행선에 붙은 자리)의 불투명도. 맥의 0.30 이다.</summary>
    private const double GlowOpacity = 0.30;

    /// <summary>
    /// 후광을 몇 겹으로 나눠 깔지.
    ///
    /// **한 겹으로는 후광이 아니라 테두리로 보인다.** 흐림이 아니라 선이라 가장자리가
    /// 딱 떨어지는데, 배율을 키우면 그 띠가 같이 두꺼워져서 굵은 테두리를 두른 것처럼
    /// 읽힌다. 겹을 나누면 안쪽으로 갈수록 진해져서 번지는 것에 가까워진다.
    /// 셋이면 계단이 눈에 안 띄면서 그리는 값도 링당 세 번뿐이다.
    /// </summary>
    private const int GlowLayers = 3;

    /// <summary>
    /// 한 겹의 불투명도.
    ///
    /// 겹쳐 칠하면 알파가 더해지는 것이 아니라 **곱으로 쌓인다.** 다 겹친 안쪽이
    /// <see cref="GlowOpacity"/> 가 되도록 거꾸로 푼 값이다 — <c>1-(1-a)^n = 0.30</c>.
    /// 겹 수를 바꿔도 가장 진한 곳은 맥과 같게 남는다.
    /// </summary>
    private static readonly double GlowLayerOpacity =
        1 - Math.Pow(1 - GlowOpacity, 1.0 / GlowLayers);

    /// <param name="scale">
    /// HUD 배율. **후광이 번지는 반경이 이 값을 따라 커진다** — 맥이 <c>s(1.5)</c> 로
    /// 배율을 곱해 쓰는 자리다. 고정 폭으로 두면 배율을 키웠을 때 번짐만 제자리라
    /// 후광이 아니라 가늘고 딱딱한 테두리로 보인다.
    ///
    /// **두께에서 끌어내지 않고 따로 받는다.** 두께의 기준값이 카드(6·5)와 펫에서
    /// 다르므로 나눠서 되돌리면 펫에서 틀린 배율이 나온다.
    /// </param>
    /// <param name="spentColor">
    /// 주간을 다 썼을 때 두 링에 함께 칠할 색. 안 넘기면 각자 제 사용률 색으로 그린다.
    ///
    /// **다 썼으면 둘 다 색을 뺀다.** 세션은 쓸 수 없어서고, 주간은 그 자신이 죽은
    /// 이유라서다. 하나만 빨갛게 남으면 마스코트는 멈췄는데 링은 살아 있어서,
    /// 아직 뭔가 되는 것처럼 읽힌다.
    /// </param>
    public static void Draw(
        DrawingContext context,
        Point center,
        double outerDiameter,
        double outerThickness,
        double innerThickness,
        double gap,
        double? sessionPercent,
        double? weeklyPercent,
        Color trackColor,
        bool grayscale,
        Color? spentColor = null,
        double scale = 1)
    {
        // 선이 지름 밖으로 삐져나가지 않게 두께의 절반만큼 안으로 넣는다.
        var outerRadius = (outerDiameter - outerThickness) / 2;
        var innerDiameter = outerDiameter - outerThickness * 2 - gap;
        var innerRadius = (innerDiameter - innerThickness) / 2;

        DrawOne(context, center, outerRadius, outerThickness, sessionPercent, trackColor, grayscale,
            spentColor, scale);
        if (innerRadius > innerThickness)
        {
            DrawOne(context, center, innerRadius, innerThickness, weeklyPercent, trackColor, grayscale,
                spentColor, scale);
        }
    }

    private static void DrawOne(
        DrawingContext context,
        Point center,
        double radius,
        double thickness,
        double? percent,
        Color trackColor,
        bool grayscale,
        Color? spentColor,
        double scale)
    {
        context.DrawEllipse(null, Paint.Pen(trackColor, thickness, round: true), center, radius, radius);

        // **값이 없는 것과 0%는 다르다.** 없으면 진행선을 아예 그리지 않는다.
        if (percent is not { } value) return;

        // 다 쓴 링은 사용률 색 대신 회색이다. **숫자는 그대로 두고 색만 뺀다** —
        // 얼마나 썼는지는 여전히 알아야 하고, 쓸 수 없다는 것만 더 알려주면 된다.
        var rgb = UsageColor.For(value);
        var color = spentColor
            ?? (grayscale ? Desaturate(rgb) : rgb.ToColor());
        var fraction = Math.Max(MinimumFraction, Math.Clamp(value, 0, 100) / 100.0);
        var arc = ArcGeometry(center, radius, fraction);

        DrawGlow(context, arc, color, thickness, scale);
        context.DrawGeometry(null, Paint.Pen(color, thickness, round: true), arc);
    }

    /// <summary>
    /// 진행선 둘레 후광.
    ///
    /// 링이 배경과 같은 밝기일 때 가장자리가 묻히는 것을 막아 준다. 맥은 흐림
    /// (<c>shadow</c>)으로 번지게 하지만 <see cref="DrawingContext"/> 에는 흐림이 없어서,
    /// **더 두껍고 옅은 선을 여러 겹 깔아** 흉내 낸다.
    ///
    /// **바깥 겹부터 깐다.** 안쪽 겹이 그 위에 겹쳐 칠해지면서 진행선에 붙은 자리가
    /// 저절로 가장 진해진다 — 겹마다 알파를 따로 계산하지 않아도 번지는 모양이 나온다.
    /// </summary>
    private static void DrawGlow(
        DrawingContext context, Geometry arc, Color color, double thickness, double scale)
    {
        // 번지는 반경이 배율을 따라간다. 배율이 0 이하로 들어오면 그릴 것이 없다.
        var spread = GlowRadius * scale;
        if (spread <= 0) return;

        var glow = Color.FromArgb(
            (byte)Math.Round(GlowLayerOpacity * 255), color.R, color.G, color.B);

        for (var layer = GlowLayers; layer >= 1; layer--)
        {
            // 선은 두께의 절반씩 양옆으로 자라므로, 밖으로 reach 만큼 나가려면 두 배다.
            var reach = spread * layer / GlowLayers;
            context.DrawGeometry(null, Paint.Pen(glow, thickness + reach * 2, round: true), arc);
        }
    }

    /// <summary>12시에서 시계 방향으로 <paramref name="fraction"/> 만큼.</summary>
    private static StreamGeometry ArcGeometry(Point center, double radius, double fraction)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var start = new Point(center.X, center.Y - radius);
            ctx.BeginFigure(start, isFilled: false, isClosed: false);

            // 반 바퀴를 넘으면 나눠 그린다. 한 번에 그리면 100%에서 호가 사라진다.
            if (fraction >= 1.0)
            {
                var bottom = new Point(center.X, center.Y + radius);
                ctx.ArcTo(bottom, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                ctx.ArcTo(start, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            else
            {
                var angle = fraction * 2 * Math.PI;
                var end = new Point(
                    center.X + radius * Math.Sin(angle),
                    center.Y - radius * Math.Cos(angle));
                ctx.ArcTo(end, new Size(radius, radius), 0,
                    isLargeArc: fraction > 0.5, SweepDirection.Clockwise, true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>조회가 끊겼을 때. 색을 빼서 지금 값이 아님을 드러낸다.</summary>
    private static Color Desaturate(Rgb rgb)
    {
        var gray = (byte)Math.Round(rgb.R * 0.299 + rgb.G * 0.587 + rgb.B * 0.114);
        return Color.FromRgb(gray, gray, gray);
    }

}
