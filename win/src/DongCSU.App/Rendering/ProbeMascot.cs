using System.Windows;
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

    public static int Run(string[] args)
    {
        if (MascotRenderer.SheetSize() is not { } size)
        {
            Console.Error.WriteLine("시트를 못 읽었다 (mascot.png 가 앱에 안 박혔다)");
            return 1;
        }

        var multiple = Math.Max(1, size.Width / MascotSheet.SheetWidth);
        var canonical = size.Width == MascotSheet.SheetWidth * multiple
            && size.Height == MascotSheet.SheetHeight * multiple;

        Console.WriteLine($"시트   {size.Width}×{size.Height}"
            + $" · 규격 {MascotSheet.SheetWidth}×{MascotSheet.SheetHeight}"
            + $" · {(canonical ? $"{multiple}배 (규격 좌표)" : "규격 아님 (균등 분할)")}");

        var cells = MascotRenderer.Measure();
        var common = MascotRenderer.CommonInk();
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

        Console.WriteLine("실패 — 그림과 칸 표가 어긋난다:");
        foreach (var problem in problems) Console.WriteLine($"  {problem}");
        return 1;
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
