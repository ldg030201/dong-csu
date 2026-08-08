using System.Windows.Media;

namespace DongCSU.App.Rendering;

/// <summary>
/// 굳혀 둔 브러시·펜을 색마다 하나씩만 만들어 나눠 쓴다.
///
/// **매번 새로 만들면 안 된다.** 카드는 1초마다, 마스코트는 그보다 자주 다시 그려지는데
/// 한 번 그릴 때마다 브러시와 펜이 수십 개씩 생긴다. 하루 종일 떠 있는 앱이라 그게
/// 그대로 쓰레기가 되어 쌓인다.
///
/// 굳힌(<c>Freeze</c>) 브러시는 값이 바뀌지 않으므로 **색이 같으면 나눠 써도 된다.**
/// 색은 팔레트와 사용률 그라데이션에서만 나와서 가짓수가 뻔하다.
/// </summary>
internal static class Paint
{
    private static readonly Dictionary<uint, SolidColorBrush> Brushes = [];
    private static readonly Dictionary<(uint Color, double Thickness, bool Round), Pen> Pens = [];

    /// <summary>같은 색이면 같은 브러시를 돌려준다.</summary>
    public static SolidColorBrush Brush(Color color)
    {
        var key = Key(color);
        if (Brushes.TryGetValue(key, out var found)) return found;

        var made = new SolidColorBrush(color);
        made.Freeze();
        Brushes[key] = made;
        return made;
    }

    /// <summary>같은 색·굵기·마감이면 같은 펜을 돌려준다.</summary>
    public static Pen Pen(Color color, double thickness, bool round = false)
    {
        // 굵기는 배율에서 나와 소수점이 붙는다. 그대로 열쇠로 쓰면 캐시가 계속 자란다.
        thickness = Math.Round(thickness, 2);

        var key = (Key(color), thickness, round);
        if (Pens.TryGetValue(key, out var found)) return found;

        var made = new Pen(Brush(color), thickness);
        if (round)
        {
            made.StartLineCap = PenLineCap.Round;
            made.EndLineCap = PenLineCap.Round;
        }
        made.Freeze();
        Pens[key] = made;
        return made;
    }

    private static uint Key(Color color) =>
        ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
