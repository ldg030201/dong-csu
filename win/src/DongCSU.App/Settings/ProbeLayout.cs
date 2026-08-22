using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DongCSU.App.Rendering;
using DongCSU.App.Services;
using DongCSU.Core;
using DongCSU.Core.Usage;

namespace DongCSU.App.Settings;

/// <summary>
/// <c>--probe-layout</c> — 설정 창을 화면 밖에 만들어 탭마다 얼마나 차지하는지 잰다.
/// 맥판의 같은 이름 통로와 짝이고, <b>CI 가 종료 코드를 본다.</b>
///
/// <b>맥과 검사하는 것이 다르다.</b> 맥은 탭 안 목록에 <c>ScrollView</c> 가 걸렸는지를
/// 보지만, 여기서는 <see cref="SettingsWindow"/> 가 탭 내용을 늘 <see cref="Ui.Scroller"/>
/// 로 감싸므로 <b>세로 스크롤은 언제나 걸려 있다</b> — 맥이 잡으려던 사고가 구조적으로
/// 안 난다. 대신 윈도우에서 진짜로 잘리는 쪽은 <b>가로</b>다. 그 스크롤은
/// <c>HorizontalScrollBarVisibility = Disabled</c> 라 내용이 뷰포트보다 넓어도 막대가
/// 안 생기고 그냥 잘려 나간다 — 창을 좁히면 오른쪽 조작부가 소리 없이 사라진다.
///
/// 그래서 <b>세로는 실패로 세지 않는다.</b> 변경 내역이 쌓인 버전 탭은 늘 창보다 길고
/// 그건 스크롤로 보는 것이 정상이다. 그걸 실패로 세면 이 통로는 늘 빨간색이 되고
/// 아무도 안 보게 된다. 다만 <b>넘치는데 스크롤이 안 걸린 것</b>은 맥이 잡으려던 바로
/// 그 사고라 실패로 센다.
///
/// 기본 크기와 최소 크기 두 번 잰다. 잘리는 것은 대개 창을 줄였을 때만 드러난다.
/// </summary>
internal static class ProbeLayout
{
    /// <summary>
    /// <c>--probe-layout</c>. 가로로 잘리는 탭이 하나도 없으면 0, 있으면 1.
    ///
    /// 인자는 받지 않는다 — 다른 진단 통로와 모양을 맞추려고 받아만 둔다.
    /// </summary>
    public static int Run(string[] args)
    {
        var passed = true;
        // **기본 크기와 최소 크기를 하드코딩하지 않는다.** 둘 다 SettingsWindow 생성자가
        // 정하고, 여기 또 적어 두면 창 크기를 바꿨을 때 엉뚱한 크기를 재게 된다.
        passed &= Measure("기본", minimum: false);
        passed &= Measure("최소", minimum: true);

        Console.WriteLine(passed ? "통과" : "실패 — 가로로 잘리는 탭이 있다");
        return passed ? 0 : 1;
    }

    /// <summary>탭을 차례로 열어 보며 잰다. 잘리는 탭이 없으면 true.</summary>
    private static bool Measure(string label, bool minimum)
    {
        var passed = true;

        // **탭 목록을 그대로 돈다.** 탭이 하나 늘면 여기 손대지 않아도 저절로 걸린다.
        var all = SettingsWindow.TabList.Select(tab => (tab.Key, Label: tab.Key)).ToArray();
        passed &= Sweep(ProbeWindow(new AppSettings()), label, minimum, all, printHeader: true);

        // **측정 탭만 한 번 더 잰다.** 재는 중이냐에 따라 화면이 통째로 다르다 — 멈추면
        // 머리·한도·토큰 카드가 통째로 빠지고 기록 목록만 남아서, 재는 중인 모습만 재면
        // 멈춘 쪽이 잘리는 것을 영영 못 본다. 고정값은 창을 만들 때 물리므로 한 창에서
        // 갈아 끼울 수 없어 창을 하나 더 만든다 — `Sweep` 이 제 창을 반드시 닫는다.
        passed &= Sweep(
            ProbeWindow(
                new AppSettings(),
                meter: UsageMeter.Preview(MeterPreview.State(running: false))),
            label, minimum, [("measure", "measure(멈춤)")], printHeader: false);

        return passed;
    }

    /// <summary>
    /// 창 하나로 탭 몇 개를 재고 <b>반드시 닫는다.</b>
    /// </summary>
    /// <param name="tabs">열 탭과 찍을 이름. 같은 탭을 다른 고정값으로 두 번 재려고 갈랐다.</param>
    /// <param name="printHeader">
    /// 머리줄(창 안쪽 크기)을 이 훑기가 찍는지. <b>창을 몇 개 만들든 한 번만 찍으므로
    /// 첫 훑기에만 참을 준다.</b> 어느 훑기가 찍는지를 호출 자리에 드러내 둔다 — 훑기를
    /// 하나 더 붙이는 날 머리줄이 두 번 나오는 것을 부르는 쪽에서 바로 본다.
    /// </param>
    private static bool Sweep(
        SettingsWindow window,
        string label,
        bool minimum,
        IReadOnlyList<(string Key, string Label)> tabs,
        bool printHeader)
    {
        if (minimum)
        {
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
        }

        var passed = true;
        try
        {
            // **레이아웃을 돌리려면 한 번 띄워야 한다.** 창을 안 띄우면 Measure·Arrange 가
            // 저절로 안 돌아서 재 봐야 전부 0 이다. RenderProbe 가 쓰는 방식 그대로다.
            window.Show();
            window.UpdateLayout();

            foreach (var (key, tabLabel) in tabs)
            {
                window.SelectTab(key);

                // 탭을 고른 직후에 재면 낡은 배치가 나온다. 왜인지는 Settle 에 적혀 있다.
                Settle(window);

                if (window.ContentScroller is not { } scroll)
                {
                    Console.WriteLine($"  {tabLabel,-16} 스크롤을 못 찾았다");
                    passed = false;
                    continue;
                }

                if (printHeader)
                {
                    Console.WriteLine(
                        $"창 안쪽 {scroll.ViewportWidth:0}×{scroll.ViewportHeight:0}pt 기준"
                        + $" ({label} {window.Width:0}×{window.Height:0})");
                    printHeader = false;
                }

                var notes = new List<string>();

                var painted = PaintedWidth(scroll);
                if (painted > scroll.ViewportWidth + 1)
                {
                    notes.Add($"가로로 {painted - scroll.ViewportWidth:0}pt 잘림");
                    passed = false;
                }

                if (scroll.ExtentHeight > scroll.ViewportHeight + 1)
                {
                    // 넘치는 것 자체는 정상이다 — 스크롤로 본다. 넘치는데 스크롤이 안
                    // 걸렸으면 그때는 볼 방법이 없어서 실패다.
                    if (scroll.ScrollableHeight <= 0)
                    {
                        notes.Add("세로로 넘치는데 스크롤이 안 걸렸다");
                        passed = false;
                    }
                    else
                    {
                        notes.Add($"{scroll.ExtentHeight - scroll.ViewportHeight:0}pt 넘침 (스크롤로 본다)");
                    }
                }

                Console.WriteLine(
                    $"  {tabLabel,-16} {scroll.ExtentHeight,5:0}pt  {string.Join(" · ", notes)}".TrimEnd());
            }
        }
        finally
        {
            // **반드시 닫는다.** 띄워 놓고 안 닫으면 창이 살아 있어 프로세스가 안 끝나고,
            // CI 가 그 자리에서 매달린다.
            window.Close();
        }

        return passed;
    }

    /// <summary>
    /// 탭 내용이 실제로 <b>차지한</b> 가로 너비. 뷰포트보다 크면 그만큼 잘려 나간 것이다.
    ///
    /// <b><see cref="ScrollViewer.ExtentWidth"/> 로는 못 잰다.</b> 가로 스크롤이 꺼져 있으면
    /// WPF 는 내용을 뷰포트 너비로 재라고 시키고, 그렇게 나온 DesiredSize 는 그 너비를
    /// 넘지 못한다 — ExtentWidth 는 늘 ViewportWidth 와 같아서 아무것도 안 걸린다.
    ///
    /// 너비를 무한으로 두고 다시 재는 방법도 안 된다. 줄바꿈하는 설명 글이 한 줄로 펴져서
    /// 멀쩡한 탭이 전부 잘린 것으로 나온다. 그래서 <b>배치가 끝난 뒤의 자리</b>를 본다 —
    /// 가로로 늘어놓는 패널은 자리가 모자라도 제 크기대로 늘어놓고 넘긴 만큼이 잘리므로,
    /// 오른쪽 끝이 뷰포트를 넘은 요소가 있으면 그게 잘린 것이다.
    /// </summary>
    private static double PaintedWidth(ScrollViewer scroll)
        => scroll.Content is Visual content ? RightEdge(scroll, content) : 0;

    private static double RightEdge(Visual root, Visual node)
    {
        var right = 0.0;

        if (node is FrameworkElement { IsVisible: true } element && element.RenderSize.Width > 0)
        {
            var box = element.TransformToAncestor(root)
                .TransformBounds(new Rect(element.RenderSize));
            right = box.Right;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is Visual child)
            {
                right = Math.Max(right, RightEdge(root, child));
            }
        }

        return right;
    }

    /// <summary>
    /// 재기·그리기 두 통로가 <b>같은 화면</b>을 보게 하는 자리.
    ///
    /// 값이 갈리면 잰 것과 그린 것이 다른 화면이 되어 둘 다 못 믿는다. 조회를 안 걸면
    /// 상태 탭은 통째로 비고 계정 탭의 로그인 카드는 아예 안 그려져서, 정작 자리를
    /// 제일 많이 먹는 부분이 빠진 채로 재게 된다. <c>--probe-layout</c> ·
    /// <c>--render-settings</c> · <c>--probe-meter ui</c> 가 모두 여기를 부른다.
    /// </summary>
    /// <param name="meter">
    /// 측정 탭이 볼 것. 안 주면 <b>재는 중인 고정값</b>(<c>MeterPreview.State()</c>)이다.
    /// <b>여기서 새 고정값을 지어내지 않는다</b> — 다른 모습이 필요하면 부르는 쪽이
    /// <c>MeterPreview</c> 에서 뽑아 넘긴다. 두 곳에서 지으면 잰 화면과 그린 화면이 갈린다.
    /// </param>
    internal static SettingsWindow ProbeWindow(
        AppSettings settings, string? latestVersion = null, UsageMeter? meter = null)
    {
        // 테스트 바이너리로 돌려도 정식판 색으로 본다. 렌더 통로와 같은 판단이다.
        MascotRenderer.TestLook = false;

        var http = UsageApi.CreateHttpClient();
        var credentials = new CredentialStore(
            new FileCredentialSource(fallbackPaths: WslCredentialPaths.All),
            refreshedTokens: new RefreshedTokenStore());
        var store = new UsageStore(new UsageApi(http, credentials));
        var updates = new UpdateService(http);

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

        // 버전 탭의 "마지막 확인" 줄. 설치본으로 친다 — 폴더에 놓인 exe 로 재면 늘
        // "설치본이 아니라 자동 업데이트를 쓸 수 없습니다" 가 나와 실제와 달라진다.
        updates.Preview(latestVersion, now.AddMinutes(-40));

        // **저장소를 안 무는 쪽으로 만든다.** 보통 생성자를 쓰면 탭을 재 볼 때마다
        // 사용자의 진짜 meter.json 이 고정값으로 덮인다.
        // **버튼을 눌러 보는 검사만 진짜 엔진을 꽂는다**(`--probe-meter ui`). 재는 것이
        // 실제로 시작되는지는 고정값으로 알 수 없다. 나머지는 전부 고정값이라 사용자의
        // `meter.json` 을 건드리지 않는다.
        meter ??= UsageMeter.Preview(MeterPreview.State());

        return new SettingsWindow(
            settings, store, meter, updates,
            onChanged: () => { }, onResetPosition: () => { },
            onTogglePet: () => { }, onLogin: () => { })
        {
            // 창을 띄워야 레이아웃이 도는데, 화면 밖에 두면 잠깐이라도 안 보인다.
            // **앞서 잰 창의 크기가 다음 창에 새어 드는 것도 여기서 막힌다** —
            // SettingsWindow 는 닫을 때 자리를 기억하지만 화면 밖 자리는 되돌리지 않는다.
            // 한 번 재는 데 창을 둘 만드는(측정 탭을 멈춘 모습으로 한 번 더 재는) 지금
            // 구조가 그 성질에 기대고 있다.
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false,
        };
    }

    /// <summary>
    /// 미뤄 둔 일을 다 비우고 배치를 확정한다. <b>한 번으로는 안 가라앉는다.</b>
    ///
    /// <c>ShowTab</c> 이 스크롤 자리 되돌리기와 다시 그리기를
    /// <see cref="DispatcherPriority.Loaded"/> 로 미뤄 두므로, 큐를 안 비우고 들여다보면
    /// <b>바뀌기 전 화면</b>이 나온다 — 재는 쪽은 낡은 크기를 재고, 눌러 보는 쪽은
    /// 누르기 전 상태를 본다. 맥이 런루프를 여덟 번 돌리는 것과 같은 이유다.
    ///
    /// <b>재는 통로와 눌러 보는 통로가 같은 것을 쓴다.</b> 각자 갖고 있으면 화면이 무엇을
    /// 어느 순위로 미루는지 바뀐 날 한쪽만 고치게 되고, 안 고친 쪽은 실패하지도 않으면서
    /// 조용히 낡은 화면을 본다.
    ///
    /// 배치를 <b>맨 뒤에</b> 확정한다. 순위가 Loaded 보다 낮은 일이 배치를 더럽혀 놓고
    /// 끝나면, 중간에 재 둔 값은 그 자리에서 낡는다.
    /// </summary>
    internal static void Settle(SettingsWindow window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }
}
