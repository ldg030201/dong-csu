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

    /// <summary>
    /// 모든 칸의 잉크를 묶은 상자. **프레임마다 다시 재지 않는다** — 시트를 읽을 때
    /// 한 번 재고 들고 있는다. 스물한 칸을 훑는 일이라 초당 열 번 하면 값이 아깝다.
    /// </summary>
    private static Int32Rect commonInk;

    /// <summary>시트가 있으면 true. 없으면 부르는 쪽이 격자로 떨어진다.</summary>
    public static bool IsAvailable => Sheet() is not null;

    /// <summary>
    /// 칸 하나를 <paramref name="bounds"/> 안에 그린다.
    ///
    /// <b>칸마다 자리를 다시 잡지 않는다.</b> 모든 칸의 잉크를 한 상자로 묶고
    /// (<see cref="CommonInk"/>) 그 상자에만 배율을 매긴 뒤, 칸은 <b>구워진 자리
    /// 그대로</b> 옮긴다. 맥이 <c>trimTogether</c> 로 칸을 함께 잘라 내고
    /// <c>MascotSpriteView</c> 가 묶음 상자 하나만 보는 것과 같은 셈이다.
    ///
    /// <b>예전에는 칸마다 바닥·머리를 다시 맞췄다.</b> 서 있는 자세끼리는 그래도
    /// 같은 답이 나왔지만(시트가 이미 그렇게 구워져 있다), 그 방식은 시트에 구워 둔
    /// 상대 위치를 지운다 — 매달린 칸은 칸 위쪽에, 벽붙기 칸은 왼쪽 끝에 그려져
    /// 있는데 그것을 바닥·가운데로 끌어내려서 <b>벽붙기가 39px 밀렸다.</b> 게다가
    /// 배율 기준이 칸(256)이라 맥(묶음 상자 253)보다 <b>1.2% 작게</b> 그렸다.
    /// </summary>
    /// <param name="flipped">
    /// 좌우를 뒤집어 그릴지. 걷기·달리기 칸이 왼쪽을 보고 그려져 있어서, 오른쪽으로
    /// 갈 때 이걸 켠다. <b>뒤집는 축은 자리의 가운데다</b> — 묶음 상자를 이미 자리
    /// 한가운데에 놓았으므로 그 점이 제자리에 남는다.
    /// </param>
    /// <param name="widthLimited">
    /// 옆으로 퍼지는 것을 자리 너비에서 막을지. <b>맥의 <c>widthLimit</c> 과 같은
    /// 자리다</b> — HUD 는 링 안에 들어가야 해서 켜고, 펫 모드는 링이 없어서 끈다.
    /// 끄면 높이에만 맞춰 커진다.
    /// </param>
    public static bool Draw(
        DrawingContext context, MascotSprite sprite, Rect bounds,
        bool flipped = false, bool widthLimited = true)
    {
        if (Resolve(sprite) is not { } slice) return false;

        var box = CommonInk();
        if (box.Width <= 0 || box.Height <= 0) return false;

        // **높이를 먼저 맞춘다.** 격자 부엉이가 같은 값을 높이로 받으므로, 이래야
        // 아이콘 갈래를 바꿔도 크기가 그대로다.
        var scale = bounds.Height / box.Height;
        if (widthLimited) scale = Math.Min(scale, bounds.Width / box.Width);

        // 묶음 상자를 자리 한가운데에 놓는다. 한쪽으로 쏠리지 않게.
        var boxLeft = bounds.Left + (bounds.Width - box.Width * scale) / 2;
        var boxTop = bounds.Top + (bounds.Height - box.Height * scale) / 2;

        var width = slice.Ink.Width * scale;
        var height = slice.Ink.Height * scale;
        var target = new Rect(
            boxLeft + (slice.Ink.X - box.X) * scale,
            boxTop + (slice.Ink.Y - box.Y) * scale,
            width, height);

        // **그림자를 안 깐다.** 맥은 `.shadow(검정 45%, 번짐 2)` 를 붙이지만, 여기서
        // 같은 값을 내려면 알파를 직접 흐려야 하고 그 결과가 그림 둘레에 뿌연 테로
        // 남았다 — 맥의 그것과 다르게 보인다. 없는 편이 어설픈 것보다 낫다.

        if (flipped)
        {
            // **자리의 가운데를 축으로 돌린다.** 위에서 머리(걷기)나 알맹이 가운데를 이미
            // 자리 한가운데에 맞춰 놨으므로, 여기서 돌리면 그 점이 제자리에 남는다.
            // 그려 놓은 상자를 축으로 삼으면 머리가 좌우로 튄다.
            context.PushTransform(new ScaleTransform(-1, 1, bounds.Left + bounds.Width / 2, bounds.Top));
            context.DrawImage(slice.Image, target);
            context.Pop();
        }
        else
        {
            context.DrawImage(slice.Image, target);
        }

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
            sheet = (TestLook ?? AppInfo.IsTestBuild) ? HueRotated(image, TestLookDegrees) : image;

            MeasureCommonInk();
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
    /// 테스트판 색으로 그릴지. **null 이면 지금 빌드를 따른다.**
    ///
    /// 렌더 통로가 <c>false</c> 를 꽂는다 — 문서 그림은 테스트 바이너리로 뽑는데,
    /// 그대로 두면 **전부 보라색이 된다.** 시트를 읽기 전에 정해야 한다.
    /// </summary>
    public static bool? TestLook { get; set; }

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

    /// <summary>
    /// 모든 칸의 잉크를 묶은 상자를 한 번 잰다. **맥의 <c>trimTogether</c> 와 같은 셈이다.**
    ///
    /// 맥은 자를 때 이 상자로 모든 칸을 함께 잘라내고, 그릴 때는 이 상자 하나에만
    /// 배율을 매긴다. 그래서 칸에 구워 둔 상대 위치가 그대로 남는다.
    /// </summary>
    private static void MeasureCommonInk()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        foreach (var sprite in Enum.GetValues<MascotSprite>())
        {
            if (SliceOf(sprite) is not { } slice) continue;
            minX = Math.Min(minX, slice.Ink.X);
            minY = Math.Min(minY, slice.Ink.Y);
            maxX = Math.Max(maxX, slice.Ink.X + slice.Ink.Width - 1);
            maxY = Math.Max(maxY, slice.Ink.Y + slice.Ink.Height - 1);
        }
        commonInk = maxX < 0 ? default : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>칸 하나를 잰 결과. 진단 통로가 읽는다.</summary>
    /// <param name="Drawn">
    /// 실제로 그려지는 칸. 시트에 없어서 <see cref="MascotSheet.Fallback"/> 을 타고
    /// 내려갔으면 <paramref name="Sprite"/> 와 다르다.
    /// </param>
    internal sealed record CellReport(
        MascotSprite Sprite, MascotSprite? Drawn, Int32Rect Ink, int HeadCenterX);

    /// <summary>
    /// 시트가 실제로 어떻게 구워져 있는지 잰다. <c>--probe-mascot</c> 가 쓴다.
    ///
    /// **픽셀을 만지는 코드를 두 벌로 두지 않으려고 여기 둔다.** 진단이 제 손으로
    /// 알파를 훑으면 그리는 쪽과 다른 답을 내놓을 수 있는데, 그러면 진단이 진단을
    /// 못 한다.
    /// </summary>
    internal static IReadOnlyList<CellReport> Measure()
    {
        var found = new List<CellReport>();
        foreach (var sprite in Enum.GetValues<MascotSprite>())
        {
            var drawn = DrawnCell(sprite);
            if (SliceOf(sprite) is { } own)
            {
                found.Add(new CellReport(sprite, sprite, own.Ink, own.HeadCenterX));
            }
            else if (drawn is { } step && SliceOf(step) is { } borrowed)
            {
                found.Add(new CellReport(sprite, step, borrowed.Ink, borrowed.HeadCenterX));
            }
            else
            {
                found.Add(new CellReport(sprite, null, default, 0));
            }
        }
        return found;
    }

    /// <summary>그 자세를 그리면 실제로 나오는 칸. 하나도 없으면 null.</summary>
    private static MascotSprite? DrawnCell(MascotSprite sprite)
    {
        for (var i = 0; i < 8; i++)
        {
            if (SliceOf(sprite) is not null) return sprite;
            if (MascotSheet.Fallback(sprite) is not { } next) return null;
            sprite = next;
        }
        return null;
    }

    /// <summary>시트의 픽셀 크기. 규격의 몇 배인지 보여줄 때 쓴다.</summary>
    internal static (int Width, int Height)? SheetSize()
        => Sheet() is { } found ? (found.PixelWidth, found.PixelHeight) : null;

    /// <summary>모든 칸의 잉크를 묶은 상자. 시트를 못 읽었으면 빈 값이다.</summary>
    internal static Int32Rect CommonInk()
    {
        Sheet();
        return commonInk;
    }
}




