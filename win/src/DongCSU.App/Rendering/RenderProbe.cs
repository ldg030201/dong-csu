using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DongCSU.App.Hud;
using DongCSU.App.Settings;
using DongCSU.Core;
using DongCSU.App.Services;
using DongCSU.Core.Owl;
using DongCSU.Core.Usage;

namespace DongCSU.App.Rendering;

/// <summary>
/// 화면을 띄우지 않고 PNG 로 뽑는다.
///
/// **눈으로 확인할 유일한 싼 방법이다.** 앱을 띄우고 창을 찾아 마우스를 옮겨 가며
/// 찍는 것은 느리고 잘 어긋난다 — 설정 창이 다른 창 뒤에 깔리면 엉뚱한 것이 찍힌다.
/// 맥의 <c>--render</c> · <c>--render-settings</c> 와 같은 자리다.
///
/// **`VisualBrush` 로 찍지 마라.** 기본이 <c>Stretch.Fill</c> 이라 **뷰의 내용 경계**를
/// 대상 사각형에 맞춰 늘린다. 배경이 창을 꽉 채우는 보기에서는 우연히 1:1 이라 안
/// 드러나는데, 펫처럼 배경이 없으면 경계가 마스코트만큼 줄어들어 그림이 확대된 채로
/// 찍힌다 — 없는 버그를 쫓게 된다. <see cref="Shoot"/> 는 <c>RenderTargetBitmap</c> 을 쓴다.
/// </summary>
internal static class RenderProbe
{
    /// <summary>
    /// HUD 를 그린다.
    ///
    /// <code>--render out.png [세션%] [주간%] [보기] [아이콘] [배율] [테마]</code>
    /// </summary>
    public static int Hud(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("--render <out.png> [세션%] [주간%] [expanded|collapsed|pet] "
                + "[owl|owlsheet|clawd|appicon|mark] [small|normal|large|xlarge] [dark|light]");
            return 2;
        }

        var session = Number(args, 2) ?? 34;
        var weekly = Number(args, 3) ?? 61;
        var mode = Word(args, 4) switch
        {
            "collapsed" => HudMode.Collapsed,
            "pet" => HudMode.Pet,
            _ => HudMode.Expanded,
        };
        var icon = Word(args, 5) switch
        {
            "owl" => IconStyle.Owl,
            "clawd" => IconStyle.Clawd,
            "appicon" => IconStyle.AppIcon,
            "mark" => IconStyle.Mark,
            _ => IconStyle.OwlSheet,
        };
        var scale = Word(args, 6) switch
        {
            "small" => HudScale.Small,
            "large" => HudScale.Large,
            "xlarge" => HudScale.ExtraLarge,
            _ => HudScale.Normal,
        };
        var dark = Word(args, 7) != "light";

        var view = new HudView
        {
            Mode = mode,
            IconStyle = icon,
            Scale = scale.Factor(),
            IsDark = dark,
            VersionBadge = AppInfo.Version,
            VersionBadgeIsTest = AppInfo.IsTestBuild,
            // 펫은 링이 마우스를 올려야 뜬다. 뽑을 때는 보이는 편이 쓸모 있다.
            PetRingDisplay = PetRingDisplay.Always,
            PetRingFade = 1,
            Snapshot = new UsageSnapshot
            {
                PlanName = "Max",
                FiveHour = new UsageWindow { Utilization = session, ResetsAt = DateTimeOffset.UtcNow.AddHours(3) },
                SevenDay = new UsageWindow { Utilization = weekly, ResetsAt = DateTimeOffset.UtcNow.AddDays(2) },
                FetchedAt = DateTimeOffset.UtcNow,
            },
        };

        var size = view.SizeFor(mode);
        Shoot(view, size, args[1]);
        Console.WriteLine($"wrote: {args[1]}  ({size.Width:0}x{size.Height:0})");
        return 0;
    }

    /// <summary>
    /// 설정 창의 한 탭을 그린다.
    ///
    /// <code>--render-settings out.png [탭] [크기] [dark|light]</code>
    /// </summary>
    public static int SettingsTab(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("--render-settings <out.png> [status|display|icon|pet|account|version] "
                + "[너비x높이] [dark|light]");
            return 2;
        }

        var tab = Word(args, 2) ?? "status";
        var size = Size(Word(args, 3)) ?? new Size(760, 760);
        var dark = Word(args, 4) == "dark";

        // 조회를 안 하므로 화면은 "아직 없음" 상태로 그려진다. 배치를 보는 통로다.
        var settings = new AppSettings { Theme = Word(args, 4) == "light" ? HudTheme.Light : HudTheme.Dark };
        var http = UsageApi.CreateHttpClient();
        var credentials = new CredentialStore(
            new FileCredentialSource(fallbackPaths: WslCredentialPaths.All),
            refreshedTokens: new RefreshedTokenStore());
        var store = new UsageStore(new UsageApi(http, credentials));
        var updates = new UpdateService(http);

        var window = new SettingsWindow(
            settings, store, updates,
            onChanged: () => { }, onResetPosition: () => { },
            onTogglePet: () => { }, onLogin: () => { })
        {
            Width = size.Width,
            Height = size.Height,
            // 창을 띄우지 않고 그린다. 화면 밖에 두어 잠깐이라도 안 보이게 한다.
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false,
        };
        window.SelectTab(tab);

        // **레이아웃을 한 번 돌려야 그릴 것이 생긴다.** 창을 안 띄우면 Measure·Arrange 가
        // 저절로 안 돈다 — 그냥 찍으면 빈 그림이 나온다.
        window.Show();
        window.UpdateLayout();

        Shoot(window, size, args[1]);
        window.Close();

        Console.WriteLine($"wrote: {args[1]}  ({size.Width:0}x{size.Height:0}, {tab})");
        return 0;
    }

    /// <summary>
    /// 부엉이 전 프레임을 한 장에 늘어놓는다. 자세를 고쳤을 때 눈으로 보는 자리다.
    ///
    /// <code>--render-owl out.png [칸 크기]</code>
    /// </summary>
    public static int Owl(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("--render-owl <out.png> [칸 크기]");
            return 2;
        }

        var cell = (int)(Number(args, 2) ?? 72);
        var document = OwlDocument.Embedded;
        var columns = document.Animations.Max(a => a.Frames.Count);
        var rows = document.Animations.Count;

        const int labelWidth = 92;
        var width = labelWidth + columns * cell;
        var height = rows * cell;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

            for (var row = 0; row < rows; row++)
            {
                var animation = document.Animations[row];
                var brushes = OwlRenderer.Brushes(document.Palettes[animation.Palette]);

                context.DrawText(
                    new FormattedText(
                        animation.Name,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        new Typeface("Consolas"),
                        12,
                        Brushes.White,
                        1),
                    new Point(8, row * cell + cell / 2.0 - 8));

                for (var column = 0; column < animation.Frames.Count; column++)
                {
                    var box = new Rect(labelWidth + column * cell, row * cell, cell, cell);
                    var pixel = OwlRenderer.CellSize(box.Height, document.Grid.Lines);
                    var drawn = OwlRenderer.MeasuredSize(pixel, document.Grid);
                    OwlRenderer.Draw(
                        context,
                        animation.Frames[column].Grid,
                        brushes,
                        new Point(
                            Math.Round(box.X + (box.Width - drawn.Width) / 2),
                            Math.Round(box.Y + (box.Height - drawn.Height) / 2)),
                        pixel);
                }
            }
        }

        Save(visual, width, height, args[1]);
        Console.WriteLine($"wrote: {args[1]}  ({rows}줄 x 최대 {columns}칸)");
        return 0;
    }

    // ── 도구 ────────────────────────────────────────────────────────

    private static void Shoot(FrameworkElement element, Size size, string path)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        Write(bitmap, path);
    }

    private static void Save(Visual visual, double width, double height, string path)
    {
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Write(bitmap, path);
    }

    private static void Write(RenderTargetBitmap bitmap, string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string? Word(string[] args, int index) =>
        args.Length > index ? args[index].ToLowerInvariant() : null;

    private static double? Number(string[] args, int index) =>
        args.Length > index && double.TryParse(args[index], out var value) ? value : null;

    private static Size? Size(string? text)
    {
        var parts = text?.Split('x');
        if (parts is not { Length: 2 }
            || !double.TryParse(parts[0], out var width)
            || !double.TryParse(parts[1], out var height))
        {
            return null;
        }
        return new Size(width, height);
    }
}
