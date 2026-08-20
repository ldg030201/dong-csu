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

    /// <summary>
    /// 마지막 확인이 왜 실패했는지. 성공했거나 아직 한 번도 안 걸었으면 null.
    ///
    /// **<see cref="LastError"/> 와 다른 물건이다.** 이건 "새 버전이 있는지 확인이 안 됐다"
    /// 이고 저건 "받다가·갈아끼우다 실패했다"라서, 버전 탭에 **둘이 같이 뜰 수 있다.**
    /// 한 자리에 몰아 넣으면 서로를 덮어써서 한쪽이 화면에서 사라진다.
    /// </summary>
    public string? CheckError { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// 값을 직접 꽂아 넣는다. **렌더 확인 전용** — 네트워크 없이 버전 탭을 채운다.
    /// 맥의 <c>UpdateChecker(preview:)</c> 와 같은 자리다.
    ///
    /// <paramref name="installed"/> 까지 받는 이유는 <see cref="IsInstalled"/> 가 계산
    /// 프로퍼티라 밖에서 못 꽂아서다. 폴더에 놓인 exe 로 그림을 뽑으면 버전 탭이 늘
    /// "설치본이 아니라 자동 업데이트를 쓸 수 없습니다"로 나와 사용자가 볼 화면과 달라진다.
    ///
    /// **문구에만 쓰이고 진짜 업데이트로는 새어 나가지 않는다** — 꽂은 값은
    /// <see cref="IsInstalled"/> 에서 <c>manager</c> 를 만들기 **전에** 돌아 나가므로,
    /// manager 가 있어야 움직이는 <see cref="DownloadAsync"/>·<see cref="Restart"/> 는
    /// 이 값을 믿고 폴더에 놓인 앱을 갈아 끼우려 들 수 없다.
    /// </summary>
    public void Preview(string? latestVersion, DateTimeOffset? lastChecked, bool installed = true)
    {
        LatestVersion = latestVersion;
        LastChecked = lastChecked;
        previewInstalled = installed;
        Changed?.Invoke();
    }

    private bool? previewInstalled;

    /// <summary>설치본이 아니면(개발 중 실행 등) 업데이트를 걸지 않는다.</summary>
    public bool IsInstalled
    {
        get
        {
            // **테스트판은 절대 업데이트하지 않는다.** 윈도우는 맥과 달리 조용히 받아
            // 갈아 끼우고 다시 뜨기 때문에, 막지 않으면 개발 빌드가 어느 날 정식판으로
            // 바뀌어 버리고 무엇을 검증하던 중이었는지 알 수 없게 된다.
            //
            // **꽂은 값보다 이게 먼저다.** 렌더 통로가 테스트 바이너리로 돌아도 설치본
            // 행세를 하게 두면, 막아 둔 이유가 그림 한 장 때문에 뚫린다.
            if (AppInfo.IsTestBuild) return false;

            // 렌더에서 꽂아 넣은 값. 여기서 돌아 나가므로 manager 는 만들어지지 않는다.
            if (previewInstalled is { } preview) return preview;

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
        // **테스트판은 확인 자체를 하지 않는다.** 화면은 "테스트판은 새 버전을 확인하지
        // 않습니다"라고 말하는데 뒤에서 원격 내역을 받아 오면 말과 실제가 어긋나고,
        // 변경 내역 목록도 앱에 박힌 것만 보여주는 맥 테스트판과 달라진다.
        //
        // **가드가 맨 앞에 있어야 한다.** `LoadChangelogAsync` 안에 넣으면 접속만 막히고
        // `LastChecked`·`IsChecking` 은 그대로 움직이며 기록에 "업데이트 확인" 줄도 남는다.
        // 부르는 쪽에 나눠 넣으면 호출부가 하나 늘 때마다 기억해야 하고 언젠가 빠뜨린다 —
        // 여기 한 곳이면 지금 있는 것과 앞으로 생길 것까지 같이 막힌다. 바닥(`lastCheckAt`)을
        // 소모하기 전이자 `IsChecking` 을 세우기 전이라, 확인 버튼이 잠긴 채 굳지도 않는다.
        if (AppInfo.IsTestBuild) return;

        if (IsChecking || !CanCheckNow) return;

        string? failure = null;

        lastCheckAt = DateTimeOffset.UtcNow;
        IsChecking = true;
        Changed?.Invoke();
        try
        {
            failure = await LoadChangelogAsync(cancellationToken).ConfigureAwait(false);

            if (IsInstalled && manager is { } updateManager)
            {
                var update = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
                LatestVersion = update?.TargetFullRelease.Version.ToString();
            }

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

            // 내역 받기가 남긴 사유가 있어도 여기서 덮는다 — Velopack 확인까지 못 간 것이
            // 더 큰 실패라, 화면에 뜰 한 줄은 그쪽이어야 한다.
            failure = $"업데이트 확인 실패: {error.Message}";
        }
        finally
        {
            // **성공·실패를 가리지 않고 찍는다.** 예전에는 try 끝에서 찍어서, 비행기 모드나
            // 회사 프록시로 어느 한쪽이 던지면 `LastChecked` 가 영영 null 로 남았다 —
            // 버튼을 눌러도 상태 줄이 "아직 확인하지 않았습니다"에 머물러 아무 반응이
            // 없는 것처럼 보였다. 걸어 본 것은 사실이므로 시각은 남긴다.
            LastChecked = DateTimeOffset.Now;
            // 성공하면 null 이 들어가 지난 사유가 지워진다. 안 지우면 한 번 실패한 뒤로
            // 주황 줄이 영영 남는다.
            CheckError = failure;
            IsChecking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 변경 내역은 릴리스와 별개로 받는다.
    ///
    /// 앱에 박혀 있는 내역은 그 버전까지밖에 모른다. **새 버전에 무엇이 들어갔는지
    /// 업데이트하기 전에 보려면** 밖에서 받아와야 한다.
    ///
    /// 성공하면 null, 실패하면 화면에 그대로 띄울 사유 한 줄을 돌려준다. 예전에는 조용히
    /// 삼켜서 404 든 깨진 피드든 화면에도 기록에도 흔적이 없었다.
    /// </summary>
    private async Task<string?> LoadChangelogAsync(CancellationToken cancellationToken)
    {
        try
        {
            // **`GetStringAsync` 를 쓰지 않는다.** 그쪽은 non-200 을 HttpRequestException 으로
            // 뭉개서 404(피드가 없다)와 회선 끊김을 구별하지 못한다. 상태 코드를 손에 쥐어야
            // 404 를 404 라 말할 수 있다.
            using var response = await http.GetAsync(Changelog.FeedUrl, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var fetch = Changelog.Read((int)response.StatusCode, body);
            if (fetch.Entries is { } entries)
            {
                RemoteEntries = entries;
                return null;
            }

            // **실패해도 `RemoteEntries` 는 그대로 둔다.** 지난번에 받아 둔 것이 있으면
            // 그게 낫다 — 일시적인 404 한 번에 잘 받아 뒀던 목록이 사라지면 안 된다.
            AppLog.Write($"변경 내역 받기 실패: {fetch.Failure}");
            return fetch.Failure;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // 앱이 꺼지는 중이면 실패가 아니다. 그대로 다시 던져 바깥 `CheckAsync` 의
            // catch 가 삼키게 둔다 — 끄는 중에 주황 줄을 남길 이유가 없다.
            if (cancellationToken.IsCancellationRequested) throw;

            var reason = $"변경 내역을 받지 못했습니다 (네트워크: {error.Message})";
            AppLog.Write($"변경 내역 받기 실패: {reason}");
            return reason;
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
