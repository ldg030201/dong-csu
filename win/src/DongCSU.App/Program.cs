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
using Microsoft.Win32;
using Velopack;

namespace DongCSU.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // **무엇이든 읽기 전에 폴더부터 정한다.** 테스트판은 설정·기록·토큰이 통째로
        // 갈려야 하는데, 늦게 정하면 앞서 읽은 것이 정식판 폴더에서 온 것이 된다.
        AppPaths.UseFolder(AppInfo.Name);

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

    /// <summary>2초면 눈으로 보기 충분하고, 표본 자체는 거의 공짜다.</summary>
    private readonly DispatcherTimer statsTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly ProcessUsageSampler sampler = new(new CurrentProcessSource());

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

        // 자격 증명을 어디서 찾았고 **왜 못 읽었는지** 남긴다. "사용량이 안 나온다"의
        // 대부분이 여기서 갈린다. 있음/없음만 적어 두면 파일이 있는데 실패한 경우에
        // 사용자가 보낸 기록만으로는 원인을 짚을 수 없다.
        var source = new FileCredentialSource(fallbackPaths: WslCredentialPaths.All);
        foreach (var attempt in source.Inspect())
        {
            AppLog.Write($"자격 증명 {attempt.Path} · {attempt.Describe()}");
        }

        var credentials = new CredentialStore(source, refreshedTokens: new RefreshedTokenStore());
        if (credentials.Current() is { } credential)
        {
            // 토큰 자체는 절대 남기지 않는다. 길이와 만료 시각만으로 충분히 갈린다.
            AppLog.Write(
                $"자격 증명 읽기 성공 · 토큰 {credential.AccessToken.Length}자 · "
                + $"플랜 {credential.SubscriptionType ?? "-"} · "
                + $"만료 {credential.ExpiresAt?.ToString("u") ?? "없음"} · "
                + $"갱신용 토큰 {(credential.RefreshToken is null ? "없음" : "있음")}"
                + (credential.IsExpired(DateTimeOffset.UtcNow) ? " (지났음 — 갱신해서 조회한다)" : ""));
        }
        else
        {
            AppLog.Write("자격 증명 읽기 실패");
        }
        var api = new UsageApi(http, credentials, refresher: new OAuthTokenRefresher(http));
        store = new UsageStore(api) { PollInterval = settings.PollInterval };
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
        hud.ModeToggled += ToggleCollapsed;
        hud.PetToggled += TogglePet;
        // 우클릭은 트레이와 **같은 메뉴**를 띄운다. 설정 창이 튀어나오면 놀란다.
        hud.ContextMenuRequested += () => tray?.ShowMenuAtCursor();
        hud.SettingsRequested += () => OpenSettings();
        hud.RefreshRequested += () => _ = store.RefreshAsync(force: true);
        hud.UpdatesRequested += () => OpenSettings("version");

        // 윈도우 테마를 바꾸면 곧바로 따라간다. 안 그러면 HUD 만 옛 색으로 남는다.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        // 모니터를 빼면 기억해 둔 자리가 보이지 않는 곳이 된다.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ApplySettings();
        hud.RestorePosition();
        if (settings.IsHudVisible) hud.Show();

        pollTimer.Tick += async (_, _) => await store.RefreshAsync().ConfigureAwait(true);
        frameTimer.Tick += (_, _) => AdvanceFrame();
        statsTimer.Tick += (_, _) =>
        {
            if (hud is null) return;
            hud.View.Stats = sampler.Sample();
            hud.View.InvalidateVisual();
        };

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
        hud.View.ExpandSide = settings.ExpandSide;
        hud.View.Scale = settings.Scale.Factor();
        hud.View.BackdropOpacity = settings.Backdrop;
        hud.View.IsDark = IsDarkTheme();
        hud.View.VersionBadge = settings.ShowsVersionBadge ? AppInfo.BadgeText : null;
        hud.View.VersionBadgeIsTest = AppInfo.IsTestBuild;
        hud.View.HasUpdate = updates.HasUpdate;

        hud.View.ShowsProcessStats = settings.ShowsProcessStats;
        hud.View.IconStyle = settings.IconStyle;
        hud.View.PetRingDisplay = settings.PetRingDisplay;

        store.PollInterval = settings.PollInterval;
        pollTimer.Interval = store.NextPollDelay();
        pollTimer.Start();

        if (settings.IsHudVisible) hud.Show(); else hud.Hide();

        SyncStatsTimer();
        RefreshHud();
    }

    /// <summary>
    /// 자원 표본은 **보이고 · 펼쳐져 있고 · 켜 뒀을 때만** 뜬다.
    ///
    /// 셋 중 하나라도 아니면 아무도 그 숫자를 못 보는데, 그걸 2초마다 재고 다시
    /// 그리는 것은 "이 앱이 얼마나 먹나"를 보여주겠다는 기능으로서 앞뒤가 안 맞는다.
    /// </summary>
    private void SyncStatsTimer()
    {
        var needed = settings.ShowsProcessStats
            && settings.IsHudVisible
            && settings.Mode != HudMode.Collapsed;

        if (needed && !statsTimer.IsEnabled)
        {
            // 멈춰 둔 사이에 쌓인 CPU 시간이 한꺼번에 튀어 보이지 않게 처음부터 다시 센다.
            sampler.Reset();
            if (hud is not null) hud.View.Stats = sampler.Sample();
            statsTimer.Start();
        }
        else if (!needed && statsTimer.IsEnabled)
        {
            statsTimer.Stop();
        }
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
        hud.View.IsStale = store.IsStale;
        hud.View.NeedsReauth = store.NeedsReauth;
        hud.View.IsRefreshing = store.IsRefreshing;
        hud.View.ErrorText = store.ErrorText;
        hud.View.NextPollAt = store.NextPollAt;
        hud.View.OwlGrid = animator.CurrentGrid;
        hud.View.OwlPaletteName = MascotPalette();
        hud.View.HasUpdate = updates.HasUpdate;
        // 펫에는 숫자를 안 그린다. 마스코트에 올리면 이게 뜬다.
        hud.View.SummaryText = store.SummaryText();
        hud.Refresh();

        tray?.UpdateOwl(animator.CurrentGrid, OwlDocument.Embedded.Palettes[MascotPalette()]);
    }

    /// <summary>
    /// 마스코트를 어떤 색으로 칠할지.
    ///
    /// 테스트판은 보라로 칠해 두 판을 나란히 띄웠을 때 한눈에 갈린다. 다만 **끊김
    /// (회색)이 테스트 표시보다 세다** — 회색은 지금 값이 아니라는 뜻이라, 그것을
    /// 보라로 덮으면 낡은 숫자를 지금 값으로 믿게 된다.
    /// </summary>
    private string MascotPalette()
    {
        var name = animator.Animation.Palette;
        return AppInfo.IsTestBuild && name == "normal" ? "test" : name;
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
        // 움직이지 않게 해 뒀으면 프레임을 넘기지 않는다. 기분에 따른 색은 그대로다 —
        // 자세만 멈출 뿐 지금 상태를 못 알리게 되는 것은 아니다.
        if (!settings.AnimatesMascot) return;
        // **정지 그림을 골라 뒀으면 아예 걸지 않는다.** 넘길 프레임이 없는데 타이머만
        // 돌면 보이지도 않는 그림을 계속 다시 그린다.
        if (!settings.IconStyle.IsAnimated()) return;
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

    /// <summary>
    /// 시스템 테마가 바뀌었다.
    ///
    /// 레지스트리 값이 실제로 바뀐 뒤에 알림이 오지만, 곧바로 읽으면 옛 값이 잡히는
    /// 경우가 있다. 한 박자 미뤄서 읽는다 — 맥판도 같은 이유로 미룬다.
    /// </summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (settings.Theme != HudTheme.System) return;

        Dispatch(() => Application.Current?.Dispatcher.BeginInvoke(ApplySettings));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatch(() =>
    {
        if (hud?.ClampIntoScreen() == true) AppLog.Write("화면 구성이 바뀌어 HUD 를 안으로 옮겼다");
    });

    /// <summary>HUD 를 주 모니터 오른쪽 위로. 설정 창의 "위치 초기화" 가 부른다.</summary>
    private void ResetHudPosition() => hud?.ResetPosition();

    /// <summary>
    /// 접었다 폈다.
    ///
    /// **펫에서는 접기가 아니라 나가기다.** 셋을 한 줄로 순환시키지 않는다 — 접으려다
    /// 펫으로 넘어가면 이 동작이 무엇을 할지 예측할 수 없어진다.
    /// </summary>
    private void ToggleCollapsed()
    {
        settings.Mode = settings.Mode switch
        {
            HudMode.Pet => settings.ModeBeforePet,
            HudMode.Collapsed => HudMode.Expanded,
            _ => HudMode.Collapsed,
        };
        settings.Save();
        ApplySettings();
    }

    /// <summary>
    /// 펫 모드를 드나든다.
    ///
    /// 나갈 때는 **들어오기 전 보기**로 돌아간다. 접어 둔 채로 펫에 들렀다 나왔는데
    /// 펼쳐져 있으면, 사용자가 해 둔 것을 앱이 되돌린 셈이 된다.
    /// </summary>
    private void TogglePet()
    {
        if (settings.Mode == HudMode.Pet)
        {
            settings.Mode = settings.ModeBeforePet;
        }
        else
        {
            settings.ModeBeforePet = settings.Mode;
            settings.Mode = HudMode.Pet;
        }
        settings.Save();
        ApplySettings();
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
            settingsWindow = new SettingsWindow(settings, store, updates, ApplySettings, ResetHudPosition, TogglePet);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }

        if (tab is not null) settingsWindow.SelectTab(tab);

        // **처음 열든 이미 열려 있든 앞으로 끌어낸다.** HUD 가 포커스를 안 받는 창이라
        // 그냥 Show 만 하면 다른 창 뒤에 깔려서, 사용자 눈에는 안 열린 것으로 보인다.
        settingsWindow.BringToFront();
    }

    /// <summary>업데이트 직전 정리. 창을 닫고 트레이 아이콘을 내린다.</summary>
    private void ReleaseForUpdate()
    {
        AppLog.Write("업데이트를 위해 창과 트레이를 정리한다");
        pollTimer.Stop();
        frameTimer.Stop();
        updateTimer.Stop();
        statsTimer.Stop();

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
        // 전역 이벤트라 끊지 않으면 앱이 끝난 뒤에도 이 객체가 잡혀 있는다.
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        pollTimer.Stop();
        frameTimer.Stop();
        updateTimer.Stop();
        statsTimer.Stop();
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
