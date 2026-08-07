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
        Color? sessionSpentColor = null)
    {
        // 선이 지름 밖으로 삐져나가지 않게 두께의 절반만큼 안으로 넣는다.
        var outerRadius = (outerDiameter - outerThickness) / 2;
        var innerDiameter = outerDiameter - outerThickness * 2 - gap;
        var innerRadius = (innerDiameter - innerThickness) / 2;

        DrawOne(context, center, outerRadius, outerThickness, sessionPercent, trackColor, grayscale,
            sessionSpentColor);
        if (innerRadius > innerThickness)
        {
            DrawOne(context, center, innerRadius, innerThickness, weeklyPercent, trackColor, grayscale);
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
        Color? spentColor = null)
    {
        var track = new Pen(Frozen(trackColor), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        track.Freeze();
        context.DrawEllipse(null, track, center, radius, radius);

        // **값이 없는 것과 0%는 다르다.** 없으면 진행선을 아예 그리지 않는다.
        if (percent is not { } value) return;

        // 다 쓴 링은 사용률 색 대신 회색이다. **숫자는 그대로 두고 색만 뺀다** —
        // 얼마나 썼는지는 여전히 알아야 하고, 쓸 수 없다는 것만 더 알려주면 된다.
        var rgb = UsageColor.For(value);
        var color = spentColor
            ?? (grayscale ? Desaturate(rgb) : Color.FromRgb(rgb.R, rgb.G, rgb.B));
        var fraction = Math.Max(MinimumFraction, Math.Clamp(value, 0, 100) / 100.0);
        var arc = ArcGeometry(center, radius, fraction);

        // 흐림 효과를 쓸 수 없으니, 더 두껍고 옅은 선을 밑에 깔아 번짐을 흉내 낸다.
        // 링이 배경과 같은 밝기일 때 가장자리가 묻히는 것을 막아 준다.
        var glow = new Pen(Frozen(Color.FromArgb(0x4D, color.R, color.G, color.B)), thickness + 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        glow.Freeze();
        context.DrawGeometry(null, glow, arc);

        var pen = new Pen(Frozen(color), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        context.DrawGeometry(null, pen, arc);
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

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
