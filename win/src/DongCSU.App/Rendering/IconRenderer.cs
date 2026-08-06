using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DongCSU.Core;

namespace DongCSU.App.Rendering;

/// <summary>
/// 링 한가운데에 그릴 그림. 부엉이 말고 나머지 셋을 그린다.
///
/// **그리는 방식이 셋으로 갈린다.** Clawd 는 픽셀 그리드라 안티에일리어싱을 끄고 정수
/// 칸으로 그려야 하고, 버스트 마크는 벡터라 반대로 켜야 하며, Claude 아이콘은 비트맵이라
/// 보간 품질을 높여야 한다. 한 방식으로 뭉뚱그리면 셋 중 둘이 뭉개진다.
/// </summary>
public static class IconRenderer
{
    /// <summary>버스트 마크 색. 맥판과 같은 주황이다.</summary>
    private static readonly Color MarkColor = Color.FromRgb(0xD9, 0x75, 0x57);

    /// <summary>
    /// Clawd. 폭을 기준으로 맞춘다 — 11×8 이라 세로로 맞추면 옆으로 삐져나온다.
    /// </summary>
    public static void DrawClawd(DrawingContext context, Rect bounds, Color eyeColor)
    {
        var cellWidth = bounds.Width / ClawdMark.Columns;
        var cellHeight = bounds.Height / ClawdMark.Lines;

        // 경계를 반올림해서 칸 사이에 실틈이 생기지 않게 한다.
        Rect Cell(int x, int y)
        {
            var left = Math.Round(bounds.Left + x * cellWidth);
            var right = Math.Round(bounds.Left + (x + 1) * cellWidth);
            var top = Math.Round(bounds.Top + y * cellHeight);
            var bottom = Math.Round(bounds.Top + (y + 1) * cellHeight);
            return new Rect(left, top, right - left, bottom - top);
        }

        var body = Frozen(OwlRenderer.ParseColor(ClawdMark.BodyHex));
        for (var y = 0; y < ClawdMark.Rows.Length; y++)
        {
            var row = ClawdMark.Rows[y];
            var x = 0;
            while (x < row.Length)
            {
                if (row[x] != '#') { x++; continue; }

                var run = 1;
                while (x + run < row.Length && row[x + run] == '#') run++;

                var first = Cell(x, y);
                var last = Cell(x + run - 1, y);
                context.DrawRectangle(
                    body, null,
                    new Rect(first.Left, first.Top, last.Right - first.Left, first.Height));
                x += run;
            }
        }

        var eyes = Frozen(eyeColor);
        foreach (var (x, y) in ClawdMark.Eyes) context.DrawRectangle(eyes, null, Cell(x, y));
    }

    /// <summary>
    /// 버스트 마크. 위를 향한 쐐기 하나를 만들어 열두 번 돌려 붙인다.
    ///
    /// 공식 브랜드 애셋이 아니라 형태만 맞춘 근사 버전이다 — 맥판과 같은 도형이다.
    /// </summary>
    public static void DrawMark(DrawingContext context, Rect bounds, int spokes = 12)
    {
        var outer = Math.Min(bounds.Width, bounds.Height) / 2;
        if (outer <= 0) return;

        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var baseHalf = outer * 0.30;
        var tipHalf = outer * 0.075;

        var spoke = new StreamGeometry();
        using (var ctx = spoke.Open())
        {
            ctx.BeginFigure(new Point(-baseHalf, 0), isFilled: true, isClosed: true);
            ctx.QuadraticBezierTo(
                new Point(-baseHalf * 0.80, -outer * 0.55), new Point(-tipHalf, -outer * 0.94), true, false);
            ctx.QuadraticBezierTo(
                new Point(0, -outer), new Point(tipHalf, -outer * 0.94), true, false);
            ctx.QuadraticBezierTo(
                new Point(baseHalf * 0.80, -outer * 0.55), new Point(baseHalf, 0), true, false);
        }
        spoke.Freeze();

        var brush = Frozen(MarkColor);
        for (var i = 0; i < spokes; i++)
        {
            var group = new TransformGroup();
            group.Children.Add(new RotateTransform(i * 360.0 / spokes));
            group.Children.Add(new TranslateTransform(center.X, center.Y));
            group.Freeze();

            context.PushTransform(group);
            context.DrawGeometry(brush, null, spoke);
            context.Pop();
        }
    }

    /// <summary>
    /// Claude 앱 아이콘. 없으면 <c>null</c> — 그때는 버스트 마크로 떨어진다.
    ///
    /// 맥판이 번들에 넣어 두고 쓰는 것과 **같은 파일**을 앱에 박아 뒀다. 파일로
    /// 따라다니게 하면 사용자가 지우거나 옛 것이 남는다.
    /// </summary>
    public static BitmapSource? ClaudeAppIcon => appIcon.Value;

    private static readonly Lazy<BitmapSource?> appIcon = new(() =>
    {
        try
        {
            using var stream = typeof(IconRenderer).Assembly
                .GetManifestResourceStream("claude-icon.png");
            if (stream is null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            return null;
        }
    });

    /// <summary>Claude 앱 아이콘을 모서리를 깎아 그린다. 그림이 없으면 false.</summary>
    public static bool DrawAppIcon(DrawingContext context, Rect bounds)
    {
        if (ClaudeAppIcon is not { } image) return false;

        var radius = bounds.Width * 0.24;
        context.PushClip(new RectangleGeometry(bounds, radius, radius));
        context.DrawImage(image, bounds);
        context.Pop();
        return true;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
