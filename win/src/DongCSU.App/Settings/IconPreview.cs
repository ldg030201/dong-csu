using System.Windows;
using System.Windows.Media;
using DongCSU.App.Rendering;
using DongCSU.Core;
using DongCSU.Core.Owl;

namespace DongCSU.App.Settings;

/// <summary>
/// 아이콘 고르기 타일 안에 들어가는 미리보기.
///
/// HUD 와 **같은 코드로 그린다.** 미리보기를 따로 그리면 고른 것과 실제로 뜨는 것이
/// 달라지는데, 그건 고르는 화면으로서 최악이다.
/// </summary>
internal sealed class IconPreview : FrameworkElement
{
    private static readonly OwlDocument Document = OwlDocument.Embedded;
    private static readonly Dictionary<string, Brush> OwlBrushes =
        OwlRenderer.Brushes(Document.Palettes["normal"]);

    private static readonly string[] IdleGrid =
        Document.Animations.Single(a => a.Name == "idle").Frames[0].Grid;

    /// <summary><c>FrameworkElement.Style</c> 과 이름이 겹치지 않게 붙여 쓴다.</summary>
    public IconStyle IconStyle { get; init; } = IconStyle.Owl;
    public bool IsDark { get; init; } = true;

    public IconPreview()
    {
        // 픽셀 아트가 뭉개지지 않게. 벡터·비트맵은 그리는 쪽에서 되돌린다.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    protected override void OnRender(DrawingContext context)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var box = new Rect(center.X - side / 2, center.Y - side / 2, side, side);

        switch (IconStyle)
        {
            case IconStyle.Owl:
                var cell = OwlRenderer.CellSize(side, Document.Grid.Lines);
                var size = OwlRenderer.MeasuredSize(cell, Document.Grid);
                OwlRenderer.Draw(context, IdleGrid, OwlBrushes, new Point(
                    Math.Round(center.X - size.Width / 2),
                    Math.Round(center.Y - size.Height / 2)), cell);
                break;

            case IconStyle.Clawd:
                var height = side * ClawdMark.Lines / ClawdMark.Columns;
                IconRenderer.DrawClawd(
                    context,
                    new Rect(center.X - side / 2, center.Y - height / 2, side, height),
                    Color.FromArgb((byte)(IsDark ? 0xE0 : 0xBF), 0, 0, 0));
                break;

            case IconStyle.AppIcon:
                Smooth(context, ctx =>
                {
                    if (!IconRenderer.DrawAppIcon(ctx, box)) IconRenderer.DrawMark(ctx, box);
                });
                break;

            default:
                Smooth(context, ctx => IconRenderer.DrawMark(ctx, box));
                break;
        }
    }

    private static void Smooth(DrawingContext context, Action<DrawingContext> body)
    {
        var group = new DrawingGroup();
        RenderOptions.SetEdgeMode(group, EdgeMode.Unspecified);
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        using (var ctx = group.Open()) body(ctx);
        group.Freeze();
        context.DrawDrawing(group);
    }
}
