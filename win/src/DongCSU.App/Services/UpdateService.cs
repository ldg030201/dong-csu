using System.IO;
using System.Net.Http;
using DongCSU.Core;
using Velopack;
using Velopack.Sources;

namespace DongCSU.App.Services;

/// <summary>
/// 새 버전 확인과 자체 업데이트.
///
/// **맥판과 다르다.** 맥은 터미널을 띄워 brew 를 돌리지만, 윈도우는 앱이 조용히 받아서
/// 다시 뜬다. 트레이에 상주하는 앱을 고치겠다고 터미널을 여는 건 이상하다.
///
/// 사용자 폴더에 깔리므로 관리자 권한을 묻지 않는다.
/// </summary>
public sealed class UpdateService(HttpClient http)
{
    private const string ReleaseFeed = "https://github.com/ldg030201/dong-csu";

    /// <summary>하루에 한 번만 확인한다. 켜 둔 채로 며칠 지나도 부담이 없어야 한다.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private UpdateManager? manager;

    public string? LatestVersion { get; private set; }
    public bool IsChecking { get; private set; }
    public DateTimeOffset? LastChecked { get; private set; }
    public IReadOnlyList<ChangelogEntry> RemoteEntries { get; private set; } = [];

    public event Action? Changed;

    /// <summary>설치본이 아니면(개발 중 실행 등) 업데이트를 걸지 않는다.</summary>
    public bool IsInstalled
    {
        get
        {
            // **테스트판은 절대 업데이트하지 않는다.** 윈도우는 맥과 달리 조용히 받아
            // 갈아 끼우고 다시 뜨기 때문에, 막지 않으면 개발 빌드가 어느 날 정식판으로
            // 바뀌어 버리고 무엇을 검증하던 중이었는지 알 수 없게 된다.
            if (AppInfo.IsTestBuild) return false;

            try
            {
                manager ??= new UpdateManager(new GithubSource(ReleaseFeed, null, prerelease: false));
                return manager.IsInstalled;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 확인 사이 최소 간격. 사용량 조회와 같은 이유로 바닥을 깐다 —
    /// **버튼을 잇달아 누르면 그만큼 그대로 나간다.**
    /// </summary>
    public static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(10);

    private DateTimeOffset? lastCheckAt;

    public bool CanCheckNow =>
        lastCheckAt is not { } last || DateTimeOffset.UtcNow - last >= MinCheckInterval;

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (IsChecking || !CanCheckNow) return;

        lastCheckAt = DateTimeOffset.UtcNow;
        IsChecking = true;
        Changed?.Invoke();
        try
        {
            await LoadChangelogAsync(cancellationToken).ConfigureAwait(false);

            if (IsInstalled && manager is { } updateManager)
            {
                var update = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
                LatestVersion = update?.TargetFullRelease.Version.ToString();
            }

            LastChecked = DateTimeOffset.Now;
            AppLog.Write($"업데이트 확인: 설치본={IsInstalled} 최신={LatestVersion ?? "-"} 지금={AppInfo.Version}");
        }
        catch (Exception error)
        {
            // **어떤 예외도 밖으로 내보내지 않는다.** 이걸 부르는 곳이 `async void`
            // 타이머 핸들러라, 새어 나가면 처리되지 않은 예외가 되어 **앱이 그대로 죽는다.**
            // 피드가 깨졌을 때 Velopack 이 던지는 것은 HttpRequestException 만이 아니다.
            //
            // 확인 실패 자체는 정상적인 일이다(비행기 모드, 회사 프록시, 깨진 피드).
            // 다음 주기에 다시 한다.
            AppLog.Write($"업데이트 확인 실패: {error.Message}");
        }
        finally
        {
            IsChecking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 변경 내역은 릴리스와 별개로 받는다.
    ///
    /// 앱에 박혀 있는 내역은 그 버전까지밖에 모른다. **새 버전에 무엇이 들어갔는지
    /// 업데이트하기 전에 보려면** 밖에서 받아와야 한다.
    /// </summary>
    private async Task LoadChangelogAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await http.GetStringAsync(Changelog.FeedUrl, cancellationToken).ConfigureAwait(false);
            if (Changelog.Parse(json) is { } feed) RemoteEntries = feed.Entries;
        }
        catch (Exception)
        {
            // 변경 내역을 못 받아도 앱에 박힌 것이 있다. 조용히 넘어간다.
        }
    }

    /// <summary>업데이트가 어디까지 갔는지.</summary>
    public enum UpdatePhase
    {
        /// <summary>아무것도 안 하는 중.</summary>
        Idle,
        /// <summary>받는 중. 68MB 라 한참 걸린다 — 화면이 진행 상황을 보여줘야 한다.</summary>
        Downloading,
        /// <summary>
        /// 다 받았다. **여기서 멈춰서 사람에게 물어본다.**
        ///
        /// 예전에는 곧바로 갈아끼우고 스스로 껐다. 그런데 갈아끼우는 쪽은 우리가 꺼지기를
        /// 잠깐 기다리다 포기하게 되어 있어서, 그 안에 안 꺼지면 **옛 앱이 그대로 남고
        /// 화면은 "곧 다시 뜹니다"에서 멈춘다.** 사람이 누른 뒤에 띄우면 그 겨루기가 없어진다.
        ///
        /// 물어보는 시점이 옳기도 하다 — 받기 전에 "앱이 꺼집니다"를 물으면 정작 꺼지는
        /// 것은 30초 넘게 받은 뒤라 시점이 어긋난다. 받는 동안은 그대로 쓸 수 있다.
        /// </summary>
        Ready,
        /// <summary>갈아끼우기를 띄웠고 곧 꺼진다.</summary>
        Swapping,
    }

    public UpdatePhase Phase { get; private set; } = UpdatePhase.Idle;

    /// <summary>받은 비율(0~100). 받는 중일 때만 뜻이 있다.</summary>
    public int DownloadedPercent { get; private set; }

    private UpdateInfo? downloaded;

    /// <summary>
    /// 새 버전을 받는다. **여기서 앱을 끄지 않는다** — 다 받으면 <see cref="UpdatePhase.Ready"/>
    /// 에서 멈춰 사람에게 물어본다.
    ///
    /// 실패해도 던지지 않는다. 대신 <see cref="LastError"/> 에 남긴다 —
    /// 눌렀는데 아무 일도 안 일어나는 것처럼 보이는 게 제일 나쁘다.
    /// </summary>
    public async Task DownloadAsync()
    {
        if (Phase != UpdatePhase.Idle) return;
        if (!IsInstalled || manager is not { } updateManager)
        {
            LastError = "설치본이 아니라 업데이트를 걸 수 없습니다.";
            Changed?.Invoke();
            return;
        }

        Phase = UpdatePhase.Downloading;
        DownloadedPercent = 0;
        LastError = null;
        Changed?.Invoke();
        try
        {
            var update = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                LastError = "받을 새 버전이 없습니다.";
                Phase = UpdatePhase.Idle;
                return;
            }

            AppLog.Write($"업데이트 받는 중: {update.TargetFullRelease.Version}");
            await updateManager.DownloadUpdatesAsync(update, Progress).ConfigureAwait(false);
            AppLog.Write("업데이트 내려받기 끝 — 사람이 누르면 갈아 끼운다");

            downloaded = update;
            DownloadedPercent = 100;
            Phase = UpdatePhase.Ready;
        }
        catch (Exception error)
        {
            LastError = $"업데이트 실패: {error.Message}";
            AppLog.Write($"업데이트 받기 실패: {error}");
            Phase = UpdatePhase.Idle;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    private void Progress(int percent)
    {
        // 한 자리씩 다 알릴 이유가 없다. 바뀔 때만 다시 그린다.
        if (percent == DownloadedPercent) return;
        DownloadedPercent = percent;
        Changed?.Invoke();
    }

    /// <summary>
    /// 받아 둔 것으로 갈아끼우고 앱을 끈다. **사람이 눌러야 여기 온다.**
    /// </summary>
    public void Restart()
    {
        if (Phase != UpdatePhase.Ready || downloaded is null || manager is not { } updateManager) return;

        Phase = UpdatePhase.Swapping;
        Changed?.Invoke();
        try
        {
            BeforeRestart?.Invoke();
            updateManager.ApplyUpdatesAndRestart(downloaded);
        }
        catch (Exception error)
        {
            LastError = $"갈아끼우기 실패: {error.Message}";
            AppLog.Write($"업데이트 적용 실패: {error}");
            Phase = UpdatePhase.Ready;
            Changed?.Invoke();
        }
    }

    /// <summary>받아 둔 것을 그대로 두고 화면만 접는다. 다음에 켤 때 이어서 할 수 있다.</summary>
    public void Dismiss()
    {
        if (Phase is UpdatePhase.Downloading or UpdatePhase.Swapping) return;
        Phase = UpdatePhase.Idle;
        downloaded = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// 갈아끼우다 멈춘 것 같을 때 손으로 끊는다. **화면에서 확인을 받고 부른다.**
    ///
    /// 여기서 멈추면 화면에 누를 것이 하나도 없어서 작업 관리자를 여는 수밖에 없다.
    /// </summary>
    public static void ForceQuit()
    {
        AppLog.Write("갈아끼우다 멈춰서 강제 종료했다");
        Environment.Exit(0);
    }

    /// <summary>마지막 업데이트 시도가 왜 실패했는지. 성공했거나 아직 안 눌렀으면 null.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 파일을 갈아 끼우기 직전에 부른다. **트레이 아이콘과 창을 여기서 정리해야 한다.**
    ///
    /// Velopack 은 우리 프로세스가 끝나기를 기다렸다가 파일을 바꾼다. 트레이 아이콘
    /// (Win32 창)이나 열린 창이 남아 있으면 프로세스가 깨끗이 안 끝나서, 기다리다
    /// 지친 업데이터가 **아무것도 못 바꾸고 물러난다.** 앱만 꺼지고 버전은 그대로인
    /// 상태가 그래서 생긴다.
    /// </summary>
    public Action? BeforeRestart { get; set; }

    /// <summary>지금 버전보다 새 것이 있나.</summary>
    public bool HasUpdate => AppVersion.IsNewer(LatestVersion, AppInfo.Version);

    /// <summary>업데이트가 도는 중인지. 그동안에는 확인 버튼 따위를 잠근다.</summary>
    public bool IsBusy => Phase != UpdatePhase.Idle;
}
