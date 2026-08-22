using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DongCSU.Core.Usage;

namespace DongCSU.App.Settings;

/// <summary>
/// 측정 탭. **시작~중지 사이에 얼마나 썼는지**를 잰다.
///
/// <b>두 숫자가 재는 범위가 다르다.</b> 한도 %p 는 클로드 앱·웹까지 포함한 계정 전체이고,
/// 토큰은 Claude Code 기록에서만 센다. 어느 쪽이 무엇을 재는지 소제목 옆에 계속 밝혀
/// 두지 않으면, 두 값이 안 맞는 것을 버그로 읽는다.
///
/// <b>파일을 가른 이유</b>는 <c>SettingsWindow.cs</c> 가 이미 1600줄이라서다. 저기에
/// 400줄을 더 얹으면 아무도 못 읽는다 — 탭 하나가 통째로 여기 산다.
/// </summary>
public sealed partial class SettingsWindow
{
    /// <summary>
    /// 탭을 열어 둔 동안의 재훑기.
    ///
    /// **앱이 평소 도는 주기(<see cref="UsageMeter.ScanInterval"/> · 60초)는 창을 안 볼
    /// 때 기준이다.** 보고 있는 동안 그 주기로 두면 숫자가 멈춘 것처럼 보인다. 덧붙은
    /// 부분만 읽어서 값이 싸기 때문에 5초로 조여도 된다.
    ///
    /// <b>훑기 주기가 두 곳에 적혀 있다</b> — 60초는 <c>Core</c> 의 상수이고 이 5초는
    /// 뷰 안의 숫자다. 언제 훑을지를 정하는 값이니 둘 다 <see cref="UsageMeter"/> 옆에
    /// 있는 편이 맞다.
    /// </summary>
    private readonly DispatcherTimer scanTick = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>기록 목록에서 읽던 자리. 훑을 때마다 탭을 다시 만들어도 여기로 되돌린다.</summary>
    private double measureHistoryOffset;

    /// <summary>
    /// 1초 티커가 갈아 끼우는 글자 둘 — 경과 시간과 표본 문구.
    ///
    /// **탭을 다시 만들 때마다 새로 잡는다.** 옛 참조에 쓰면 화면에서 떨어져 나간
    /// <c>TextBlock</c> 을 고치게 되어 숫자가 그대로 멈춘다. 재고 있지 않으면 둘 다
    /// 안 그려지므로 null 이다.
    /// </summary>
    private TextBlock? elapsedText;

    /// <inheritdoc cref="elapsedText"/>
    private TextBlock? sampleText;

    // ── 배선 ────────────────────────────────────────────────────────

    /// <summary>
    /// 측정 쪽 배선. 생성자에서 한 번 부른다.
    ///
    /// <b><see cref="UsageMeter.Changed"/> 는 UI 스레드가 아닐 수 있다.</b> 토큰 훑기가
    /// 스레드풀에서 끝나면서 그대로 울리므로, 반드시 디스패처로 넘긴 다음 그린다.
    /// </summary>
    private void HookMeasure()
    {
        // 겹쳐 돌지 않고 예외를 전부 삼키므로 던져 두고 잊어도 된다.
        scanTick.Tick += (_, _) => { _ = meter.ScanTokensAsync(); };
        meter.Changed += OnMeterChanged;
    }

    /// <summary>
    /// 측정 쪽 배선을 푼다. 창이 닫힐 때 반드시 부른다.
    ///
    /// **설정 창은 닫을 때마다 버려지지만 <c>meter</c> 는 앱 수명만큼 산다.** 안 풀면
    /// 열고 닫을 때마다 죽은 창이 하나씩 <c>Changed</c> 에 매달려 남는다.
    /// </summary>
    private void UnhookMeasure()
    {
        scanTick.Stop();
        meter.Changed -= OnMeterChanged;
    }

    private void OnMeterChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        // **중지 직후의 마지막 표본과 마지막 훑기가 여기로 들어온다.** 이 배선이 없으면
        // 중지한 기록이 늘 0%p 로 보인다 — `Stop()` 한 순간에는 아직 값이 안 왔다.
        //
        // **숫자가 실제로 바뀌는 자리도 여기 하나뿐이다.** 1초 티커는 글자 둘만 갈아
        // 끼우므로(`TickMeasure`), 한도·토큰·모델별 표는 훑기나 표본이 들어온 이때
        // 다시 그려진다.
        if (TabList[Selected].Key == "measure") ShowTab();
    }));

    /// <summary>
    /// 5초 재훑기를 지금 탭에 맞춘다. <see cref="SyncTicker"/> 가 부른다.
    ///
    /// 켜지는 순간 한 번 훑는 것이 맥의 <c>onAppear</c> 자리다. **<see cref="ShowTab"/>
    /// 안에서 훑지 않는다** — 재는 동안에는 그게 훑을 때마다 다시 부르는 자리가 된다.
    ///
    /// <b>언제 훑을지는 <see cref="UsageMeter.WantsScanning"/> 이 정한다.</b> 여기서
    /// <c>IsRunning</c> 으로 따로 판단하면 일시정지로 탭을 열어 둔 동안 타이머가 계속
    /// 돌고, <see cref="UsageMeter.ScanTokensAsync"/> 는 매번 첫 가드에서 되돌아온다.
    /// </summary>
    private void SyncMeasureScan(string key)
    {
        var needed = key == "measure" && meter.WantsScanning;
        if (needed && !scanTick.IsEnabled)
        {
            scanTick.Start();
            _ = meter.ScanTokensAsync();
        }
        else if (!needed && scanTick.IsEnabled)
        {
            scanTick.Stop();
        }
    }

    // ── 탭 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 측정 탭 전체.
    ///
    /// **중지하면 위쪽 셋이 통째로 사라진다.** 멈춘 값을 계속 펼쳐 두면 아직 재는 중으로
    /// 읽히고, 같은 값이 아래 기록에도 있어서 두 번 잰 것으로 보인다. 끝난 것은 기록에서 본다.
    ///
    /// <b>상태를 한 번만 뜨고 아래로 넘긴다.</b> <c>meter</c> 의 <c>IsRunning</c>·
    /// <c>TracksInOrder</c> 는 전부 락을 잡고 <see cref="UsageMeter.State"/> 를 거치는
    /// 통과 속성이라, 그리는 도중에 또 읽으면 <b>한 프레임 안에서 앞줄과 뒷줄이 다른
    /// 상태를 볼 수 있다</b> — 머리는 "재는 중"인데 한도 카드는 멈춘 뒤의 값인 식이다.
    /// 복사본을 통째로 갈아 끼우는 <see cref="MeterState"/> 의 설계가 화면에서 무효가
    /// 된다. <c>meter</c> 는 버튼이 부르는 명령에만 남긴다.
    /// </summary>
    private UIElement MeasureTab(SettingsPalette palette)
    {
        var state = meter.State;
        var now = DateTimeOffset.Now;

        // 매초 갈아 끼울 글자는 이 판에서 새로 잡는다. 안 그려지는 판에서는 null 로 남아
        // 티커가 옛 줄을 고치지 않는다.
        elapsedText = null;
        sampleText = null;

        var panel = Stack();
        panel.Children.Add(MeasureTitle(palette));

        if (state.IsRunning)
        {
            panel.Children.Add(MeasureHeader(palette, state, now));
            panel.Children.Add(MeasureLimits(palette, state.TracksInOrder, store.ErrorText));
            panel.Children.Add(MeasureTokens(
                palette,
                state.Tokens,
                state.TokensByModel,
                settings.MeasureIncludesCache,
                ClaudeCodeUsage.IsAvailable,
                // **`ApplyAndRedraw` 여야 한다.** 줄 수·합계·모델별 표가 함께 바뀐다.
                value => { settings.MeasureIncludesCache = value; ApplyAndRedraw(); }));
        }

        panel.Children.Add(MeasureControls(palette, state, now));

        if (state.History.Count > 0) panel.Children.Add(MeasureHistory(palette, state));

        return panel;
    }

    /// <summary>
    /// 1초 티커가 부르는 자리. **탭을 다시 만들지 않는다.**
    ///
    /// 재는 동안 매초 달라지는 것은 <b>글자 둘</b>뿐이다 — 경과 시간과 표본 나이.
    /// 기록 목록은 재는 동안 아예 안 움직이고(<c>UsageMeter.SyncArchived</c> 가 재는
    /// 중이면 그대로 되돌아간다), 한도·토큰은 훑기나 표본이 들어올 때 바뀐다.
    ///
    /// <b>여기서 <see cref="ShowTab"/> 을 부르면 1분에 60번 탭을 새로 짓는다.</b> 한 번에
    /// 기록 50줄이 딸려 오는데 <see cref="Ui.Scroller"/> 에는 가상화가 없어서, 안 보이는
    /// 줄까지 전부 measure·arrange 를 탄다.
    /// </summary>
    private void TickMeasure()
    {
        // **여기서도 상태는 한 번만 뜬다.** 두 글자가 서로 다른 순간을 보면 표본 나이가
        // 경과 시간보다 나중 것이 될 수 있다.
        var state = meter.State;
        if (!state.IsRunning) return;

        var now = DateTimeOffset.Now;
        if (elapsedText is { } elapsed)
        {
            elapsed.Text = RemainingTime.ElapsedText(state.Elapsed(now) ?? TimeSpan.Zero);
        }
        if (sampleText is { } sample)
        {
            sample.Text = MeasureText.SampleText(state.Samples, state.LastSampledAt, now);
        }
    }

    /// <summary>
    /// 제목 옆 <c>beta</c> 딱지. **숫자가 기대와 어긋날 수 있다는 뜻이다** — 한도가
    /// 1%p 눈금이라 잔돈이 안 잡히고, 토큰은 Claude Code 것뿐이라 앱·웹에서 쓴 것이
    /// 빠진다. 다듬어 믿을 만해지면 뺀다.
    /// </summary>
    private UIElement MeasureTitle(SettingsPalette palette)
    {
        // 아래 여백은 바깥 줄이 준다. 제목에 그대로 두면 알약이 제목 위쪽에 붙어 뜬다.
        var title = Ui.Title(palette, TabList[Selected].Title);
        title.Margin = new Thickness(0, 0, 8, 0);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        row.Children.Add(title);
        row.Children.Add(Ui.Pill(palette, "beta", palette.Warning));
        return row;
    }

    /// <summary>
    /// 재는 중 머리 — 잰 시간과 상태.
    ///
    /// 잰 시간은 <b>멈춰 있던 시간을 뺀 값</b>이다. 잠깐 세우고 밥 먹고 온 시간이
    /// 측정에 들어가면 안 된다.
    ///
    /// 맥의 아이콘 둘(<c>pause.circle</c> · <c>record.circle</c>)을 옮기지 않는다.
    /// <see cref="Ui.Pill"/> 이 이미 색 있는 캡슐이라 상태가 색만으로 갈리고, 상태 탭이
    /// "재로그인 필요·오래된 값"을 알약으로 내는 것과 생김새가 같아진다 — 설정 창 안에서
    /// 상태 표시가 한 가지 모양으로 통일된다.
    /// </summary>
    private UIElement MeasureHeader(SettingsPalette palette, MeterState state, DateTimeOffset now)
    {
        var elapsed = new TextBlock
        {
            Text = RemainingTime.ElapsedText(state.Elapsed(now) ?? TimeSpan.Zero),
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = palette.Brush(palette.Primary),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // **매초 글자만 갈아 끼우는 줄이다**(`TickMeasure`). 자릿수 폭이 흔들리면 옆의
        // 알약이 좌우로 떤다.
        Tabular(elapsed);
        elapsedText = elapsed;

        var pill = Ui.Pill(
            palette,
            state.IsPaused ? "일시정지" : "재는 중",
            state.IsPaused ? palette.Warning : palette.Danger);
        pill.Margin = new Thickness(10, 0, 0, 0);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(elapsed);
        row.Children.Add(pill);
        return row;
    }

    // ── 한도 소모 ───────────────────────────────────────────────────

    /// <summary>
    /// 한도 소모 카드.
    ///
    /// <b>지금 재는 것과 지난 기록이 같은 함수를 부른다</b>(<see cref="MeasureRecordDialog"/>).
    /// 두 화면이 다르게 보이면 어느 쪽이 맞는지 알 수 없다.
    /// </summary>
    /// <param name="errorText">조회가 실패해 있으면 그 사유. 기록 상세처럼 조회와 무관한 자리는 null.</param>
    /// <param name="emptyText">
    /// 한도가 하나도 없을 때의 문구. 기본은 재는 중 기준이다 — 시작을 누르면 곧바로
    /// 조회가 나가므로, 여기 오래 머물면 조회가 실패한 것이다.
    /// </param>
    internal static UIElement MeasureLimits(
        SettingsPalette palette,
        IReadOnlyList<LimitTrack> tracks,
        string? errorText,
        string emptyText = "기준점을 잡는 중…")
    {
        var panel = new StackPanel();
        panel.Children.Add(MeasureSection(palette, "한도 소모", "클로드 앱·웹 포함"));

        if (tracks.Count == 0)
        {
            panel.Children.Add(errorText is null
                ? Ui.Hint(palette, emptyText)
                : Ui.Hint(palette, $"조회 실패: {errorText}", palette.Warning));
            return panel;
        }

        var rows = new List<UIElement>();
        foreach (var track in tracks)
        {
            if (rows.Count > 0) rows.Add(Ui.Divider(palette));
            rows.Add(LimitRow(palette, track));
        }
        panel.Children.Add(Ui.Card(palette, [.. rows]));
        return panel;
    }

    /// <summary>
    /// 한도 한 줄.
    ///
    /// **값 쪽을 통째로 <see cref="Ui.Row"/> 의 조작부로 넘긴다.** <c>Ui.Row</c> 는
    /// DockPanel 이라 오른쪽을 먼저 재고 남은 폭을 왼쪽 글에 주므로, 좁은 창에서는
    /// 한도 이름이 줄바꿈하고 숫자는 안 잘린다 — 그게 맞는 동작이다.
    /// </summary>
    private static UIElement LimitRow(SettingsPalette palette, LimitTrack track)
    {
        var value = new StackPanel { Orientation = Orientation.Horizontal };

        if (track.Resets > 0)
        {
            // 재는 도중에 창이 새로 열려 값이 0 으로 떨어졌는데도 계속 쌓았다는 표시다.
            // 이게 없으면 왜 100%p 를 넘는지 알 수 없다.
            value.Children.Add(new TextBlock
            {
                Text = $"리셋 {track.Resets}회 넘김",
                FontSize = 11.5,
                Foreground = palette.Brush(palette.Faint),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var amount = new TextBlock
        {
            Text = MeasureText.LimitValue(track),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(palette.Primary),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Tabular(amount);
        value.Children.Add(amount);

        return Ui.Row(palette, track.Title, value);
    }

    // ── 토큰 ────────────────────────────────────────────────────────

    /// <summary>
    /// 토큰 카드와 모델별 표.
    ///
    /// 갈래 셋이다 — 기록 폴더를 못 찾았거나, 찾았지만 아직 센 것이 없거나, 표가 나오거나.
    /// <b>폴더가 없을 때 0 을 그리지 않는다</b>: 안 쓴 것과 못 읽은 것이 같은 화면이 된다.
    /// </summary>
    /// <param name="available">기록 폴더가 있는지. 지난 기록은 얼려 둔 값이라 늘 true 다.</param>
    /// <param name="emptyText">센 것이 없을 때의 문구.</param>
    internal static UIElement MeasureTokens(
        SettingsPalette palette,
        TokenTally tokens,
        IReadOnlyDictionary<string, TokenTally> byModel,
        bool includesCache,
        bool available,
        Action<bool> onToggleCache,
        string emptyText = "아직 없음")
    {
        var panel = new StackPanel();
        panel.Children.Add(MeasureSection(palette, "토큰", "Claude Code만"));

        if (!available || tokens.IsEmpty)
        {
            // **캐시 포함 토글도 같이 뺀다.** 켤 값이 없는데 스위치만 남으면 눌러도
            // 화면이 안 바뀌어 고장으로 보인다.
            panel.Children.Add(Ui.Hint(
                palette,
                available ? emptyText : "Claude Code 기록을 찾지 못했습니다 (WSL 안은 보지 않습니다)"));
            return panel;
        }

        var rows = new List<UIElement>();
        var list = MeasureText.TokenRows(tokens, includesCache);
        for (var i = 0; i < list.Count; i++)
        {
            // 마지막 줄이 늘 합계다. 거기에만 선을 긋고 굵게 찍는다.
            var total = i == list.Count - 1;
            if (total) rows.Add(Ui.Divider(palette));
            rows.Add(TokenRow(palette, list[i].Label, list[i].Value, emphasised: total));
        }

        rows.Add(Ui.Divider(palette));
        rows.Add(Ui.Row(palette, "캐시 포함", Ui.Toggle(palette, includesCache, onToggleCache),
            hint: "캐시 읽기가 전체의 90%를 넘어 기본으로 감춰 둡니다."));
        panel.Children.Add(Ui.Card(palette, [.. rows]));

        var models = MeasureText.ModelRows(byModel, includesCache);
        if (models.Count == 0) return panel;

        panel.Children.Add(MeasureSection(palette, "모델별", "합계"));

        var modelRows = new List<UIElement>();
        foreach (var (model, value) in models)
        {
            if (modelRows.Count > 0) modelRows.Add(Ui.Divider(palette));
            modelRows.Add(TokenRow(palette, model, value, emphasised: false));
        }
        panel.Children.Add(Ui.Card(palette, [.. modelRows]));
        return panel;
    }

    private static UIElement TokenRow(SettingsPalette palette, string label, string value, bool emphasised)
    {
        var text = new TextBlock
        {
            Text = value,
            FontSize = 13,
            FontWeight = emphasised ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = palette.Brush(emphasised ? palette.Primary : palette.Secondary),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Tabular(text);
        return Ui.Row(palette, label, text);
    }

    // ── 조작 ────────────────────────────────────────────────────────

    /// <summary>
    /// 조작 버튼 줄과 안내.
    ///
    /// **한 번에 최대 둘만 뜬다.** 상황에 안 맞는 버튼은 흐려 두지 않고 아예 안 그린다 —
    /// 흐린 버튼은 왜 못 누르는지 화면이 답해 주지 못한다.
    /// </summary>
    private UIElement MeasureControls(SettingsPalette palette, MeterState state, DateTimeOffset now)
    {
        var buttons = new List<UIElement>();
        if (state.IsPaused)
        {
            buttons.Add(Ui.Button(palette, "계속", () => { meter.Resume(); AfterMeterAction(); }, Ui.ButtonKind.Accent));
            buttons.Add(Ui.Button(palette, "중지", () => { meter.Stop(); AfterMeterAction(); }));
        }
        else if (state.IsRunning)
        {
            buttons.Add(Ui.Button(palette, "일시정지", () => { meter.Pause(); AfterMeterAction(); }));
            buttons.Add(Ui.Button(palette, "중지", () => { meter.Stop(); AfterMeterAction(); }));
        }
        else
        {
            // 중지한 것은 기록으로 넘어갔다. 여기는 늘 새로 시작하는 자리다.
            buttons.Add(Ui.Button(palette, "시작", () => { meter.Start(); AfterMeterAction(); }, Ui.ButtonKind.Accent));
        }

        // 버튼 사이 여백은 다른 탭과 같은 자리에서 나온다. **위 여백만 걷어낸다** —
        // 아래 DockPanel 이 이미 그만큼 띄우고 있어서 두 번 들어간다.
        var group = Ui.ButtonRow([.. buttons]);
        group.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        group.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

        // **버튼을 먼저 붙이고 표본 문구를 마지막 자식으로 둔다.** 문구를 `Dock.Right` 로
        // 먼저 붙이면 좁은 창에서 버튼이 잘린다 — 잘려도 되는 쪽은 문구다.
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(group, Dock.Left);
        row.Children.Add(group);

        if (state.IsRunning)
        {
            // 매초 갈아 끼우는 둘 중 하나다(`TickMeasure`). 나이가 늘어나는 것뿐이라
            // 탭을 새로 지을 이유가 없다.
            var sample = new TextBlock
            {
                Text = MeasureText.SampleText(state.Samples, state.LastSampledAt, now),
                FontSize = 11.5,
                Foreground = palette.Brush(palette.Tertiary),
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            sampleText = sample;
            row.Children.Add(sample);
        }

        var stack = new StackPanel();
        stack.Children.Add(row);
        // **시작을 눌렀을 때 성공·실패를 따로 내지 않는다.** 한도 카드의
        // "기준점을 잡는 중…" / "조회 실패: …" 가 그 자리다.
        stack.Children.Add(Ui.Hint(palette, MeasureText.Guide(CurrentPollTitle())));
        return stack;
    }

    /// <summary>
    /// 재기 상태를 바꾼 뒤. **<see cref="SyncTicker"/> 를 꼭 같이 부른다** —
    /// 재는 중이냐에 따라 1초 티커와 5초 재훑기가 켜졌다 꺼진다.
    /// </summary>
    private void AfterMeterAction()
    {
        ShowTab();
        SyncTicker();
    }

    // ── 기록 목록 ───────────────────────────────────────────────────

    /// <summary>
    /// 끝난 측정 목록. 누르면 그때 값을 그대로 펼쳐 본다.
    ///
    /// 50개(<see cref="UsageMeter.HistoryLimit"/>)까지 쌓이므로 <b>탭 안에서 따로
    /// 넘겨본다.</b> 그대로 늘어놓으면 탭이 그만큼 길어져서 시작 버튼이 위로 밀려난다.
    /// </summary>
    private UIElement MeasureHistory(SettingsPalette palette, MeterState state)
    {
        var panel = new StackPanel();

        var clear = Ui.Button(palette, "전체 지우기", ClearMeasureHistory);
        // **최소 폭(480)에서 소제목과 한 줄에 선다.** 기본 여백 그대로면 둘이 부딪힌다.
        clear.Padding = new Thickness(10, 4, 10, 4);
        clear.Margin = new Thickness(8, 12, 0, 2);
        clear.VerticalAlignment = VerticalAlignment.Bottom;

        var head = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(clear, Dock.Right);
        head.Children.Add(clear);
        head.Children.Add(MeasureSection(palette, "측정 기록", "최신 순 · 캐시 제외"));
        panel.Children.Add(head);

        var list = Stack();
        // 오른쪽은 이 목록의 스크롤 막대 자리다. 안 비우면 줄이 막대에 깔린다.
        list.Margin = new Thickness(0, 0, 12, 0);
        foreach (var record in state.History)
        {
            list.Children.Add(MeasureHistoryRow(palette, record));
        }

        var (listHost, listScroll) = Ui.Scroller(palette, list);

        // 읽던 자리를 지킨다. 재는 동안에는 훑을 때마다 이 탭이 통째로 다시 만들어지는데,
        // 그때마다 맨 위로 튀면 목록을 읽을 수가 없다.
        // **값을 먼저 챙겨 둔다** — 새 스크롤이 자리를 잡으면서 필드를 0 으로 덮어쓴다.
        var readAt = measureHistoryOffset;
        listScroll.ScrollChanged += (_, _) => measureHistoryOffset = listScroll.VerticalOffset;
        if (readAt > 0)
        {
            Dispatcher.BeginInvoke(
                new Action(() => listScroll.ScrollToVerticalOffset(readAt)),
                DispatcherPriority.Loaded);
        }

        // **목록의 높이를 못 박는다.** 바깥 스크롤이 세로 높이를 무한히 제안하므로,
        // 안 막으면 목록이 제 길이대로 늘어나고 머리(시작 버튼·안내 문구)가 위로
        // 밀려난다 — 버전 탭에서 이미 겪은 자리다. **탭 전체가 아니라 목록에만** 건다.
        //
        // `MaxHeight` 만 걸면 맥의 "짧으면 그냥 늘어놓는다"가 공짜로 따라온다 —
        // ScrollViewer 의 DesiredSize 는 내용 크기라 짧으면 그만큼만 차지하고,
        // `Ui.Scroller` 의 막대도 내용이 뷰포트 안에 들어가면 스스로 숨는다.
        //
        // **재는 중이면 뷰포트를 아예 안 본다.** 답이 상수 하나라 바인딩할 것이 없다 —
        // 갈래를 컨버터 안에 두면 `ConverterParameter` 로 넘겨야 하는데, 그 값은 걸 때
        // 한 번 정해지고 다시는 갱신되지 않아서 굳는다.
        if (state.IsRunning)
        {
            listHost.MaxHeight = RunningHistoryHeight;
        }
        else if (scroller is { } view)
        {
            listHost.SetBinding(FrameworkElement.MaxHeightProperty, new System.Windows.Data.Binding(nameof(ScrollViewer.ViewportHeight))
            {
                Source = view,
                Converter = HistoryHeight.Instance,
            });
        }
        panel.Children.Add(listHost);

        return panel;
    }

    /// <summary>
    /// 재는 중에 기록 목록에 주는 높이.
    ///
    /// 위쪽 살아 있는 값(경과 시간·한도·토큰)이 자리를 거의 다 써서 "남는 만큼"이
    /// 음수가 된다. 정해진 만큼만 주고 넘치는 것은 바깥 스크롤이 받는다. 맥과 같은 값이다.
    /// </summary>
    private const double RunningHistoryHeight = 168;

    /// <summary>바깥 스크롤의 뷰포트 높이 → 기록 목록에 줄 높이. **멈췄을 때만 건다.**</summary>
    private sealed class HistoryHeight : IValueConverter
    {
        public static readonly HistoryHeight Instance = new();

        /// <summary>
        /// 멈췄을 때 목록 위에 있는 것들(제목·시작 버튼·안내 문구·머리줄)이 쓰는 자리.
        ///
        /// **재지 않고 어림한다.** 실제 머리가 이보다 길면 그만큼 탭이 뷰포트를 넘고,
        /// 넘친 만큼은 바깥 스크롤이 받는다 — 틀려도 무엇이 사라지지는 않는다.
        /// </summary>
        private const double HeadAllowance = 190;

        /// <summary>아주 좁은 창에서도 목록에 이만큼은 준다. 더 얇으면 한 줄도 안 보인다.</summary>
        private const double MinHeight = 120;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // 처음 한 판은 뷰포트가 아직 0 이다. 배치가 끝나 값이 들어오면 다시 걸린다.
            var viewport = value is double height ? height : 0;
            var room = viewport - BodyPadding.Top - BodyPadding.Bottom - HeadAllowance;
            return Math.Max(MinHeight, room);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 기록 한 줄.
    ///
    /// **목록에는 캐시를 절대 안 넣는다.** 그 판단은 <see cref="MeasureText.RecordTokens"/>
    /// 안에 있다 — 여기서 손으로 만들면 캐시를 넣을지 정하는 자리가 둘이 된다.
    /// </summary>
    private UIElement MeasureHistoryRow(SettingsPalette palette, MeterRecord record)
    {
        var summary = new TextBlock
        {
            Text = MeasureText.Headline(record),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = palette.Brush(palette.Primary),
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Tabular(summary);

        var tokens = new TextBlock
        {
            Text = MeasureText.RecordTokens(record),
            FontSize = 11.5,
            Foreground = palette.Brush(palette.Tertiary),
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 1, 0, 0),
        };
        Tabular(tokens);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(summary);
        right.Children.Add(tokens);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = MeasureText.RecordDate(record),
            FontSize = 13,
            Foreground = palette.Brush(palette.Primary),
            // **줄어드는 쪽은 날짜다.** 최소 폭에서는 날짜와 요약이 한 줄에 못 서는데,
            // 요약이 잘리면 무엇을 잰 기록인지 알 수 없게 된다.
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        left.Children.Add(new TextBlock
        {
            Text = RemainingTime.ElapsedText(record.Duration),
            FontSize = 11.5,
            Foreground = palette.Brush(palette.Tertiary),
            Margin = new Thickness(0, 1, 0, 0),
        });

        var chevron = new TextBlock
        {
            // 눌러서 펼쳐 볼 것이 있다는 표시. 없으면 이 줄이 눌리는 줄 모른다.
            Text = "",   // ChevronRight
            FontFamily = TabIcon.Font,
            FontSize = 11,
            Foreground = palette.Brush(palette.Faint),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(chevron, Dock.Right);
        dock.Children.Add(chevron);
        DockPanel.SetDock(right, Dock.Right);
        right.Margin = new Thickness(12, 0, 0, 0);
        dock.Children.Add(right);
        dock.Children.Add(left);

        // **배경이 `Transparent` 여야 글자 없는 자리도 눌린다.** `null` 이면 마우스가
        // 그대로 통과한다(맥의 `contentShape(Rectangle())` 에 해당).
        var row = new Border
        {
            Background = palette.Brush(Colors.Transparent),
            Cursor = Cursors.Hand,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(Ui.Radius),
            Child = dock,
        };
        row.MouseEnter += (_, _) => row.Background = palette.Brush(palette.Hover);
        row.MouseLeave += (_, _) => row.Background = palette.Brush(Colors.Transparent);
        row.MouseLeftButtonUp += (_, _) =>
        {
            MeasureRecordDialog.Show(this, palette, meter, record, settings.MeasureIncludesCache);
            // 지웠든 아니든 다시 그린다 — 목록은 여기서 그리지 않으면 안 바뀐다.
            // 1초 티커는 글자 둘만 갈아 끼우고, 멈춰 있으면 그마저도 안 돈다.
            ShowTab();
        };
        return row;
    }

    private void ClearMeasureHistory()
    {
        // **무엇이 몇 개 지워지는지 적는다.** "정말 지울까요?" 만으로는 몇 개짜리
        // 목록을 날리는지 알 수 없다.
        var count = meter.State.History.Count;
        if (!ConfirmDialog.Ask(this, Palette, "측정 기록을 전부 지울까요?",
                $"기록 {count}개가 지워집니다. 되돌릴 수 없습니다.", "전체 지우기"))
        {
            return;
        }

        meter.ClearHistory();
        ShowTab();
    }

    // ── 같이 쓰는 부품 ──────────────────────────────────────────────

    /// <summary>
    /// 소제목 + 주석. <see cref="Ui.Section"/> 은 제목 한 줄만 내는데, 측정 화면은
    /// **무엇을 재는 범위인지**를 소제목마다 밝혀 둬야 한다.
    /// </summary>
    internal static UIElement MeasureSection(SettingsPalette palette, string title, string note)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Section(palette, title));
        row.Children.Add(new TextBlock
        {
            Text = note,
            FontSize = 11,
            Foreground = palette.Brush(palette.Faint),
            Margin = new Thickness(6, 18, 0, 6),
        });
        return row;
    }

    /// <summary>
    /// 자릿수를 고정한다. **매초 글자가 갈리는 화면이라** 숫자 폭이 흔들리면 옆에
    /// 붙은 것이 좌우로 떨고, 세로로 늘어선 값들의 자릿점도 어긋난다.
    ///
    /// <c>System.Windows.Documents</c> 를 통째로 끌어오지 않는 것은 거기 <c>List</c> ·
    /// <c>Section</c> 처럼 우리가 이미 쓰는 이름이 들어 있어서다.
    /// </summary>
    private static void Tabular(TextBlock text) =>
        System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
}
