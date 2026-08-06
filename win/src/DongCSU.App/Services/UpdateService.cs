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

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (IsChecking) return;
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
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            // 확인 실패는 정상적인 일이다(비행기 모드, 회사 프록시). 다음 주기에 다시 한다.
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
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// 받아서 깔고 다시 뜬다. 성공하면 이 프로세스는 끝난다.
    ///
    /// 실패해도 던지지 않는다. 대신 <see cref="LastError"/> 에 남긴다 —
    /// 눌렀는데 아무 일도 안 일어나는 것처럼 보이는 게 제일 나쁘다.
    /// </summary>
    public async Task<bool> ApplyAsync()
    {
        if (IsApplying) return false;
        if (!IsInstalled || manager is not { } updateManager)
        {
            LastError = "설치본이 아니라 업데이트를 걸 수 없습니다.";
            Changed?.Invoke();
            return false;
        }

        IsApplying = true;
        LastError = null;
        Changed?.Invoke();
        try
        {
            var update = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                LastError = "받을 새 버전이 없습니다.";
                return false;
            }

            await updateManager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            updateManager.ApplyUpdatesAndRestart(update);
            return true;
        }
        catch (Exception error)
        {
            LastError = $"업데이트 실패: {error.Message}";
            return false;
        }
        finally
        {
            IsApplying = false;
            Changed?.Invoke();
        }
    }

    /// <summary>마지막 업데이트 시도가 왜 실패했는지. 성공했거나 아직 안 눌렀으면 null.</summary>
    public string? LastError { get; private set; }

    /// <summary>지금 버전보다 새 것이 있나.</summary>
    public bool HasUpdate => AppVersion.IsNewer(LatestVersion, AppInfo.Version);

    /// <summary>업데이트를 받는 중인지. 68MB 라 한참 걸린다 — 화면이 이걸 보여줘야 한다.</summary>
    public bool IsApplying { get; private set; }
}
