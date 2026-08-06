using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DongCSU.App.Services;
using DongCSU.Core;
using DongCSU.Core.Usage;

namespace DongCSU.App.Settings;

/// <summary>
/// 설정 창. 왼쪽에 탭, 오른쪽에 내용.
///
/// 맥판과 같은 구성이되 **펫 탭이 없다** — 윈도우 첫 배포에는 펫 모드가 없다.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly UsageStore store;
    private readonly UpdateService updates;
    private readonly Action onChanged;
    private readonly ContentControl body = new();
    private readonly ListBox tabs = new();

    private static readonly (string Key, string Title)[] TabList =
    [
        ("status", "상태"),
        ("display", "표시"),
        ("account", "계정"),
        ("version", "버전"),
    ];

    public SettingsWindow(AppSettings settings, UsageStore store, UpdateService updates, Action onChanged)
    {
        this.settings = settings;
        this.store = store;
        this.updates = updates;
        this.onChanged = onChanged;

        Title = $"{AppInfo.Name} 설정";
        Width = 520;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = true;

        foreach (var (_, title) in TabList) tabs.Items.Add(title);
        tabs.Width = 130;
        tabs.SelectedIndex = 0;
        tabs.SelectionChanged += (_, _) => ShowTab();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(tabs, 0);
        grid.Children.Add(tabs);

        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(18),
        };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);

        Content = grid;
        ShowTab();
    }

    /// <summary>
    /// 지금 탭을 다시 그린다.
    ///
    /// 사용량이 새로 들어오거나 업데이트 확인이 끝났을 때 부른다. 이게 없으면
    /// **창을 열어 둔 채로는 숫자가 영영 안 바뀐다** — 탭을 눌러야만 갱신된다.
    /// </summary>
    public void Refresh() => ShowTab();

    /// <summary>탭을 하나 열어 둔 채로 띄운다. 트레이에서 "버전"으로 바로 갈 때 쓴다.</summary>
    public void SelectTab(string key)
    {
        var index = Array.FindIndex(TabList, t => t.Key == key);
        if (index >= 0) tabs.SelectedIndex = index;
    }

    private void ShowTab()
    {
        var key = TabList[Math.Max(0, tabs.SelectedIndex)].Key;
        body.Content = key switch
        {
            "display" => DisplayTab(),
            "account" => AccountTab(),
            "version" => VersionTab(),
            _ => StatusTab(),
        };
    }

    // ── 상태 ──────────────────────────────────────────────────────

    private UIElement StatusTab()
    {
        var panel = Stack();
        var now = DateTimeOffset.Now;

        if (store.Snapshot is { } snapshot)
        {
            if (snapshot.PlanName is { } plan) panel.Children.Add(Label($"플랜: {plan}"));
            panel.Children.Add(Label(WindowLine("5시간 세션", snapshot.FiveHour, now)));
            panel.Children.Add(Label(WindowLine("7일 주간", snapshot.SevenDay, now)));
            panel.Children.Add(Hint(RemainingTime.AgeText(snapshot.FetchedAt, now)));
        }
        else
        {
            panel.Children.Add(Label("아직 사용량을 받지 못했습니다."));
        }

        if (store.ErrorText is { } error) panel.Children.Add(Hint($"마지막 조회 실패: {error}"));

        var refresh = new Button
        {
            Content = store.IsRefreshing ? "조회 중…" : "새로고침",
            IsEnabled = !store.IsRefreshing,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 4, 14, 4),
        };
        refresh.Click += async (_, _) =>
        {
            await store.RefreshAsync(force: true).ConfigureAwait(true);
            ShowTab();
        };
        panel.Children.Add(refresh);

        return panel;
    }

    private static string WindowLine(string label, UsageWindow? window, DateTimeOffset now) =>
        window is { } value
            ? $"{label}: {Math.Round(value.Utilization):F0}%  ({RemainingTime.Text(value.ResetsAt, now)})"
            : $"{label}: –";

    // ── 표시 ──────────────────────────────────────────────────────

    private UIElement DisplayTab()
    {
        var panel = Stack();

        panel.Children.Add(Row("테마", EnumBox<HudTheme>(settings.Theme,
            ["시스템에 맞춤", "밝게", "어둡게"],
            value => { settings.Theme = value; Apply(); })));

        panel.Children.Add(Row("크기", EnumBox<HudScale>(settings.Scale,
            [.. System.Enum.GetValues<HudScale>().Select(s => s.Title())],
            value => { settings.Scale = value; Apply(); })));

        panel.Children.Add(Row("조회 주기", Choice(
            [60, 180, 300, 600, 1800],
            ["1분", "3분", "5분", "10분", "30분"],
            settings.PollIntervalSeconds,
            value => { settings.PollIntervalSeconds = value; Apply(); })));

        panel.Children.Add(Row("펼침 방향", EnumBox<HudExpandSide>(settings.ExpandSide,
            ["오른쪽", "왼쪽"],
            value => { settings.ExpandSide = value; Apply(); })));

        panel.Children.Add(Check("HUD 표시", settings.IsHudVisible,
            value => { settings.IsHudVisible = value; Apply(); }));

        panel.Children.Add(Check("접어서 링만 보기", settings.Mode == HudMode.Collapsed,
            value => { settings.Mode = value ? HudMode.Collapsed : HudMode.Expanded; Apply(); }));

        panel.Children.Add(Check("왼쪽 위에 버전 표시", settings.ShowsVersionBadge,
            value => { settings.ShowsVersionBadge = value; Apply(); }));

        panel.Children.Add(Hint("HUD는 드래그로 옮길 수 있고, 더블클릭하면 접었다 펴집니다."));

        panel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 8) });

        var startup = Check("로그인할 때 자동 시작", StartupService.IsEnabled, value =>
        {
            if (!StartupService.SetEnabled(value)) ShowTab();   // 실패하면 표시를 되돌린다
            settings.StartsAtLogin = StartupService.IsEnabled;
            Apply();
        });
        panel.Children.Add(startup);

        return panel;
    }

    // ── 계정 ──────────────────────────────────────────────────────

    private UIElement AccountTab()
    {
        var panel = Stack();

        // 자격 증명 파일이 실제로 있는지부터 보여준다.
        //
        // **터미널을 안 쓰는 사람이 있다.** 그 사람한테 "claude auth login 을 치세요"만
        // 내밀면 막다른 길이다. 무엇이 없는지, 어디를 봐야 하는지를 화면에서 알려준다.
        var found = FileCredentialSource.DefaultPaths().FirstOrDefault(File.Exists);

        panel.Children.Add(Label(found is null
            ? "Claude Code 로그인 정보를 찾지 못했습니다."
            : "Claude Code 로그인 정보를 찾았습니다."));

        panel.Children.Add(Hint(found ?? string.Join("\n", FileCredentialSource.DefaultPaths())));

        if (found is null)
        {
            panel.Children.Add(Hint(
                "\nClaude 앱(또는 Claude Code)에서 한 번 로그인하면 이 파일이 만들어집니다.\n"
                + "터미널은 필요 없습니다 — Claude 앱을 열고 Claude Code로 아무 대화나 시작해 보세요."));
        }
        else if (store.NeedsReauth)
        {
            panel.Children.Add(Hint(
                "\n토큰이 만료됐습니다. Claude 앱에서 다시 로그인하면 조회가 재개됩니다.\n"
                + "토큰 수명이 8시간이라 종종 필요합니다."));
        }
        else
        {
            panel.Children.Add(Hint(
                "\n사용량은 이 파일에 담긴 토큰으로 읽습니다. 토큰은 Authorization 헤더로만 쓰이고\n"
                + "어디에도 다시 쓰거나 남기지 않습니다."));
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var openFolder = new Button
        {
            Content = "폴더 열기",
            Padding = new Thickness(14, 4, 14, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        openFolder.Click += (_, _) => OpenClaudeFolder();
        buttons.Children.Add(openFolder);

        var recheck = new Button { Content = "다시 확인", Padding = new Thickness(14, 4, 14, 4) };
        recheck.Click += async (_, _) =>
        {
            await store.RefreshAsync(force: true).ConfigureAwait(true);
            ShowTab();
        };
        buttons.Children.Add(recheck);

        panel.Children.Add(buttons);
        return panel;
    }

    /// <summary>탐색기로 Claude 설정 폴더를 연다. 없으면 만들지 않고 상위를 연다.</summary>
    private static void OpenClaudeFolder()
    {
        var folder = Path.GetDirectoryName(FileCredentialSource.DefaultPaths().Last());
        if (string.IsNullOrEmpty(folder)) return;

        var target = Directory.Exists(folder)
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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

    // ── 버전 ──────────────────────────────────────────────────────

    private UIElement VersionTab()
    {
        var panel = Stack();
        panel.Children.Add(Label($"지금 버전: {AppInfo.Version}"));

        if (!updates.IsInstalled)
        {
            panel.Children.Add(Hint("설치본이 아니라 자동 업데이트를 쓸 수 없습니다."));
        }
        else if (updates.HasUpdate)
        {
            panel.Children.Add(Label($"새 버전 {updates.LatestVersion}"));
            var apply = new Button
            {
                // 68MB 를 받는다. 눌렀는데 아무 일도 안 일어나는 것처럼 보이면 안 된다.
                Content = updates.IsApplying ? "받는 중… (68MB)" : "업데이트",
                IsEnabled = !updates.IsApplying,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 4, 14, 4),
            };
            apply.Click += async (_, _) =>
            {
                ShowTab();                                      // 먼저 "받는 중"으로 바꾼다
                await updates.ApplyAsync().ConfigureAwait(true);
                ShowTab();                                      // 실패했으면 이유가 뜬다
            };
            panel.Children.Add(apply);
            panel.Children.Add(Hint("받아서 깔고 나면 앱이 저절로 다시 뜹니다."));
        }
        else if (updates.LastChecked is not null)
        {
            panel.Children.Add(Hint("최신 버전입니다."));
        }

        var check = new Button
        {
            Content = updates.IsChecking ? "확인 중…" : "업데이트 확인",
            IsEnabled = !updates.IsChecking,
            Margin = new Thickness(0, 6, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 4, 14, 4),
        };
        check.Click += async (_, _) => { await updates.CheckAsync().ConfigureAwait(true); ShowTab(); };
        panel.Children.Add(check);

        if (updates.LastError is { } updateError) panel.Children.Add(Hint(updateError));

        panel.Children.Add(Check("하루에 한 번 새 버전 확인", settings.ChecksForUpdates,
            value => { settings.ChecksForUpdates = value; Apply(); }));

        panel.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 8) });
        panel.Children.Add(Label("변경 내역"));

        // 원격 내역이 있으면 그걸 쓴다. 아직 안 받았으면 앱에 박힌 것을 보여준다.
        var entries = updates.RemoteEntries.Count > 0 ? updates.RemoteEntries : Changelog.Entries;
        foreach (var entry in entries)
        {
            var header = entry.Date is { } date ? $"{entry.Version}  ({date})" : $"{entry.Version}  (예정)";
            panel.Children.Add(new TextBlock
            {
                Text = header,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 2),
            });
            foreach (var note in entry.Notes)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"· {note}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 1, 0, 1),
                    Foreground = Brushes.Gray,
                });
            }
        }

        return panel;
    }

    // ── 부품 ──────────────────────────────────────────────────────

    private void Apply()
    {
        settings.Save();
        onChanged();
    }

    private static StackPanel Stack() => new() { Orientation = Orientation.Vertical };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 3, 0, 3),
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = Brushes.Gray,
        Margin = new Thickness(0, 3, 0, 3),
        TextWrapping = TextWrapping.Wrap,
    };

    private static UIElement Row(string label, UIElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5), LastChildFill = true };
        var text = new TextBlock { Text = label, Width = 84, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    private static CheckBox Check(string label, bool value, Action<bool> onSet)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 5, 0, 5) };
        box.Checked += (_, _) => onSet(true);
        box.Unchecked += (_, _) => onSet(false);
        return box;
    }

    private static ComboBox EnumBox<T>(T current, IReadOnlyList<string> titles, Action<T> onSet)
        where T : struct, System.Enum
    {
        var values = System.Enum.GetValues<T>();
        var box = new ComboBox { SelectedIndex = Array.IndexOf(values, current) };
        foreach (var title in titles) box.Items.Add(title);
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < values.Length) onSet(values[box.SelectedIndex]);
        };
        return box;
    }

    private static ComboBox Choice(
        IReadOnlyList<int> values, IReadOnlyList<string> titles, int current, Action<int> onSet)
    {
        var box = new ComboBox { SelectedIndex = Math.Max(0, values.ToList().IndexOf(current)) };
        foreach (var title in titles) box.Items.Add(title);
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < values.Count) onSet(values[box.SelectedIndex]);
        };
        return box;
    }
}
