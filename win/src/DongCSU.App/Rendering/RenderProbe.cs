using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DongCSU.App.Hud;
using DongCSU.App.Settings;
using DongCSU.App.Tray;
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
        ReleaseLook();

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
            VersionBadgeIsTest = false,
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

        // 격자 부엉이는 뷰가 스스로 못 채운다. 맥은 뷰가 애니메이터를 들고 있어서
        // 저절로 채워지지만, 여기는 꽂아 주는 쪽이 없으면 링만 있고 가운데가 빈다.
        ApplyMascot(view, session, weekly);

        var size = view.SizeFor(mode);
        Shoot(view, size, args[1]);
        Console.WriteLine($"wrote: {args[1]}  ({size.Width:0}x{size.Height:0})");
        return 0;
    }

    /// <summary>
    /// 넘긴 사용률대로 마스코트를 꽂는다. 앱의 <c>RefreshMascot</c> 과 같은 일이다.
    ///
    /// **이걸 안 하면 <c>owl</c> 로 뽑았을 때 가운데가 통째로 빈다** —
    /// <c>HudView.DrawOwl</c> 이 격자가 없으면 그냥 돌아 나오고, 예외도 종료 코드도
    /// 멀쩡해서 눈치채기 어렵다. <c>owlsheet</c> 는 시트에서 잘라 그려 멀쩡해 보이는
    /// 것이 함정이다.
    ///
    /// 애니메이터를 만들어 놓고 <c>Advance</c> 를 안 부르므로 그 기분의 첫 프레임에
    /// 머문다 — 정지 그림이 나온다.
    /// </summary>
    private static void ApplyMascot(HudView view, double session, double weekly)
    {
        var animator = new OwlAnimator(OwlDocument.Embedded);

        // **<see cref="UsageStore.IsWeeklySpent"/> 와 같은 기준이어야 한다**(SevenDay >= 100).
        // 여기만 다르게 적으면 그림과 실제 앱이 어긋나서, 그림으로 한 검증이 무의미해진다.
        var spent = weekly >= 100;
        // 문턱은 owl.json 이 들고 있다. 숫자를 여기 적어 두면 맥이 기준을 바꿨을 때 어긋난다.
        var mood = OwlMoodResolver.Resolve(
            OwlDocument.Embedded, session, isDisconnected: false, isWeeklySpent: spent);

        // **`SetMood` 보다 먼저 꽂는다.** `IsUnusable` 이 팔레트와 시트 칸을 모두 덮으므로
        // 순서가 뒤집히면 방금 읽어 둔 값이 낡는다.
        animator.IsUnusable = spent;
        animator.SetMood(mood);

        view.OwlGrid = animator.CurrentGrid;
        // **`Program.MascotPalette()` 를 흉내내지 않는다.** 그쪽은 테스트판이면 `normal` 을
        // `test` 로 바꾸는데, 렌더 통로는 <see cref="ReleaseLook"/> 로 정식판 색을 못 박아
        // 뒀다. 여기서 또 바꾸면 두 통로가 서로 다른 색을 그린다.
        view.OwlPaletteName = animator.PaletteName;
        view.MascotFrame = animator.MascotFrame;
        // 격자 부엉이에는 안 걸리지만 `owlsheet` 는 이 값을 본다. 같이 옮겨야 두 아이콘
        // 스타일이 같은 자세로 나온다.
        view.MascotFlipped = animator.SpriteFlipped;
        // 자세만 탈진하고 색이 안 빠지면 "아직 여유가 있다"로 읽힌다. 링·숫자와 같은 규칙이다.
        view.IsWeeklySpent = spent;
    }

    /// <summary>
    /// 설정 창의 한 탭을 그린다.
    ///
    /// <code>--render-settings out.png [탭] [크기] [dark|light] [새 버전]</code>
    /// </summary>
    public static int SettingsTab(string[] args)
    {
        ReleaseLook();

        if (args.Length < 2)
        {
            Console.WriteLine("--render-settings <out.png> [status|display|icon|pet|account|version] "
                + "[너비x높이] [dark|light] [새 버전(예: 1.2.3)]");
            return 2;
        }

        var tab = Word(args, 2) ?? "status";
        var size = Size(Word(args, 3)) ?? new Size(760, 760);
        // 새 버전이 있는 모습을 보고 싶을 때만 준다. 맥의 `update=1.2.3` 과 같은 자리다.
        // **소문자로 뭉개지 않는다** — 버전 문자열은 그대로 화면에 나온다.
        var latest = args.Length > 5 ? args[5] : null;

        var settings = new AppSettings { Theme = Word(args, 4) == "light" ? HudTheme.Light : HudTheme.Dark };
        var http = UsageApi.CreateHttpClient();
        var credentials = new CredentialStore(
            new FileCredentialSource(fallbackPaths: WslCredentialPaths.All),
            refreshedTokens: new RefreshedTokenStore());
        var store = new UsageStore(new UsageApi(http, credentials));
        var updates = new UpdateService(http);

        // **조회를 한 번도 안 걸어서 화면이 통째로 빈다.** 상태 탭은 플랜·사용률·마지막
        // 조회가 전부 비고, 계정 탭의 로그인 카드는 스냅샷이 없으면 아예 안 그려진다.
        // 맥 `writeSettings` 와 **같은 고정값**을 꽂아 사용자가 볼 화면과 맞춘다.
        var now = DateTimeOffset.Now;
        store.Preview(
            new UsageSnapshot
            {
                PlanName = "Max",
                FiveHour = new UsageWindow(34, now.AddHours(3)),
                SevenDay = new UsageWindow(61, now.AddHours(26)),
                FetchedAt = now,
                // 이 둘은 자격 증명에서 온다. 계정 탭이 보여주는 줄이라 같이 꽂는다.
                RateLimitTier = "default_claude_max_5x",
                TokenExpiresAt = now.AddHours(6).AddMinutes(41),
            },
            // 상태 탭이 조회 카운트다운을 그린다. 예정 시각까지 넣어야 실제와 같아진다.
            nextPoll: now.AddMinutes(7).AddSeconds(12));

        // 버전 탭의 "마지막 확인" 줄. 설치본으로 친다 — 폴더에 놓인 exe 로 뽑으면
        // 늘 "설치본이 아니라 자동 업데이트를 쓸 수 없습니다" 가 나와 실제와 달라진다.
        updates.Preview(latest, now.AddMinutes(-40));

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

    /// <summary>
    /// 트레이 아이콘을 **기분마다 한 줄, 프레임마다 한 칸**으로 늘어놓는다.
    ///
    /// <code>--render-menubar out.png [아이콘 크기] [확대] [idle|tired|exhausted|offline|all] [test]</code>
    ///
    /// 맥은 한 장만 뽑지만 여기는 프레임을 나란히 놓는다. 아직 눈으로 못 본 것이
    /// 정지 그림이 아니라 **눈 깜빡임**이고, 그건 프레임을 늘어놓아야만 보인다.
    ///
    /// 그림은 트레이가 쓰는 <see cref="TrayIconArt.Render"/> 를 그대로 부른다 —
    /// 여기서 한 벌 더 그리면 화면과 다른 코드로 확인하는 것이라 아무것도 확인한 것이
    /// 아니게 된다. 격자 자체가 맞는지는 <c>OwlComposerTests</c> 가 이미 대조하므로,
    /// 이 통로가 보는 것은 **32px 로 줄였을 때 그 격자가 무엇이 되는가** 하나다.
    /// </summary>
    public static int Menubar(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("--render-menubar <out.png> [아이콘 크기] [확대] "
                + "[idle|tired|exhausted|offline|all] [test]");
            return 2;
        }

        // 기본값은 실제 트레이 크기(32) 와 한 칸이 보일 만큼의 확대(6).
        var size = Math.Max(1, (int)(Number(args, 2) ?? 32));
        var zoom = Math.Max(1, (int)(Number(args, 3) ?? 6));

        // `test` 는 마지막 자리지만, 기분을 안 적고 바로 붙여도 알아듣게 둔다.
        var test = args.Skip(4).Any(a => string.Equals(a, "test", StringComparison.OrdinalIgnoreCase));
        var picked = Word(args, 4);

        var document = OwlDocument.Embedded;
        string[] moods = ["idle", "tired", "exhausted", "offline"];
        var animations = moods
            .Where(name => picked is null or "all" or "test" || picked == name)
            .Select(name => document.Animations.FirstOrDefault(a => a.Name == name))
            .OfType<OwlAnimation>()
            .ToList();

        if (animations.Count == 0)
        {
            Console.WriteLine($"모르는 기분: {picked} ({string.Join(" · ", moods)} · all)");
            return 2;
        }

        // **칸 크기는 아이콘과 같은 셈법으로 구한다.** `TrayIconArt` 가 한 칸을 내림해서
        // 그리므로 나온 비트맵이 `size` 보다 작을 수 있고, 그걸 칸에 늘려 맞추면 정수
        // 배율이 깨져 "칸이 뭉개진 것"과 "확대가 부드러운 것"을 구별할 수 없게 된다.
        var pixel = Math.Max(1, size / document.Grid.Lines);
        var artWidth = pixel * document.Grid.Columns * zoom;
        var artHeight = pixel * document.Grid.Lines * zoom;
        var cellWidth = Math.Max(size * zoom, artWidth);
        var cellHeight = Math.Max(size * zoom, artHeight);

        const int labelWidth = 92;
        var columns = animations.Max(a => a.Frames.Count);
        var half = columns * cellWidth;
        var width = labelWidth + half * 2;
        var height = animations.Count * cellHeight;

        // **트레이 아이콘은 배경이 투명이다.** 검정 위에서만 보면 밝은 테마에서 무엇이
        // 되는지 모른 채 넘어가므로, 작업 표시줄과 비슷한 중간 회색 옆에 밝은 회색 띠를
        // 나란히 깔고 같은 프레임을 두 번 그린다.
        var darkBand = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        var lightBand = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

        var group = new DrawingGroup();
        // 픽셀 아트라 부드럽게 하면 뭉개진다. 확대할 때 이웃 픽셀을 그대로 늘려야
        // 32px 에서 한 칸이 몇 픽셀인지 보인다 — 이 통로의 목적이 그것이다.
        RenderOptions.SetEdgeMode(group, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);

        using (var context = group.Open())
        {
            context.DrawRectangle(darkBand, null, new Rect(0, 0, width, height));
            context.DrawRectangle(lightBand, null, new Rect(labelWidth + half, 0, half, height));

            for (var row = 0; row < animations.Count; row++)
            {
                var animation = animations[row];
                // 팔레트는 `OwlAnimator.PaletteName` 과 같은 규칙이다 — 기분이 제 팔레트를
                // 들고 있으면(offline) 그쪽이 이기고, 평소 색일 때만 테스트판 보라로 바꾼다.
                var paletteName = test && animation.Palette == "normal" ? "test" : animation.Palette;
                var palette = document.Palettes[paletteName];

                context.DrawText(
                    new FormattedText(
                        animation.Name,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        new Typeface("Consolas"),
                        12,
                        Brushes.White,
                        1),
                    new Point(8, row * cellHeight + cellHeight / 2.0 - 8));

                for (var column = 0; column < animation.Frames.Count; column++)
                {
                    var image = Shrink(animation.Frames[column].Grid, palette, size);

                    // 어두운 쪽과 밝은 쪽에 같은 것을 한 번씩.
                    foreach (var band in new[] { labelWidth, labelWidth + half })
                    {
                        var box = new Rect(
                            band + column * cellWidth, row * cellHeight, cellWidth, cellHeight);
                        context.DrawImage(
                            image,
                            new Rect(
                                Math.Round(box.X + (box.Width - artWidth) / 2),
                                Math.Round(box.Y + (box.Height - artHeight) / 2),
                                artWidth,
                                artHeight));
                    }
                }
            }
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawDrawing(group);

        Save(visual, width, height, args[1]);
        Console.WriteLine(
            $"wrote: {args[1]}  ({animations.Count}줄 x 최대 {columns}칸, {size}px x{zoom}"
            + $"{(test ? ", test" : "")})");
        return 0;
    }

    /// <summary>
    /// 한 프레임을 **진짜 트레이 크기**로 줄여 온다.
    ///
    /// GDI+ 비트맵을 WPF 로 넘기는 길인데, <c>GetHbitmap</c> 쪽은 핸들을 손으로 지워야
    /// 해서 한 번 빠뜨리면 조용히 샌다. PNG 스트림으로 돌리면 그럴 일이 없다 —
    /// 프레임 스물몇 개를 도는 자리라 안전한 쪽을 골랐다.
    /// </summary>
    private static BitmapImage Shrink(
        string[] grid, IReadOnlyDictionary<string, string> palette, int size)
    {
        using var bitmap = TrayIconArt.Render(grid, palette, size);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        // 스트림을 닫은 뒤에 그리므로 지금 다 읽어 둬야 한다.
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>
    /// **정식판 색으로 그린다.** 문서 그림은 테스트 바이너리로 뽑는데, 그대로 두면
    /// 마스코트가 전부 보라색이고 버전 딱지에 `test` 가 붙는다 — 사용자가 볼 화면이
    /// 아니다. 테스트판 모습을 보고 싶으면 앱을 띄워서 본다.
    /// </summary>
    private static void ReleaseLook() => MascotRenderer.TestLook = false;

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
