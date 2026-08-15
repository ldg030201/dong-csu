using System.IO;
using System.Windows;
using System.Windows.Controls;
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
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly UsageStore store;
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
    private int selected;

    private static readonly (string Key, string Title)[] TabList =
    [
        ("status", "상태"),
        ("display", "표시"),
        ("icon", "아이콘"),
        ("pet", "펫"),
        ("account", "계정"),
        ("version", "버전"),
    ];

    private SettingsPalette Palette => SettingsPalette.For(IsDarkTheme());

    public SettingsWindow(
        AppSettings settings,
        UsageStore store,
        UpdateService updates,
        Action onChanged,
        Action onResetPosition,
        Action onTogglePet,
        Action onLogin)
    {
        this.settings = settings;
        this.store = store;
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
        ResizeMode = ResizeMode.CanResize;   // 최대화·전체화면·가장자리 드래그가 다 열린다
        ShowInTaskbar = true;
        Content = root;

        tick.Tick += (_, _) => { if (TabList[selected].Key == "status") ShowTab(); };

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

        selected = index;
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

        body.Margin = new Thickness(24, 22, 24, 18);
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
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(TabIcon.Make(TabList[i].Key, 13, palette.Brush(palette.Secondary)));
            row.Children.Add(new TextBlock
            {
                Text = TabList[i].Title,
                FontSize = 13,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var item = new Border
            {
                CornerRadius = new CornerRadius(Ui.Radius),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(8, 1, 8, 1),
                Cursor = Cursors.Hand,
                Child = row,
            };
            item.MouseLeftButtonUp += (_, _) => { selected = index; PaintNav(palette); ShowTab(); SyncTicker(); };
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
            var chosen = i == selected;
            navItems[i].Background = palette.Brush(chosen ? palette.AccentSoft : Colors.Transparent);

            var row = (StackPanel)navItems[i].Child;
            var brush = palette.Brush(chosen ? palette.Accent : palette.Secondary);
            // 아이콘도 같이 물든다. 글자만 바꾸면 고른 줄에서 아이콘만 흐릿하게 남는다.
            ((TextBlock)row.Children[0]).Foreground = brush;

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

        var quit = Ui.Button(palette, $"{AppInfo.Name} 종료", ConfirmQuit);
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
        var needed = TabList[selected].Key == "status";
        if (needed && !tick.IsEnabled) tick.Start();
        else if (!needed && tick.IsEnabled) tick.Stop();
    }

    private void ShowTab()
    {
        var palette = Palette;
        // 내용을 갈아 끼우면 스크롤이 맨 위로 간다. 읽던 자리를 도로 맞춰 준다.
        var offset = scroller?.VerticalOffset ?? 0;

        body.Content = TabList[selected].Key switch
        {
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

    // ── 상태 ────────────────────────────────────────────────────────

    private UIElement StatusTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(Ui.Title(palette, "상태"));

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
            InfoRow(palette, "조회 주기", PollTitle(settings.PollIntervalSeconds))));

        if (store.ErrorText is { } error) panel.Children.Add(Ui.Hint(palette, $"마지막 조회 실패: {error}"));

        panel.Children.Add(Ui.ButtonRow(
            Ui.Button(palette, store.IsRefreshing ? "조회 중…" : "새로고침", async () =>
            {
                await store.RefreshAsync(force: true).ConfigureAwait(true);
                ShowTab();
            }, Ui.ButtonKind.Accent, enabled: !store.IsRefreshing)));

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

    private static string PollTitle(int seconds) => seconds switch
    {
        60 => "1분",
        180 => "3분",
        300 => "5분",
        1800 => "30분",
        _ => "10분",
    };


    // ── 표시 ────────────────────────────────────────────────────────

    private UIElement DisplayTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(Ui.Title(palette, "표시"));

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
            Ui.Row(palette, "크기", Ui.Segmented(palette,
                [.. Enum.GetValues<HudScale>().Select(s => s.Title())],
                (int)settings.Scale,
                index => { settings.Scale = (HudScale)index; Apply(); }), enabled: visible),
            Ui.Divider(palette),
            Ui.Row(palette, "배경 불투명도",
                Ui.Slider(palette, settings.BackdropOpacity, AppSettings.MinBackdropOpacity, 1.0, value =>
                {
                    // **여기서 다시 그리지 않는다.** 탭을 통째로 다시 만들면 드래그가 끊긴다.
                    settings.BackdropOpacity = value;
                    Apply();
                }),
                hint: "너무 투명하면 글자가 안 읽혀 아래를 막아 뒀다.", enabled: visible)));

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
                ["1분", "3분", "5분", "10분", "30분"],
                Math.Max(0, Array.IndexOf(PollChoices, settings.PollIntervalSeconds)),
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

    private static readonly int[] PollChoices = [60, 180, 300, 600, 1800];

    /// <summary>
    /// 설정을 통째로 되돌린다.
    ///
    /// **되돌릴 수 없으니 한 번 묻는다.** 자동 시작도 함께 끈다 — 설정 파일에는 없지만
    /// 사용자가 보기에 그것도 이 앱의 설정이다.
    /// </summary>
    /// <summary>
    /// 설정 창의 종료 버튼은 **실수로 누르기 쉬운 자리라** 한 번 확인한다. 종료하면
    /// 트레이 아이콘까지 사라져서 다시 켤 곳을 찾아야 한다.
    ///
    /// **트레이 메뉴의 종료는 안 묻는다** — 메뉴를 열어 고른 것이라 실수일 수가 없다.
    /// </summary>
    private void ConfirmQuit()
    {
        var answer = MessageBox.Show(
            this,
            "종료하면 사용량 표시와 트레이 아이콘이 모두 사라집니다.",
            $"{AppInfo.Name}를 종료할까요?",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            // **파란 버튼이 곧 할 일이다.** 안 걸어 두면 취소가 파랗게 잡혀서,
            // 정작 하려던 것이 흰 버튼이 된다.
            MessageBoxResult.OK);
        if (answer == MessageBoxResult.OK) Application.Current.Shutdown();
    }

    private void ResetEverything()
    {
        var answer = MessageBox.Show(
            this,
            "되돌릴 수 없습니다. 로그인할 때 자동 시작도 함께 꺼집니다.",
            "모든 설정을 초기화할까요?",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            // 파란 버튼이 곧 할 일이다. 위 종료 확인과 같은 이유다.
            MessageBoxResult.OK);
        if (answer != MessageBoxResult.OK) return;

        var fresh = new AppSettings();
        settings.Mode = fresh.Mode;
        settings.Theme = fresh.Theme;
        settings.Scale = fresh.Scale;
        settings.ExpandSide = fresh.ExpandSide;
        settings.IconStyle = fresh.IconStyle;
        settings.PollIntervalSeconds = fresh.PollIntervalSeconds;
        settings.IsHudVisible = fresh.IsHudVisible;
        settings.ShowsVersionBadge = fresh.ShowsVersionBadge;
        settings.ChecksForUpdates = fresh.ChecksForUpdates;
        settings.ShowsProcessStats = fresh.ShowsProcessStats;
        settings.AnimatesMascot = fresh.AnimatesMascot;
        settings.BackdropOpacity = fresh.BackdropOpacity;
        settings.ModeBeforePet = fresh.ModeBeforePet;
        settings.PetRingDisplay = fresh.PetRingDisplay;
        settings.PetWanders = fresh.PetWanders;
        settings.PetDodgesCursor = fresh.PetDodgesCursor;
        settings.WindowLeft = null;
        settings.WindowTop = null;

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
        panel.Children.Add(Ui.Title(palette, "아이콘"));
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
            IsDark = palette.IsDark,
            Width = 44,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel();
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
        panel.Children.Add(Ui.Title(palette, "펫"));

        var visible = settings.IsHudVisible;
        var isPet = settings.Mode == HudMode.Pet;

        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "펫 모드", Ui.Toggle(palette, isPet, _ => { onTogglePet(); Rebuild(); }),
                hint: "배경과 숫자를 걷어내고 마스코트만 띄운다. HUD의 마스코트를 더블클릭해도 들어간다.",
                enabled: visible),
            Ui.Divider(palette),
            Ui.Row(palette, "사용량 링", Ui.Segmented(palette,
                [.. Enum.GetValues<PetRingDisplay>().Select(d => d.Title())],
                (int)settings.PetRingDisplay,
                index => { settings.PetRingDisplay = (PetRingDisplay)index; Apply(); }),
                hint: "펫 뒤에 두르는 이중 링이다. 바깥이 5시간 세션, 안쪽이 7일 주간.",
                enabled: visible)));

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

    // ── 계정 ────────────────────────────────────────────────────────

    private UIElement AccountTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(Ui.Title(palette, "계정"));

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

        panel.Children.Add(Ui.ButtonRow([.. buttons]));

        return panel;
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

    private UIElement VersionTab(SettingsPalette palette)
    {
        var panel = Stack();
        panel.Children.Add(Ui.Title(palette, "버전"));

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

        if (updates.HasUpdate && updates.IsInstalled)
        {
            buttons.Insert(0, Ui.Button(palette,
                // 68MB 를 받는다. 눌렀는데 아무 일도 안 일어나는 것처럼 보이면 안 된다.
                updates.IsApplying ? "받는 중… (68MB)" : $"{updates.LatestVersion} 로 업데이트",
                async () =>
                {
                    ShowTab();
                    await updates.ApplyAsync().ConfigureAwait(true);
                    ShowTab();
                }, Ui.ButtonKind.Accent, enabled: !updates.IsApplying));
        }
        panel.Children.Add(Ui.ButtonRow([.. buttons]));

        if (updates.LastError is { } updateError) panel.Children.Add(Ui.Hint(palette, updateError));

        panel.Children.Add(Ui.Section(palette, "확인"));
        panel.Children.Add(Ui.Card(palette,
            Ui.Row(palette, "하루에 한 번 새 버전 확인", Ui.Toggle(palette, settings.ChecksForUpdates,
                value => { settings.ChecksForUpdates = value; Apply(); }),
                hint: AppInfo.IsTestBuild ? "테스트판은 새 버전을 확인하지 않습니다" : null,
                enabled: !AppInfo.IsTestBuild)));

        panel.Children.Add(Ui.Section(palette, "변경 내역"));

        // 원격 것으로 갈아치우지 않고 **합친다** — 방금 올린 버전을 쓰는 앱은 자기보다
        // 뒤처진 목록을 받을 수 있고, 그러면 자기 버전 항목이 화면에서 사라진다.
        foreach (var entry in Changelog.Merge(updates.RemoteEntries))
        {
            panel.Children.Add(ChangelogEntryView(palette, entry));
        }

        return panel;
    }

    private string StatusLine()
    {
        if (AppInfo.IsTestBuild) return "테스트판은 새 버전을 확인하지 않습니다";
        // 맥은 brew 로 깔려서 이 경우가 없다. 윈도우는 폴더에 놓인 exe 로도 돌 수 있다.
        if (!updates.IsInstalled) return "설치본이 아니라 자동 업데이트를 쓸 수 없습니다";
        if (updates.HasUpdate) return $"새 버전 {updates.LatestVersion}";
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
    /// <summary>세로줄을 아이콘 한가운데로 내리는 왼쪽 여백.</summary>
    private const double RuleInset = (TabIcon.Width - RuleWidth) / 2;
    /// <summary>제목 글자가 시작하는 자리. 딸린 줄도 여기에 맞춘다.</summary>
    private const double TextInset = TabIcon.Width + 6;

    /// <summary>
    /// 갈래 딱지 폭. **가장 넓은 갈래 이름에서 뽑는다.**
    ///
    /// 눈대중으로 잡으면 좁을 때는 글자가 잘리고 넓을 때는 뒤따르는 글이 멀찍이 떨어져
    /// 보인다. 갈래를 하나 더 만들어도 여기가 알아서 따라온다.
    /// </summary>
    private static readonly double BadgeWidth = Enum.GetValues<ChangeKind>()
        .Max(kind => Ui.TextWidth(kind.Title(), 10, FontWeights.Medium)) + 18;

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
        ChangeKind.Change => palette.Test,
        ChangeKind.Fix => palette.Warning,
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

    protected override void OnClosed(EventArgs e)
    {
        tick.Stop();
        base.OnClosed(e);
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

