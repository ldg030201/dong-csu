using System.Windows;
using System.Windows.Media;
using DongCSU.Core.Owl;

namespace DongCSU.App.Rendering;

/// <summary>
/// 부엉이 그리드를 칸 단위로 그린다.
///
/// **한 칸은 반드시 정수 크기여야 한다.** 나누어떨어지지 않는 크기로 그리면 어떤 행은
/// 2px, 어떤 행은 3px가 되어 자리마다 다른 얼굴이 된다. 그래서 원하는 높이를 그대로
/// 쓰지 않고 칸 크기를 내림한 뒤 다시 곱한다.
/// </summary>
public static class OwlRenderer
{
    /// <summary>글자 하나가 팔레트의 어느 색인지.</summary>
    public static string? PaletteKey(char mark) => mark switch
    {
        '#' => "body",
        'd' => "wing",
        'l' => "belly",
        'w' => "face",
        'k' => "pupil",
        'y' => "beak",
        _ => null,
    };

    /// <summary>주어진 높이에 들어가는 가장 큰 정수 칸 크기. 최소 1.</summary>
    public static int CellSize(double availableHeight, int lines) =>
        Math.Max(1, (int)Math.Floor(availableHeight / lines));

    public static Size MeasuredSize(int cell, OwlGrid grid) =>
        new(cell * grid.Columns, cell * grid.Lines);

    /// <summary>
    /// 그린다. <paramref name="origin"/> 은 그림의 왼쪽 위 모서리다.
    ///
    /// 칸마다 사각형을 하나씩 그리지 않고 **가로로 이어진 같은 색을 한 번에** 그린다.
    /// 15×13 이면 195개가 30개 남짓으로 줄어서, 매 프레임 다시 그려도 부담이 없다.
    /// </summary>
    public static void Draw(
        DrawingContext context,
        string[] grid,
        IReadOnlyDictionary<string, Brush> brushes,
        Point origin,
        int cell)
    {
        for (var y = 0; y < grid.Length; y++)
        {
            var row = grid[y];
            var x = 0;
            while (x < row.Length)
            {
                var key = PaletteKey(row[x]);
                if (key is null) { x++; continue; }

                var run = 1;
                while (x + run < row.Length && row[x + run] == row[x]) run++;

                if (brushes.TryGetValue(key, out var brush))
                {
                    context.DrawRectangle(
                        brush,
                        null,
                        new Rect(origin.X + x * cell, origin.Y + y * cell, run * cell, cell));
                }
                x += run;
            }
        }
    }

    /// <summary>팔레트(이름 → #RRGGBB)를 굳혀 둔 브러시로. 매 프레임 만들지 않는다.</summary>
    public static Dictionary<string, Brush> Brushes(IReadOnlyDictionary<string, string> palette)
    {
        var result = new Dictionary<string, Brush>(palette.Count);
        foreach (var (key, hex) in palette)
        {
            var brush = new SolidColorBrush(ParseColor(hex));
            brush.Freeze();
            result[key] = brush;
        }
        return result;
    }

    public static Color ParseColor(string hex)
    {
        var text = hex.TrimStart('#');
        var value = uint.Parse(text, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);
        return Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
