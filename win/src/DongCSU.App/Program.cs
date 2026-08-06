using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DongCSU.App.Hud;
using DongCSU.App.Services;
using DongCSU.App.Settings;
using DongCSU.App.Tray;
using DongCSU.Core;
using DongCSU.Core.Owl;
using DongCSU.Core.Usage;
using Velopack;

namespace DongCSU.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 진단 통로. 창을 띄우지 않고 확인만 한다 — 맥판의 --render/--dump 와 같은 자리다.
        if (Diagnostics.TryRun(args, out var exitCode)) return exitCode;

        // Velopack 은 설치·업데이트 때 자기 인자를 받아 처리하고 프로세스를 끝낸다.
        // **다른 무엇보다 먼저 불러야 한다.** 창을 먼저 띄우면 설치 중에 창이 깜빡인다.
        VelopackApp.Build().Run();

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controller = new AppController();
        application.Startup += (_, _) => controller.Start();
        application.Exit += (_, _) => controller.Dispose();
        return application.Run();
    }
}

/// <summary>창 · 트레이 · 조회를 잇는 곳. 맥판의 <c>AppDelegate</c> 자리다.</summary>
public sealed class AppController : IDisposable
{
    private readonly AppSettings settings = AppSettings.Load();
    private readonly HttpClient http = UsageApi.CreateHttpClient();
    private readonly OwlAnimator animator = new(OwlDocument.Embedded);
    private readonly DispatcherTimer pollTimer = new();
    private readonly DispatcherTimer frameTimer = new();
    private readonly DispatcherTimer updateTimer = new();

    private UsageStore store = null!;
    private UpdateService updates = null!;
    private HudWindow? hud;
    private TrayIcon? tray;
    private SettingsWindow? settingsWindow;

    public void Start()
    {
        AppLog.Start();
        AppLog.Write($"시작 {AppInfo.Version} · 경로 {Environment.ProcessPath}");

        // 업데이트하면 앱 경로가 바뀐다. 옛 경로가 남아 있으면 로그인할 때 아무것도 안 뜬다.
        StartupService.RepairIfEnabled();

        // 자격 증명을 어디서 찾았는지 남긴다. "사용량이 안 나온다"의 대부분이 여기서 갈린다.
        foreach (var candidate in FileCredentialSource.DefaultPaths())
        {
            AppLog.Write($"자격 증명 후보: {candidate} · {(File.Exists(candidate) ? "있음" : "없음")}");
        }

        var credentials = new CredentialStore(new FileCredentialSource());
        if (credentials.Current() is { } credential)
        {
            // 토큰 자체는 절대 남기지 않는다. 길이와 만료 시각만으로 충분히 갈린다.
            AppLog.Write(
                $"자격 증명 읽기 성공 · 토큰 {credential.AccessToken.Length}자 · "
                + $"플랜 {credential.SubscriptionType ?? "-"} · "
                + $"만료 {credential.ExpiresAt?.ToString("u") ?? "없음"}"
                + (credential.IsExpired(DateTimeOffset.UtcNow) ? " (파일 기준으로는 지남 — 그래도 조회는 해 본다)" : ""));
        }
        else
        {
            AppLog.Write("자격 증명 읽기 실패");
        }
        store = new UsageStore(new UsageApi(http, credentials)) { PollInterval = settings.PollInterval };
        store.Changed += OnStoreChanged;

        updates = new UpdateService(http);
        // 갈아 끼우기 전에 트레이 아이콘과 창을 놓아 준다. 남아 있으면 프로세스가
        // 깨끗이 안 끝나서 Velopack 이 파일을 못 바꾸고 물러난다.
        updates.BeforeRestart = () => Dispatch(ReleaseForUpdate);
        updates.Changed += () => Dispatch(() =>
        {
            RefreshHud();
            settingsWindow?.Refresh();
        });

        tray = new TrayIcon();
        tray.RefreshRequested += () => _ = store.RefreshAsync(force: true);
        tray.SettingsRequested += () => OpenSettings();
        tray.LoginRequested += () => OpenSettings("account");
        tray.QuitRequested += Quit;
        tray.Activated += ToggleHudVisible;

        hud = new HudWindow(settings);
        hud.ModeToggled += () =>
        {
            settings.Mode = settings.Mode == HudMode.Collapsed ? HudMode.Expanded : HudMode.Collapsed;
            settings.Save();
            ApplySettings();
        };
        hud.ContextMenuRequested += () => OpenSettings();

        ApplySettings();
        hud.RestorePosition();
        if (settings.IsHudVisible) hud.Show();

        pollTimer.Tick += async (_, _) => await store.RefreshAsync().ConfigureAwait(true);
        frameTimer.Tick += (_, _) => AdvanceFrame();

        updateTimer.Interval = UpdateService.CheckInterval;
        updateTimer.Tick += async (_, _) =>
        {
            if (settings.ChecksForUpdates) await updates.CheckAsync().ConfigureAwait(true);
        };
        updateTimer.Start();

        _ = store.RefreshAsync(force: true);
        if (settings.ChecksForUpdates) _ = updates.CheckAsync();

        StartFrameTimer();
    }

    /// <summary>설정이 바뀌면 창·타이머를 거기에 맞춘다.</summary>
    private void ApplySettings()
    {
        if (hud is null) return;

        hud.View.Mode = settings.Mode;
        hud.View.Scale = settings.Scale.Factor();
        hud.View.BackdropOpacity = settings.BackdropOpacity;
        hud.View.IsDark = IsDarkTheme();
        hud.View.VersionBadge = settings.ShowsVersionBadge ? AppInfo.Version : null;
        hud.View.HasUpdate = updates.HasUpdate;

        store.PollInterval = settings.PollInterval;
        pollTimer.Interval = store.NextPollDelay();
        pollTimer.Start();

        if (settings.IsHudVisible) hud.Show(); else hud.Hide();

        RefreshHud();
    }

    private bool IsDarkTheme() => settings.Theme switch
    {
        HudTheme.Light => false,
        HudTheme.Dark => true,
        _ => SystemTheme.IsDark(),
    };

    private void OnStoreChanged() => Dispatch(() =>
    {
        if (!store.IsRefreshing)
        {
            AppLog.Write(store.ErrorText is { } failure
                ? $"조회 실패: {failure}"
                : $"조회 성공: {store.SummaryText()}");
        }

        // 다음 조회 시각은 결과에 따라 달라진다(429 를 맞으면 물러난다).
        pollTimer.Interval = store.NextPollDelay();

        var session = store.Snapshot?.FiveHour?.Utilization;
        if (animator.SetMood(OwlMoodResolver.Resolve(OwlDocument.Embedded, session, store.IsDisconnected)))
        {
            StartFrameTimer();
        }

        RefreshHud();
        tray?.UpdateSummary(store.SummaryText(), store.NeedsReauth);
        settingsWindow?.Refresh();
    });

    private void RefreshHud()
    {
        if (hud is null) return;

        hud.View.Snapshot = store.Snapshot;
        hud.View.IsDisconnected = store.IsDisconnected;
        hud.View.OwlGrid = animator.CurrentGrid;
        hud.View.OwlPaletteName = animator.Animation.Palette;
        hud.View.HasUpdate = updates.HasUpdate;
        hud.Refresh();

        tray?.UpdateOwl(animator.CurrentGrid, animator.CurrentPalette);
    }

    /// <summary>
    /// 다음 프레임까지 타이머를 건다.
    ///
    /// **반복 타이머를 걸지 않는다.** 프레임마다 보여줄 시간이 다르고(눈 깜빡임은 0.05초,
    /// 평소 자세는 2초) 흔들림도 붙어서, 한 박자로 돌리면 애니메이션이 어긋난다.
    /// 프레임이 하나뿐인 기분(끊김)에서는 아예 걸지 않는다.
    /// </summary>
    private void StartFrameTimer()
    {
        frameTimer.Stop();
        if (animator.CurrentDelay() is not { } delay) return;

        frameTimer.Interval = delay;
        frameTimer.Start();
    }

    private void AdvanceFrame()
    {
        frameTimer.Stop();
        if (animator.Advance() is not { } delay) return;

        RefreshHud();
        frameTimer.Interval = delay;
        frameTimer.Start();
    }

    private void ToggleHudVisible()
    {
        settings.IsHudVisible = !settings.IsHudVisible;
        settings.Save();
        ApplySettings();
    }

    private void OpenSettings(string? tab = null)
    {
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(settings, store, updates, ApplySettings);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show();
        }
        else
        {
            settingsWindow.Activate();
        }

        if (tab is not null) settingsWindow.SelectTab(tab);
    }

    /// <summary>업데이트 직전 정리. 창을 닫고 트레이 아이콘을 내린다.</summary>
    private void ReleaseForUpdate()
    {
        AppLog.Write("업데이트를 위해 창과 트레이를 정리한다");
        pollTimer.Stop();
        frameTimer.Stop();
        updateTimer.Stop();

        settingsWindow?.Close();
        settingsWindow = null;
        hud?.SavePosition();
        hud?.Close();
        hud = null;

        tray?.Dispose();
        tray = null;
    }

    private void Quit()
    {
        hud?.SavePosition();
        settings.Save();
        Application.Current.Shutdown();
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        pollTimer.Stop();
        frameTimer.Stop();
        updateTimer.Stop();
        tray?.Dispose();
        http.Dispose();
    }
}

/// <summary>윈도우가 어두운 테마인지. 레지스트리에 있다.</summary>
public static class SystemTheme
{
    public static bool IsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // 0 이 어두움이다. 값이 없으면(옛 윈도우) 밝은 쪽으로 본다.
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}
