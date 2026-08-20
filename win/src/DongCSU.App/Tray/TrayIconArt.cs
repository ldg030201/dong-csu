using System.Drawing;
using System.Drawing.Imaging;
using DongCSU.App.Rendering;
using DongCSU.Core.Owl;

namespace DongCSU.App.Tray;

/// <summary>
/// 트레이 아이콘 그림을 비트맵으로 그린다.
///
/// **아이콘을 만드는 일과 그림을 그리는 일을 갈라 둔 것이다.** 진단 통로
/// (<c>--render-menubar</c>)가 눈 깜빡임을 보려면 프레임마다 그림이 필요한데,
/// 아이콘 핸들까지 만들 이유는 없다. 그렇다고 그리는 코드를 한 벌 더 두면
/// **화면과 다른 코드로 확인하는 것이라 아무것도 확인한 것이 아니게 된다** —
/// 그래서 트레이도 이걸 부른다.
/// </summary>
internal static class TrayIconArt
{
    /// <summary>
    /// 한 프레임을 <paramref name="size"/> 안에 들어가는 크기로 그린다.
    ///
    /// **한 칸은 정수 크기로 내림한다.** 나누어떨어지지 않게 그리면 어떤 줄은 2px,
    /// 어떤 줄은 3px 가 되어 자리마다 다른 얼굴이 된다. 그래서 나온 비트맵은
    /// <paramref name="size"/> 보다 작을 수 있다.
    ///
    /// **부르는 쪽이 <c>using</c> 으로 놓아준다.** GDI 비트맵이라 저절로 사라지지
    /// 않는다 — 트레이는 한 장이라 티가 안 나지만, 진단 통로는 프레임 스물몇 개를
    /// 잇달아 그려서 놓아주지 않으면 그만큼 쥐고 있게 된다.
    /// </summary>
    public static Bitmap Render(
        string[] grid, IReadOnlyDictionary<string, string> palette, int size)
    {
        var document = OwlDocument.Embedded;
        var cell = Math.Max(1, size / document.Grid.Lines);
        var width = cell * document.Grid.Columns;
        var height = cell * document.Grid.Lines;

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            for (var y = 0; y < grid.Length; y++)
            {
                var row = grid[y];
                for (var x = 0; x < row.Length; x++)
                {
                    var key = OwlRenderer.PaletteKey(row[x]);
                    if (key is null || !palette.TryGetValue(key, out var hex)) continue;

                    var media = OwlRenderer.ParseColor(hex);
                    using var brush = new SolidBrush(
                        System.Drawing.Color.FromArgb(media.R, media.G, media.B));
                    graphics.FillRectangle(brush, x * cell, y * cell, cell, cell);
                }
            }
        }

        return bitmap;
    }
}
