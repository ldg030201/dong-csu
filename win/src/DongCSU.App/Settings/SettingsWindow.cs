using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DongCSU.App.Rendering;
using DongCSU.App.Services;
using DongCSU.Core;
using DongCSU.Core.Usage;

namespace DongCSU.App.Settings;

/// <summary>
/// 설정 창. 왼쪽에 탭, 오른쪽에 내용, 아래에 버전과 종료.
///
/// 맥판과 같은 여섯 탭이다. 펫은 아직 만드는 중이라 링 표시까지만 열려 있다.
///
/// **크기를 고정하지 않는다.** 고DPI 나 큰 글꼴에서 항목이 잘리고, 창을 키워 편하게
/// 볼 수도 없다. 내용은 늘어나고, 좁히면 스크롤이 생긴다.
///
/// **측정 탭은 <c>SettingsWindow.Measure.cs</c> 에 따로 있다.** 이 파일이 이미 길어서
/// 탭 하나가 통째로 들어오면 아무도 못 읽는다.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly UsageStore store;

    /// <summary>측정. **앱 수명만큼 살고 창은 닫힐 때마다 버려진다** — 구독을 꼭 푼다.</summary>
    private readonly UsageMeter meter;

    private readonly UpdateService updates;
    private readonly Action onChanged;

    /// <summary>HUD 를 기본 자리로 되돌린다. 창을 들고 있는 쪽만 할 수 있는 일이다.</summary>
    private readonly Action onResetPosition;

    /// <summary>펫 모드를 드나든다. 복귀 지점을 챙겨야 해서 화면 쪽이 직접 하지 않는다.</summary>
    private readonly Action onTogglePet;

    /// <summary>Claude Code 로그인 창을 띄운다. 프로세스를 띄우는 일이라 앱 쪽이 한다.</summary>
    private readonly Action onLogin;

    private readonly Border root = new();

    /// <summary>
    /// 다시 그릴 때마다 **새로 만든다.**
    ///
    /// 같은 요소를 새 부모에 붙이면 WPF 가 "이미 다른 요소의 논리 자식"이라며 던진다.
    /// 떼어냈다 붙이는 것보다 새로 만드는 편이 빠뜨릴 데가 없다.
    /// </summary>
    private ContentControl body = new();

    /// <summary>상태 탭은 카운트다운이 초 단위로 움직인다. 그 탭일 때만 돈다.</summary>
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly List<Border> navItems = [];

    /// <summary>
    /// 지금 열린 탭. **값은 창이 아니라 설정 객체가 들고 있다** — 창은 닫을 때마다
    /// 버려져서, 여기 두면 닫았다 열 때마다 상태 탭으로 튄다.
    ///
    /// 모르는 탭 키(옛 값·오타)에서 <c>FindIndex</c> 가 -1 을 돌려주므로 <c>Math.Max</c> 로
    /// 상태 탭까지만 떨어뜨린다. 안 막으면 <c>TabList[-1]</c> 로 터진다.
    /// </summary>
    private int Selected
    {
        get => Math.Max(0, Array.FindIndex(TabList, t => t.Key == settings.SettingsTab));
        set => settings.SettingsTab = TabList[Math.Clamp(value, 0, TabList.Length - 1)].Key;
    }

    /// <summary>
    /// 닫을 때 기억해 둔 자리와 크기. **정적이다** — 창을 닫으면 <c>Program</c> 이 객체를
    /// 버리므로 창 안에 두면 같이 사라진다.
    ///
    /// **설정 파일에는 안 적는다.** 앱을 껐다 켜면 다시 가운데에서 여는 것이 맞다 —
    /// 맥도 컨트롤러가 <c>lastFrame</c> 을 들고 있을 뿐 UserDefaults 에 남기지 않는다.
    /// </summary>
    private static Rect? lastBounds;
    private static bool lastMaximized;

    /// <summary>
    /// 탭 목록. **진단 통로(<c>--probe-layout</c>)가 그대로 돌린다** — 거기에 손으로 또
    /// 적으면 탭을 늘렸을 때 새 탭만 조용히 안 재진다.
    ///
    /// **키를 바꾸지 마라.** 변경 내역이 <c>tab: "pet"</c> 으로 탭을 가리키고
    /// (<c>ChangelogGroup.Tab</c>), 저장된 설정 탭도 이 값으로 남아 있다. 제목은
    /// 사이드바와 본문 제목이 같이 쓴다(<see cref="TabTitle"/>).
    /// </summary>
    internal static readonly (string Key, string Title)[] TabList =
    [
        ("status", "상태"),
        // **상태 다음이다**(맥과 같은 차례). 둘 다 "지금 값을 보는 곳"이라 붙어 있어야 한다.
        // 중간에 끼워도 저장된 탭이 안 어긋난다 — `Selected` 가 번호가 아니라 키로 찾는다.
        ("measure", "측정"),
        ("display", "표시"),
        ("icon", "아이콘"),
        ("pet", "펫 모드"),
        ("account", "계정"),
        ("version", "버전"),
    ];

    /// <summary>
    /// 탭 내용 둘레 여백. **한 곳에서만 적는다** — 변경 내역 목록에 줄 높이를 여기서 빼기
    /// 때문에(<see cref="ChangelogHeight"/>), 두 곳에 적으면 버전 탭이 뷰포트보다 딱
    /// 이만큼 길어진다.
    /// </summary>
    private static readonly Thickness BodyPadding = new(24, 22, 24, 18);

    private SettingsPalette Palette => SettingsPalette.For(IsDarkTheme());

    public SettingsWindow(
        AppSettings settings,
        UsageStore store,
        UsageMeter meter,
        UpdateService updates,
        Action onChanged,
        Action onResetPosition,
        Action onTogglePet,
        Action onLogin)
    {
        this.settings = settings;
        this.store = store;
        this.meter = meter;
        this.updates = updates;
        this.onChanged = onChanged;
        this.onResetPosition = onResetPosition;
        this.onTogglePet = onTogglePet;
        this.onLogin = onLogin;

        Title = $"{AppInfo.Name} 설정";
        Width = 720;
        Height = 560;
        MinWidth = 480;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // 닫기 전에 옮겨 두고 키워 둔 자리로 다시 연다. **모니터를 뺐으면 되돌리지
        // 않는다** — HUD 와 달리 설정 창에는 "위치 초기화" 가 없어서, 화면 밖에 뜨면
        // 다시 볼 방법이 없다. 그때는 예전처럼 가운데에서 연다.
        if (lastBounds is { } bounds && IsOnAnyScreen(bounds))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            if (lastMaximized) WindowState = WindowState.Maximized;
        }

        ResizeMode = ResizeMode.CanResize;   // 최대화·전체화면·가장자리 드래그가 다 열린다
        ShowInTaskbar = true;
        Content = root;

        // 측정 탭도 초가 움직인다(경과 시간·표본 나이). 언제 도는지는 `SyncTicker` 가 정한다.
        tick.Tick += (_, _) => { if (TabList[Selected].Key is "status" or "measure") ShowTab(); };

        HookMeasure();
        Rebuild();
    }

    /// <summary>
    /// 지금 탭을 다시 그린다.
    ///
    /// 사용량이 새로 들어오거나 업데이트 확인이 끝났을 때 부른다. 이게 없으면
    /// **창을 열어 둔 채로는 숫자가 영영 안 바뀐다.**
    /// </summary>
    public void Refresh()
    {
        // **탭 안만 다시 그린다.** 조회는 5~10분마다 오고 새로고침 한 번에 두 번 온다.
        // 그때마다 창을 통째로 다시 만들면 변경 내역을 읽던 자리가 맨 위로 튀고,
        // 불투명도 막대를 끌던 손에서 막대가 빠져나간다.
        //
        // 테마가 바뀐 때만 색을 다시 잡느라 통째로 만든다.
        var dark = IsDarkTheme();
        if (dark != lastDark) { lastDark = dark; Rebuild(); return; }

        ShowTab();
    }

    /// <summary>마지막으로 색을 잡을 때의 테마. 바뀌었을 때만 통째로 다시 만든다.</summary>
    private bool lastDark;

    /// <summary>탭을 하나 열어 둔 채로 띄운다. HUD 의 새 버전 표시가 여기로 보낸다.</summary>
    public void SelectTab(string key)
    {
        var index = Array.FindIndex(TabList, t => t.Key == key);
        if (index < 0) return;

        Selected = index;
        Rebuild();
    }

    private bool IsDarkTheme() => SystemTheme.IsDark(settings.Theme);

    // ── 뼈대 ────────────────────────────────────────────────────────

    private void Rebuild()
    {
        var palette = Palette;
        Background = palette.Brush(palette.Window);
        body = new ContentControl();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(BuildSidebar(palette));

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        body.Margin = BodyPadding;
        var (scrollHost, scroll) = Ui.Scroller(palette, body);
        scroller = scroll;
        Grid.SetRow(scrollHost, 0);
        right.Children.Add(scrollHost);

        var footer = BuildFooter(palette);
        Grid.SetRow(footer, 1);
        right.Children.Add(footer);

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        root.Child = grid;
        ShowTab();
        SyncTicker();
    }

    private UIElement BuildSidebar(SettingsPalette palette)
    {
        var nav = new StackPanel();
        navItems.Clear();

        var name = new TextBlock
        {
            Text = AppInfo.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(AppInfo.IsTestBuild ? palette.Test : palette.Secondary),
            Margin = new Thickness(16, 18, 12, 14),
        };
        nav.Children.Add(name);

        for (var i = 0; i < TabList.Length; i++)
        {
            var index = i;

            // 아이콘 + 이름. **변경 내역 묶음이 같은 아이콘을 쓴다** — 어느 메뉴 이야기인지
            // 여기와 눈으로 맞춰 보라고 붙인 것이라, 한쪽만 바꾸면 뜻이 없어진다.
            //
            // **`StackPanel` 이 아니라 `DockPanel` 이다.** 새 버전 표시가 줄 오른쪽 끝에
            // 붙어야 하는데, 가로 `StackPanel` 은 자식을 왼쪽부터 붙여 놓기만 한다.
            var row = new DockPanel { LastChildFill = false };
            var icon = TabIcon.Make(TabList[i].Key, 13, palette.Brush(palette.Secondary));
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);

            var label = new TextBlock
            {
                Text = TabList[i].Title,
                FontSize = 13,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);

            // **숫자가 기대와 어긋날 수 있다는 표시.** 한도는 1%p 눈금이라 잔돈이 안
            // 잡히고, 토큰은 Claude Code 것만 센다. 다듬어 믿을 만해지면 뺀다.
            //
            // **반드시 라벨 뒤에 넣는다.** `PaintNav` 가 `row.Children[0]`(아이콘)·
            // `[1]`(라벨) 을 번호로 찍어 물들이므로, 사이에 끼우면 라벨 대신 이 딱지가
            // 강조색으로 물든다.
            if (TabList[i].Key == "measure")
            {
                var beta = new TextBlock
                {
                    Text = "beta",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = palette.Brush(palette.Warning),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(beta, Dock.Left);
                row.Children.Add(beta);
            }

            // **새 버전이 있을 때만.** 설정 창을 열어도 어느 탭을 봐야 하는지 알 방법이
            // 없었다 — HUD 딱지는 창을 열면 사라진다.
            //
            // 점이 아니라 **버전 탭과 같은 내려받기 화살표**다(맥도 그렇다). 점은 무슨
            // 뜻인지 알 수 없지만 화살표는 받을 것이 있다는 말이라, 아래 아이콘과 같은
            // 그림이 오른쪽에 한 번 더 뜨는 것이 뜻이 통한다.
            if (TabList[i].Key == "version" && updates.HasUpdate)
            {
                var badge = TabIcon.Make("version", 12, palette.Brush(palette.Accent));
                DockPanel.SetDock(badge, Dock.Right);
                row.Children.Add(badge);
            }

            var item = new Border
            {
                CornerRadius = new CornerRadius(Ui.Radius),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(8, 1, 8, 1),
                Cursor = Cursors.Hand,
                Child = row,
            };
            item.MouseLeftButtonUp += (_, _) => { Selected = index; PaintNav(palette); ShowTab(); SyncTicker(); };
            navItems.Add(item);
            nav.Children.Add(item);
        }
        PaintNav(palette);

        return new Border
        {
            Background = palette.Brush(palette.Sidebar),
            BorderBrush = palette.Brush(palette.Line),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = nav,
        };
    }

    private void PaintNav(SettingsPalette palette)
    {
        for (var i = 0; i < navItems.Count; i++)
        {
            var chosen = i == Selected;
            navItems[i].Background = palette.Brush(chosen ? palette.AccentSoft : Colors.Transparent);

            var row = (System.Windows.Controls.Panel)navItems[i].Child;
            var brush = palette.Brush(chosen ? palette.Accent : palette.Secondary);
            // 아이콘도 같이 물든다. 글자만 바꾸면 고른 줄에서 아이콘만 흐릿하게 남는다.
            ((TextBlock)row.Children[0]).Foreground = brush;

            // 뒤에 새 버전 표시가 붙어 있을 수 있다. **거기는 손대지 않는다** — 고른 줄에서도
            // 강조색으로 남아야 무엇을 알리는 표시인지 그대로 읽힌다.
            var text = (TextBlock)row.Children[1];
            text.Foreground = brush;
            text.FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private UIElement BuildFooter(SettingsPalette palette)
    {
        var footerVersion = new TextBlock
        {
            Text = AppInfo.DisplayVersion,
            FontSize = 11.5,
            Foreground = palette.Brush(AppInfo.IsTestBuild ? palette.Test : palette.Faint),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid { Margin = new Thickness(24, 12, 24, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(footerVersion, 0);
        grid.Children.Add(footerVersion);

        // **앱 이름을 붙이지 않는다.** 이 창이 그 앱의 설정 창이라 어느 앱을 끄는지는
        // 이미 정해져 있고, 무엇이 사라지는지는 눌렀을 때 뜨는 확인 창이 말한다. 맥과 같다.
        var quit = Ui.Button(palette, "종료", ConfirmQuit);
        Grid.SetColumn(quit, 1);
        grid.Children.Add(quit);

        return new Border
        {
            BorderBrush = palette.Brush(palette.Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
    }

    private void SyncTicker()
    {
        var key = TabList[Selected].Key;

        // **재고 있지 않은 측정 탭에서는 안 돈다.** 움직이는 숫자가 하나도 없는데
        // 1초마다 기록 목록을 통째로 다시 만드는 것은 그냥 낭비다.
        var needed = key == "status" || (key == "measure" && meter.IsRunning);
        if (needed && !tick.IsEnabled) tick.Start();
        else if (!needed && tick.IsEnabled) tick.Stop();

        SyncMeasureScan(key);
    }

    private void ShowTab()
    {
        var palette = Palette;
        // 내용을 갈아 끼우면 스크롤이 맨 위로 간다. 읽던 자리를 도로 맞춰 준다.
        var offset = scroller?.VerticalOffset ?? 0;

        body.Content = TabList[Selected].Key switch
        {
            "measure" => MeasureTab(palette),
            "display" => DisplayTab(palette),
            "icon" => IconTab(palette),
            "pet" => PetTab(palette),
            "account" => AccountTab(palette),
            "version" => VersionTab(palette),
            _ => StatusTab(palette),
        };

        // 새 내용의 높이가 잡힌 뒤라야 그만큼 내려갈 수 있다.
        if (offset > 0 && scroller is { } view)
        {
            Dispatcher.BeginInvoke(
                new Action(() => view.ScrollToVerticalOffset(offset)),
                DispatcherPriority.Loaded);
        }
    }

    /// <summary>탭 내용을 감싼 스크롤. 내용을 갈아 끼운 뒤 읽던 자리를 맞추는 데 쓴다.</summary>
    private ScrollViewer? scroller;

    /// <summary>
    /// 진단 통로(<c>--probe-layout</c>)가 안의 내용 크기를 잰다. 화면 코드는 이걸 쓰지
    /// 않는다 — 창을 안 띄우고 탭이 가로로 잘리는지 알아내는 유일한 길이라 열어 둔다.
    /// </summary>
    public ScrollViewer? ContentScroller => scroller;

    private void Apply()
    {
        settings.Save();
        onChanged();
    }

    /// <summary>설정을 바꾸고 이 탭을 다시 그린다. 다른 항목의 활성 상태가 함께 바뀔 때 쓴다.</summary>
    private void ApplyAndRedraw()
    {
        Apply();
        ShowTab();
    }

    private static StackPanel Stack() => new();

    /// <summary>
    /// 탭 맨 위 제목. **사이드바와 같은 곳(<see cref="TabList"/>)에서 나온다** —
    /// 두 곳에 적어 두면 탭 이름을 바꿨을 때 사이드바만 바뀌고 본문은 옛 이름으로 남는다.
    /// </summary>
    private TextBlock TabTitle(SettingsPalette palette) => Ui.Title(palette, TabList[Selected].Title);

    // ── 상태 ────────────────────────────────────────────────────────

    private UIElement StatusTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(TabTitle(palette));

        var now = DateTimeOffset.Now;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock
        {
            Text = store.Snapshot?.PlanName ?? "플랜 알 수 없음",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(palette.Primary),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        if (store.NeedsReauth) header.Children.Add(Ui.Pill(palette, "재로그인 필요", palette.Warning));
        else if (store.IsStale) header.Children.Add(Ui.Pill(palette, "오래된 값", palette.Warning));
        panel.Children.Add(header);

        panel.Children.Add(Ui.Card(palette,
            // HUD 는 좁아서 "세션·주간"만 쓰지만, 여기서는 몇 시간짜리인지까지 밝힌다.
            UsageRow(palette, "세션 (5시간)", store.Snapshot?.FiveHour, now),
            Ui.Divider(palette),
            UsageRow(palette, "주간 (7일)", store.Snapshot?.SevenDay, now)));

        panel.Children.Add(Ui.Section(palette, "조회"));
        panel.Children.Add(Ui.Card(palette,
            InfoRow(palette, "마지막 조회",
                store.Snapshot is { } snap ? RemainingTime.AgeText(snap.FetchedAt, now) : "아직 없음"),
            Ui.Divider(palette),
            InfoRow(palette, "다음 조회", RemainingTime.CountdownText(store.NextPollAt, now)),
            Ui.Divider(palette),
            InfoRow(palette, "조회 주기",
                PollTitle(PollChoices[PollIndex(settings.PollIntervalSeconds)]))));

        if (store.ErrorText is { } error) panel.Children.Add(Ui.Hint(palette, $"마지막 조회 실패: {error}"));

        // **바닥에 걸려 있는 동안은 눌러도 안 나간다.** 눌리는데 아무 일도 안 일어나면
        // 고장으로 보이므로 몇 초 남았는지 적고 잠가 둔다. 이 탭은 1초마다 다시 그려져서
        // 알아서 풀린다.
        var left = (int)Math.Ceiling(store.FetchCooldown().TotalSeconds);
        var title = store.IsRefreshing ? "조회 중…" : left > 0 ? $"새로고침 ({left}초)" : "새로고침";

        panel.Children.Add(Ui.ButtonRow(
            Ui.Button(palette, title, async () =>
            {
                await store.RefreshAsync(force: true).ConfigureAwait(true);
                ShowTab();
            }, Ui.ButtonKind.Accent, enabled: !store.IsRefreshing && store.CanFetchNow)));

        return panel;
    }

    private UIElement UsageRow(SettingsPalette palette, string label, UsageWindow? window, DateTimeOffset now)
    {
        var value = new StackPanel { Orientation = Orientation.Horizontal };
        value.Children.Add(new TextBlock
        {
            Text = window is { } filled ? $"{Math.Round(filled.Utilization):F0}%" : "—",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            // 숫자를 링과 같은 색으로 칠한다. 어느 링 얘기인지 여기서도 이어진다.
            Foreground = palette.Brush(window is { } w
                ? UsageColor.For(w.Utilization).ToColor()
                : palette.Tertiary),
            VerticalAlignment = VerticalAlignment.Center,
        });
        value.Children.Add(new TextBlock
        {
            Text = RemainingTime.Text(window?.ResetsAt, now),
            FontSize = 12,
            Foreground = palette.Brush(palette.Tertiary),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 0, 3),
        });

        return Ui.Row(palette, label, value);
    }

    private static UIElement InfoRow(SettingsPalette palette, string label, string value) =>
        Ui.Row(palette, label, new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            Foreground = palette.Brush(palette.Secondary),
        });

    /// <summary>
    /// 고를 수 있는 조회 주기. **초와 문구가 한 곳에서 나온다** — 예전에는 분절 컨트롤에
    /// 문구를 따로 적어 둬서, 하나만 고치면 고른 값과 보이는 글이 어긋났다.
    /// 맥은 <c>PollInterval</c> 열거값이 제목을 들고 있다.
    ///
    /// 너무 조이면 429가 나므로 아무 초나 넣지 못하게 정해진 값 중에서 고르게 한다.
    /// </summary>
    private static readonly int[] PollChoices = [60, 180, 300, 600, 1800];

    /// <summary>
    /// 분절 컨트롤에 늘어놓을 문구. <b>글로 읽는 자리보다 짧다.</b>
    ///
    /// 맥은 여기에 펼침 메뉴를 써서 한 번에 하나만 보이므로 "10분마다" 가 그대로 들어간다.
    /// 우리는 분절 컨트롤이라 <b>다섯 칸이 한 줄에 늘어선다</b> — "마다" 를 다섯 번 되풀이하면
    /// 최소 폭(480)에서 96pt 가 잘려 나가고, 잘리지 않더라도 줄 이름이 이미 "조회 주기" 라
    /// 칸마다 또 붙일 말이 아니다. <c>--probe-layout</c> 이 이 잘림을 잡았다.
    /// </summary>
    private static string[] PollLabels => [.. PollChoices.Select(PollLabel)];

    private static string PollLabel(int seconds) =>
        seconds < 3600 ? $"{seconds / 60}분" : $"{seconds / 3600}시간";

    /// <summary>
    /// 글로 읽는 자리(상태 탭의 "조회 주기" 값). <b>맥의 <c>PollInterval.title</c> 과 같다.</b>
    ///
    /// 여기는 값 하나를 문장처럼 읽는 자리라 "10분" 만으로는 무엇이 10분인지 안 잡힌다.
    /// </summary>
    private static string PollTitle(int seconds) =>
        seconds < 3600 ? $"{seconds / 60}분마다" : $"{seconds / 3600}시간마다";

    /// <summary>
    /// 지금 주기가 목록에서 몇 번째인지. **모르는 값이면 기본값(10분) 자리로 떨어뜨린다** —
    /// 옛 설정 파일이나 손으로 고친 값이 들어오면 <c>IndexOf</c> 가 -1 을 돌려준다.
    /// </summary>
    private static int PollIndex(int seconds)
    {
        var found = Array.IndexOf(PollChoices, seconds);
        return found >= 0 ? found : Array.IndexOf(PollChoices, 600);
    }

    // ── 표시 ────────────────────────────────────────────────────────

    private UIElement DisplayTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(TabTitle(palette));

        var visible = settings.IsHudVisible;
        // 펫에 들어가 있으면 복귀 지점이 기준이다.
        var effective = settings.Mode == HudMode.Pet ? settings.ModeBeforePet : settings.Mode;
        var expanded = effective != HudMode.Collapsed;

        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "HUD 표시", Ui.Toggle(palette, visible, value =>
            {
                settings.IsHudVisible = value;
                ApplyAndRedraw();   // 아래 항목들의 활성 상태가 함께 바뀐다
            })),
            Ui.Divider(palette),
            // 펫에 들어가 있으면 **복귀 지점**을 바꾼다. 지금 모드를 바꾸면 펫에서
            // 튕겨 나오고, 나중에 나올 때 돌아갈 자리도 어긋난다.
            Ui.Row(palette, "접어서 링만 보기", Ui.Toggle(palette, !expanded, value =>
            {
                var target = value ? HudMode.Collapsed : HudMode.Expanded;
                if (settings.Mode == HudMode.Pet) settings.ModeBeforePet = target;
                else settings.Mode = target;
                ApplyAndRedraw();
            }), hint: settings.Mode == HudMode.Pet ? "펫에서 나왔을 때의 모습이다." : null,
                enabled: visible),
            Ui.Divider(palette),
            Ui.Row(palette, "펼침 방향", Ui.Segmented(palette, ["오른쪽", "왼쪽"],
                (int)settings.ExpandSide,
                index => { settings.ExpandSide = (HudExpandSide)index; Apply(); }), enabled: visible)));

        panel.Children.Add(Ui.Section(palette, "모양"));
        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "테마", Ui.Segmented(palette, ["시스템", "밝게", "어둡게"],
                (int)settings.Theme,
                index => { settings.Theme = (HudTheme)index; Apply(); Rebuild(); })),
            Ui.Divider(palette),
            // **HUD 를 꺼 뒀어도 열어 둔다.** 켜기 전에 미리 골라 두는 값이라, 잠가 두면
            // "켜고, 고르고, 다시 끄기" 를 시키게 된다. 맥도 여기는 안 잠근다.
            Ui.Row(palette, "크기", Ui.Segmented(palette,
                [.. Enum.GetValues<HudScale>().Select(s => s.Title())],
                (int)settings.Scale,
                index => { settings.Scale = (HudScale)index; Apply(); })),
            Ui.Divider(palette),
            Ui.Row(palette, "배경 불투명도",
                Ui.Slider(palette, settings.BackdropOpacity, AppSettings.MinBackdropOpacity, 1.0, value =>
                {
                    // **여기서 다시 그리지 않는다.** 탭을 통째로 다시 만들면 드래그가 끊긴다.
                    settings.BackdropOpacity = value;
                    Apply();
                }),
                hint: "너무 투명하면 글자가 안 읽혀 아래를 막아 뒀다.")));

        panel.Children.Add(Ui.Section(palette, "곁들이"));
        panel.Children.Add(Ui.Card(palette,
            // 테스트판은 번호 뒤에 test 가 붙는다. 두 판을 나란히 띄웠을 때 이걸로 가른다.
            Ui.Row(palette,
                AppInfo.IsTestBuild ? "왼쪽 위에 버전 표시 (테스트판은 test)" : "왼쪽 위에 버전 표시",
                Ui.Toggle(palette, settings.ShowsVersionBadge,
                value => { settings.ShowsVersionBadge = value; Apply(); }), enabled: visible),
            Ui.Divider(palette),
            Ui.Row(palette, "아래 줄에 CPU·메모리 표시", Ui.Toggle(palette, settings.ShowsProcessStats,
                value => { settings.ShowsProcessStats = value; Apply(); }),
                hint: "dong-csu 자신이 쓰는 자원이다. 켜면 카드가 한 줄 길어진다.",
                enabled: visible && expanded)));

        panel.Children.Add(Ui.Section(palette, "조회"));
        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "조회 주기", Ui.Segmented(palette,
                PollLabels,
                PollIndex(settings.PollIntervalSeconds),
                index => { settings.PollIntervalSeconds = PollChoices[index]; ApplyAndRedraw(); }),
                hint: "너무 조이면 서버가 요청을 제한한다."),
            Ui.Divider(palette),
            Ui.Row(palette, "로그인할 때 자동 시작", Ui.Toggle(palette, StartupService.IsEnabled, value =>
            {
                // 진짜 상태는 레지스트리에 있다. 실패하면 표시를 되돌린다.
                if (!StartupService.SetEnabled(value)) ShowTab();
                Apply();
            }))));

        panel.Children.Add(Ui.ButtonRow(
            Ui.Button(palette, "위치 초기화", onResetPosition, enabled: visible),
            Ui.Button(palette, "모든 설정 초기화", ResetEverything, Ui.ButtonKind.Danger)));

        panel.Children.Add(Ui.Hint(palette,
            "창 위치·크기·아이콘·펫 설정을 전부 처음 상태로 되돌린다."));

        panel.Children.Add(Ui.Hint(palette,
            "HUD는 드래그로 옮길 수 있고, 더블클릭하면 보기가 넘어간다. "
            // 화면 구성이 바뀌어 안 보이게 되는 것은 윈도우에서 더 흔하다. 그래서 한 줄 더 붙인다.
            + "화면 밖으로 보냈거나 모니터를 빼서 안 보이면 위치 초기화를 누르면 된다 — "
            + "주 모니터 오른쪽 위로 돌아온다."));

        return panel;
    }

    /// <summary>
    /// 설정 창의 종료 버튼은 **실수로 누르기 쉬운 자리라** 한 번 확인한다. 종료하면
    /// 트레이 아이콘까지 사라져서 다시 켤 곳을 찾아야 한다.
    ///
    /// **트레이 메뉴의 종료는 안 묻는다** — 메뉴를 열어 고른 것이라 실수일 수가 없다.
    /// </summary>
    private void ConfirmQuit()
    {
        if (ConfirmDialog.Ask(this, Palette,
            $"{AppInfo.Name}를 종료할까요?",
            "종료하면 사용량 표시와 트레이 아이콘이 모두 사라집니다.",
            "종료"))
        {
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// 설정을 통째로 되돌린다.
    ///
    /// **되돌릴 수 없으니 한 번 묻는다.** 자동 시작도 함께 끈다 — 설정 파일에는 없지만
    /// 사용자가 보기에 그것도 이 앱의 설정이다.
    /// </summary>
    private void ResetEverything()
    {
        if (!ConfirmDialog.Ask(this, Palette,
            "모든 설정을 초기화할까요?",
            "되돌릴 수 없습니다. 로그인할 때 자동 시작도 함께 꺼집니다.",
            "초기화")) return;

        // **되돌릴 목록을 여기서 적지 않는다.** 손으로 옮겨 적던 시절에 실제로
        // `PetHidesRingWhileHeld` 한 줄이 빠져 있었다 — 설정을 하나 더할 때마다 같은
        // 누락이 난다. 무엇이 설정인지는 `AppSettings` 가 안다.
        settings.ResetToDefaults();

        // 자동 시작만은 여기서 끈다. 레지스트리에 있어 설정 파일 밖이다.
        StartupService.SetEnabled(false);
        AppLog.Write("설정을 모두 초기화했다");

        Apply();
        // 자리도 되돌린다. 값만 지우면 창은 옮겨 둔 자리에 그대로 남는다.
        onResetPosition();
        Rebuild();
    }

    // ── 아이콘 ──────────────────────────────────────────────────────

    private UIElement IconTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(TabTitle(palette));
        panel.Children.Add(Ui.Hint(palette, "HUD 링 가운데에 그릴 그림이다."));

        foreach (var group in Enum.GetValues<IconStyleGroup>())
        {
            var strip = new WrapPanel();
            foreach (var style in Enum.GetValues<IconStyle>().Where(s => s.Group() == group))
            {
                strip.Children.Add(IconTile(palette, style));
            }

            // **접힌 묶음은 눌러야 열린다.** 지금 그걸 쓰고 있으면 펴 놓는다 —
            // 접힌 채로 두면 어디서 고른 것인지 찾을 수 없다.
            if (group.IsCollapsed())
            {
                panel.Children.Add(new Expander
                {
                    Header = group.Title(),
                    Foreground = new SolidColorBrush(palette.Secondary),
                    Content = strip,
                    IsExpanded = settings.IconStyle.Group() == group,
                    Margin = new Thickness(0, 14, 0, 0),
                });
                continue;
            }

            panel.Children.Add(Ui.Section(palette, group.Title()));
            panel.Children.Add(strip);
        }

        var animated = settings.IconStyle.IsAnimated();
        panel.Children.Add(Ui.Section(palette, "움직임"));
        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "캐릭터 애니메이션", Ui.Toggle(palette, settings.AnimatesMascot,
                value => { settings.AnimatesMascot = value; Apply(); }),
                // 정지 그림(Claude 쪽)을 골라 두면 켤 것이 없어서 잠긴다.
                hint: animated ? null : $"{settings.IconStyle.ShortTitle()}은(는) 정지 그림이다.",
                enabled: animated)));

        panel.Children.Add(Ui.Hint(palette,
            "Claude 쪽 그림에는 애니메이션을 넣지 않습니다 — 저작권이 Anthropic에 있어 "
            + "새 자세를 만들어 붙일 그림이 아닙니다."));

        return panel;
    }

    private UIElement IconTile(SettingsPalette palette, IconStyle style)
    {
        var chosen = settings.IconStyle == style;

        var preview = new IconPreview
        {
            IconStyle = style,
            // **테마가 아니라 판을 따른다.** 이 값은 Clawd 눈의 먹색 알파를 고르는 데만
            // 쓰이는데, `IconPreview` 가 어느 테마에서든 어두운 판을 깔고 그 위에 그리므로
            // 창이 밝아도 눈은 어두운 쪽이 맞다.
            IsDark = true,
            Width = 44,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel();
        // 어두운 판은 `IconPreview` 가 제 안에서 깐다. **여기서 또 두르지 않는다** —
        // 두 겹이 되면 판 안에 판이 생겨 모서리가 두 번 깎인다.
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock
        {
            Text = style.ShortTitle(),
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            Foreground = palette.Brush(chosen ? palette.Accent : palette.Tertiary),
            Margin = new Thickness(0, 6, 0, 0),
        });

        // **베타는 고르는 자리에 붙인다.** 변경 내역에만 적으면 그걸 읽은 사람만 알고,
        // 여기서 고르는 사람은 다 만들어진 것으로 안다.
        if (style == IconStyle.OwlSheet)
        {
            var beta = Ui.Pill(palette, "베타", palette.Warning);
            beta.HorizontalAlignment = HorizontalAlignment.Center;
            beta.Margin = new Thickness(0, 5, 0, 0);
            stack.Children.Add(beta);
        }

        var tile = new Border
        {
            Width = 92,
            Background = palette.Brush(chosen ? palette.AccentSoft : palette.Card),
            BorderBrush = palette.Brush(chosen ? palette.Accent : palette.Line),
            BorderThickness = new Thickness(chosen ? 1.5 : 1),
            CornerRadius = new CornerRadius(Ui.Radius + 2),
            Padding = new Thickness(10, 12, 10, 10),
            Margin = new Thickness(0, 0, 10, 10),
            Cursor = Cursors.Hand,
            ToolTip = style.Title(),
            Child = stack,
        };
        tile.MouseLeftButtonUp += (_, _) =>
        {
            settings.IconStyle = style;
            ApplyAndRedraw();
        };
        return tile;
    }

    // ── 펫 ──────────────────────────────────────────────────────────

    private UIElement PetTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(TabTitle(palette));

        var visible = settings.IsHudVisible;
        var isPet = settings.Mode == HudMode.Pet;

        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "펫 모드", Ui.Toggle(palette, isPet, _ => { onTogglePet(); Rebuild(); }),
                hint: "배경과 숫자를 걷어내고 마스코트만 띄운다. HUD의 마스코트를 더블클릭해도 들어간다.",
                enabled: visible),
            Ui.Divider(palette),
            // 펫 뒤에만 두르는 링이라 펫 모드가 아니면 고를 것이 없다. 열어 두면 눌러도
            // 화면이 그대로라 고장으로 보인다.
            Ui.Row(palette, "사용량 링", Ui.Segmented(palette,
                RingTitles,
                (int)settings.PetRingDisplay,
                index => { settings.PetRingDisplay = (PetRingDisplay)index; Apply(); }),
                hint: "펫 뒤에 두르는 이중 링이다. 바깥이 5시간 세션, 안쪽이 7일 주간. "
                    + "\"올리면\"은 마우스를 올려둔 동안에만 나타난다.",
                enabled: visible && isPet)));

        // 펫 모드에서만 도는 것들이라 제목에 적어 둔다. 안 그러면 왜 잠겼는지 알 수 없다.
        panel.Children.Add(Ui.Section(palette, "스스로 움직이기 (펫 모드에서만)"));
        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "혼자 돌아다니기", Ui.Toggle(palette, settings.PetWanders,
                value => { settings.PetWanders = value; Apply(); }),
                hint: "가만히 두면 화면을 천천히 걸어다닌다. 글을 쓰는 동안에는 멈춘다.",
                enabled: visible && isPet),
            Ui.Divider(palette),
            Ui.Row(palette, "커서 피하기", Ui.Toggle(palette, settings.PetDodgesCursor,
                value => { settings.PetDodgesCursor = value; Apply(); }),
                hint: "커서를 올려둔 채 1초 가까이 잡지 않으면 반대쪽으로 비켜준다.",
                enabled: visible && isPet),
            Ui.Divider(palette),
            Ui.Row(palette, "들고 있을 때 감추기", Ui.Toggle(palette, settings.PetHidesRingWhileHeld,
                value => { settings.PetHidesRingWhileHeld = value; Apply(); }),
                hint: "집어 들면 사용량 링과 버튼 줄이 사라진다. \"항상 표시\"로 해 뒀어도 "
                    + "들고 있는 동안은 안 보인다.",
                enabled: visible && isPet)));

        panel.Children.Add(Ui.Hint(palette,
            "잡고 있는 동안, 글을 쓰는 동안, 화면이 잠긴 동안, 조회가 끊긴 동안에는 움직이지 않는다."));

        return panel;
    }

    /// <summary>
    /// 사용량 링 분절 컨트롤의 문구. **<c>PetRingDisplay.Title()</c> 을 그대로 쓰지 않는다.**
    ///
    /// 거기 "마우스를 올리면" 이 88pt 라 세 칸이 290pt 가 되는데, 최소 크기(480)에서 이
    /// 카드가 쓸 수 있는 가로는 그보다 좁다 — 가로 스크롤이 없어서 오른쪽 칸이 막대도
    /// 없이 잘려 나갔다(<c>--probe-layout</c> 이 18pt 로 잡았다). 맥은 같은 자리에 펼침
    /// 메뉴를 써서 폭이 안 걸린다.
    ///
    /// 짧은 이름만 칸에 넣고 <b>무슨 뜻인지는 아래 설명 줄이 받는다.</b> 값마다 하나씩
    /// 뽑으므로 열거값을 늘려도 칸 수는 저절로 맞는다.
    /// </summary>
    private static string[] RingTitles =>
        [.. Enum.GetValues<PetRingDisplay>().Select(ShortRingTitle)];

    private static string ShortRingTitle(PetRingDisplay display) => display switch
    {
        PetRingDisplay.Always => "항상",
        PetRingDisplay.Never => "안 함",
        _ => "올리면",
    };

    // ── 계정 ────────────────────────────────────────────────────────

    private UIElement AccountTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(TabTitle(palette));

        // 자격 증명을 어디서 찾았고 왜 못 읽었는지 그대로 보여준다.
        //
        // **터미널을 안 쓰는 사람이 있다.** 그 사람한테 "claude auth login 을 치세요"만
        // 내밀면 막다른 길이다. 무엇이 없는지, 어디를 봐야 하는지를 화면에서 알려준다.
        var attempts = new FileCredentialSource(fallbackPaths: WslCredentialPaths.All).Inspect();
        var success = attempts.FirstOrDefault(a => a.Found);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = success is null ? "로그인 정보를 찾지 못했습니다" : "로그인 정보를 찾았습니다",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(palette.Primary),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        if (success is not null && store.NeedsReauth)
        {
            header.Children.Add(Ui.Pill(palette, "만료", palette.Warning));
        }
        panel.Children.Add(header);

        // 무엇으로 로그인돼 있는지. **조회에 성공한 적이 있어야 안다** — 플랜 이름은
        // 서버가 주고, 등급과 만료는 자격 증명에서 온다.
        if (store.Snapshot is { } snapshot)
        {
            panel.Children.Add(Ui.Section(palette, "로그인"));

            var rows = new StackPanel();
            rows.Children.Add(InfoRow(palette, "플랜", snapshot.PlanName ?? "—"));
            if (UsageSnapshot.TierText(snapshot.RateLimitTier) is { } tier)
            {
                rows.Children.Add(Ui.Divider(palette));
                rows.Children.Add(InfoRow(palette, "한도 등급", tier));
            }
            rows.Children.Add(Ui.Divider(palette));
            rows.Children.Add(InfoRow(palette, "토큰 만료", TokenExpiryText(snapshot.TokenExpiresAt)));

            panel.Children.Add(Ui.Card(palette, rows));
        }

        // 살펴본 자리를 전부 늘어놓는다 — 왜 안 됐는지가 여기서 갈린다.
        panel.Children.Add(Ui.Section(palette, "찾아본 자리"));
        var looked = new StackPanel();
        foreach (var attempt in attempts)
        {
            var row = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
            row.Children.Add(new TextBlock
            {
                Text = attempt.Path,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                // **경로는 접히지 않는다.** 띄어쓰기가 없어서 줄바꿈이 걸릴 자리가 없고,
                // 가로 스크롤도 없어서 긴 경로(WSL 자리는 더 길다)는 오른쪽이 소리 없이
                // 사라진다. 말줄임으로 끊고 전체는 마우스를 올리면 보인다.
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = attempt.Path,
                Foreground = palette.Brush(attempt.Found ? palette.Primary : palette.Secondary),
            });
            row.Children.Add(new TextBlock
            {
                Text = attempt.Describe(),
                FontSize = 11,
                Foreground = palette.Brush(attempt.Found ? palette.Accent : palette.Tertiary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0),
            });
            looked.Children.Add(row);
        }
        if (attempts.Count == 0)
        {
            looked.Children.Add(Ui.Hint(palette, "찾아본 자리가 없습니다."));
        }
        panel.Children.Add(Ui.Card(palette, looked));

        panel.Children.Add(Ui.Section(palette, "안내"));
        panel.Children.Add(Ui.Card(palette, new TextBlock
        {
            Text = success is null
                ? attempts.Any(a => a.Problem == CredentialProblem.NoClaudeLogin)
                    ? "파일은 있는데 Claude 로그인이 안 들어 있습니다. Claude 앱이나 Claude Code에서 "
                      + "한 번 로그인하면 채워집니다. WSL 안에서 쓰신다면 그쪽 홈도 찾아봅니다."
                    : "Claude 앱(또는 Claude Code)에서 한 번 로그인하면 이 파일이 만들어집니다. "
                      + "터미널은 필요 없습니다 — Claude 앱을 열고 Claude Code로 아무 대화나 시작해 보세요."
                : store.NeedsReauth
                    ? "토큰이 만료됐고 갱신도 실패했습니다. 아래 재로그인을 누르면 창이 하나 열리고, "
                      + "거기서 로그인을 마치면 조회가 다시 시작됩니다."
                    : "만료된 토큰은 앱이 스스로 갱신합니다. Claude Code를 켜 두지 않아도 사용량이 계속 들어옵니다. "
                      + "토큰은 Authorization 헤더로만 쓰이고 어디에도 다시 쓰거나 남기지 않습니다.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Foreground = palette.Brush(palette.Secondary),
            Margin = new Thickness(0, 8, 0, 8),
        }));

        var buttons = new List<UIElement>
        {
            Ui.Button(palette, "Claude 폴더 열기", OpenClaudeFolder),
            Ui.Button(palette, "기록 열기", () => OpenPath(AppLog.DefaultPath)),
        };

        // **재로그인이 필요할 때만 나온다.** 평소에는 앱이 스스로 갱신하므로 누를 일이
        // 없고, 늘 띄워 두면 멀쩡한 로그인을 다시 하게 만든다.
        if (store.NeedsReauth) buttons.Add(Ui.Button(palette, "Claude Code 재로그인…", onLogin));

        buttons.Add(Ui.Button(palette, "다시 확인", async () =>
        {
            await store.RefreshAsync(force: true).ConfigureAwait(true);
            ShowTab();
        }, Ui.ButtonKind.Accent));

        panel.Children.Add(WrappingButtonRow([.. buttons]));

        return panel;
    }

    /// <summary>
    /// 접히는 단추 줄. **<c>Ui.ButtonRow</c> 대신 여기서만 쓴다.**
    ///
    /// 계정 탭은 단추가 셋에서 넷(재로그인이 붙는다)이라 최소 크기(480)에서 한 줄에 못
    /// 들어간다. 가로 <c>StackPanel</c> 은 그래도 한 줄로 늘어놓고 넘긴 만큼은 잘려 나가서
    /// 오른쪽 단추가 막대도 없이 사라졌다(<c>--probe-layout</c> 이 39pt 로 잡았다).
    /// <c>WrapPanel</c> 은 자리가 모자라면 아랫줄로 넘긴다.
    ///
    /// 아래 여백 8 은 두 줄이 됐을 때 줄끼리 붙지 않게 하는 것이다.
    /// </summary>
    private static UIElement WrappingButtonRow(params UIElement[] buttons)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var button in buttons)
        {
            button.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 8));
            row.Children.Add(button);
        }
        return row;
    }

    /// <summary>
    /// 토큰이 언제까지인지.
    ///
    /// **이미 지났어도 "만료됨"으로 끝내지 않는다** — 앱이 스스로 갱신하므로 그대로
    /// 두면 사용자가 할 일이 있는 줄 안다.
    /// </summary>
    private static string TokenExpiryText(DateTimeOffset? expiresAt)
    {
        if (expiresAt is not { } at) return "—";
        var now = DateTimeOffset.UtcNow;
        return at <= now ? "만료됨 (곧 갱신)" : $"{RemainingTime.ClockText(at, now)} 뒤";
    }

    /// <summary>탐색기로 Claude 설정 폴더를 연다. 없으면 만들지 않고 상위를 연다.</summary>
    private static void OpenClaudeFolder()
    {
        var folder = Path.GetDirectoryName(FileCredentialSource.DefaultPaths().Last());
        if (string.IsNullOrEmpty(folder)) return;

        OpenPath(Directory.Exists(folder)
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>탐색기나 기본 프로그램으로 연다. 없는 경로면 상위 폴더를 연다.</summary>
    private static void OpenPath(string target)
    {
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            target = Path.GetDirectoryName(target) ?? target;
            if (!Directory.Exists(target)) return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException)
        {
        }
    }

    // ── 버전 ────────────────────────────────────────────────────────

    /// <summary>
    /// 버전 탭. **머리는 위에 붙어 있고 변경 내역만 제 안에서 넘어간다.**
    ///
    /// 한 덩어리로 넘기면 변경 내역을 읽으려고 내렸을 때 지금 버전과 "업데이트 확인"
    /// 버튼이 화면 밖으로 밀려 나간다 — 받을 것이 있는지 보러 온 탭에서 정작 그게
    /// 안 보인다. 맥이 <c>tabBodyHeight</c> 로 하는 것과 같은 자리다.
    /// </summary>
    private UIElement VersionTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(TabTitle(palette));

        var big = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        big.Children.Add(new TextBlock
        {
            Text = AppInfo.Version,
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = palette.Brush(palette.Primary),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (AppInfo.IsTestBuild)
        {
            var pill = Ui.Pill(palette, "테스트 빌드", palette.Test);
            pill.Margin = new Thickness(10, 6, 0, 0);
            big.Children.Add(pill);
        }
        panel.Children.Add(big);
        panel.Children.Add(new TextBlock
        {
            Text = StatusLine(),
            FontSize = 12.5,
            Foreground = palette.Brush(palette.Secondary),
            Margin = new Thickness(0, 0, 0, 4),
        });

        if (updates.LastChecked is { } checkedAt)
        {
            panel.Children.Add(Ui.Hint(palette,
                $"마지막 확인: {RemainingTime.AgeText(checkedAt, DateTimeOffset.Now)}"));
        }

        var buttons = new List<UIElement>
        {
            Ui.Button(palette, updates.IsChecking ? "확인 중…" : "업데이트 확인", async () =>
            {
                await updates.CheckAsync().ConfigureAwait(true);
                ShowTab();
            }, enabled: !updates.IsChecking && !AppInfo.IsTestBuild),
        };

        // **받는 중·다 받음 단계에서는 그 줄이 버튼을 대신한다.** 아래 UpdateStage 가
        // 무엇을 눌러야 하는지 정한다.
        if (updates.HasUpdate && updates.IsInstalled && !updates.IsBusy)
        {
            // **번호를 박아 넣지 않는다.** 바로 윗줄이 이미 "새 버전 2.3.1" 이라고
            // 말하고 있어서, 버튼에 또 적으면 같은 번호가 두 번 나온다. 맥과 같다.
            buttons.Insert(0, Ui.Button(palette,
                "업데이트",
                async () =>
                {
                    ShowTab();
                    // **누르면 바로 받기 시작한다.** 확인은 진짜로 꺼지기 직전에 받는다 —
                    // 받기 전에 물으면 정작 꺼지는 건 30초 넘게 받은 뒤라 시점이 어긋난다.
                    await updates.DownloadAsync().ConfigureAwait(true);
                    ShowTab();
                }, Ui.ButtonKind.Accent));
        }
        panel.Children.Add(Ui.ButtonRow([.. buttons]));

        if (UpdateStage(palette) is { } stage) panel.Children.Add(stage);

        if (updates.LastError is { } updateError) panel.Children.Add(Ui.Hint(palette, updateError));

        // **확인 실패와 내려받기 실패는 다른 물건이다.** 회사 프록시·비행기 모드에서는
        // 확인 자체가 안 되는데, 그걸 안 적으면 눌러도 아무 반응이 없어 보인다.
        if (updates.CheckError is { } checkError)
        {
            panel.Children.Add(Ui.Hint(palette, checkError, palette.Warning));
        }

        panel.Children.Add(Ui.Section(palette, "확인"));
        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "하루에 한 번 새 버전 확인", Ui.Toggle(palette, settings.ChecksForUpdates,
                value => { settings.ChecksForUpdates = value; Apply(); }),
                hint: AppInfo.IsTestBuild ? "테스트판은 새 버전을 확인하지 않습니다" : null,
                enabled: !AppInfo.IsTestBuild)));

        // 목록의 이름표라 목록과 같이 넘어가면 안 된다. 여기까지가 붙어 있는 머리다.
        panel.Children.Add(Ui.Section(palette, "변경 내역"));

        // 원격 것으로 갈아치우지 않고 **합친다** — 방금 올린 버전을 쓰는 앱은 자기보다
        // 뒤처진 목록을 받을 수 있고, 그러면 자기 버전 항목이 화면에서 사라진다.
        var list = Stack();
        // 오른쪽은 이 목록의 스크롤 막대 자리다. 안 비우면 버전 카드가 막대에 깔린다.
        list.Margin = new Thickness(0, 0, 12, 0);
        foreach (var entry in Changelog.Merge(updates.RemoteEntries))
        {
            list.Children.Add(ChangelogEntryView(palette, entry));
        }

        var (listHost, listScroll) = Ui.Scroller(palette, list);

        // 읽던 자리를 지킨다. 조회가 들어올 때마다 이 탭이 다시 그려지는데, 그때마다 맨
        // 위로 튀면 긴 목록을 읽을 수가 없다 — 바깥 스크롤에서 하던 것과 같은 처리다.
        // **값을 먼저 챙겨 둔다** — 새 스크롤이 처음 자리를 잡으면서 0 으로 덮어쓴다.
        var readAt = changelogOffset;
        listScroll.ScrollChanged += (_, _) => changelogOffset = listScroll.VerticalOffset;
        if (readAt > 0)
        {
            Dispatcher.BeginInvoke(
                new Action(() => listScroll.ScrollToVerticalOffset(readAt)),
                DispatcherPriority.Loaded);
        }

        // **목록의 높이를 못 박는다.** 바깥 스크롤은 세로 높이를 무한히 제안해서, 안
        // 막으면 목록이 제 길이대로 늘어나고 머리가 그만큼 위로 밀려난다 — 고치기 전이
        // 정확히 그 모습이었다. 맥이 `tabBodyHeight` 로 하는 것과 같은 자리다.
        //
        // **높이를 못 박는 자리로 탭 전체가 아니라 목록을 고른 이유가 있다.** 탭 전체를
        // 못 박으면 그보다 머리가 길어졌을 때(업데이트를 받는 중이면 카드가 하나 더 붙는다)
        // 넘친 부분이 바깥 스크롤에도 안 잡혀 영영 못 보게 된다. 목록만 막아 두면 넘치는
        // 만큼은 늘 바깥 스크롤이 받는다 — 좁을 때 밀려나기는 해도 사라지지는 않는다.
        if (scroller is { } view)
        {
            listHost.SetBinding(FrameworkElement.MaxHeightProperty, new System.Windows.Data.Binding(nameof(ScrollViewer.ViewportHeight))
            {
                Source = view,
                Converter = ChangelogHeight.Instance,
            });
        }
        panel.Children.Add(listHost);

        return panel;
    }

    /// <summary>변경 내역 목록에서 읽던 자리. 탭을 다시 그려도 여기로 되돌린다.</summary>
    private double changelogOffset;

    /// <summary>
    /// 바깥 스크롤의 뷰포트 높이 → 변경 내역 목록에 줄 높이.
    /// </summary>
    private sealed class ChangelogHeight : IValueConverter
    {
        public static readonly ChangelogHeight Instance = new();

        /// <summary>
        /// 목록 위에 있는 것들(제목·지금 버전·버튼 줄·확인 카드)이 쓸 자리.
        ///
        /// **재지 않고 어림한다.** 실제 머리가 이보다 길면 그만큼 탭이 뷰포트를 넘고,
        /// 넘친 만큼은 바깥 스크롤이 받는다 — 틀려도 무엇이 사라지지는 않는다.
        /// </summary>
        private const double HeadAllowance = 300;

        /// <summary>아주 좁은 창에서도 목록에 이만큼은 준다. 더 얇으면 한 줄도 안 보인다.</summary>
        private const double MinHeight = 120;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // 처음 한 판은 뷰포트가 아직 0 이다. 배치가 끝나 값이 들어오면 이 묶음이
            // 다시 걸려 제 높이로 앉는다.
            var viewport = value is double height ? height : 0;
            var room = viewport - BodyPadding.Top - BodyPadding.Bottom - HeadAllowance;
            return Math.Max(MinHeight, room);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 업데이트가 도는 동안 뜨는 줄. 아무것도 안 하는 중이면 null.
    ///
    /// **68MB 를 받는다.** 눌렀는데 아무 일도 안 일어나는 것처럼 보이면 안 된다 —
    /// 몇 %까지 왔는지 보여주고, 다 받으면 여기서 멈춰 사람에게 물어본다.
    /// </summary>
    private UIElement? UpdateStage(SettingsPalette palette)
    {
        if (updates.Phase == UpdateService.UpdatePhase.Idle) return null;

        var rows = new StackPanel();
        rows.Children.Add(new TextBlock
        {
            Text = updates.Phase switch
            {
                UpdateService.UpdatePhase.Downloading => $"새 버전을 받는 중… {updates.DownloadedPercent}%",
                UpdateService.UpdatePhase.Ready => "다 받았습니다 — 앱을 껐다 다시 띄우면 끝납니다",
                _ => "앱을 갈아끼우는 중 — 곧 다시 뜹니다",
            },
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = palette.Brush(palette.Primary),
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (updates.Phase == UpdateService.UpdatePhase.Downloading)
        {
            rows.Children.Add(new System.Windows.Controls.ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = updates.DownloadedPercent,
                Height = 6,
                Foreground = palette.Brush(palette.Accent),
                Background = palette.Brush(palette.TrackOff),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        if (updates.Phase == UpdateService.UpdatePhase.Ready)
        {
            rows.Children.Add(Ui.ButtonRow(
                Ui.Button(palette, "지금 다시 띄우기", updates.Restart, Ui.ButtonKind.Accent),
                Ui.Button(palette, "나중에", () => { updates.Dismiss(); ShowTab(); })));
        }

        if (updates.Phase == UpdateService.UpdatePhase.Swapping)
        {
            // **빠져나갈 길을 둔다.** 여기서 멈추면 화면에 누를 것이 하나도 없어서
            // 작업 관리자를 여는 수밖에 없다.
            rows.Children.Add(Ui.ButtonRow(Ui.Button(palette, "강제 종료", ConfirmForceQuit)));
        }

        return Ui.Card(palette, rows);
    }

    private void ConfirmForceQuit()
    {
        if (ConfirmDialog.Ask(this, Palette,
            "강제로 종료할까요?",
            "갈아끼우는 도중이라 앱이 반쯤 바뀐 채로 남을 수 있습니다. "
            + "그때는 설치본을 다시 받아 깔면 됩니다.",
            "강제 종료"))
        {
            UpdateService.ForceQuit();
        }
    }

    private string StatusLine()
    {
        if (AppInfo.IsTestBuild) return "테스트판은 새 버전을 확인하지 않습니다";
        // 맥은 brew 로 깔려서 이 경우가 없다. 윈도우는 폴더에 놓인 exe 로도 돌 수 있다.
        if (!updates.IsInstalled) return "설치본이 아니라 자동 업데이트를 쓸 수 없습니다";
        if (updates.HasUpdate) return $"새 버전 {updates.LatestVersion}";
        // **`LastChecked` 보다 먼저 본다.** 실패해도 확인 시각은 찍히므로, 이 줄이 없으면
        // 못 물어본 확인이 "최신 버전입니다" 로 읽힌다. 자세한 사유는 아래 힌트 줄에
        // 있다 — 이 줄은 `TextWrapping` 이 없어서 긴 글이 잘린다.
        if (updates.CheckError is not null) return "새 버전을 확인하지 못했습니다";
        if (updates.LastChecked is not null) return "최신 버전입니다";
        return "아직 확인하지 않았습니다";
    }

    private UIElement ChangelogEntryView(SettingsPalette palette, ChangelogEntry entry)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock
        {
            Text = entry.Version,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(palette.Primary),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        // 지금 쓰는 버전이 목록 어디인지 알 수 있어야 한다.
        if (entry.Version == AppInfo.Version) header.Children.Add(Ui.Pill(palette, "지금 버전", palette.Accent));
        else if (entry.Date is null) header.Children.Add(Ui.Pill(palette, "준비 중", palette.Warning));

        if (entry.Date is { } date)
        {
            header.Children.Add(new TextBlock
            {
                Text = date,
                FontSize = 11.5,
                Foreground = palette.Brush(palette.Faint),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
        }

        var notes = new StackPanel();
        notes.Children.Add(header);

        if (entry.Groups is { Count: > 0 } groups)
        {
            foreach (var group in groups) notes.Children.Add(ChangelogGroupView(palette, group));
        }
        else
        {
            // 묶음이 없는 옛 항목. **뒤늦게 나누지 않는다** — 이미 나간 문구라,
            // 사용자가 그때 본 것과 달라지면 안 된다.
            foreach (var note in entry.Notes)
            {
                notes.Children.Add(new TextBlock
                {
                    Text = $"· {note}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 19,
                    Foreground = palette.Brush(palette.Secondary),
                    Margin = new Thickness(2, 1, 0, 1),
                });
            }
        }

        return new Border
        {
            Background = palette.Brush(palette.Card),
            BorderBrush = palette.Brush(palette.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Ui.Radius + 2),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = notes,
        };
    }

    // ── 변경 내역 묶음 ──────────────────────────────────────────────
    //
    // 묶음 머리의 가로 자리는 **한 계산에서 나와야 한다.** 따로 적어 두면 세로줄이
    // 아이콘 옆으로 비끼고, 딸린 줄이 제목과 다른 자리에서 시작한다.

    private const double RuleWidth = 2;
    /// <summary>
    /// 세로줄을 아이콘 한가운데로 내리는 왼쪽 여백.
    ///
    /// **맥은 6.5 인데 여기는 7 이다.** 맥의 SF Symbol 자리가 15 이고 우리 Segoe 글리프
    /// 자리는 16 이라, 숫자를 맞추면 세로줄이 아이콘 한가운데에서 반 픽셀 비낀다.
    /// 맞출 것은 값이 아니라 <b>아이콘 한가운데</b>라는 산식이다.
    /// </summary>
    private const double RuleInset = (TabIcon.Width - RuleWidth) / 2;

    /// <summary>
    /// 아이콘과 제목 사이. **맥과 같은 들여쓰기(20)가 나오게 잡은 값이다** —
    /// 맥은 아이콘 15 + 사이 5, 우리는 아이콘 16 + 사이 4.
    /// </summary>
    private const double IconGap = 4;

    /// <summary>제목 글자가 시작하는 자리. 딸린 줄도 여기에 맞춘다. 맥과 같은 20 이다.</summary>
    private const double TextInset = TabIcon.Width + IconGap;

    /// <summary>
    /// 갈래 딱지 폭. **가장 넓은 갈래 이름에서 뽑는다.**
    ///
    /// 눈대중으로 잡으면 좁을 때는 글자가 잘리고 넓을 때는 뒤따르는 글이 멀찍이 떨어져
    /// 보인다. 갈래를 하나 더 만들어도 여기가 알아서 따라온다.
    ///
    /// **재는 글꼴은 <see cref="Ui.Pill"/> 이 실제로 그리는 글꼴이어야 한다.** 한동안
    /// 10pt Medium 으로 재고 11pt SemiBold 로 그렸는데, 갈래 이름이 전부 두 글자라
    /// 여백에 묻혀 안 드러났을 뿐이다 — 세 글자짜리 갈래를 만드는 날 잘렸다.
    /// 뒤에 더하는 18 은 알약 좌우 여백(8+8)에 2pt 여유다.
    /// </summary>
    private static readonly double BadgeWidth = Enum.GetValues<ChangeKind>()
        .Max(kind => Ui.TextWidth(kind.Title(), Ui.PillFontSize, Ui.PillFontWeight)) + 18;

    /// <summary>
    /// 기능 묶음 하나. 대분류를 달고, 딸린 줄들을 세로줄로 묶어 준다.
    ///
    /// **들여쓰기만으로는 어디까지가 그 묶음인지 안 보인다.** 딱지가 줄마다 붙어 있어서
    /// 왼쪽 끝이 들쭉날쭉해 보이는데, 세로줄이 그 경계를 대신 그어 준다.
    /// </summary>
    private static UIElement ChangelogGroupView(SettingsPalette palette, ChangelogGroup group)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 3) };
        head.Children.Add(TabIcon.Make(group.Tab, 11, palette.Brush(palette.Tertiary)));
        head.Children.Add(new TextBlock
        {
            Text = group.Title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(palette.Primary),
            Margin = new Thickness(TextInset - TabIcon.Width, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        // 묶음 자체가 이번에 생긴 기능이면 제목 옆에 붙는다. 항목마다 "신규"가 줄줄이
        // 달리는 것보다 "이 기능이 새로 생겼다"가 한눈에 들어온다.
        if (group.IsNew)
        {
            var pill = Ui.Pill(palette, ChangeKind.New.Title(), KindColor(palette, ChangeKind.New));
            pill.Margin = new Thickness(6, 0, 0, 0);
            head.Children.Add(pill);
        }

        var lines = new StackPanel();
        foreach (var note in group.Notes)
        {
            // **`DockPanel` 이다.** 가로 `StackPanel` 은 자식에게 남은 폭을 안 물려줘서
            // `TextWrapping` 을 걸어도 안 접히고 오른쪽으로 흘러 나간다.
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1, 0, 1) };

            // **새로 생긴 기능에는 갈래를 안 붙인다.** 전부 새것이라 가를 것이 없고,
            // 제목 옆 "신규" 가 이미 그 말을 한다.
            if (!group.IsNew)
            {
                var badge = Ui.Pill(palette, note.Kind.Title(), KindColor(palette, note.Kind));
                // 갈래 이름이 전부 두 글자라 폭이 거의 같지만, 1pt 만 달라도 뒤따르는
                // 글의 시작점이 줄마다 흔들린다.
                badge.Width = BadgeWidth;
                badge.VerticalAlignment = VerticalAlignment.Top;
                badge.Margin = new Thickness(0, 1, 6, 0);
                DockPanel.SetDock(badge, Dock.Left);
                line.Children.Add(badge);
            }

            line.Children.Add(new TextBlock
            {
                Text = note.Text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Foreground = palette.Brush(palette.Secondary),
            });
            lines.Children.Add(line);
        }

        // 아이콘 한가운데에서 아래로 흐르는 세로줄. 여기까지가 이 묶음이라는 표시다.
        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(RuleInset, 0, 0, 0) };
        var rule = new Border
        {
            Width = RuleWidth,
            CornerRadius = new CornerRadius(RuleWidth / 2),
            Background = palette.Brush(palette.Line),
            Margin = new Thickness(0, 1, TextInset - RuleInset - RuleWidth, 1),
        };
        DockPanel.SetDock(rule, Dock.Left);
        body.Children.Add(rule);
        body.Children.Add(lines);

        var whole = new StackPanel();
        whole.Children.Add(head);
        whole.Children.Add(body);
        return whole;
    }

    private static Color KindColor(SettingsPalette palette, ChangeKind kind) => kind switch
    {
        ChangeKind.New => palette.Good,
        ChangeKind.Improve => palette.Accent,
        // **`Test` 를 쓰지 않는다.** 그 보라는 '테스트 빌드' 딱지의 색이라, 한 화면에
        // 둘이 같이 뜨면 뜻이 겹쳐서 어느 쪽이 무슨 말인지 흐려진다.
        ChangeKind.Change => palette.Changed,
        // **`Warning` 을 쓰지 않는다.** 그 호박색은 재로그인 필요 · 오래된 값 · 만료 ·
        // 베타처럼 **지금 문제가 있다**는 자리에 쓴다. 갈래의 '오류'는 반대로 이미
        // 고쳐진 것이라, 맥과 같은 주황으로 갈라 둔다.
        ChangeKind.Fix => palette.Fixed,
        _ => palette.Faint,
    };

    /// <summary>
    /// 앞으로 끌어낸다.
    ///
    /// **HUD 는 <c>WS_EX_NOACTIVATE</c> 라 포커스를 받지 않는다.** 그래서 HUD 의 설정
    /// 버튼으로 이 창을 열면 우리 프로세스가 전면이 아닌 상태이고, 그냥 <c>Show()</c> 만
    /// 하면 창은 떴는데 **다른 창 뒤에 깔린다.** 사용자 눈에는 최소화된 것처럼 보인다.
    ///
    /// <c>SetForegroundWindow</c> 는 전면이 아닌 프로세스가 부르면 윈도우가 거절하고
    /// 작업 표시줄만 깜빡이게 한다. 전면 창의 입력 큐에 잠깐 붙었다 떼면 그 제한이 풀린다.
    /// </summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        Show();
        Activate();

        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (self == IntPtr.Zero) return;

        var foreground = NativeWindow.GetForegroundWindow();
        var us = NativeWindow.GetCurrentThreadId();
        var them = foreground == IntPtr.Zero
            ? 0
            : NativeWindow.GetWindowThreadProcessId(foreground, IntPtr.Zero);

        var attached = them != 0 && them != us && NativeWindow.AttachThreadInput(us, them, true);
        try
        {
            NativeWindow.SetForegroundWindow(self);
        }
        finally
        {
            if (attached) NativeWindow.AttachThreadInput(us, them, false);
        }

        Focus();
    }

    /// <summary>
    /// 닫히기 직전에 자리와 크기를 챙긴다.
    ///
    /// **<c>Left</c>·<c>Top</c>·<c>ActualWidth</c> 가 아니라 <c>RestoreBounds</c> 여야 한다.**
    /// 최대화한 채로 닫으면 앞의 값들은 화면 전체라, 다음에 열어 최대화를 풀었을 때
    /// 창이 화면만 해진다. <c>RestoreBounds</c> 는 최대화하기 전의 자리를 들고 있다.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        lastMaximized = WindowState == WindowState.Maximized;

        var bounds = RestoreBounds;
        // 한 번도 안 뜬 창은 비어 있다. 그걸 기억하면 다음에 0×0 으로 뜬다.
        if (bounds.Width > 0 && bounds.Height > 0) lastBounds = bounds;

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        tick.Stop();
        UnhookMeasure();
        base.OnClosed(e);
    }

    /// <summary>
    /// 기억해 둔 자리가 아직 화면 안인지.
    ///
    /// **<c>Forms.Screen</c> 을 쓰면 안 된다.** 그쪽은 물리 픽셀이고 <c>Window.Left</c> 는
    /// DIP 라, 배율이 100%가 아닌 화면에서 값이 어긋난다. <c>SystemParameters</c> 쪽은
    /// DIP 라 그대로 견줄 수 있다. 같은 판단이 <c>Hud/HudWindow.cs</c> 에도 있다.
    /// </summary>
    private static bool IsOnAnyScreen(Rect bounds)
    {
        var all = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        // 조금이라도 걸쳐 있으면 잡아서 옮길 수 있다.
        return all.IntersectsWith(bounds);
    }
}

internal static partial class NativeWindow
{
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attach, uint attachTo, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool join);
}

