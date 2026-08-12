using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DongCSU.Core;
using DongCSU.Core.Owl;

namespace DongCSU.App.Rendering;

/// <summary>
/// 그림 한 장(<c>mascot.png</c>)에서 칸을 잘라 마스코트를 그린다.
///
/// **격자로 그리던 것을 대신한다.** 맥 2.4.0 에서 HUD·펫의 마스코트가 이 통로로 바뀌었고,
/// 격자(<c>OwlMark</c>·<c>owl.json</c>)는 메뉴바·앱 아이콘과 시트를 굽는 도구로 남았다.
/// 두 판이 같은 그림을 쓰려면 여기도 시트를 봐야 한다.
///
/// 시트는 맥 번들에 들어가는 것과 **같은 파일**이다. 사본을 두면 한쪽만 바뀐다 —
/// Claude 앱 아이콘을 그렇게 다루는 것과 같은 이유다.
/// </summary>
internal static class MascotRenderer
{
    /// <summary>
    /// 칸 하나를 잘라 둔 것과 그 안에서 그림이 실제로 차지하는 자리.
    ///
    /// **칸에는 여백이 많다.** 256 칸 안에 그림이 가운데쯤 떠 있어서, 칸을 그대로
    /// 그리면 마스코트가 자리보다 훨씬 작게 보인다. 잉크 상자를 재서 그것을 채운다.
    /// </summary>
    private sealed record Slice(BitmapSource Image, Int32Rect Ink, int HeadCenterX);

    private static readonly Dictionary<MascotSprite, Slice?> Cache = [];
    private static BitmapSource? sheet;
    private static bool tried;

    /// <summary>걷기·뛰기 칸 중 가장 낮은 잉크 바닥. 그것을 땅으로 삼는다.</summary>
    private static int? gaitGround;

    /// <summary>시트가 있으면 true. 없으면 부르는 쪽이 격자로 떨어진다.</summary>
    public static bool IsAvailable => Sheet() is not null;

    /// <summary>
    /// 칸 하나를 <paramref name="bounds"/> 안에 그린다.
    ///
    /// 세로는 **바닥을 맞춘다** — 서 있든 걷든 발이 같은 줄에 와야 한다. 걷기·뛰기는
    /// 그림에 그려진 뜬 높이를 지킨다(<see cref="MascotSheet.KeepsLift"/>).
    ///
    /// 가로는 걷기만 **머리를 기준**으로 맞춘다. 잉크 상자 가운데로 맞추면 다리가
    /// 벌어질 때마다 몸이 앞뒤로 밀린다.
    /// </summary>
    /// <param name="flipped">
    /// 좌우를 뒤집어 그릴지. 걷기·달리기 칸이 왼쪽을 보고 그려져 있어서, 오른쪽으로
    /// 갈 때 이걸 켠다. **뒤집는 축은 칸의 가운데가 아니라 그려 놓은 자리의 가운데다** —
    /// 칸을 기준으로 돌리면 발이 땅에서 뜨거나 몸이 옆으로 밀린다.
    /// </param>
    public static bool Draw(DrawingContext context, MascotSprite sprite, Rect bounds, bool flipped = false)
    {
        if (Resolve(sprite) is not { } slice) return false;

        // 칸 안에서 그림이 차지하는 만큼만 키운다. 칸째로 맞추면 그림이 작아 보인다.
        var scale = Math.Min(bounds.Width / MascotSheet.Cell, bounds.Height / MascotSheet.Cell);

        var width = slice.Ink.Width * scale;
        var height = slice.Ink.Height * scale;

        // 바닥 맞추기. 걷기는 땅을 걷기끼리 공유해서 뜬 높이가 남는다.
        var ground = MascotSheet.KeepsLift(sprite) && gaitGround is { } shared
            ? shared
            : slice.Ink.Y + slice.Ink.Height;
        var bottom = bounds.Bottom - (ground - (slice.Ink.Y + slice.Ink.Height)) * scale;

        var centerX = MascotSheet.CentersOnHead(sprite)
            ? slice.HeadCenterX
            : slice.Ink.X + slice.Ink.Width / 2.0;
        var left = bounds.Left + bounds.Width / 2 - (centerX - slice.Ink.X) * scale;

        var target = new Rect(left, bottom - height, width, height);

        if (!flipped)
        {
            context.DrawImage(slice.Image, target);
            return true;
        }

        // **자리의 가운데를 축으로 돌린다.** 위에서 머리(걷기)나 알맹이 가운데를 이미
        // 자리 한가운데에 맞춰 놨으므로, 여기서 돌리면 그 점이 제자리에 남는다.
        // 그려 놓은 상자를 축으로 삼으면 머리가 좌우로 튄다.
        context.PushTransform(new ScaleTransform(-1, 1, bounds.Left + bounds.Width / 2, bounds.Top));
        context.DrawImage(slice.Image, target);
        context.Pop();
        return true;
    }

    /// <summary>안 그려진 칸이면 대신할 칸으로 내려간다. 끝까지 없으면 null.</summary>
    private static Slice? Resolve(MascotSprite sprite)
    {
        for (var i = 0; i < 8; i++)
        {
            if (SliceOf(sprite) is { } found) return found;
            if (MascotSheet.Fallback(sprite) is not { } next) return null;
            sprite = next;
        }
        return null;
    }

    private static Slice? SliceOf(MascotSprite sprite)
    {
        if (Cache.TryGetValue(sprite, out var cached)) return cached;

        var made = Cut(sprite);
        Cache[sprite] = made;
        return made;
    }

    private static Slice? Cut(MascotSprite sprite)
    {
        if (Sheet() is not { } source) return null;

        // 시트가 규격의 몇 배인지. 정수배로 그려도 좌표가 맞는다.
        var multiple = Math.Max(1, source.PixelWidth / MascotSheet.SheetWidth);
        var (x, y, side) = MascotSheet.Box(sprite, multiple);
        if (x + side > source.PixelWidth || y + side > source.PixelHeight) return null;

        var cell = new CroppedBitmap(source, new Int32Rect(x, y, side, side));
        cell.Freeze();

        var ink = OpaqueBounds(cell);
        if (ink.Width <= 0 || ink.Height <= 0) return null;

        return new Slice(cell, ink, HeadCenter(cell, ink));
    }

    /// <summary>그림이 실제로 있는 자리. 투명한 여백을 뺀다.</summary>
    private static Int32Rect OpaqueBounds(BitmapSource cell)
    {
        var alpha = AlphaMap(cell, out var width, out var height);

        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (alpha[row * width + column] <= 8) continue;
                if (column < minX) minX = column;
                if (column > maxX) maxX = column;
                if (row < minY) minY = row;
                if (row > maxY) maxY = row;
            }
        }

        return maxX < 0 ? default : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// 머리의 가로 가운데.
    ///
    /// 잉크 상자의 **위쪽 3분의 1**만 본다. 옆모습 걷기에서 아래쪽은 다리가 앞뒤로
    /// 벌어지지만 머리는 걸음 내내 제자리다.
    /// </summary>
    private static int HeadCenter(BitmapSource cell, Int32Rect ink)
    {
        var alpha = AlphaMap(cell, out var width, out _);
        var until = ink.Y + Math.Max(1, ink.Height / 3);

        int minX = int.MaxValue, maxX = int.MinValue;
        for (var row = ink.Y; row < until; row++)
        {
            for (var column = ink.X; column < ink.X + ink.Width; column++)
            {
                if (alpha[row * width + column] <= 8) continue;
                if (column < minX) minX = column;
                if (column > maxX) maxX = column;
            }
        }

        return maxX < minX ? ink.X + ink.Width / 2 : (minX + maxX) / 2;
    }

    private static byte[] AlphaMap(BitmapSource cell, out int width, out int height)
    {
        var converted = new FormatConvertedBitmap(cell, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        width = converted.PixelWidth;
        height = converted.PixelHeight;

        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var alpha = new byte[width * height];
        for (var i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
        return alpha;
    }

    private static BitmapSource? Sheet()
    {
        if (tried) return sheet;
        tried = true;

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("mascot.png");
            if (stream is null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            sheet = AppInfo.IsTestBuild ? HueRotated(image, TestLookDegrees) : image;

            MeasureGaitGround();
            return sheet;
        }
        catch (Exception)
        {
            // 시트를 못 읽어도 앱이 죽으면 안 된다. 격자로 떨어진다.
            return null;
        }
    }

    /// <summary>
    /// 테스트판에서 색상을 돌리는 각도.
    ///
    /// **링도 카드도 없는 펫 모드에서는 정식판과 구분할 방법이 색뿐이다.** 격자
    /// 부엉이가 보라색 팔레트로 하던 일을, 그림이 기본이 되면서 여기가 이어받는다.
    /// 색을 정해 칠하지 않고 **돌리는** 이유는 어떤 그림이 들어올지 모르기 때문이다 —
    /// 나중에 사용자 그림을 받게 돼도 그대로 먹는다. 맥과 같은 42도다.
    /// </summary>
    private const double TestLookDegrees = 42;

    /// <summary>
    /// 시트 전체의 색상을 돌린다. **불러올 때 한 번만** 한다 — 칸마다 돌리면 같은
    /// 계산을 스물한 번 하고, 그리는 순간에 돌리면 프레임마다 한다.
    ///
    /// 채도·밝기·투명도는 건드리지 않는다. 옮기는 것은 색상뿐이다.
    /// </summary>
    private static BitmapSource HueRotated(BitmapSource source, double degrees)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            // 투명한 자리는 색이 없다. 돌려 봐야 보이지 않고 계산만 는다.
            if (pixels[i + 3] == 0) continue;
            Rotate(ref pixels[i + 2], ref pixels[i + 1], ref pixels[i], degrees);
        }

        var rotated = BitmapSource.Create(
            width, height, converted.DpiX, converted.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        rotated.Freeze();
        return rotated;
    }

    /// <summary>한 점의 색상만 돌린다. HSV 로 옮겼다 되돌린다.</summary>
    private static void Rotate(ref byte red, ref byte green, ref byte blue, double degrees)
    {
        double r = red / 255.0, g = green / 255.0, b = blue / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var span = max - min;

        // 회색에는 돌릴 색상이 없다. 눈동자의 흰자·검은자가 여기 걸린다.
        if (span <= 0) return;

        var hue = max == r ? (g - b) / span
            : max == g ? (b - r) / span + 2
            : (r - g) / span + 4;
        hue = (hue * 60 + degrees) % 360;
        if (hue < 0) hue += 360;

        var sector = (int)(hue / 60) % 6;
        var fraction = hue / 60 - (int)(hue / 60);
        var p = min;
        var q = max - span * fraction;
        var t = min + span * fraction;

        (r, g, b) = sector switch
        {
            0 => (max, t, p),
            1 => (q, max, p),
            2 => (p, max, t),
            3 => (p, q, max),
            4 => (t, p, max),
            _ => (max, p, q),
        };

        red = (byte)Math.Round(r * 255);
        green = (byte)Math.Round(g * 255);
        blue = (byte)Math.Round(b * 255);
    }

    /// <summary>걷기·뛰기 칸에서 가장 낮은 잉크 바닥. 그것이 땅이다.</summary>
    private static void MeasureGaitGround()
    {
        var lowest = 0;
        foreach (var sprite in Enum.GetValues<MascotSprite>())
        {
            if (!MascotSheet.KeepsLift(sprite)) continue;
            if (SliceOf(sprite) is not { } slice) continue;

            lowest = Math.Max(lowest, slice.Ink.Y + slice.Ink.Height);
        }
        gaitGround = lowest > 0 ? lowest : null;
    }
}

