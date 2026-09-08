namespace DongCSU.Core.Usage;

/// <summary>5시간 · 7일 사용량 창 하나.</summary>
/// <param name="Utilization">0–100 으로 자른 사용률.</param>
/// <param name="ResetsAt">초기화 시각. 서버가 안 주면 null.</param>
public readonly record struct UsageWindow(double Utilization, DateTimeOffset? ResetsAt);

/// <summary>
/// 서버가 따로 내려주는 한도 하나.
///
/// <c>five_hour</c>·<c>seven_day</c> 둘만 읽으면 **모델별로 갈린 한도를 놓친다.** 응답의
/// <c>limits</c> 배열에는 그것까지 들어 있다(<c>weekly_scoped</c>). HUD 는 둘만 그리면
/// 되지만 측정 기록은 이쪽을 센다 — 나중에 "오퍼스에 얼마 썼나"를 물을 수 있어야 한다.
/// </summary>
public sealed record UsageLimit
{
    /// <summary><c>session</c> · <c>weekly_all</c> · <c>weekly_scoped</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>모델별 한도일 때만 채워진다.</summary>
    public string? ModelName { get; init; }

    /// <summary>0–100 으로 자른 사용률.</summary>
    public required double Percent { get; init; }

    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>
    /// 창이 새로 열려도 같은 한도를 가리키는 이름. **측정이 이 값으로 기록을 묶는다** —
    /// 초기화 시각이나 차례로 묶으면 창이 넘어갈 때마다 다른 한도가 된다.
    /// </summary>
    public string Id => ModelName is { } model ? $"{Kind}/{model}" : Kind;

    /// <summary>
    /// 링·숫자가 쓰는 꼴. **세 곳에서 같은 두 줄을 적고 있었다** — HUD 링 · HUD 줄 ·
    /// 설정 창 상태 탭이 저마다 <c>new UsageWindow(Percent, ResetsAt)</c> 를 적으면,
    /// 나중에 한도에 칸이 하나 늘 때 한 곳만 빠뜨려도 세 자리가 서로 다른 값을 그린다.
    /// </summary>
    public UsageWindow Window => new(Percent, ResetsAt);

    /// <summary>화면에 쓰는 이름. 모르는 <see cref="Kind"/> 는 원문을 그대로 둔다.</summary>
    public string Title => ModelName is { } model
        ? $"주간 · {model}"
        : Kind switch
        {
            "session" => "세션 (5시간)",
            "weekly_all" => "주간 (7일)",
            _ => Kind,
        };
}

public sealed record UsageSnapshot
{
    public string? PlanName { get; init; }
    public UsageWindow? FiveHour { get; init; }
    public UsageWindow? SevenDay { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }

    /// <summary>
    /// 서버가 준 한도 전부. **옛 응답에는 없어서 빈 목록일 수 있다** — 없다고 던지지 않는다.
    ///
    /// <see cref="FiveHour"/>·<see cref="SevenDay"/> 를 대체하지 않고 **덧붙는다.** HUD 는
    /// 그 둘을 그대로 쓰고, 측정만 모델별로 갈린 것까지 필요해서 이쪽을 본다.
    ///
    /// 이 레코드는 값 비교가 자동이지만 목록 칸은 **참조 비교**가 된다. 내용이 같아도 다른
    /// 스냅숏으로 잡히므로, 나중에 "값이 안 바뀌었으면 다시 안 그린다" 를 넣게 되면
    /// 이 칸을 뺀 비교를 따로 만들어야 한다.
    /// </summary>
    public IReadOnlyList<UsageLimit> Limits { get; init; } = [];

    /// <summary>
    /// 화면에 그릴 모델별 주간 한도(<c>weekly_scoped</c>) 하나. **제일 많이 쓴 것.**
    ///
    /// **세션·주간과 달리 없을 수 있다.** 요금제와 쓴 모델에 따라 달라서, 화면에서도
    /// 있을 때만 그린다. 여럿이면 많이 쓴 것을 준다 — 링은 하나만 그릴 수 있다.
    ///
    /// **<c>Kind</c> 로 거르지 않는다.** 지금은 <c>weekly_scoped</c> 뿐이지만 서버가
    /// 다른 이름으로 모델별을 줘도 잡히도록 <see cref="UsageLimit.ModelName"/> 로 본다.
    ///
    /// <c>MaxBy</c> 는 동점이면 앞의 것을 남긴다 — 맥의 <c>max(by:)</c> 와 같다.
    /// </summary>
    public UsageLimit? ScopedLimit =>
        Limits.Where(limit => limit.ModelName is not null).MaxBy(limit => limit.Percent);

    // 아래 둘은 서버가 아니라 **자격 증명에서 온다.** 계정 탭이 보여준다.
    // 조회할 때 자격 증명을 이미 읽으므로 같이 실어 보내면 따로 읽을 일이 없다.

    /// <summary><c>default_claude_max_5x</c> 같은 요금제 등급 원문.</summary>
    public string? RateLimitTier { get; init; }

    /// <summary>지금 쓰는 액세스 토큰이 언제까지인지.</summary>
    public DateTimeOffset? TokenExpiresAt { get; init; }

    /// <summary>
    /// <c>default_claude_max_5x</c> → <c>Max 5x</c>.
    ///
    /// **못 알아보는 값이면 원문을 그대로 둔다.** 서버가 형태를 바꿨을 때 빈칸이 되는
    /// 것보다, 낯설어도 실제 값이 보이는 편이 낫다.
    /// </summary>
    public static string? TierText(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        // `default_claude_max_5x` 처럼 앞에 붙는 것들을 걷어내고 배수만 남긴다.
        var parts = trimmed.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return trimmed;

        var multiple = parts[^1];
        if (!multiple.EndsWith('x') || multiple.Length < 2
            || !multiple[..^1].All(char.IsDigit))
        {
            return trimmed;
        }

        var plan = parts[^2];
        return $"{char.ToUpperInvariant(plan[0])}{plan[1..]} {multiple}";
    }
}

public enum UsageErrorKind
{
    /// <summary>Claude Code 로그인 정보를 못 찾았다.</summary>
    NoCredentials,
    /// <summary>토큰이 만료됐다. 재로그인해야 한다.</summary>
    TokenExpired,
    /// <summary>429. 잠시 쉬었다 다시 걸어야 한다.</summary>
    RateLimited,
    Http,
    Network,
    Decode,
}

/// <summary>
/// 조회가 실패한 이유.
///
/// 예외를 쓰지 않고 결과로 돌려준다 — 조회 실패는 **정상적인 상태**이고(잠들었다 깨면
/// 늘 한 번 실패한다) 화면이 그걸 그려야 하기 때문이다. 예외로 만들면 부르는 쪽이
/// try 로 감싸 놓고 정작 화면에는 아무것도 못 띄우게 된다.
/// </summary>
public sealed record UsageError(UsageErrorKind Kind, string Message, TimeSpan? RetryAfter = null)
{
    /// <summary>다시 걸어도 결과가 같은 오류인지. 이 경우 폴링을 계속할 값어치가 없다.</summary>
    public bool IsTerminal => Kind is UsageErrorKind.NoCredentials or UsageErrorKind.TokenExpired;

    public static UsageError NoCredentials() =>
        new(UsageErrorKind.NoCredentials, "Claude 로그인 정보 없음");

    /// <param name="fileAlsoSaidExpired">
    /// 자격 증명 파일에 적힌 만료 시각도 지나 있었는지. 서버가 거절한 것과 구분해
    /// 적어 둔다 — 원인이 갈리기 때문이다. 파일만 지나 있고 서버는 받아 주는 경우가
    /// 흔해서, 이 값이 참이어도 그것만으로 만료라고 단정하지 않는다.
    /// </param>
    public static UsageError TokenExpired(bool fileAlsoSaidExpired = false) =>
        new(UsageErrorKind.TokenExpired,
            fileAlsoSaidExpired
                ? "토큰 만료 (파일·서버 모두) — Claude Code 재로그인 필요"
                : "서버가 토큰을 거절함 — Claude Code 재로그인 필요");

    public static UsageError RateLimited(TimeSpan? retryAfter) =>
        new(UsageErrorKind.RateLimited, "요청 제한 (429)", retryAfter);

    public static UsageError Http(int status) =>
        new(UsageErrorKind.Http, $"HTTP {status}");

    public static UsageError Network(string message) =>
        new(UsageErrorKind.Network, $"네트워크: {message}");

    public static UsageError Decode() =>
        new(UsageErrorKind.Decode, "응답 파싱 실패");
}

/// <summary>성공이면 <see cref="Snapshot"/>, 실패면 <see cref="Error"/>. 둘 중 하나만 채워진다.</summary>
public sealed record UsageResult
{
    public UsageSnapshot? Snapshot { get; private init; }
    public UsageError? Error { get; private init; }

    public bool IsSuccess => Snapshot is not null;

    public static UsageResult Ok(UsageSnapshot snapshot) => new() { Snapshot = snapshot };
    public static UsageResult Fail(UsageError error) => new() { Error = error };
}
