using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DongCSU.App.Hud;
using DongCSU.Core;
using DongCSU.Core.Usage;

namespace DongCSU.App.Settings;

/// <summary>
/// <c>--probe-meter ui</c> — 측정 화면의 <b>버튼을 실제로 눌러 본다.</b>
///
/// <b>눈으로는 볼 수 없는 검사다.</b> 화면이 그려지는 것은 <c>--render-settings</c> 가
/// 보여주지만, <i>시작을 누르면 정말 재기 시작하는가</i> 는 눌러 보고 그 뒤 상태를
/// 들여다봐야만 알 수 있다. 맥의 <c>--probe-perch selftest</c> 와 같은 자리다 —
/// 거기도 "붙었는지는 그림을 보면 알지만 누르면 떨어지는가는 볼 수 없다" 고 적혀 있다.
///
/// 버튼을 <b>글자로 찾아 누른다.</b> 자리(좌표)로 찾으면 배치를 바꿀 때마다 검사가
/// 깨지고, 무엇을 눌렀는지도 안 남는다.
///
/// <b>고정값이 아니라 진짜 엔진을 쓴다.</b> 저장소만 임시 파일로 돌려서 사용자의
/// <c>meter.json</c> 을 건드리지 않는다.
/// </summary>
internal static class ProbeMeasureUi
{
    public static int Run(string[] args)
    {
        var failures = new List<string>();
        var temp = Path.Combine(Path.GetTempPath(), $"dong-csu-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            CheckHudButtons(failures);
            CheckMeasureTab(failures, temp);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine("통과 — 측정 화면의 버튼이 다 먹는다");
            return 0;
        }

        Console.WriteLine("실패:");
        foreach (var line in failures) Console.WriteLine($"  {line}");
        return 1;
    }

    // ── HUD · 펫의 버튼 줄 ──────────────────────────────────────────

    /// <summary>
    /// 버튼이 몇 칸인지와 <b>측정 자리를 눌렀을 때 측정으로 잡히는지.</b>
    ///
    /// 자리를 재는 곳과 누른 것을 판정하는 곳이 갈려 있어서(<c>ButtonRects</c> ↔
    /// <c>HitTest</c>), 한쪽만 고치면 <b>엉뚱한 버튼이 눌린다</b> — 화면은 멀쩡해 보인다.
    /// </summary>
    private static void CheckHudButtons(List<string> failures)
    {
        Console.WriteLine("HUD 버튼");

        foreach (var (mode, want) in new[] { (HudMode.Expanded, 4), (HudMode.Collapsed, 4), (HudMode.Pet, 3) })
        {
            var view = new HudView { Mode = mode, IconStyle = IconStyle.OwlSheet };
            var size = view.SizeFor(mode);
            view.Width = size.Width;
            view.Height = size.Height;
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();

            // 각 자리 한가운데를 눌러 무엇으로 잡히는지 본다.
            var hits = new List<HudHit>();
            foreach (var hit in new[] { HudHit.Collapse, HudHit.Measure, HudHit.Settings, HudHit.Refresh })
            {
                if (view.ProbeButtonCenter(hit) is not { } center) continue;
                hits.Add(view.HitTest(center));
            }

            var buttons = hits.Count;
            var ok = buttons == want;
            Console.WriteLine($"  {mode,-10} 버튼 {buttons}칸 (기대 {want})  "
                + string.Join(" · ", hits));
            if (!ok) failures.Add($"{mode} 의 버튼이 {buttons}칸이다 (기대 {want})");

            // 측정 자리를 눌렀는데 측정으로 안 잡히면 자리와 판정이 어긋난 것이다.
            if (view.ProbeButtonCenter(HudHit.Measure) is { } measure
                && view.HitTest(measure) != HudHit.Measure)
            {
                failures.Add($"{mode} 에서 측정 버튼 자리를 눌렀는데 {view.HitTest(measure)} 로 잡힌다");
            }
        }
    }

    // ── 설정 창 측정 탭 ────────────────────────────────────────────

    private static void CheckMeasureTab(List<string> failures, string temp)
    {
        Console.WriteLine();
        Console.WriteLine("측정 탭 버튼");

        var settings = new AppSettings();
        var records = Path.Combine(temp, "projects");
        Directory.CreateDirectory(records);

        // **진짜 엔진이다.** 고정값(MeterPreview)으로는 버튼이 상태를 바꾸는지 알 수 없다.
        // 저장소와 기록 폴더만 임시 자리로 돌려 사용자 파일을 안 건드린다.
        var meter = new UsageMeter(
            new MeterStore(Path.Combine(temp, "meter.json")), transcriptRoot: records);

        var window = ProbeLayout.ProbeWindow(settings, meter: meter);
        try
        {
            window.Show();
            window.SelectTab("measure");
            Settle(window);

            Step(failures, window, "시작", () => meter.IsRunning, "재는 중이 아니다");

            // **토큰이 있어야 볼 수 있는 것들이 있다.** 토큰 카드와 캐시 포함 토글은
            // 셀 것이 없으면 통째로 감춘다(켤 값이 없는데 스위치만 남으면 눌러도 화면이
            // 안 바뀌어 고장으로 보인다). 그래서 시작한 **뒤에** 기록 한 줄을 지어낸다 —
            // 측정은 시작 시각 뒤의 것만 세므로 순서를 뒤집으면 0 으로 남는다.
            WriteFakeTranscript(records);
            meter.ScanTokensAsync().GetAwaiter().GetResult();
            window.SelectTab("measure");
            Settle(window);

            var tokens = meter.State.Tokens;
            Console.WriteLine($"  훑기          응답 {tokens.Responses} · 캐시 제외 {tokens.WithoutCache} 토큰");
            if (tokens.Responses == 0) failures.Add("지어낸 기록을 훑었는데 응답이 0이다");

            // 캐시 포함 토글. **설정에 남아야** 창을 닫았다 열어도 같은 값이 보인다.
            var wasIncluding = settings.MeasureIncludesCache;
            Step(failures, window, "캐시 포함",
                () => settings.MeasureIncludesCache != wasIncluding,
                "캐시 포함 설정이 안 바뀐다", kind: ToggleKind);

            Step(failures, window, "일시정지", () => meter.IsPaused, "멈춘 상태가 아니다");
            Step(failures, window, "계속", () => meter.IsRunning && !meter.IsPaused, "다시 재는 중이 아니다");

            var before = meter.State.History.Count;
            Step(failures, window, "중지", () => !meter.IsRunning, "아직 재는 중이다");
            var after = meter.State.History.Count;
            Console.WriteLine($"  기록          {before} → {after}건");
            if (after != before + 1) failures.Add($"중지했는데 기록이 안 남았다 ({before} → {after})");

            // 기록 상세. **모달이라 여기서 열지 않는다** — 열면 검사가 그 자리에 선다.
            // 창을 만들 수 있는지와, 지우기가 엔진까지 닿는지만 본다.
            var record = meter.State.History.FirstOrDefault();
            if (record is null)
            {
                failures.Add("기록이 없어 상세 창을 확인하지 못했다");
            }
            else
            {
                meter.DeleteRecord(record);
                var left = meter.State.History.Count;
                Console.WriteLine($"  기록 하나 지우기  {after} → {left}건");
                if (left != after - 1) failures.Add($"기록 하나를 지웠는데 {left}건이 남았다");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private const string ToggleKind = "토글";

    /// <summary>
    /// 기록 한 줄을 지어낸다. Claude Code 가 남기는 것과 <b>같은 모양</b>이어야 한다 —
    /// 최상위 <c>timestamp</c> 와 <c>message.{id,model,usage}</c>.
    ///
    /// 캐시 값을 일부러 크게 넣는다. 캐시 포함 토글을 눌렀을 때 합계가 <b>눈에 띄게</b>
    /// 달라져야 그 토글이 실제로 무언가를 한다는 것을 알 수 있다.
    /// </summary>
    private static void WriteFakeTranscript(string root)
    {
        // **원시 문자열로 적지 않는다.** JSON 의 닫는 중괄호가 셋이라 보간 문자열의
        // 중괄호 규칙과 부딪힌다 — 실제로 컴파일이 깨졌다.
        var at = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var line =
            "{\"type\":\"assistant\",\"timestamp\":\"" + at + "\","
            + "\"message\":{\"id\":\"msg_probe_ui_1\",\"model\":\"claude-opus-5\","
            + "\"usage\":{\"input_tokens\":120,\"output_tokens\":3400,"
            + "\"cache_creation_input_tokens\":50000,\"cache_read_input_tokens\":9000000}}}";

        File.WriteAllText(Path.Combine(root, "probe.jsonl"), line + Environment.NewLine);
    }

    /// <summary>
    /// 글자로 찾아 누르고, 눌린 뒤 상태를 본다.
    ///
    /// <b>못 찾은 것과 눌러도 안 바뀐 것을 갈라 적는다.</b> 둘을 뭉뚱그리면 배치를
    /// 바꿔 버튼이 사라진 날에도 "안 먹는다" 로만 나와서 어디를 볼지 알 수 없다.
    /// </summary>
    private static void Step(
        List<string> failures, SettingsWindow window, string label,
        Func<bool> expectation, string whenWrong, string kind = "버튼")
    {
        var found = Find(window, label);
        if (found is null)
        {
            Console.WriteLine($"  {label,-12} 못 찾음");
            failures.Add($"{kind} '{label}' 을 화면에서 못 찾았다");
            return;
        }

        found();
        Settle(window);

        var ok = expectation();
        Console.WriteLine($"  {label,-12} {(ok ? "먹는다" : "안 먹는다")}");
        if (!ok) failures.Add($"{kind} '{label}' 을 눌렀는데 {whenWrong}");
    }

    /// <summary>
    /// 글자가 <paramref name="label"/> 인 누를 것을 찾는다. 누르는 몸짓을 돌려준다.
    ///
    /// <b>우리 단추와 토글은 <see cref="Button"/> 이 아니라 <see cref="Border"/> 다</b>
    /// (<c>Ui.Button</c> · <c>Ui.Toggle</c>). 둘 다 <see cref="UIElement.MouseLeftButtonUp"/>
    /// 를 듣고 손 모양 커서를 달고 있어서, <b>손 모양을 단 테두리</b>를 누를 것으로 본다.
    ///
    /// <b><see cref="UIElement.MouseLeftButtonDown"/> 을 쏘면 아무 일도 안 일어난다</b> —
    /// 실제로 처음에 그렇게 짰고 "시작을 눌렀는데 안 먹는다" 로 나왔다. 검사가 틀렸던
    /// 것이지 화면이 틀린 것이 아니었다. 무엇을 듣는지 보고 그대로 쏴야 한다.
    /// </summary>
    private static Action? Find(DependencyObject root, string label)
    {
        // 단추: 글자를 품은 손 모양 테두리. 글자에서 위로 올라가며 가장 가까운 것을 집는다.
        foreach (var text in Descendants(root).OfType<TextBlock>())
        {
            if (text.Text != label) continue;

            if (Ancestors(text).OfType<Border>().FirstOrDefault(IsPressable) is { } button)
            {
                return () => Press(button);
            }

            // 토글: 글자는 줄 왼쪽에 있고 누를 것은 줄 오른쪽에 따로 있다.
            // 글자를 품은 줄까지 올라간 뒤 그 안에서 손 모양을 찾는다.
            foreach (var row in Ancestors(text))
            {
                if (Descendants(row).OfType<Border>().FirstOrDefault(IsPressable) is not { } knob) continue;
                return () => Press(knob);
            }
        }

        return null;
    }

    /// <summary>손 모양 커서를 단 것이 누를 수 있는 것이다. 우리 화면의 규칙이다.</summary>
    private static bool IsPressable(Border border)
        => border.Cursor == System.Windows.Input.Cursors.Hand;

    private static void Press(UIElement target)
        => target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });

    /// <summary>
    /// 미뤄 둔 일을 다 비운다.
    ///
    /// 우리 화면은 스크롤 자리 되돌리기와 다시 그리기를 <see cref="DispatcherPriority.Loaded"/>
    /// 로 미뤄 두므로, 큐를 안 비우고 상태를 보면 <b>누르기 전 화면</b>을 보게 된다.
    /// </summary>
    private static void Settle(SettingsWindow window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject node)
    {
        var current = VisualTreeHelper.GetParent(node);
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }
}
