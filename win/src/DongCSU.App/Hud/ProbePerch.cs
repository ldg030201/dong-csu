using System.Windows;
using DongCSU.App.Rendering;
using DongCSU.Core;
using DongCSU.Core.Owl;
using DongCSU.Core.Pet;

namespace DongCSU.App.Hud;

/// <summary>
/// <c>--probe-perch [selftest|windows]</c> — 창에 붙는 계산을 화면 없이 잰다.
/// 맥 <c>ProbePerch.swift</c> 와 짝이다.
///
/// <b>안 붙는다는 말을 들었을 때 볼 자리다.</b> <c>PerchFinder.Snap</c> 은 되는 자리
/// 하나만 돌려주고 나머지는 조용히 버린다 — 그 "조용히" 가 문제라, 같은 걸러내기를
/// 순서대로 다시 걸으면서 어디서 걸렸는지 찍는다(<c>PerchFinder.Explain</c>).
///
/// <c>selftest</c> 는 <b>가짜 창 목록</b>으로 붙는 규칙을 검사한다 — 바깥 것에 안
/// 기대므로 CI 에 걸어도 된다. <c>windows</c> 는 <b>지금 떠 있는 진짜 창</b>에
/// <b>앱이 쓰는 것과 같은 계산기</b>(<see cref="PerchPlanner"/>)를 태워 본다.
/// </summary>
internal static class ProbePerch
{
    /// <summary>검사에 쓰는 그림 크기. 실제 펫 마스코트(배율 1)와 같은 자리수다.</summary>
    private static readonly PetRect Mascot = new(0, 0, 85, 84);

    public static int Run(string[] args)
    {
        var what = args.Length > 1 ? args[1].ToLowerInvariant() : "selftest";
        return what switch
        {
            "windows" => SurveyWindows(),
            "selftest" => SelfTest(),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("--probe-perch [selftest|windows]");
        return 2;
    }

    /// <summary>
    /// 지금 떠 있는 창과, 거기에 붙을 수 있는 자리.
    ///
    /// <b>창을 안 띄운다</b> — 목록을 뜨는 데 우리 창이 필요 없다. 그래서 우리 프로세스
    /// 창은 애초에 하나도 없고, pid 걸러내기가 헛돌지도 않는다.
    /// </summary>
    private static int SurveyWindows()
    {
        // 창이 없으니 계수는 1:1 로 본다. 배율이 섞인 자리를 재려면 앱을 띄워야 한다 —
        // 여기서 보려는 것은 **어떤 창이 목록에 남는가**다.
        var windows = WindowSurvey.OnScreenWindowsRaw();
        Console.WriteLine($"창 {windows.Count}개 (앞에 있는 것이 먼저)");
        foreach (var window in windows)
        {
            Console.WriteLine($"  {window.Owner,-20} [{window.Handle}] "
                + PerchFinder.Box(window.Frame));
        }

        if (windows.Count == 0)
        {
            Console.Error.WriteLine("창이 하나도 안 잡혔다 — 걸러내기가 너무 세다");
            return 1;
        }

        // **앱이 쓰는 것과 같은 계산기를 태운다.** 따로 셈하면 표에는 "가능" 이 뜨는데
        // 실제로는 안 붙는 자리가 생긴다 — 맥에서 실제로 그랬다.
        // **사용자의 진짜 설정을 읽는다.** 기본값으로 재면 잡는 깊이를 손으로 맞춰 둔
        // 사람이나 라쿤을 골라 둔 사람에게 진단이 앱과 다른 숫자를 낸다 — 그러면 진단이
        // 진단을 못 한다.
        var settings = AppSettings.Load();

        var view = new HudView
        {
            Mode = HudMode.Pet,
            Scale = settings.Scale.Factor(),
            IconStyle = settings.IconStyle,
        };
        // 창이 없어서 작업 영역을 못 읽는다. 화면 밖 판정은 여기서 끄고, 그것까지
        // 보려면 앱을 띄워 놓고 기록을 봐라(놓을 때마다 같은 표가 남는다).
        var planner = new PerchPlanner(view, settings, work: null);

        // 첫 창의 위 테두리 바로 위에 놓아 본다. **왜 붙고 왜 안 붙는지**가 여기서 나온다.
        var first = windows[0].Frame;
        if (planner.MascotRect(new Point(0, 0)) is not { } shape)
        {
            Console.Error.WriteLine("그림을 못 읽었다 — 시트가 앱에 안 박혔다");
            return 1;
        }

        var origin = new Point(
            first.X + first.Width / 2 - shape.Width / 2 - shape.X,
            first.Y - shape.Height - shape.Y);
        var mascot = planner.MascotRect(origin)!.Value;

        Console.WriteLine();
        Console.WriteLine($"놓은 자리 {PerchFinder.Box(mascot)} — 첫 창의 위 테두리");
        // **앱이 쓰는 것과 같은 것을 엮어 준다** — 걸러내기가 하나 늘 때 표만 옛
        // 규칙으로 "가능" 이라고 말하지 않게.
        foreach (var line in planner.Explain(origin, windows)) Console.WriteLine(line);

        var picked = planner.Snap(origin, windows);
        Console.WriteLine();
        Console.WriteLine(picked is { } spot
            ? $"고른 자리: {spot.Edge} · 창 {spot.Window} · 오프셋 {spot.Offset:0} "
              + $"· 잠기는 깊이 {planner.Sink(spot):0.0}pt"
            : "고른 자리: 없다");
        return 0;
    }

    /// <summary>
    /// 붙는 규칙이 맞는지. <b>가짜 창 목록으로 돈다</b> — 빌드 기계에서도 같은 답이 나온다.
    /// </summary>
    private static int SelfTest()
    {
        var failed = 0;
        Console.WriteLine("창에 붙기 — 규칙 검사");

        // 화면 한가운데 큰 창 하나.
        var window = new PerchWindow(1, new PetRect(400, 300, 800, 600), "가짜창");
        IReadOnlyList<PerchWindow> one = [window];
        var limit = PerchFinder.SnapDistance(Mascot.Width, Mascot.Height);

        PetRect At(double x, double y) => new(x, y, Mascot.Width, Mascot.Height);

        // 위 테두리 바로 위 — 발이 닿는다.
        failed |= Check("위 테두리에 앉는다",
            PerchFinder.Snap(At(760, 300 - Mascot.Height), limit, one)?.Edge == MascotPerch.Top);

        // 아래 테두리 바로 아래 — 손이 닿는다.
        failed |= Check("아래 테두리에 매달린다",
            PerchFinder.Snap(At(760, 900), limit, one)?.Edge == MascotPerch.Bottom);

        // 오른쪽 테두리 바깥.
        failed |= Check("오른쪽 테두리를 껴안는다",
            PerchFinder.Snap(At(1200, 560), limit, one)?.Edge == MascotPerch.Right);

        failed |= Check("왼쪽 테두리를 껴안는다",
            PerchFinder.Snap(At(400 - Mascot.Width, 560), limit, one)?.Edge == MascotPerch.Left);

        // 멀리 놓으면 안 붙는다.
        failed |= Check("멀면 안 붙는다",
            PerchFinder.Snap(At(760, 300 - Mascot.Height - limit - 10), limit, one) is null);

        // **모서리 끝을 겨냥해도 안 튄다.** 오프셋 하한이 그림 폭의 절반이라,
        // 붙자마자 첫 추적 틱이 옆으로 밀어 넣는 일이 없어야 한다.
        var corner = PerchFinder.Snap(At(400 - Mascot.Width / 2, 300 - Mascot.Height), limit, one);
        failed |= Check("모서리 끝을 겨냥해도 안 튄다",
            corner is { } edge && Math.Abs(edge.Offset - Mascot.Width / 2) < 0.001);

        // **가려진 테두리에는 안 붙는다.** 앞에 있는 창이 그 자리를 덮고 있으면 뺀다.
        var cover = new PerchWindow(2, new PetRect(600, 100, 400, 400), "덮개");
        IReadOnlyList<PerchWindow> covered = [cover, window];
        failed |= Check("가려진 테두리에는 안 붙는다",
            PerchFinder.Snap(At(760, 300 - Mascot.Height), limit, covered) is null);

        // 대조군 — 덮개를 치우면 붙는다. **이게 없으면 위 검사가 늘 통과한다.**
        failed |= Check("가리는 창이 없으면 붙는다",
            PerchFinder.Snap(At(760, 300 - Mascot.Height), limit, one) is not null);

        // 너무 작은 창은 애초에 목록에 안 들어오지만, 모서리가 그림보다 짧아도 안 붙는다.
        var narrow = new PerchWindow(3, new PetRect(400, 300, 60, 600), "좁은창");
        failed |= Check("모서리가 그림보다 짧으면 안 붙는다",
            PerchFinder.Snap(At(400, 300 - Mascot.Height), limit, [narrow]) is null);

        // 붙은 창을 찾는다 / 못 찾는다.
        failed |= Check("맨 앞 창을 맨 앞으로 본다",
            PerchFinder.Locate(1, one) is { IsFront: true });
        failed |= Check("뒤 창을 앞으로 보지 않는다",
            PerchFinder.Locate(1, covered) is { IsFront: false });
        failed |= Check("닫힌 창은 못 찾는다", PerchFinder.Locate(99, one) is null);

        failed |= CheckInk();

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "전부 통과" : "실패");
        return failed;
    }

    /// <summary>
    /// 자세마다 그림이 창 안에서 덮는 자리를 실제 시트에서 재 본다.
    ///
    /// <b>여기가 뒤집히면 절반이 반대로 나온다.</b> 잉크 비율은 그림에서 재는 값이라
    /// 테스트로 굳힐 수 없다(<c>Core</c> 는 시트를 못 본다) — 그림이 바뀌거나 좌우
    /// 반전을 잘못 얹으면 이 통로에서만 드러난다.
    /// </summary>
    private static int CheckInk()
    {
        Console.WriteLine();
        Console.WriteLine("자세마다 그림이 덮는 자리 (펫 창 128x160, 배율 1)");

        var view = new HudView
        {
            Mode = HudMode.Pet,
            Scale = 1,
            IconStyle = IconStyle.OwlSheet,
        };

        var failed = 0;
        var window = new PetRect(0, 0, HudView.BasePetWidth, HudView.BasePetWidth + HudView.BasePetButtonRow);

        foreach (var perch in Enum.GetValues<MascotPerch>())
        {
            var ink = view.PetMascotInkRect(perch);
            if (ink is not { } box)
            {
                Console.WriteLine($"  {Pad(perch.ToString(), 10)}(못 잼 — 시트를 못 읽었다)");
                failed = 1;
                continue;
            }

            var rect = new PetRect(box.X, box.Y, box.Width, box.Height);
            var inside = window.ContainsRect(rect);
            var drawn = MascotRenderer.ResolvedSprite(view.IconStyle, perch.Sprite());
            Console.WriteLine(
                $"  {Pad(perch.ToString(), 10)}{perch.Sprite(),-8}"
                + $"{(drawn == perch.Sprite() ? "" : $"→{drawn} ")}"
                + $"({rect.X:0.0},{rect.Y:0.0}) {rect.Width:0.0}x{rect.Height:0.0}"
                + $"{(perch.FlipsSprite() ? "  뒤집음" : "")}"
                + $"   {(inside && rect.Width > 0 && rect.Height > 0 ? "통과" : "실패 — 창 밖")}");

            if (!inside || rect.Width <= 0 || rect.Height <= 0) failed = 1;
        }

        // **좌우는 거울이어야 한다.** 뒤집기 축을 잘못 잡으면 왼쪽 벽붙기가 몇십 pt 밀린다.
        if (view.PetMascotInkRect(MascotPerch.Left) is { } left
            && view.PetMascotInkRect(MascotPerch.Right) is { } right)
        {
            var mirrored = Math.Abs((left.Left + left.Right) - (HudView.BasePetWidth * 2 - (right.Left + right.Right)));
            failed |= Check("좌우 벽붙기가 서로 거울이다", mirrored < 0.5);
        }

        return failed;
    }

    private static int Check(string label, bool ok)
    {
        Console.WriteLine($"  {Pad(label, 40)}{(ok ? "통과" : "실패")}");
        return ok ? 0 : 1;
    }

    /// <summary>한글이 두 칸을 먹는 것을 세어서 폭을 맞춘다.</summary>
    private static string Pad(string text, int width)
    {
        var used = text.Sum(c => c >= 0x1100 ? 2 : 1);
        return text + new string(' ', Math.Max(0, width - used));
    }
}
