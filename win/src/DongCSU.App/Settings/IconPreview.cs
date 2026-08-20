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
///
/// 그림 뒤에는 어두운 판을 깐다. HUD 가 어느 테마에서든 반투명한 어두운 배경이라
/// 그게 실물에 가깝다 — 자세히는 <see cref="PlateColor"/>.
/// </summary>
internal sealed class IconPreview : FrameworkElement
{
    private static readonly OwlDocument Document = OwlDocument.Embedded;
    private static readonly Dictionary<string, Brush> OwlBrushes =
        OwlRenderer.Brushes(Document.Palettes["normal"]);

    private static readonly string[] IdleGrid =
        Document.Animations.Single(a => a.Name == "idle").Frames[0].Grid;

    /// <summary>
    /// 그림 뒤에 까는 어두운 판. 맥과 같은 흰색 0.16 이다.
    ///
    /// **테마를 따라가지 않는다.** 실제 HUD 는 밝은 테마에서도 반투명한 어두운
    /// 배경이라, 밝은 카드 색 위에 바로 그리면 어두운 픽셀 아트가 **검은 칩처럼**
    /// 뭉쳐 보인다. 고른 것과 실제로 뜨는 것이 달라 보이면 고르는 화면으로서 최악이다.
    /// </summary>
    private static readonly Color PlateColor = Color.FromRgb(0x29, 0x29, 0x29);

    /// <summary>
    /// 판 모서리를 깎는 정도. 판 한 변에 대한 비율이다 — 맥은 50 짜리 판에 10 을 쓴다.
    /// </summary>
    private const double PlateCornerRatio = 0.2;

    /// <summary>
    /// 판 안쪽 여백. 한 변에 대한 비율이고 사방에 같이 준다.
    ///
    /// **그림이 판 끝에 닿으면 판을 깐 뜻이 없어진다** — 배경이 아니라 그림에 두른
    /// 테두리처럼 읽힌다. 맥은 76×50 판에 28pt 아이콘을 얹어 절반 남짓만 쓰는데,
    /// 여기는 판이 정사각이라 그 비율대로 줄이면 너무 작아진다. 양쪽 10%만 덜어 내면
    /// 격자 부엉이 한 칸이 2px 로 떨어져서 맥 타일과 거의 같은 크기가 된다.
    /// </summary>
    private const double PlateInset = 0.1;

    /// <summary><c>FrameworkElement.Style</c> 과 이름이 겹치지 않게 붙여 쓴다.</summary>
    public IconStyle IconStyle { get; init; } = IconStyle.Owl;
    public bool IsDark { get; init; } = true;

    protected override void OnRender(DrawingContext context)
    {
        var plate = new Rect(0, 0, ActualWidth, ActualHeight);
        var side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0) return;

        var corner = side * PlateCornerRatio;
        context.DrawRoundedRectangle(Paint.Brush(PlateColor), null, plate, corner, corner);

        side -= side * PlateInset * 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var box = new Rect(center.X - side / 2, center.Y - side / 2, side, side);

        switch (IconStyle)
        {
            // 미리보기는 늘 서 있는 칸이다. 타일마다 다른 자세를 보이면 무엇이 다른
            // 그림인지가 아니라 무엇이 다른 자세인지로 읽힌다.
            case IconStyle.OwlSheet
                when MascotRenderer.Draw(context, MascotSprite.Idle, box):
                break;

            case IconStyle.OwlSheet:
            case IconStyle.Owl:
                var cell = OwlRenderer.CellSize(side, Document.Grid.Lines);
                var size = OwlRenderer.MeasuredSize(cell, Document.Grid);
                Pixelated(context, ctx => OwlRenderer.Draw(ctx, IdleGrid, OwlBrushes, new Point(
                    Math.Round(center.X - size.Width / 2),
                    Math.Round(center.Y - size.Height / 2)), cell));
                break;

            case IconStyle.Clawd:
                var height = side * ClawdMark.Lines / ClawdMark.Columns;
                Pixelated(context, ctx => IconRenderer.DrawClawd(
                    ctx,
                    new Rect(center.X - side / 2, center.Y - height / 2, side, height),
                    Color.FromArgb((byte)(IsDark ? 0xE0 : 0xBF), 0, 0, 0)));
                break;

            case IconStyle.AppIcon:
                if (!IconRenderer.DrawAppIcon(context, box)) IconRenderer.DrawMark(context, box);
                break;

            default:
                IconRenderer.DrawMark(context, box);
                break;
        }
    }

    /// <summary>
    /// 이 안에서만 각지게 그린다.
    ///
    /// **컨트롤 전체에 <c>EdgeMode.Aliased</c> 를 걸지 않는다.** 예전에는 생성자에서
    /// 그렇게 했는데, 그 설정이 픽셀 아트(격자 부엉이·Clawd)만이 아니라 **매끈한
    /// 그림에까지 걸렸다** — 시트 마스코트는 256 칸짜리 그림을 줄여 그리는 것이고
    /// Claude 아이콘은 비트맵이라, 최근접으로 다루면 가장자리가 계단처럼 진다.
    /// 어두운 판의 깎인 모서리도 같이 각져 버린다.
    ///
    /// 그래서 기본은 부드럽게 두고 지켜야 할 것이 좁은 쪽만 감싼다.
    /// <c>HudView.DrawPixelated</c> 와 같은 수법이고, 같은 이유다.
    /// </summary>
    private static void Pixelated(DrawingContext context, Action<DrawingContext> body)
    {
        var group = new DrawingGroup();
        RenderOptions.SetEdgeMode(group, EdgeMode.Aliased);
        using (var ctx = group.Open()) body(ctx);
        group.Freeze();
        context.DrawDrawing(group);
    }
}
