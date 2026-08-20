namespace DongCSU.Core.Usage;

/// <summary>
/// 사용량을 주기적으로 가져와서 화면에 물려주는 상태 저장소.
///
/// 타이머를 직접 돌리지 않는다 — <see cref="RefreshAsync"/> 를 언제 부를지는 화면 쪽이
/// 정하고, 여기는 **다음에 언제 부르면 되는지**(<see cref="NextPollDelay"/>)만 알려준다.
/// 그래야 시계 없이 테스트할 수 있고, 화면이 꺼졌을 때 폴링을 멈추는 것도 쉬워진다.
/// </summary>
public sealed class UsageStore(UsageApi api, TimeProvider? time = null)
{
    /// <summary>사용량 API 는 레이트리밋 창을 쓴다. 너무 조이면 429 가 난다.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(5);

    /// <summary>429 를 맞고 처음 물러나는 시간. 맥과 같은 60·120·240·300초 사다리의 첫 칸이다.</summary>
    public static readonly TimeSpan RateLimitBackoffBase = TimeSpan.FromSeconds(60);

    /// <summary>물러나기 상한. 이걸 넘기면 한 번 막혔을 때 사실상 안 돌아온다.</summary>
    public static readonly TimeSpan MaxRateLimitBackoff = TimeSpan.FromMinutes(5);

    private readonly TimeProvider time = time ?? TimeProvider.System;
    private DateTimeOffset? backoffUntil;
    private DateTimeOffset? lastAttemptAt;
    private int consecutiveRateLimits;

    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    public UsageSnapshot? Snapshot { get; private set; }

    /// <summary>
    /// 값을 직접 꽂아 넣는다. **테스트와 렌더 확인 전용** — 네트워크 없이 화면을 채운다.
    /// 맥의 <c>init(preview:)</c> 와 같은 자리다.
    ///
    /// <paramref name="nextPoll"/> 까지 받는 이유는 상태 탭이 **조회 카운트다운을 그리기**
    /// 때문이다. 예정 시각이 없으면 그 줄만 비어서 실제 화면과 달라진다.
    /// </summary>
    public void Preview(
        UsageSnapshot? snapshot,
        string? error = null,
        bool needsReauth = false,
        DateTimeOffset? nextPoll = null)
    {
        Snapshot = snapshot;
        ErrorText = error;
        NeedsReauth = needsReauth;
        previewNextPollAt = nextPoll;
        Changed?.Invoke();
    }

    /// <summary>
    /// 렌더·테스트 전용으로 꽂은 다음 조회 예정 시각. 실제 조회가 돌면 <see cref="Apply"/> 가 덮는다.
    /// </summary>
    private DateTimeOffset? previewNextPollAt;
    public string? ErrorText { get; private set; }
    public bool IsRefreshing { get; private set; }

    /// <summary>자격 증명이 없거나 만료됐다. 다시 걸어도 소용없고 재로그인이 필요하다.</summary>
    public bool NeedsReauth { get; private set; }

    /// <summary>화면에 떠 있는 숫자가 마지막 성공값(= 지금 값이 아닐 수 있음)인지.</summary>
    public bool IsStale => Snapshot is not null && ErrorText is not null;

    /// <summary>
    /// 화면 숫자가 지금 값이 아닌 상태.
    ///
    /// 마스코트가 회색이 되는 조건이다. 여러 곳에서 같은 판단을 하면 어긋나서
    /// 캐릭터만 회색이고 숫자는 멀쩡해 보이는 일이 생기므로 여기 한 곳에 둔다.
    /// </summary>
    public bool IsDisconnected => NeedsReauth || IsStale;

    /// <summary>
    /// 주간 한도를 다 썼다.
    ///
    /// **이러면 세션이 얼마 남았든 쓸 수 없다.** 세션 링만 초록으로 남아 있으면
    /// 아직 여유가 있는 것처럼 보이므로, 화면에서도 마스코트에서도 같이 죽은 것으로
    /// 다룬다. 여러 곳에서 따로 판단하면 어긋나므로 여기 한 곳에 둔다.
    /// </summary>
    public bool IsWeeklySpent => Snapshot?.SevenDay?.Utilization >= 100;

    /// <summary>값이 바뀔 때마다 부른다. 화면이 여기 붙어서 다시 그린다.</summary>
    public event Action? Changed;

    /// <summary>다음 조회까지 기다릴 시간. 429 를 맞았으면 그만큼 물러난다.</summary>
    public TimeSpan NextPollDelay()
    {
        if (backoffUntil is { } until)
        {
            var remaining = until - time.GetUtcNow();
            if (remaining > TimeSpan.Zero) return remaining;
        }
        return PollInterval;
    }

    /// <summary>
    /// 다음 조회 예정 시각. 아직 한 번도 안 걸었으면 null.
    ///
    /// **<see cref="NextPollDelay"/> 를 더해서 구하면 안 된다.** 물러나는 중에는 그 함수가
    /// *남은* 시간을 주므로, 마지막 조회 시각에 더하면 이미 지난 시각이 나온다.
    /// 화면이 이 값으로 카운트다운을 그린다.
    /// </summary>
    public DateTimeOffset? NextPollAt
    {
        get
        {
            // 꽂아 넣은 값이 있으면 그것이 먼저다. 렌더에서는 조회를 한 번도 안 걸어서
            // 아래 계산이 늘 null 이 되고, 카운트다운 줄만 통째로 빈다.
            if (previewNextPollAt is { } preview) return preview;
            if (backoffUntil is { } until && until > time.GetUtcNow()) return until;
            return lastAttemptAt is { } at ? at + PollInterval : null;
        }
    }

    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (IsRefreshing) return;
        // 손으로 새로고침한 것은 물러나기를 무시한다. 사용자가 기다리고 있다.
        if (!force && backoffUntil is { } until && time.GetUtcNow() < until) return;
        // **바닥은 force 로도 못 뚫는다.** 위의 물러나기와 다른 물건이다.
        if (!CanFetchNow) return;

        lastFetchAt = time.GetUtcNow();
        IsRefreshing = true;
        Changed?.Invoke();
        try
        {
            var result = await api.FetchAsync(cancellationToken).ConfigureAwait(false);
            Apply(result);
        }
        finally
        {
            IsRefreshing = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 조회 사이 최소 간격.
    ///
    /// **<c>force</c> 로도 못 뚫는 바닥이다.** 새로고침 버튼·절전 복귀·화면 켜짐이
    /// 겹치면 몇 초 안에 여러 번 나가는데, 사용량 API 는 창이 좁아서 그것만으로 429 가 된다.
    ///
    /// 429 를 맞은 뒤 쉬는 백오프와 **다른 물건이다** — 저쪽은 맞고 나서 물러서는 것이고
    /// 이건 맞기 전에 막는 것이다. <c>force</c> 가 백오프를 무시하도록 둔 이유(재로그인
    /// 직후처럼 사람이 상황을 바꾼 뒤엔 바로 봐야 한다)는 그대로 살아 있다.
    /// </summary>
    public static readonly TimeSpan MinFetchInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 마지막으로 조회를 **내보낸** 시각. 성공·실패를 가리지 않는다 — 실패한 요청도
    /// 서버 쪽 계산에는 똑같이 들어간다.
    /// </summary>
    private DateTimeOffset? lastFetchAt;

    /// <summary>
    /// 다음 조회까지 남은 시간. 0 이면 지금 할 수 있다.
    ///
    /// **눌렀는데 아무 일도 안 일어나면 고장으로 보인다.** 버튼에 숫자로 보여준다.
    /// </summary>
    public TimeSpan FetchCooldown()
    {
        if (lastFetchAt is not { } last) return TimeSpan.Zero;
        var left = MinFetchInterval - (time.GetUtcNow() - last);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>지금 조회를 내보낼 수 있는지. 버튼을 잠그는 데도 쓴다.</summary>
    public bool CanFetchNow => FetchCooldown() <= TimeSpan.Zero;

    /// <summary>결과를 상태에 반영한다. 테스트가 네트워크 없이 이걸 직접 부른다.</summary>
    public void Apply(UsageResult result)
    {
        // 성공이든 실패든 한 번 걸었다. 다음 조회 시각은 여기서부터 센다.
        lastAttemptAt = time.GetUtcNow();
        // 꽂아 둔 예정 시각은 여기서 버린다. 안 그러면 실제로 도는 앱에서도 화면이
        // 영영 같은 시간을 가리킨다.
        previewNextPollAt = null;

        if (result.Snapshot is { } snapshot)
        {
            Snapshot = snapshot;
            ErrorText = null;
            NeedsReauth = false;
            backoffUntil = null;
            consecutiveRateLimits = 0;
            return;
        }

        var error = result.Error!;
        ErrorText = error.Message;
        NeedsReauth = error.Kind is UsageErrorKind.NoCredentials or UsageErrorKind.TokenExpired;

        if (error.Kind == UsageErrorKind.RateLimited)
        {
            consecutiveRateLimits++;
            // **조회 주기와 떼어낸 고정 사다리다** — 60·120·240초, 최대 5분. 맥과 같은 기준.
            //
            // 예전에는 밑값이 사용자가 고른 조회 주기였는데, 그게 거꾸로였다. 자주 보도록
            // 설정한 사람일수록 429 를 맞기 쉬운데 옛 식은 **그 사람만 빨리 돌아오게 하고**
            // 드물게 보는 사람(30분 주기)은 첫 429 에 곧바로 30분을 재웠다. 막힌 정도는
            // 서버 사정이지 우리가 얼마나 자주 보느냐가 아니다.
            var ladder = TimeSpan.FromSeconds(Math.Min(
                RateLimitBackoffBase.TotalSeconds * Math.Pow(2, consecutiveRateLimits - 1),
                MaxRateLimitBackoff.TotalSeconds));
            // 서버가 알려준 시간은 **더 길 때만** 따른다. 5초를 주더라도 우리 바닥은 지킨다.
            // HTTP-date 로 이미 지난 시각이 오면 음수 TimeSpan 이 되는데, 이 비교가 그것도
            // 같이 걸러 준다 — 음수는 사다리보다 짧아서 그냥 무시된다.
            var hinted = error.RetryAfter is { } after && after > ladder ? after : ladder;
            backoffUntil = time.GetUtcNow() + hinted;
        }
        else
        {
            consecutiveRateLimits = 0;
            backoffUntil = null;
        }
    }

    /// <summary>메뉴 맨 위에 띄우는 한 줄.</summary>
    public string SummaryText()
    {
        if (Snapshot is not { } snapshot) return ErrorText ?? "사용량 불러오는 중…";

        var now = time.GetUtcNow();
        var parts = new List<string>();
        if (snapshot.PlanName is { } plan) parts.Add(plan);
        if (snapshot.FiveHour is { } fiveHour)
        {
            parts.Add($"세션 {Math.Round(fiveHour.Utilization):F0}%{ResetSuffix(fiveHour.ResetsAt, now)}");
        }
        if (snapshot.SevenDay is { } sevenDay)
        {
            parts.Add($"주간 {Math.Round(sevenDay.Utilization):F0}%{ResetSuffix(sevenDay.ResetsAt, now)}");
        }
        if (ErrorText is { } error) parts.Add($"(갱신 실패: {error})");

        return parts.Count == 0 ? "사용량 정보 없음" : string.Join(" · ", parts);
    }

    private static string ResetSuffix(DateTimeOffset? resetsAt, DateTimeOffset now) =>
        resetsAt is null ? "" : $" ({RemainingTime.Text(resetsAt, now)})";
}
