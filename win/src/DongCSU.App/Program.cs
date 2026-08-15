using System.ComponentModel;
using System.Diagnostics;
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
using DongCSU.Core.Pet;
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

    /// <summary>
    /// 펫이 걷는 타이머. **반복이 아니라 한 번씩** 건다 — 엔진이 다음에 언제 깨워
    /// 달라고 알려 주고(쉴 때는 몇 초, 걸을 때는 0.1초), 깨울 것이 없으면 아예 안 건다.
    /// </summary>
    private readonly DispatcherTimer motionTimer = new();

    /// <summary>
    /// 커서를 피할지 보는 타이머. **걷기 타이머와 따로 돈다.**
    ///
    /// 걷기 타이머는 쉴 때 3~11초를 통째로 잔다. 거기에 얹으면 그동안 커서가 위에
    /// 올라와 있어도 아무 일이 없어서, **한 번 비킨 뒤로는 안 비키는 것처럼 보인다.**
    /// 맥도 회피는 제 타이머로 따로 건다.
    /// </summary>
    private readonly DispatcherTimer dodgeTimer = new()
    {
        Interval = DodgeWatchFar,
    };

    /// <summary>커서가 펫 근처에 있을 때. 0.5초 머무름을 재려면 이만큼은 촘촘해야 한다.</summary>
    private static readonly TimeSpan DodgeWatchClose = TimeSpan.FromMilliseconds(100);

    /// <summary>멀리 있을 때. 다가오는 것만 알아채면 되므로 드문드문 봐도 된다.</summary>
    private static readonly TimeSpan DodgeWatchFar = TimeSpan.FromMilliseconds(400);

    private readonly PetMotion motion = new();
    private PetStage? stage;
    private readonly PetHoverTracker hover = new();

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
            // 재로그인을 어디서 해야 하는지가 여기서 갈린다. WSL 안에서 쓰던 사람은
            // 윈도우 쪽에서 로그인해 봐야 우리가 읽는 파일이 안 바뀐다.
            if (attempt.Found) credentialPath = attempt.Path;
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
        tray.LoginRequested += StartLogin;
        tray.QuitRequested += Quit;
        tray.Activated += ToggleHudVisible;

        hud = new HudWindow(settings);
        stage = new PetStage(hud);
        hud.ModeToggled += ToggleCollapsed;
        hud.PetToggled += TogglePet;
        // 손에 잡히면 멈추고, 놓으면 다시 걷는다. 잡혀 있는 동안은 끌리는 자세다.
        hud.HeldChanged += OnHeldChanged;
        // 크기를 옮기는 동안에는 걸음을 멈춰 뒀다. 끝나면 다시 켠다.
        hud.Settled += () => SyncMotion();
        hud.DizzyStarted += OnDizzyStarted;
        // 끌리는 자세는 프레임을 돌리는 게 아니라 **끄는 속도**로 만든다.
        hud.DragMoved += v => animator.SetDragVelocity(v.X, v.Y, DateTimeOffset.UtcNow);
        // 우클릭은 트레이와 **같은 메뉴**를 띄운다. 설정 창이 튀어나오면 놀란다.
        hud.ContextMenuRequested += () => tray?.ShowMenuAtCursor();
        hud.SettingsRequested += () => OpenSettings();
        hud.RefreshRequested += () => _ = store.RefreshAsync(force: true);
        hud.FetchCooldownWanted += () =>
            hud.View.FetchCooldownSeconds = (int)Math.Ceiling(store.FetchCooldown().TotalSeconds);
        hud.UpdatesRequested += () => OpenSettings("version");

        // 윈도우 테마를 바꾸면 곧바로 따라간다. 안 그러면 HUD 만 옛 색으로 남는다.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        // 모니터를 빼면 기억해 둔 자리가 보이지 않는 곳이 된다.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        // 잠그거나 사용자를 바꾸면 아무도 안 본다. 그동안 움직임을 멈춘다.
        SystemEvents.SessionSwitch += OnSessionSwitch;

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
        motionTimer.Tick += (_, _) => OnMotionTick();
        dodgeTimer.Tick += (_, _) => OnDodgeTick();
        dizzyTimer.Tick += (_, _) =>
        {
            dizzyTimer.Stop();
            animator.IsDizzy = false;
            StartFrameTimer();
            RefreshHud();
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

        hud.View.ExpandSide = settings.ExpandSide;
        hud.ExpandsLeft = settings.ExpandSide == HudExpandSide.Left;
        hud.View.Scale = settings.Scale.Factor();
        hud.View.ShowsProcessStats = settings.ShowsProcessStats;
        // **배율과 자원 줄을 정한 뒤에 부른다** — 그래야 옮겨갈 크기를 제대로 잰다.
        // 크기가 달라지는 보기 갈아타기는 창이 애니메이션으로 옮긴다.
        hud.SetMode(settings.Mode);
        hud.View.BackdropOpacity = settings.Backdrop;
        hud.View.IsDark = IsDarkTheme();
        // 트레이 메뉴와 HUD 우클릭 메뉴는 같은 것이다. 셋이 나란히 떠 있으므로 색을 맞춘다.
        tray?.ApplyTheme(IsDarkTheme());
        hud.View.VersionBadge = settings.ShowsVersionBadge ? AppInfo.BadgeText : null;
        hud.View.VersionBadgeIsTest = AppInfo.IsTestBuild;
        hud.View.HasUpdate = updates.HasUpdate;

        hud.View.IconStyle = settings.IconStyle;
        hud.View.PetRingDisplay = settings.PetRingDisplay;
        hud.View.HidesPetRingWhileHeld = settings.PetHidesRingWhileHeld;

        store.PollInterval = settings.PollInterval;
        pollTimer.Interval = store.NextPollDelay();
        pollTimer.Start();

        if (settings.IsHudVisible) hud.Show(); else hud.Hide();

        SyncStatsTimer();
        SyncMotion();
        // 숨겼다 켰거나, 움직임·아이콘 설정이 바뀌었을 수 있다. 프레임 타이머를 다시 잡는다.
        StartFrameTimer();
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
        // **자원 줄은 펼침에만 그려진다.** 접힘·펫에서는 `OnRender` 가 그 자리에 닿지도
        // 않는데, `Mode != Collapsed` 로 두면 펫에서 2초마다 프로세스를 재고 다시 그린다.
        var needed = settings.ShowsProcessStats
            && settings.IsHudVisible
            && settings.Mode == HudMode.Expanded;

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

    private bool IsDarkTheme() => SystemTheme.IsDark(settings.Theme);

    private void OnStoreChanged() => Dispatch(() =>
    {
        // 한 번만 만들어 기록·트레이·펫 툴팁이 나눠 쓴다.
        summary = store.SummaryText();

        if (!store.IsRefreshing)
        {
            AppLog.Write(store.ErrorText is { } failure ? $"조회 실패: {failure}" : $"조회 성공: {summary}");
        }

        // 다음 조회 시각은 결과에 따라 달라진다(429 를 맞으면 물러난다).
        pollTimer.Interval = store.NextPollDelay();

        var session = store.Snapshot?.FiveHour?.Utilization;
        var mood = OwlMoodResolver.Resolve(
            OwlDocument.Embedded, session, store.IsDisconnected, store.IsWeeklySpent);
        // 주간을 다 썼으면 자세는 탈진 그대로 두고 색을 뺀다. 링·숫자와 같은 규칙이다.
        animator.IsUnusable = store.IsWeeklySpent;
        if (animator.SetMood(mood))
        {
            StartFrameTimer();
        }

        // 끊겼다 돌아왔는지에 따라 걸음을 멈추거나 다시 켠다.
        SyncMotion();
        RefreshHud();
        tray?.UpdateSummary(summary, store.NeedsReauth);
        settingsWindow?.Refresh();
    });

    private void RefreshHud()
    {
        if (hud is null) return;

        hud.View.Snapshot = store.Snapshot;
        hud.View.IsDisconnected = store.IsDisconnected;
        hud.View.IsWeeklySpent = store.IsWeeklySpent;
        hud.View.IsStale = store.IsStale;
        hud.View.NeedsReauth = store.NeedsReauth;
        hud.View.IsRefreshing = store.IsRefreshing;
        hud.View.ErrorText = store.ErrorText;
        hud.View.NextPollAt = store.NextPollAt;
        hud.View.HasUpdate = updates.HasUpdate;
        // 펫에는 숫자를 안 그린다. 마스코트에 올리면 이게 뜬다.
        //
        // **여기서 만들지 않는다.** 프레임을 넘길 때마다 불리는데(끌리는 동안 초당 11번)
        // 그때마다 목록 하나에 문자열 여덟 개를 새로 만든다. 값은 조회가 바뀔 때만 달라진다.
        hud.View.SummaryText = summary;

        RefreshMascot();
        hud.Refresh();
    }

    /// <summary>
    /// 마스코트 그림만 갈아 끼운다. **프레임을 넘길 때는 이것만 하면 된다.**
    ///
    /// 나머지 열두 개는 조회 결과가 바뀔 때만 달라지는데, 프레임 경로에서 같이 넣으면
    /// 걷는 동안 초당 네 번, 끌리는 동안 열한 번 헛일을 한다.
    /// </summary>
    private void RefreshMascot()
    {
        if (hud is null) return;

        var grid = animator.CurrentGrid;
        var palette = MascotPalette();

        hud.View.OwlGrid = grid;
        hud.View.OwlPaletteName = palette;
        hud.View.MascotFrame = animator.MascotFrame;
        hud.View.MascotFlipped = animator.SpriteFlipped;
        tray?.UpdateOwl(grid, OwlDocument.Embedded.Palettes[palette]);
    }

    /// <summary>메뉴와 펫 툴팁에 쓰는 한 줄. 조회가 바뀔 때만 다시 만든다.</summary>
    private string summary = "";

    /// <summary>자격 증명을 실제로 읽어 온 자리. 못 읽었으면 null. 재로그인 통로가 본다.</summary>
    private string? credentialPath;

    /// <summary>
    /// Claude Code 로그인 창을 띄운다.
    ///
    /// **앱 안에서 처리하지 않는다.** 대화형이고 브라우저까지 오가는 흐름이라 콘솔에
    /// 넘긴다 — 맥이 터미널에 `.command` 를 던지는 것과 같은 자리다. 우리는 자격 증명
    /// 파일을 **읽기만** 하므로, 그 파일을 쓰는 것은 Claude Code 쪽 일이다.
    /// </summary>
    private void StartLogin()
    {
        var insideWsl = ClaudeCli.IsInsideWsl(credentialPath);
        var executable = ClaudeCli.Resolve(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            File.Exists);

        if (ClaudeCli.LoginCommand(executable, insideWsl) is not { } command)
        {
            AppLog.Write("재로그인: claude 실행 파일을 찾지 못했다");
            MessageBox.Show(
                "Claude Code 실행 파일을 찾지 못했습니다.\n\n"
                + "터미널에서 직접 claude auth login 을 실행해 주세요.",
                $"{AppInfo.Name} 재로그인",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(command.File, command.Arguments) { UseShellExecute = true });
            AppLog.Write($"재로그인 창을 띄웠다{(insideWsl ? " (WSL)" : "")}");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            AppLog.Write($"재로그인 창을 띄우지 못했다: {error.Message}");
            return;
        }

        // 로그인이 끝나면 새 토큰이 파일에 적힌다. 잠시 뒤 한 번 더 조회한다.
        // **끝났는지 지켜볼 방법이 없다** — 콘솔이 꺼져도 로그인은 브라우저에서
        // 이어지므로 프로세스가 끝나는 것은 신호가 못 된다.
        var wait = new DispatcherTimer { Interval = ClaudeCli.RetryAfterLogin };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            _ = store.RefreshAsync(force: true);
        };
        wait.Start();
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
        var name = animator.PaletteName;
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
        // **아무도 안 보고 있으면 넘기지 않는다.** 애니메이션은 사용량 조회보다 훨씬 자주
        // 깨어나서, 숨겨 뒀거나 화면이 잠긴 동안 계속 돌면 배터리만 먹는다.
        if (screensAsleep || !settings.IsHudVisible) return;
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

        // **그림만 바뀐다.** 사용량·오류·다음 조회는 그대로이므로 다시 넣지 않는다.
        RefreshMascot();
        hud?.View.InvalidateVisual();

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

    // ── 펫이 스스로 움직이는 것 ─────────────────────────────────────

    /// <summary>
    /// 지금 펫을 움직여도 되는지.
    ///
    /// 하나라도 아니면 타이머를 끈다. 보이지도 않는 것을 0.1초마다 옮기는 것은
    /// 배터리만 먹는다.
    /// </summary>
    private bool ShouldMove =>
        hud is { } window
        && settings.IsHudVisible
        && settings.Mode == HudMode.Pet
        && !window.IsHeld
        && !window.IsResizing
        && !screensAsleep
        // 조회가 끊긴 동안에는 멈춰 있는다. 회색으로 굳은 채 걸어다니면
        // "멈췄다"는 표시가 무색해진다.
        && !store.IsDisconnected
        // **주간을 다 썼을 때도 같다.** 그때는 아예 죽은 것으로 다루므로 스스로 걷지도,
        // 커서를 피하지도 않는다. 색만 빼고 계속 돌아다니면 살아 있는 것으로 보인다.
        && !store.IsWeeklySpent
        && (settings.PetWanders || settings.PetDodgesCursor);

    /// <summary>잡혔다 놓였다. 자세를 바꾸고 움직임을 멈췄다 다시 켠다.</summary>
    private void OnHeldChanged()
    {
        if (hud is { } window)
        {
            animator.IsDragged = window.IsHeld;
            animator.IsDizzy = window.Shake.IsDizzy;
            StartFrameTimer();
            RefreshHud();
        }
        SyncMotion();
    }

    /// <summary>
    /// 흔들어서 어지러워졌다. **놓을 때까지 기다리지 않는다** — 흔드는 그 자리에서
    /// 바로 눈이 풀려야 흔든 보람이 있다.
    ///
    /// 손에 들려 있는 동안에는 몸이 매달린 자세 그대로고 눈만 바뀐다. 통째로 비틀거리는
    /// 그림으로 갈아타면 허공에서 휘청이는 꼴이라 무엇이 흔들리는 건지 알 수 없다.
    /// </summary>
    private void OnDizzyStarted() => Dispatch(() =>
    {
        dizzyTimer.Stop();
        dizzyTimer.Interval = PetShake.DizzyDuration;
        dizzyTimer.Start();

        animator.IsDizzy = true;
        StartFrameTimer();
        RefreshHud();
    });

    private readonly DispatcherTimer dizzyTimer = new();

    private void SyncMotion()
    {
        if (!ShouldMove)
        {
            motionTimer.Stop();
            dodgeTimer.Stop();
            hover.Reset();
            // **걸음을 멈추면 자세도 되돌린다.** 안 그러면 걷다가 펫에서 나갔을 때
            // 카드 안의 부엉이가 영영 걷는다.
            motion.Halt();
            ApplyGait(motion.Gait);
            return;
        }

        motion.Wanders = settings.PetWanders;
        motion.DodgesCursor = settings.PetDodgesCursor;
        // 배회를 끄면 걷던 것이 그 자리에 서므로 자세를 바로 맞춘다.
        ApplyGait(motion.Gait);

        if (settings.PetDodgesCursor) dodgeTimer.Start(); else { dodgeTimer.Stop(); hover.Reset(); }

        if (!motionTimer.IsEnabled)
        {
            motion.Reset();
            ScheduleMotion(PetMotion.TickInterval);
        }
    }

    /// <summary>
    /// 커서가 위에 머무는지 보고, 머물면 비켜선다.
    ///
    /// **판단은 좌표로 한다** — 비켜선 뒤 커서가 그 자리에 그대로 있으면 WPF 마우스
    /// 이벤트가 다시 오지 않아서, 호버 상태에 기대면 한 번 비키고 굳는다.
    /// </summary>
    private void OnDodgeTick()
    {
        if (hud is not { } window || stage is null || !ShouldMove || !settings.PetDodgesCursor)
        {
            dodgeTimer.Stop();
            hover.Reset();
            return;
        }

        var cursor = stage.Cursor;

        // **커서가 멀면 느리게 본다.** 펫 모드를 켜 둔 내내 0.1초마다 깨우면, 커서가
        // 다른 모니터에 있어도 하루 86만 번을 헛돈다. 가까이 오면 그때 촘촘히 본다 —
        // 0.5초를 세려면 그 정도는 필요하다.
        var near = window.CursorIsNear(cursor);
        var wanted = near ? DodgeWatchClose : DodgeWatchFar;
        if (dodgeTimer.Interval != wanted) dodgeTimer.Interval = wanted;

        if (!near)
        {
            hover.Reset();
            return;
        }

        stage.SinceLastKey = window.SinceLastKey;

        var now = DateTimeOffset.UtcNow;
        if (!hover.Update(now, window.CursorWantsDodge(cursor))) return;

        // **비키지 못했어도 다시 센다.** 글을 쓰는 중이거나 이미 비키는 중이면 실패하는데,
        // 그때 그냥 두면 커서가 그대로 있는 동안 영영 다시 시도하지 않는다.
        if (motion.RequestDodge(stage)) ScheduleMotion(PetMotion.TickInterval);
        hover.Restart(now);
    }

    private void ScheduleMotion(TimeSpan? delay)
    {
        motionTimer.Stop();
        if (delay is not { } wait) return;

        motionTimer.Interval = wait < TimeSpan.FromMilliseconds(16) ? TimeSpan.FromMilliseconds(16) : wait;
        motionTimer.Start();
    }

    private void OnMotionTick()
    {
        motionTimer.Stop();
        if (hud is not { } window || stage is null || !ShouldMove) return;

        // 글을 쓰는 동안에는 새로 걷지 않는다. 커서 피하기는 dodgeTimer 가 따로 본다.
        stage.SinceLastKey = window.SinceLastKey;

        var tick = motion.Tick(stage);

        if (tick.MoveTo is { } to)
        {
            window.Left = to.X;
            window.Top = to.Y;
        }

        // **도착했을 때만 저장한다.** 매 틱 부르면 초당 열 번 설정 파일을 다시 쓴다.
        if (tick.Settled) window.SavePosition();

        ApplyGait(tick.Gait, tick.FacingRight);

        ScheduleMotion(tick.NextWakeup);
    }

    /// <summary>걸음이 바뀌면 자세를 갈아 끼운다. 걸음을 켜고 끄는 곳은 여기 하나뿐이다.</summary>
    /// <param name="facingRight">보는 쪽. null 이면 보던 쪽 그대로다.</param>
    private void ApplyGait(PetGait? gait, bool? facingRight = null)
    {
        // **보는 쪽은 걸음이 그대로여도 바뀐다.** 걷는 도중에 방향을 틀 때가 그렇다.
        var turned = animator.SetFacing(facingRight);

        if (gait == lastGait)
        {
            // 박자는 건드리지 않는다. 프레임 타이머를 다시 걸면 걷다가 발이 멈칫한다.
            if (turned) RefreshHud();
            return;
        }

        lastGait = gait;
        animator.SetGait(gait);
        StartFrameTimer();
        RefreshHud();
    }

    private PetGait? lastGait;

    /// <summary>
    /// 아무도 화면을 안 보고 있는 상태(잠금·사용자 전환).
    ///
    /// 그동안 펫을 움직이지 않는다. 애니메이션은 사용량 조회보다 훨씬 자주 깨어나므로,
    /// 보이지도 않는 그림을 0.1초마다 옮기는 것은 배터리만 먹는다.
    /// </summary>
    private bool screensAsleep;

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var asleep = e.Reason is SessionSwitchReason.SessionLock
            or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect;

        // 잠금 해제·연결은 되돌린다.
        var awake = e.Reason is SessionSwitchReason.SessionUnlock
            or SessionSwitchReason.ConsoleConnect
            or SessionSwitchReason.RemoteConnect;

        if (!asleep && !awake) return;

        Dispatch(() =>
        {
            screensAsleep = asleep;
            AppLog.Write(asleep ? "화면이 잠겨 펫을 멈춘다" : "화면이 돌아와 펫을 다시 움직인다");

            // 멈추기 전에 지금 자리를 남긴다.
            if (asleep) hud?.SavePosition();
            SyncMotion();
            StartFrameTimer();
        });
    }

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
            settingsWindow = new SettingsWindow(
                settings, store, updates, ApplySettings, ResetHudPosition, TogglePet, StartLogin);
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
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        pollTimer.Stop();
        frameTimer.Stop();
        updateTimer.Stop();
        statsTimer.Stop();
        motionTimer.Stop();
        dodgeTimer.Stop();
        tray?.Dispose();
        http.Dispose();
    }
}

/// <summary>윈도우가 어두운 테마인지. 레지스트리에 있다.</summary>
public static class SystemTheme
{
    /// <summary>
    /// 설정을 실제 밝기로 푼다.
    ///
    /// **한 곳에서만 푼다.** HUD 와 설정 창이 각자 풀면 항목이 하나 늘 때 한쪽만 고치기
    /// 쉽고, 그러면 두 창이 서로 다른 테마로 뜬다.
    /// </summary>
    public static bool IsDark(HudTheme theme) => theme switch
    {
        HudTheme.Light => false,
        HudTheme.Dark => true,
        _ => IsDark(),
    };

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
