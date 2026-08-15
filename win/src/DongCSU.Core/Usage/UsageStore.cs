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

    private readonly TimeProvider time = time ?? TimeProvider.System;
    private DateTimeOffset? backoffUntil;
    private DateTimeOffset? lastAttemptAt;
    private int consecutiveRateLimits;

    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    public UsageSnapshot? Snapshot { get; private set; }

    /// <summary>
    /// 값을 직접 꽂아 넣는다. **테스트와 렌더 확인 전용** — 네트워크 없이 화면을 채운다.
    /// 맥의 <c>init(preview:)</c> 와 같은 자리다.
    /// </summary>
    public void Preview(UsageSnapshot? snapshot, string? error = null, bool needsReauth = false)
    {
        Snapshot = snapshot;
        ErrorText = error;
        NeedsReauth = needsReauth;
        Changed?.Invoke();
    }
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
            // 서버가 시간을 알려주면 그대로 따르고, 아니면 주기를 배로 늘려 가며 물러난다.
            // 상한을 두지 않으면 한 번 막혔을 때 영영 안 돌아온다.
            var backoff = error.RetryAfter
                ?? TimeSpan.FromSeconds(Math.Min(
                    PollInterval.TotalSeconds * Math.Pow(2, consecutiveRateLimits),
                    TimeSpan.FromMinutes(30).TotalSeconds));
            backoffUntil = time.GetUtcNow() + backoff;
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
