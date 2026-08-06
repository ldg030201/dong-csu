namespace DongCSU.Core.Usage;

/// <summary>5시간 · 7일 사용량 창 하나.</summary>
/// <param name="Utilization">0–100 으로 자른 사용률.</param>
/// <param name="ResetsAt">초기화 시각. 서버가 안 주면 null.</param>
public readonly record struct UsageWindow(double Utilization, DateTimeOffset? ResetsAt);

public sealed record UsageSnapshot
{
    public string? PlanName { get; init; }
    public UsageWindow? FiveHour { get; init; }
    public UsageWindow? SevenDay { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
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
