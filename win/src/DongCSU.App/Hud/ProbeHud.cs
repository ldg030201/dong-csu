using System.Windows;
using DongCSU.Core;
using DongCSU.Core.Usage;

namespace DongCSU.App.Hud;

/// <summary>
/// <c>--probe-hud</c> — HUD 카드가 <b>모든 조합에서 안 넘치는지</b> 잰다.
/// 맥 <c>ProbeHUD.swift</c> 와 짝이다.
///
/// <b>눈으로는 못 잡는다.</b> 카드 밖으로 넘친 그림은 창 경계에서 잘려 나가서,
/// 스크린샷을 봐도 "원래 저렇게 생긴 것" 처럼 보인다. <c>--render</c> 는 더 못 잡는다 —
/// 그림 크기를 내용에 맞춰 잡으므로 넘칠 자리 자체가 없다.
///
/// 그래서 화면이 아니라 <b>치수</b>를 잰다. 창 크기와 그 안에 놓이는 것들을 뷰가 쓰는
/// 셈 그대로 물어봐서(<c>HudView.Probe*</c>), 하나라도 창 밖으로 나가면 실패시킨다.
///
/// 보기 3가지 × 모델별 한도 × 자원 사용량 × 배율 4단계 = 48가지를 전부 돈다.
/// <b>바깥 것에 안 기댄다</b> — 자격 증명도 네트워크도 안 본다. CI 에 걸어도 된다.
/// </summary>
internal static class ProbeHud
{
    public static int Run()
    {
        var failed = new List<string>();
        var rows = 0;

        foreach (var mode in new[] { HudMode.Expanded, HudMode.Collapsed, HudMode.Pet })
        {
            Console.WriteLine();
            Console.WriteLine(Label(mode));
            Console.WriteLine("  모델별 CPU 배율        창          링  가운데  검사");

            foreach (var scoped in new[] { false, true })
            {
                foreach (var stats in new[] { false, true })
                {
                    foreach (var scale in Enum.GetValues<HudScale>())
                    {
                        rows++;
                        // **한 벌만 만든다.** 재는 쪽과 찍는 쪽이 다른 뷰면, 값이 갈렸을 때
                        // 표에는 통과가 뜨는데 실패로 세는 꼴이 날 수 있다.
                        var view = View(mode, scoped, stats, scale, toRight: true);
                        var notes = Check(view, mode, scoped, stats, scale);
                        var panel = view.SizeFor(mode);
                        var ring = view.ProbeRingRect().Width;
                        var room = view.ProbeIconRoom();

                        Console.WriteLine(
                            $"  {(scoped ? "켬" : "끔"),-5} {(stats ? "켬" : "끔"),-3} {scale.Title(),-9}"
                            + $" {panel.Width,4:0}x{panel.Height,-4:0}  {ring,5:0.0} {room,5:0.0}   "
                            + (notes.Count == 0 ? "통과" : string.Join(" · ", notes)));

                        if (notes.Count > 0)
                        {
                            failed.Add($"{Label(mode)} 모델별{(scoped ? "켬" : "끔")}"
                                + $"/CPU{(stats ? "켬" : "끔")}/{scale.Title()}");
                        }
                    }
                }
            }
        }

        PrintToggles();

        Console.WriteLine();
        Console.WriteLine($"{rows}가지 중 {rows - failed.Count}가지 통과");
        foreach (var name in failed) Console.WriteLine($"  실패 — {name}");
        Console.WriteLine();
        Console.WriteLine(failed.Count == 0 ? "전부 통과" : "실패");
        return failed.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// 보기마다 무엇을 그리는지 = 설정 창에서 무엇을 만질 수 있는지.
    ///
    /// <b>둘이 같아야 한다.</b> 그리는데 잠겨 있으면 눈앞에 보이는 것을 못 끄고,
    /// 안 그리는데 열려 있으면 눌러도 아무 일이 없다. <see cref="HudView.Draws"/>
    /// 한 곳에서 나오므로 이 표가 곧 설정 창의 잠금 상태다.
    /// </summary>
    private static void PrintToggles()
    {
        Console.WriteLine();
        Console.WriteLine("보기마다 그리는 것 (= 설정 창에서 만질 수 있는 것)");
        Console.WriteLine($"  {"",-18}펼치기  접기   펫");

        (string Name, HudElement Element)[] elements =
        [
            ("CPU·메모리 줄", HudElement.ProcessStats),
            ("모델별 링", HudElement.ScopedRing),
            ("버전 딱지", HudElement.VersionBadge),
        ];

        foreach (var (name, element) in elements)
        {
            var marks = new[] { HudMode.Expanded, HudMode.Collapsed, HudMode.Pet }
                .Select(mode => HudView.Draws(element, mode) ? "  O     " : "  -     ");
            // 한글은 두 칸을 먹는다. 폭을 글자 수가 아니라 **칸 수**로 맞춘다.
            Console.WriteLine("  " + ProbeText.Pad(name, 18) + string.Join("", marks));
        }
    }

    private static string Label(HudMode mode) => mode switch
    {
        HudMode.Expanded => "펼친 카드",
        HudMode.Collapsed => "접은 카드",
        _ => "펫",
    };

    /// <summary>재는 데 쓸 뷰 하나. 값은 실제 앱이 넣는 것과 같은 꼴로 채운다.</summary>
    private static HudView View(HudMode mode, bool scoped, bool stats, HudScale scale, bool toRight)
        => new()
        {
            Mode = mode,
            Scale = scale.Factor(),
            ExpandSide = toRight ? HudExpandSide.Right : HudExpandSide.Left,
            ShowsProcessStats = stats,
            ShowsScopedLimit = scoped,
            VersionBadge = AppInfo.Version,
            HasUpdate = true,
            Snapshot = new UsageSnapshot
            {
                FiveHour = new UsageWindow { Utilization = 34 },
                SevenDay = new UsageWindow { Utilization = 61 },
                FetchedAt = DateTimeOffset.UtcNow,
                // **켠 모습을 재려면 값이 있어야 한다.** 서버가 안 주면 링이 안 늘고,
                // 그러면 켠 조합을 잰다면서 끈 것과 같은 것을 재게 된다.
                Limits = scoped
                    ?
                    [
                        new UsageLimit
                        {
                            Kind = "weekly_scoped",
                            ModelName = "Fable",
                            Percent = 18,
                        },
                    ]
                    : [],
            },
        };

    /// <summary>어긋난 것만 골라 돌려준다. 빈 목록이면 통과다.</summary>
    private static List<string> Check(
        HudView view, HudMode mode, bool scoped, bool stats, HudScale scale)
    {
        var notes = new List<string>();
        var panel = view.SizeFor(mode);
        var ring = view.ProbeRingRect();
        var f = scale.Factor();

        // **선 굵기를 따로 더하지 않는다. 맥과 갈리는 자리다.**
        //
        // 맥은 지름 위에 선을 얹어서 `지름 + 선` 이 실제로 덮는 폭이고, 그래서 펫 링을
        // 124 → 123 으로 내려야 했다(2.5.2). 우리 `RingRenderer.DrawOne` 은
        // `반지름 = (지름 - 두께) / 2` 로 **지름 안에서 두께의 절반을 물러서므로**
        // 덮는 폭이 정확히 지름이다 — 배율 1의 펫에서 재면 중심 64 ± 62 = 2~126 이라
        // 창 128 안에 든다. 맥 숫자를 그대로 옮기면 링만 이유 없이 1pt 작아진다.

        void Over(string what, double used, double have)
        {
            if (used > have + 0.5) notes.Add($"{what} {used:0} > {have:0}");
        }

        switch (mode)
        {
            case HudMode.Collapsed:
                // **여백 숫자를 여기 다시 적지 않는다.** 링과 버튼의 진짜 자리로 잰다 —
                // 여백을 고쳤을 때 옛 합으로 재면서 통과시키면 이 진단이 뜻이 없다.
                // 창 밖으로 나가는 것과 겹치는 것은 아래 공통 검사가 본다.
                foreach (var (_, rect) in view.ProbeButtonRects())
                {
                    Over("가로", ring.Right + 8 * f, rect.Left);
                }
                Over("세로", ring.Height, panel.Height);
                break;

            case HudMode.Expanded:
                // 링은 위쪽 줄 안에 놓인다. 아래 자원 사용량 줄은 그 밖이다.
                Over("세로", ring.Height,
                    panel.Height - (stats ? HudView.BaseStatsRowHeight * f : 0));
                Over("가로", ring.Right + 13 * f + 10 * f, panel.Width);

                // **글자 자리가 줄어들면 안 된다.** 링이 커진 만큼 카드를 안 넓히면
                // 여기가 좁아지고, 좁아진 만큼 숫자가 버튼 밑으로 파고든다.
                var text = panel.Width - ring.Right - 13 * f - 10 * f;
                var want = HudView.BaseExpandedWidth * f - view.ProbePlainRingDiameter()
                    - (13 + 13 + 10) * f;
                if (text < want - 0.5) notes.Add($"글자 자리 {text:0} < {want:0} (링에 먹힘)");
                break;

            default:
                // 펫은 다른 링(더 크고 선이 얇다)을 쓴다. 버튼 줄은 링 자리 밖이다.
                Over("세로", ring.Height, panel.Height - HudView.BasePetButtonRow * f);
                Over("가로", ring.Width, panel.Width);
                // **가운데 자리는 안 잰다.** 펫은 마스코트가 링 위로 올라오는 것이 맞다.
                break;
        }

        // 누를 자리들이 창 안에 있어야 한다. 밖으로 나가면 버튼이 안 눌린다.
        foreach (var toRight in new[] { true, false })
        {
            var sided = View(mode, scoped, stats, scale, toRight);
            var bounds = new Rect(0, 0, panel.Width, panel.Height);
            bounds.Inflate(0.5, 0.5);
            var side = toRight ? "오른쪽" : "왼쪽";

            foreach (var (target, rect) in sided.ProbeButtonRects())
            {
                if (!bounds.Contains(rect)) notes.Add($"{target} 자리가 창 밖 ({side})");
            }

            if (!bounds.Contains(sided.ProbeUpdateBadgeRect()))
            {
                notes.Add($"새 버전 표시가 창 밖 ({side})");
            }

            if (!bounds.Contains(sided.ProbeRingRect()))
            {
                notes.Add($"링이 창 밖 ({side})");
            }

            // 링(=마스코트 더블클릭 자리)과 버튼이 겹치면 버튼을 눌러도 펫으로 들어간다.
            if (mode != HudMode.Pet)
            {
                foreach (var (target, rect) in sided.ProbeButtonRects())
                {
                    if (rect.IntersectsWith(sided.ProbeRingRect()))
                    {
                        notes.Add($"링이 {target} 와 겹침 ({side})");
                    }
                }
            }
        }

        return notes;
    }
}
