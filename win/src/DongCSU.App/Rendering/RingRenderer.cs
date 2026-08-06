using System.Windows;
using System.Windows.Media;
using DongCSU.Core.Usage;

namespace DongCSU.App.Rendering;

/// <summary>
/// 사용률 링. 바깥이 5시간 세션, 안쪽이 7일 주간이다.
///
/// WPF 에는 호를 바로 그리는 것이 없어서 <see cref="StreamGeometry"/> 로 만든다.
/// 100%는 <c>ArcTo</c> 한 번으로 그릴 수 없다(시작점과 끝점이 같아서 아무것도 안 그려진다).
/// 반 바퀴씩 두 번 그린다.
/// </summary>
public static class RingRenderer
{
    /// <summary>두 링 사이 간격.</summary>
    public const double Gap = 2;

    public static void Draw(
        DrawingContext context,
        Point center,
        double outerDiameter,
        double thickness,
        double? sessionPercent,
        double? weeklyPercent,
        Color trackColor,
        bool grayscale)
    {
        var outerRadius = (outerDiameter - thickness) / 2;
        var innerRadius = outerRadius - thickness - Gap;

        DrawOne(context, center, outerRadius, thickness, sessionPercent, trackColor, grayscale);
        if (innerRadius > thickness)
        {
            DrawOne(context, center, innerRadius, thickness, weeklyPercent, trackColor, grayscale);
        }
    }

    private static void DrawOne(
        DrawingContext context,
        Point center,
        double radius,
        double thickness,
        double? percent,
        Color trackColor,
        bool grayscale)
    {
        var track = new Pen(new SolidColorBrush(trackColor), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        track.Freeze();
        context.DrawEllipse(null, track, center, radius, radius);

        if (percent is not { } value || value <= 0) return;

        var rgb = UsageColor.For(value);
        var color = grayscale ? Desaturate(rgb) : Color.FromRgb(rgb.R, rgb.G, rgb.B);
        var pen = new Pen(new SolidColorBrush(color), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();

        context.DrawGeometry(null, pen, ArcGeometry(center, radius, Math.Min(100, value) / 100.0));
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
