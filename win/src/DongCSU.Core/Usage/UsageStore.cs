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
    private int consecutiveRateLimits;

    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    public UsageSnapshot? Snapshot { get; private set; }
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

    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (IsRefreshing) return;
        // 손으로 새로고침한 것은 물러나기를 무시한다. 사용자가 기다리고 있다.
        if (!force && backoffUntil is { } until && time.GetUtcNow() < until) return;

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

    /// <summary>결과를 상태에 반영한다. 테스트가 네트워크 없이 이걸 직접 부른다.</summary>
    public void Apply(UsageResult result)
    {
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
