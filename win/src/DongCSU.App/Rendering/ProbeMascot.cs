using System.Windows;
using DongCSU.Core;
using DongCSU.Core.Owl;

namespace DongCSU.App.Rendering;

/// <summary>
/// <c>--probe-mascot</c> — 시트가 실제로 어떻게 구워져 있는지 재서 찍는다.
/// 맥판의 같은 이름 통로와 짝이다.
///
/// <b>왜 필요한가.</b> 시트의 칸 표(<see cref="MascotSheet"/>)는 맥
/// <c>MascotSprite.swift</c> 를 <b>손으로 옮겨 적은 것</b>인데, 격자 부엉이
/// (<c>shared/owl.json</c>)와 달리 <b>어긋났는지 알려 주는 것이 아무것도 없다.</b>
/// 그림 자체는 맥 번들과 같은 파일을 쓰므로 그림은 안 갈리는데, <b>그 그림을 어떻게
/// 읽느냐</b>는 갈릴 수 있다 — 그리고 갈려도 화면에 그럴듯한 것이 나와서 안 드러난다.
///
/// 그래서 <b>그림에서 잰 것</b>과 <b>표에 적어 둔 것</b>을 맞대 본다. 맞대는 것은 하나다:
/// 각 칸이 칸 안에서 <b>어느 모서리에 붙어 있는지</b>(<see cref="MascotSheet.Anchor"/>).
/// 맥이 시트를 구울 때 그 값으로 자리를 잡아 놓기 때문에, 표가 틀리면 잰 값과 어긋난다.
/// </summary>
internal static class ProbeMascot
{
    /// <summary>모서리에 붙었다고 볼 여유(픽셀). 그리는 사람이 1~2px 어긋나게 그릴 수 있다.</summary>
    private const int Slack = 6;

    /// <summary>
    /// <c>--probe-mascot [캐릭터]</c>. 캐릭터를 안 적으면 **시트로 도는 것을 전부** 잰다.
    ///
    /// **하나만 재면 새 캐릭터가 조용히 빠진다.** 칸 표는 캐릭터마다 다시 쓰지 않고
    /// 하나를 나눠 쓰므로, 라쿤 시트가 부엉이와 다르게 구워져 있으면 라쿤에서만
    /// 어긋난다 — 부엉이만 재면 CI 가 그걸 못 잡는다.
    /// </summary>
    public static int Run(string[] args)
    {
        var picked = args.Length > 1 ? Style(args[1]) : null;
        if (args.Length > 1 && picked is null)
        {
            Console.Error.WriteLine($"모르는 캐릭터: {args[1]} (owl · raccoon)");
            return 1;
        }

        var styles = picked is { } one
            ? [one]
            : Enum.GetValues<IconStyle>().Where(style => style.UsesSheet()).ToArray();

        var failed = 0;
        foreach (var style in styles)
        {
            if (styles.Length > 1)
            {
                Console.WriteLine($"── {style.ShortTitle()} ({style.SheetResource()}.png) "
                    + new string('─', 40));
            }
            failed |= Check(style);
            if (styles.Length > 1) Console.WriteLine();
        }
        return failed;
    }

    /// <summary>
    /// 이름으로 캐릭터를 고른다. 리소스 이름과 enum 이름을 다 받는다.
    ///
    /// <b>이름표를 따로 두지 않는다.</b> 캐릭터를 더하면 리소스 이름(<c>raccoon</c>)과
    /// enum 이름(<c>RaccoonSheet</c>)으로 저절로 잡힌다 — 여기에 한 줄씩 늘어놓으면
    /// 캐릭터를 더할 때마다 찾아 고쳐야 하는 자리가 하나 더 생긴다.
    /// </summary>
    private static IconStyle? Style(string name)
    {
        foreach (var style in Enum.GetValues<IconStyle>())
        {
            if (!style.UsesSheet()) continue;
            if (Same(style.SheetResource(), name) || Same(style.ToString(), name)) return style;
        }

        // **부엉이만 예외다.** 리소스 이름이 캐릭터 이름과 달라서(`mascot`) 위에서
        // 안 잡힌다 — 옛 이름이라 파일째 바꾸면 맥과 어긋난다.
        return Same(name, "owl") ? IconStyle.OwlSheet : null;

        static bool Same(string? a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static int Check(IconStyle style)
    {
        if (MascotRenderer.SheetSize(style) is not { } size)
        {
            Console.Error.WriteLine(
                $"시트를 못 읽었다 ({style.SheetResource()}.png 가 앱에 안 박혔다)");
            return 1;
        }

        var multiple = Math.Max(1, size.Width / MascotSheet.SheetWidth);
        var canonical = size.Width == MascotSheet.SheetWidth * multiple
            && size.Height == MascotSheet.SheetHeight * multiple;

        Console.WriteLine($"시트   {size.Width}×{size.Height}"
            + $" · 규격 {MascotSheet.SheetWidth}×{MascotSheet.SheetHeight}"
            + $" · {(canonical ? $"{multiple}배 (규격 좌표)" : "규격 아님 (균등 분할)")}");

        var cells = MascotRenderer.Measure(style);
        var common = MascotRenderer.CommonInk(style);
        var side = MascotSheet.Cell * multiple;

        // **맥의 배율 기준이 이 상자다.** 칸(256)이 아니라 여기 높이로 나눈다.
        Console.WriteLine($"공통 상자 {common.Width}×{common.Height}"
            + $" (칸 {side} 대비 세로 {(double)common.Height / side * 100:0.0}%)");
        Console.WriteLine();
        Console.WriteLine($"{"칸",-14}{"그려지는 것",-14}{"잉크(x,y,w,h)",-26}{"머리x",-7}붙은 모서리");

        var problems = new List<string>();

        foreach (var cell in cells)
        {
            if (cell.Drawn is null)
            {
                Console.WriteLine($"{cell.Sprite,-14}{"(없음)",-14}");
                problems.Add($"{cell.Sprite} 는 대신 그릴 칸조차 없다");
                continue;
            }

            var ink = cell.Ink;
            var edges = Edges(ink, side);
            var borrowed = cell.Drawn == cell.Sprite ? "" : $"→ {cell.Drawn}";

            Console.WriteLine(
                $"{cell.Sprite,-14}{borrowed,-14}"
                + $"{$"{ink.X},{ink.Y},{ink.Width},{ink.Height}",-26}"
                + $"{cell.HeadCenterX,-7}{(edges.Count == 0 ? "-" : string.Join(" ", edges))}");

            // 빌려 온 칸은 제 자리로 안 구워져 있는 것이 정상이라 안 따진다.
            if (cell.Drawn != cell.Sprite) continue;

            // **뜬 만큼을 지키는 칸은 바닥에 안 닿는 것이 정상이다.** 걸음은 다리가
            // 모이는 순간 몸이 뜨고, 뛸 때는 두 발이 다 뜬다 — 그것을 바닥에 붙이면
            // 그림이 담고 있는 오르내림이 통째로 사라진다.
            if (MascotSheet.KeepsLift(cell.Sprite)) continue;

            var want = MascotSheet.Anchor(cell.Sprite);
            if (!edges.Contains(Name(want)))
            {
                problems.Add(
                    $"{cell.Sprite} 는 {Name(want)} 에 붙어 있어야 하는데"
                    + $" 실제로는 {(edges.Count == 0 ? "아무 데도 안 붙었다" : string.Join("·", edges))}"
                    + $" (잉크 {ink.X},{ink.Y},{ink.Width},{ink.Height} / 칸 {side})");
            }
        }

        Console.WriteLine();
        if (problems.Count == 0)
        {
            Console.WriteLine("통과 — 구워진 자리가 칸 표와 맞는다");
            return 0;
        }

        // **beta 캐릭터는 여기서 앱을 멈추지 않는다.**
        //
        // 이 검사가 잡으려는 것은 **칸 표를 잘못 옮겨 적은 것**인데, 실제로 걸리는 것에는
        // 그리는 쪽이 몇 px 어긋나게 그린 것도 섞인다. 둘을 구분할 방법이 없다.
        //
        // 그림을 다듬는 중이라고 못 박아 둔 캐릭터(<c>IsBeta</c>)에서는 뒤쪽이 훨씬 잦고,
        // 그것 때문에 CI 가 빨개지면 **부엉이의 진짜 표 오류까지 같이 묻힌다.** 대신
        // 조용히 넘기지도 않는다 — 줄은 그대로 찍고 무엇을 봐야 하는지 남긴다.
        var beta = style.IsBeta();
        Console.WriteLine(beta
            ? "⚠ 그림과 칸 표가 어긋난다 (beta 캐릭터라 실패로 치지 않는다):"
            : "실패 — 그림과 칸 표가 어긋난다:");
        foreach (var problem in problems) Console.WriteLine($"  {problem}");
        return beta ? 0 : 1;
    }

    /// <summary>잉크가 칸의 어느 모서리에 닿아 있는지.</summary>
    private static List<string> Edges(Int32Rect ink, int side)
    {
        var found = new List<string>();
        if (ink.Y <= Slack) found.Add("위");
        if (ink.Y + ink.Height >= side - Slack) found.Add("아래");
        if (ink.X <= Slack) found.Add("왼쪽");
        if (ink.X + ink.Width >= side - Slack) found.Add("오른쪽");
        return found;
    }

    private static string Name(MascotAnchor anchor) => anchor switch
    {
        MascotAnchor.Top => "위",
        MascotAnchor.Leading => "왼쪽",
        _ => "아래",
    };
}
