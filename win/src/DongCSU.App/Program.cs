using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DongCSU.App.Hud;
using DongCSU.App.Rendering;
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

        // **어디서 죽든 한 줄은 남긴다.** 이것이 없으면 예외가 나도 윈도우 기본 오류
        // 상자만 뜨고 기록은 통째로 비어서, "오류가 떴다" 는 말을 들어도 무엇이 어디서
        // 났는지 짚을 자리가 없다. 실제로 그래서 못 짚었다.
        //
        // **삼키지 않는다.** 여기서 막으면 깨진 채로 계속 도는데, 그건 죽는 것보다
        // 나쁘다 — 틀어진 값이 그대로 설정과 기록에 저장된다. 남기기만 하고 흘려보낸다.
        AppLog.Start();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write(
            $"처리 못 한 예외 (끝남={e.IsTerminating}): {Describe(e.ExceptionObject as Exception)}");
        TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Write(
            $"돌보지 않은 작업 예외: {Describe(e.Exception)}");

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException += (_, e) => AppLog.Write(
            $"화면 쪽에서 처리 못 한 예외: {Describe(e.Exception)}");

        var controller = new AppController();
        application.Startup += (_, _) => controller.Start();
        application.Exit += (_, _) => controller.Dispose();
        return application.Run();
    }

    /// <summary>
    /// 기록에 남길 예외 한 덩어리.
    ///
    /// <b>토큰도 자격 증명도 여기 오지 않는다</b> — 갈래 · 메시지 · 호출 자리뿐이고,
    /// 그 셋이면 어디서 났는지 짚기에 넉넉하다.
    /// </summary>
    private static string Describe(Exception? error) => error is null
        ? "(무엇인지 모름)"
        : $"{error.GetType().Name}: {error.Message}{Environment.NewLine}{error.StackTrace}";
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

    /// <summary>
    /// 사용량 측정. **앱 수명만큼 산다** — 설정 창은 열고 닫을 때마다 버려지지만
    /// 재던 것은 창을 닫아도 계속돼야 한다.
    /// </summary>
    private readonly UsageMeter meter = new(new MeterStore());

    /// <summary>
    /// 측정이 토큰 기록을 다시 훑는 주기(60초).
    ///
    /// **<c>Core</c> 에는 타이머를 두지 않았다.** 화면 없는 쪽이 <c>DispatcherTimer</c> 를
    /// 들면 맥에서 컴파일되지 않고, 검사도 시간을 밀 수 없다. 다른 주기들과 마찬가지로
    /// 여기서 걸고 <see cref="SyncScanTimer"/> 가 켰다 껐다 한다.
    /// </summary>
    private readonly DispatcherTimer scanTimer = new() { Interval = UsageMeter.ScanInterval };

    /// <summary>
    /// 직전에 본 "하루에 한 번 확인" 설정. **꺼짐 → 켜짐 전이만 잡으려고 들고 있는다.**
    ///
    /// 설정이 바뀔 때마다 확인을 내보내면 설정 창에서 슬라이더 하나만 움직여도 네트워크가
    /// 나간다. 앱을 띄우면서 처음 값을 심어 둬야 뜨자마자 전이로 오인하지 않는다.
    /// </summary>
    private bool checkedForUpdatesWas;

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

        // **재는 동안 따로 조회하지 않는다.** 평소 조회가 들어올 때마다 측정이 그 표본으로
        // 한도 소모를 쌓는다 — 측정이 제 주기로 또 물으면 요청이 두 배가 되고 429 를 부른다.
        store.SnapshotReceived += meter.Record;
        // 시작·계속·중지를 누른 그 자리에서 기준점을 잡아야 한다. 다음 조회까지 기다리면
        // 그 사이에 쓴 것은 기준이 없어서 못 센다.
        //
        // **force 를 주지 않는다** — force 는 429 백오프를 무시해서 요청 제한을 더 부른다.
        // 30초 바닥은 `UsageMeter` 쪽이 이미 지킨다.
        meter.SampleWanted += () => _ = store.RefreshAsync();
        meter.Changed += OnMeterChanged;

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
        // 메뉴가 떠 있는 동안에는 펫이 멈춰 있어야 한다. 제 메뉴를 두고 걸어나가면
        // 메뉴가 가리키던 것이 어디 것인지 알 수 없어진다.
        tray.MenuOpenChanged += open => hud?.SetMenuOpen(open);

        hud = new HudWindow(settings);
        stage = new PetStage(hud);
        hud.ModeToggled += ToggleCollapsed;
        hud.PetToggled += TogglePet;
        // 손에 잡히면 멈추고, 놓으면 다시 걷는다. 잡혀 있는 동안은 끌리는 자세다.
        hud.HeldChanged += OnHeldChanged;
        // 끌어다 놓았다. **붙을 자리가 있으면 여기서 붙는다** — 놓기 전에 자세를 미리
        // 잡아 두는 것은 `OnDragging` 이 한다.
        hud.Dropped += OnDropped;
        // 끄는 동안 "놓으면 여기 걸린다" 를 보여준다.
        hud.Dragging += OnDragging;
        // 크기를 옮기는 동안에는 걸음을 멈춰 뒀다. 끝나면 다시 켠다.
        hud.Settled += () => SyncMotion();
        hud.DizzyStarted += OnDizzyStarted;
        // 끌리는 자세는 프레임을 돌리는 게 아니라 **끄는 속도**로 만든다.
        hud.DragMoved += v => animator.SetDragVelocity(v.X, v.Y, DateTimeOffset.UtcNow);
        // 우클릭은 트레이와 **같은 메뉴**를 띄운다. 설정 창이 튀어나오면 놀란다.
        hud.ContextMenuRequested += () => tray?.ShowMenuAtCursor();
        hud.SettingsRequested += () => OpenSettings();
        // **재기를 시작하지 않는다. 측정 화면을 열기만 한다.**
        //
        // HUD 는 손이 스치는 자리다. 여기서 바로 재기 시작하면 재던 것이 끊기고, 눌러서
        // 시작됐다 한들 카드 위에는 그 사실이 거의 안 보인다. 시작·일시정지·중지는
        // 값이 보이는 자리에서 누르게 한다 — 맥의 `handleOpenMeasure` 와 같은 자리다.
        hud.MeasureRequested += () => OpenSettings("measure");
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

        // **처음 값을 `ApplySettings()` 보다 먼저 심는다.** 아래 첫 확인을 거는 자리보다
        // 이게 먼저 불려서, 안 심으면 앱이 뜨자마자 꺼짐 → 켜짐 전이로 오인해 확인이
        // 두 번 나간다.
        checkedForUpdatesWas = settings.ChecksForUpdates;
        // 같은 이유로 여기도 처음 값을 심는다 — 안 심으면 첫 알림을 전이로 오인한다.
        measuringWas = meter.IsRunning;

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
        // 겹쳐 돌지 않고 예외도 삼키므로 기다리지 않고 던져 둔다.
        scanTimer.Tick += (_, _) => _ = meter.ScanTokensAsync();
        dizzyTimer.Tick += (_, _) =>
        {
            dizzyTimer.Stop();
            animator.IsDizzy = false;
            StartFrameTimer();
            RefreshHud();
            // **여기를 빠뜨리면 흔든 뒤로 영영 안 걷는다.** 어지러운 동안 걷기 타이머가
            // null 깨우기로 멎어 있어서, 풀렸다고 알려 주지 않으면 아무도 다시 걸지 않는다.
            SyncMotion();
        };

        updateTimer.Interval = UpdateService.CheckInterval;
        updateTimer.Tick += async (_, _) =>
        {
            if (settings.ChecksForUpdates) await updates.CheckAsync().ConfigureAwait(true);
        };
        updateTimer.Start();

        _ = store.RefreshAsync(force: true);
        if (settings.ChecksForUpdates) _ = updates.CheckAsync();

        // **끄고 있던 동안 쌓인 것을 뜨자마자 한 번 얹는다.** 안 그러면 다시 켠 뒤
        // 첫 훑기까지 1분 동안 토큰 칸이 빈 채로 서 있는다.
        if (meter.WantsScanning) _ = meter.ScanTokensAsync();
        SyncScanTimer();

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
        hud.View.ShowsScopedLimit = settings.ShowsScopedLimit;
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
        // 앱이 뜰 때 이미 재는 중일 수 있다 — 측정은 파일에 남아서 껐다 켜도 이어진다.
        hud.View.IsMeasuring = meter.IsRunning;

        hud.View.IconStyle = settings.IconStyle;
        hud.View.PetRingDisplay = settings.PetRingDisplay;
        hud.View.HidesPetRingWhileHeld = settings.PetHidesRingWhileHeld;

        store.PollInterval = settings.PollInterval;
        pollTimer.Interval = store.NextPollDelay();
        pollTimer.Start();

        // **자동 확인을 켜면 그 자리에서 한 번 확인한다.** 안 그러면 켜 놓고도 아무 일이
        // 없어서 "업데이트 확인"을 따로 눌러야 한다 — 맥의 `UpdateChecker.start()` 도
        // 타이머를 걸기 전에 한 번 부른다.
        //
        // **전이일 때만 부른다.** 매번 부르면 설정 창에서 무엇을 만질 때마다 확인이
        // 나간다(껐다 켜기를 연타해도 `UpdateService.CanCheckNow` 의 10초 바닥이 막는다).
        // 타이머를 껐다 켜는 것은 하루 주기를 **켠 시점부터** 다시 세기 위해서다 —
        // 그냥 두면 켜자마자 한 번 확인해 놓고 남은 주기가 끝나 또 확인한다.
        // 테스트판은 `CheckAsync` 초입의 가드가 되돌리므로 여기서 또 보지 않는다.
        if (settings.ChecksForUpdates && !checkedForUpdatesWas)
        {
            updateTimer.Stop();
            updateTimer.Start();
            _ = updates.CheckAsync();
        }
        // 끄는 쪽은 다음 Tick 이 `settings.ChecksForUpdates` 로 걸러 낸다.
        checkedForUpdatesWas = settings.ChecksForUpdates;

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
        //
        // **규칙은 `HudView.Draws` 한 곳에서 나온다.** 여기에 `Mode == Expanded` 를
        // 따로 적어 두면, 나중에 접은 카드에도 자원 줄을 붙이기로 했을 때 화면에는
        // 나오는데 표본 타이머가 안 돌아 영영 `--` 로 남는다.
        var needed = settings.ShowsProcessStats
            && settings.IsHudVisible
            && HudView.Draws(HudElement.ProcessStats, settings.Mode);

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

    /// <summary>
    /// 측정 값이 바뀌었다.
    ///
    /// **UI 스레드가 아닐 수 있다** — 토큰 훑기가 스레드풀에서 돌다가 여기로 알린다.
    /// </summary>
    private void OnMeterChanged() => Dispatch(() =>
    {
        // 시작·중지·일시정지에서 훑기 주기가 켜졌다 꺼진다.
        SyncScanTimer();

        // **재는 중인지가 바뀔 때만 HUD 를 다시 그린다.** 측정은 표본을 받을 때마다,
        // 토큰을 셀 때마다 알리는데 HUD 에서 달라지는 것은 측정 버튼 색 하나뿐이다.
        // 그때마다 링·부엉이·버튼을 통째로 다시 그리면 재는 내내 헛일이다 —
        // 맥이 `wasMeasuring` 으로 거른 것과 같은 자리다.
        if (measuringWas == meter.IsRunning) return;
        measuringWas = meter.IsRunning;

        if (hud is not null)
        {
            hud.View.IsMeasuring = meter.IsRunning;
            hud.View.InvalidateVisual();
        }
    });

    /// <summary>직전에 본 "재는 중". 이 값이 바뀔 때만 HUD 를 다시 그린다.</summary>
    private bool measuringWas;

    /// <summary>
    /// 토큰 훑기를 **재는 중일 때만** 돌린다.
    ///
    /// 안 재는 동안 1분마다 기록 폴더를 훑으면 아무도 안 보는 숫자를 위해 디스크를 읽는다.
    /// 일시정지도 같다 — 세워 둔 동안 쓴 것은 애초에 측정에 안 들어간다.
    /// </summary>
    private void SyncScanTimer()
    {
        var needed = meter.WantsScanning;
        if (needed && !scanTimer.IsEnabled) scanTimer.Start();
        else if (!needed && scanTimer.IsEnabled) scanTimer.Stop();
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
            OwlDocument.Embedded, session, store.IsDisconnected, store.IsSpent);
        // 다 썼으면 자세는 탈진 그대로 두고 색을 뺀다. 링·숫자와 같은 규칙이다.
        //
        // **세션도 본다.** 세션을 다 쓴 사람은 다음 창이 열릴 때까지 한 글자도 못
        // 보내는데, 주간만 보던 시절에는 마스코트만 멀쩡히 걸어다녔다.
        animator.IsUnusable = store.IsSpent;
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
        // **세션 몫을 따로 넘긴다.** 세션 링·세션 줄만 회색이 되는 경우가 있어서
        // 하나로 합칠 수 없다 — 주간은 다음 창이 열리면 실제로 쓸 수 있는 양이다.
        hud.View.IsSessionSpent = store.IsSessionSpent;
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

        hud.View.OwlGrid = grid;
        hud.View.OwlPaletteName = animator.PaletteName;
        hud.View.MascotFrame = animator.MascotFrame;
        hud.View.MascotFlipped = animator.SpriteFlipped;
        tray?.UpdateOwl(grid, OwlDocument.Embedded.Palettes[TrayPalette()]);
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
            File.Exists,
            Directory.EnumerateDirectories);

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
            // 어느 실행 파일을 띄웠는지 남긴다 — "재로그인을 눌렀는데 엉뚱한 claude 가
            // 떴다" 를 기록만으로 짚을 수 있어야 한다. **토큰은 여기 없다.**
            AppLog.Write($"재로그인 창을 띄웠다{(insideWsl ? " (WSL)" : "")}"
                + $" · {executable ?? "wsl"}");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            AppLog.Write($"재로그인 창을 띄우지 못했다: {error.Message}");
            // **조용히 물러나지 않는다.** 눌렀는데 아무 일도 안 일어나면 사용자는 앱이
            // 고장 난 줄 안다 — 맥은 같은 자리에서 알림 창을 낸다.
            MessageBox.Show(
                "재로그인 창을 띄우지 못했습니다.\n\n"
                + "터미널에서 직접 claude auth login 을 실행해 주세요.",
                $"{AppInfo.Name} 재로그인",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
    /// 트레이 아이콘을 어떤 색으로 칠할지.
    ///
    /// 테스트판은 보라로 칠해 두 판을 나란히 띄웠을 때 한눈에 갈린다. 다만 **끊김
    /// (회색)이 테스트 표시보다 세다** — 회색은 지금 값이 아니라는 뜻이라, 그것을
    /// 보라로 덮으면 낡은 숫자를 지금 값으로 믿게 된다.
    ///
    /// **HUD 마스코트에는 안 쓴다.** 한동안 거기도 보라로 칠했는데, 그러면 캐릭터가
    /// 캐릭터로 안 보인다 — 새로 그린 그림을 확인하려고 띄운 테스트판에서 정작 그 색을
    /// 못 본다. 두 판을 가르는 일은 여기 트레이 아이콘과 버전 딱지(<c>2.5.0 test</c>)가
    /// 이미 하고 있다. 맥 2.5.2 가 같은 이유로 뺐고, 거기서도 메뉴바 아이콘만 남겼다.
    /// </summary>
    private string TrayPalette()
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
        // **한도를 다 썼을 때도 같다.** 그때는 아예 죽은 것으로 다루므로 스스로 걷지도,
        // 커서를 피하지도 않는다. 색만 빼고 계속 돌아다니면 살아 있는 것으로 보인다.
        //
        // 주간뿐 아니라 **세션**도 본다(`IsSpent`) — 세션을 다 쓰면 주간이 아무리
        // 남아 있어도 지금은 못 쓴다.
        && !store.IsSpent
        && (settings.PetWanders || settings.PetDodgesCursor);

    /// <summary>
    /// 지금 <b>붙어 있어도 되는</b> 상황인지.
    ///
    /// <b><see cref="ShouldMove"/> 와 갈라 둔다.</b> 저쪽에는 눌림(<c>IsHeld</c>)과
    /// 배회·회피 설정이 들어 있는데, 그건 <b>잠깐 멈추는 이유</b>일 뿐 떨어질 이유가
    /// 아니다. 하나로 묶으면 붙여 놓은 것을 한 번 누르거나 우클릭 메뉴를 열기만 해도
    /// 자세만 매달린 채 배회가 시작된다 — 맥에서 실제로 그랬다.
    /// </summary>
    private bool CanStayPerched => CanPerchNow && hud is { IsCarried: false };

    /// <summary>
    /// 붙을 수 있는 판인지. <b>손에 들려 있는지는 안 본다.</b>
    ///
    /// <see cref="CanStayPerched"/> 에서 그 한 조건만 뺀 것이다 — 끌고 가는 동안에는
    /// 늘 들려 있어서(<c>IsCarried</c>) 붙어 있을 수는 없지만, <b>놓으면 붙을 수
    /// 있는지</b>는 끄는 동안에도 보여줘야 한다.
    ///
    /// <b>조건을 두 벌로 두지 않는다.</b> 미리보기·놓기·기록이 저마다 같은 조건을 다시
    /// 적고 있었고 그 중 하나는 이미 <c>UsesSheet()</c> 를 빠뜨린 채였다 — 조건이 하나
    /// 늘 때마다 네 곳을 다 찾아야 하고, 한 곳만 놓치면 미리보기는 "붙는다" 고 하는데
    /// 놓으면 안 붙는 꼴이 된다.
    /// </summary>
    private bool CanPerchNow =>
        hud is not null
        && settings.IsHudVisible
        && settings.Mode == HudMode.Pet
        && settings.PetPerches
        // **매달린 자세가 있는 그림만 붙어 있는다.** 격자 부엉이와 Claude 쪽 그림에는
        // 그 칸이 없어서, 붙여 놓아도 테두리에 그냥 선 것으로 보인다 — 붙어 있는 채로
        // 캐릭터를 바꾼 사람이 그 꼴을 본다.
        && settings.IconStyle.UsesSheet()
        && !screensAsleep
        && !store.IsDisconnected
        && !store.IsSpent;

    /// <summary>
    /// 그림 사정과 화면 사정을 이어 붙이는 계산기. <b>진단 통로도 같은 것을 쓴다</b>
    /// (<c>--probe-perch</c>) — 따로 셈하면 표에는 "가능" 이 뜨는데 실제로는 안 붙는
    /// 자리가 생긴다.
    /// </summary>
    private PerchPlanner Planner(HudWindow window) =>
        new(window.View, settings, stage?.WorkArea);

    /// <summary>
    /// 지금 떠 있는 창 목록. 우리 창은 빠진다.
    ///
    /// <b>한 번 뜬 것을 나눠 쓴다.</b> 목록 한 번이 창 수만큼 DWM 을 부르는데, 놓을
    /// 때마다 붙을 자리를 찾느라 한 번 · 왜 안 붙었는지 적느라 또 한 번 뜨고 있었다.
    /// </summary>
    private IReadOnlyList<PerchWindow> SurveyWindows(HudWindow window) =>
        stage is null ? [] : WindowSurvey.OnScreenWindows(stage, window.Handle);

    /// <summary>
    /// 끌고 가는 동안. 놓으면 걸릴 자리를 미리 보여주고 자세도 그때 잡는다.
    ///
    /// <b>이게 없으면 어디에 걸리는지 놓아 봐야 안다.</b> 붙는 문턱은 그림에서 재는데
    /// 펫의 창은 그보다 훨씬 커서(링이 128) 눈으로는 얼마나 가까운지 가늠이 안 된다.
    /// </summary>
    private void OnDragging()
    {
        if (hud is not { } window) { ClearPerchHint(); return; }
        // **끌고 있는 동안에는 `CanStayPerched` 가 거짓이다**(그게 떨어지는 조건이다).
        // 여기서 볼 것은 "놓으면 붙을 수 있나" 라서 끌림만 뺀 쪽을 본다.
        if (!CanPerchNow) { ClearPerchHint(); return; }

        // **매 픽셀마다 창 목록을 뜨지 않는다.** `LocationChanged` 는 마우스가 움직일
        // 때마다 오는데(초당 백 번 넘는다) 목록 한 번이 창 수만큼 DWM 을 부른다.
        // 30Hz 면 눈에는 끊김이 안 보이고 값은 4분의 1이 된다.
        var now = Environment.TickCount64;
        if (now - lastHintAt < 33) return;
        lastHintAt = now;

        // **계산기는 한 번만 만든다.** 만들 때마다 작업 영역을 묻느라 모니터를 훑는데,
        // 30Hz 로 두 벌을 만들면 같은 값을 초당 예순 번 다시 묻는 셈이다.
        var planner = Planner(window);
        if (planner.Snap(new Point(window.Left, window.Top), SurveyWindows(window))
            is not { } spot)
        {
            ClearPerchHint();
            return;
        }

        if (planner.Ink(spot.Edge) is not { } ink) { ClearPerchHint(); return; }

        // 놓을 자세를 미리 잡는다. **어떤 자세로 붙을지는 마스코트가 말해 준다.**
        if (animator.SetPerch(spot.Edge))
        {
            StartFrameTimer();
            RefreshHud();
        }

        var sink = planner.Sink(spot);
        var landing = PerchFinder.LandingArea(spot, ink.Width, ink.Height, sink);
        perchHint ??= new PerchHint();
        perchHint.Show(landing, spot.Edge, sink, below: window.Handle);
    }

    private void ClearPerchHint()
    {
        perchHint?.Hide();
        // 붙어 있는 것이 아니라 미리보기였을 뿐이면 자세도 되돌린다.
        if (motion.PerchedSpot is null && animator.SetPerch(null))
        {
            StartFrameTimer();
            RefreshHud();
        }
    }

    private PerchHint? perchHint;

    /// <summary>끌 때 미리보기를 마지막으로 잰 시각(밀리초). 30Hz 로 조인다.</summary>
    private long lastHintAt;

    /// <summary>끌어다 놓았다. 붙을 자리가 있으면 붙인다.</summary>
    private void OnDropped()
    {
        perchHint?.Hide();
        if (hud is not { } window) return;

        var planner = Planner(window);
        var windows = SurveyWindows(window);

        if (!CanStayPerched
            || planner.Snap(new Point(window.Left, window.Top), windows) is not { } spot)
        {
            LogPerchAttempt(window, planner, windows);
            // **붙어 있던 것도 놓는다.** 자세만 되돌리고 여기를 빼먹으면 그림은 선
            // 자세인데 창은 계속 테두리를 따라다닌다 — 아무것도 안 잡고 모서리에 붙어
            // 미끄러지는 꼴이다.
            if (motion.ReleasePerch()) window.SetPerched(false);
            // 미리보기로 잡아 둔 자세를 되돌린다.
            if (animator.SetPerch(null)) { StartFrameTimer(); RefreshHud(); }
            SyncMotion();
            return;
        }

        if (planner.Origin(spot) is not { } at) { SyncMotion(); return; }
        if (!motion.Perch(spot)) { SyncMotion(); return; }

        window.Left = at.X;
        window.Top = at.Y;
        // **붙는 순간은 걷는 중이 아니라 `Tick` 의 `Settled` 를 못 탄다.** 여기서 직접
        // 저장하지 않으면 붙은 자리가 안 남아서, 껐다 켜면 붙기 전 자리로 돌아간다.
        window.SavePosition();

        AppLog.Write($"창에 붙었다 — {spot.Edge} · 창 {spot.Window}");
        // 붙은 면과 잠기는 깊이를 같이 넘긴다 — 창 안으로 넘어간 쪽이 마우스를 안 받게
        // 하려면 창이 그 둘을 알아야 한다.
        window.SetPerched(true, spot.Edge, planner.Sink(spot));
        if (animator.SetPerch(spot.Edge)) StartFrameTimer();
        ApplyGait(null);
        RefreshHud();
        // **타이머는 `SyncMotion` 이 잡는다.** 여기서 직접 걸면 커서 감시 타이머가
        // 그대로 남아 붙어 있는 내내 0.4초마다 헛돈다 — 붙어 있으면 안 비키므로
        // (`RequestDodge` 가 거른다) 깨어날 이유가 없다.
        SyncMotion();
    }

    /// <summary>
    /// 놓았는데 <b>안 붙었을 때</b> 왜 안 붙었는지 기록에 남긴다.
    ///
    /// <b>안 붙는다는 말을 들었을 때 볼 자리다.</b> 화면에는 아무 일도 안 일어난 것으로만
    /// 보이고, <c>--probe-perch</c> 는 <b>그때</b> 어디에 놓았는지를 모른다 — 놓은 자리와
    /// 그 순간의 창 목록이 같이 남아야 짚을 수 있다. 맥 <c>logPerchAttempt</c> 와 같은 자리다.
    ///
    /// <b>붙었을 때는 부르는 쪽이 한 줄만 적는다.</b> 잘 되는 경우까지 창 목록을 통째로
    /// 남기면 기록이 금세 창 목록으로 뒤덮인다.
    /// </summary>
    /// <param name="planner">놓을 자리를 찾을 때 쓴 그 계산기. <b>다시 만들지 않는다.</b></param>
    /// <param name="windows">
    /// 그때 뜬 그 창 목록. <b>다시 뜨지 않는다</b> — 새로 뜨면 기록에 남는 것이 판정에
    /// 쓰인 것과 다른 순간의 화면이 되어, 짚으라고 남긴 기록이 짚을 수 없게 된다.
    /// </param>
    private void LogPerchAttempt(
        HudWindow window, PerchPlanner planner, IReadOnlyList<PerchWindow> windows)
    {
        // 붙이기를 껐거나 펫이 아니면 애초에 시도한 적이 없다. 기록을 더럽히지 않는다.
        if (!CanPerchNow) return;

        var origin = new Point(window.Left, window.Top);
        if (planner.MascotRect(origin) is not { } mascot) return;

        // **한 번에 쓴다.** `AppLog.Write` 는 부를 때마다 파일을 열고 닫아서, 창마다
        // 부르면 놓을 때 한 번에 서른 번 넘게 디스크를 두드린다 — 그것도 화면 갈래에서.
        AppLog.Write(string.Join(Environment.NewLine, [
            $"창에 안 붙었다 — 그림 자리 {PerchFinder.Box(mascot)} "
                + $"· 아이콘 {settings.IconStyle}",
            .. planner.Explain(origin, windows),
        ]));
    }

    /// <summary>붙어 있는 동안의 한 틱. 창을 따라가고 층을 맞춘다.</summary>
    private void FollowPerch(HudWindow window)
    {
        if (stage is null) return;

        var windows = SurveyWindows(window);
        var planner = Planner(window);
        var mascot = (motion.PerchedSpot is { } current ? planner.Ink(current.Edge) : null)
            ?? new PetRect(0, 0, window.Width, window.Height);

        var tick = motion.Follow(
            windows,
            mascot,
            origin: planner.Origin,
            buried: spot => planner.IsBuried(spot, windows),
            WindowSurvey.IsDraggingSomething);

        if (tick.Dropped)
        {
            window.SetPerched(false);
            // **화면 안으로 되당긴다.** 창을 따라가다 화면 밖까지 나갔을 수 있는데,
            // 그대로 놓으면 안 보이는 자리에서 걷기 시작한다. 맥 `unperch()` 가
            // `clamped(frame().origin)` 으로 같은 일을 한다.
            window.ClampIntoScreen();
            if (animator.SetPerch(null)) StartFrameTimer();
            RefreshHud();
            SyncMotion();
            return;
        }

        if (tick.MoveTo is { } at && (at.X != window.Left || at.Y != window.Top))
        {
            window.Left = at.X;
            window.Top = at.Y;
        }

        // 깊이는 창을 따라가다 달라질 수 있다(화면 끝에서 더 깊이 앉는다).
        if (motion.PerchedSpot is { } now) window.SetPerched(true, now.Edge, planner.Sink(now));

        // **묻혔으면 앞으로 끌어올리지 않는다.** 올리면 그 창을 덮은 창 위에 펫만
        // 떠서, 아무것도 없는 자리에 매달린 것으로 보인다.
        window.SetPerchFront(tick.Front, motion.PerchedSpot?.Window ?? 0);
        ScheduleMotion(tick.NextWakeup);
    }

    /// <summary>잡혔다 놓였다. 자세를 바꾸고 움직임을 멈췄다 다시 켠다.</summary>
    private void OnHeldChanged()
    {
        if (hud is { } window)
        {
            // **매달린 자세는 실제로 끌었을 때만 나온다.** 버튼을 누르고만 있거나 눌렀다
            // 그 자리에서 떼는 클릭까지 버둥거리면 새로고침 한 번에 부엉이가 요동친다.
            // 멈추는 것은 `IsHeld` 가 따로 본다 — 눌림도 여전히 멈추는 이유다.
            animator.IsDragged = window.IsCarried;
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
        // 대개 손에 잡혀 있어 이미 멈춰 있지만, 놓은 뒤 남은 시간을 위해 상태는 맞춰 둔다.
        SyncMotion();
    });

    private readonly DispatcherTimer dizzyTimer = new();

    private void SyncMotion()
    {
        motion.Perches = settings.PetPerches;
        motion.CanStayPerched = CanStayPerched;

        // **붙어 있을 수 없게 됐으면 먼저 뗀다**(펫에서 나감·화면 잠김·설정 끔).
        if (motion.ReleasePerchIfNeeded())
        {
            hud?.SetPerched(false);
            if (animator.SetPerch(null)) StartFrameTimer();
            RefreshHud();
        }

        // **붙어 있으면 자리를 지킨다.** 눌림·메뉴로 `ShouldMove` 가 거짓이 돼도
        // 떨어지지 않는다 — 그건 잠깐 멈추는 이유일 뿐이다.
        if (motion.PerchedSpot is not null)
        {
            motionTimer.Stop();
            dodgeTimer.Stop();
            hover.Reset();
            ScheduleMotion(PetMotion.PerchTick);
            return;
        }

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
        // **기본은 꺼짐.** 켜면 걸어서든 커서를 피해서든 옆 화면으로 넘어간다.
        motion.CrossesScreens = settings.PetCrossesScreens;
        // **기분을 그대로 쓴다.** `animator.Mood` 는 `OnStoreChanged` 가 `OwlMoodResolver`
        // 로 정해 넣은 값이라, 그림과 걸음이 같은 신호에서 나온다. 여기서 사용률을 다시
        // 견주면 마스코트는 주저앉았는데 산책은 계속 나가는 어긋남이 생긴다.
        motion.IsDrained = animator.Mood == OwlMood.Exhausted;
        // 어지러움도 같다 — 여기서 시간을 따로 세면 그림과 움직임이 어긋난다.
        motion.IsDizzy = animator.IsDizzy;
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
        if (hud is not { } window || stage is null) return;

        // **붙어 있으면 걷는 갈래로 안 간다.** 창을 따라가는 것이 전부다.
        if (motion.PerchedSpot is not null)
        {
            FollowPerch(window);
            return;
        }

        if (!ShouldMove) return;

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
                settings, store, meter, updates, ApplySettings, ResetHudPosition, TogglePet, StartLogin);
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
        scanTimer.Stop();

        settingsWindow?.Close();
        settingsWindow = null;
        // **막대 창도 닫는다.** 남겨 두면 Velopack 이 프로세스가 안 끝났다고 보고
        // 파일을 못 바꾼다 — HUD·트레이를 놓아 주는 것과 같은 이유다.
        perchHint?.Close();
        perchHint = null;
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
        scanTimer.Stop();
        perchHint?.Close();
        perchHint = null;
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
