using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DongCSU.Core.Usage;

/// <summary>
/// Anthropic 사용량 API.
///
/// **토큰은 Authorization 헤더로만 쓴다.** 디스크에 쓰거나 로그에 남기지 않는다.
/// 접속하는 곳은 이 주소 하나뿐이다.
/// </summary>
public sealed class UsageApi(
    HttpClient http,
    CredentialStore credentials,
    TimeProvider? time = null,
    ITokenRefresher? refresher = null)
{
    public static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/usage");

    private readonly TimeProvider time = time ?? TimeProvider.System;

    /// <summary>
    /// 갱신 요청 사이 최소 간격.
    ///
    /// 갱신은 사용량 조회 안에서만 일어나므로 대개 그쪽 바닥에 함께 걸린다. 다만
    /// **갱신용 토큰까지 죽으면 조회마다 갱신을 다시 시도하게 되어**, 조회를 막아도
    /// 갱신 쪽만 계속 나갈 수 있다. 여기서도 한 번 더 막는다.
    /// </summary>
    public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(10);

    private DateTimeOffset? lastRefreshAt;

    private bool CanRefreshNow()
    {
        var now = time.GetUtcNow();
        if (lastRefreshAt is { } last && now - last < MinRefreshInterval) return false;
        lastRefreshAt = now;
        return true;
    }

    /// <summary>기본 설정의 HttpClient. 앱 전체가 하나를 돌려 쓴다.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        // 매번 서버 값을 받아야 한다. 캐시된 응답이 오면 새로고침이 안 먹는 것처럼 보인다.
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public async Task<UsageResult> FetchAsync(CancellationToken cancellationToken = default)
    {
        var credential = credentials.Current();
        if (credential is null) return UsageResult.Fail(UsageError.NoCredentials());

        // **파일에 적힌 만료 시각만 보고 포기하지 않는다.**
        //
        // Claude Code 는 토큰을 메모리에서 갱신하고 `.credentials.json` 을 곧바로 다시
        // 쓰지 않는다. 그래서 Claude 가 멀쩡히 도는 중에도 파일은 만료로 보일 수 있다.
        // 여기서 잘라 버리면 조회를 **한 번도 시도하지 않고** "토큰 만료"만 띄운다
        // (1.1.0 에서 실제로 그랬다). 유효한지는 서버가 안다 — 걸어 보고 401 이면 그때 만료다.
        var looksExpired = credential.IsExpired(time.GetUtcNow());

        // **스냅숏에는 실제로 통한 자격 증명을 싣는다.** 갱신에 성공하면 아래에서 갈아
        // 끼운다 — 파일에서 읽은 것을 그대로 넘기면 만료 시각이 옛것으로 남는다.
        var effective = credential;

        var attempt = await SendAsync(credential.AccessToken, cancellationToken).ConfigureAwait(false);
        if (attempt.Error is { } failure) return UsageResult.Fail(failure);

        // **서버가 거절하면 갱신해서 딱 한 번만 다시 건다.**
        //
        // 데스크톱 앱만 쓰는 사용자에게는 `.credentials.json` 을 갱신해 줄 사람이 없어서,
        // 여기서 갱신하지 않으면 그 파일이 만료된 뒤로 사용량이 **다시는** 안 나온다.
        if (attempt.Rejected)
        {
            // **갱신에도 바닥을 깐다.** 대개는 조회 쪽 바닥에 함께 걸리지만, 갱신용
            // 토큰까지 죽으면 조회마다 갱신을 다시 시도하게 되어 갱신 쪽만 계속 나간다.
            var outcome = credential.RefreshToken is { } refreshToken && refresher is not null
                && CanRefreshNow()
                ? await refresher.RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false)
                // 갱신을 걸어 볼 수단이 없다. **못 닿은 것이 아니므로** 예전처럼 만료로
                // 끝낸다 — 물러나 봐야 다음 조회에서도 똑같이 못 건다.
                : RefreshOutcome.Denied();

            if (outcome.Token is not { } renewed)
            {
                // **거절당한 것과 닿지 못한 것을 가른다.**
                //
                // 서버가 리프레시 토큰을 회전시킨 뒤라면 `.credentials.json` 에 남은 것은
                // 이미 죽어 있고, 우리가 갱신해 둔 토큰이 **살아 있는 유일한 갱신 수단**이다.
                // 통신이 잠깐 끊긴 것까지 거절로 세면 그 하나를 버리고 재로그인으로 떨어진다
                // (노트북이 깨어나 랜이 아직 안 붙었을 때 잘 난다).
                //
                // 못 닿은 것은 만료가 아니므로 Network 로 돌려준다 — IsTerminal 이 false 라
                // UsageStore.Apply 가 NeedsReauth 를 세우지 않고 다음 폴링에 다시 건다.
                // **여기서는 아무것도 지우지 않는다.** Invalidate() 를 남겨 두면 파일 재읽기
                // 바닥까지 매번 뚫린다.
                if (!outcome.Rejected)
                {
                    return UsageResult.Fail(UsageError.Network("토큰 갱신에 닿지 못함"));
                }

                credentials.DiscardRefreshed();
                credentials.Invalidate();
                return UsageResult.Fail(UsageError.TokenExpired(looksExpired));
            }

            credentials.ApplyRefreshed(renewed);

            // **여기서 갈아 끼우지 않으면 계정 탭이 방금 갱신한 토큰을 두고 한 주기(기본
            // 10분) 동안 "만료됨" 이라고 말한다.** 스냅숏의 만료 시각은 파일이 아니라
            // 실제로 쓴 토큰의 것이어야 한다. 새로 만들지 않고 `with` 를 쓰는 이유는
            // 플랜·한도 등급이 갱신 응답에 안 와서 파일 쪽 값이 그대로 따라와야 해서다.
            effective = credential with
            {
                AccessToken = renewed.AccessToken,
                ExpiresAt = renewed.ExpiresAt,
                RefreshToken = renewed.RefreshToken ?? credential.RefreshToken,
            };

            attempt = await SendAsync(renewed.AccessToken, cancellationToken).ConfigureAwait(false);
            if (attempt.Error is { } retryFailure) return UsageResult.Fail(retryFailure);

            if (attempt.Rejected)
            {
                // 갓 갱신한 토큰까지 거절당했다. 재로그인 말고는 길이 없다.
                // 지워 두지 않으면 죽은 토큰을 살아 있다고 믿고 영원히 헛조회한다.
                credentials.DiscardRefreshed();
                credentials.Invalidate();
                return UsageResult.Fail(UsageError.TokenExpired(looksExpired));
            }
        }

        return Parse(attempt.Body ?? "", effective, time.GetUtcNow());
    }

    /// <summary>한 번 걸어 본 결과. 셋 중 하나다 — 본문을 받았거나, 거절당했거나, 실패했다.</summary>
    private readonly record struct Attempt(string? Body, bool Rejected, UsageError? Error)
    {
        public static Attempt Ok(string body) => new(body, false, null);
        public static Attempt Denied() => new(null, true, null);
        public static Attempt Failed(UsageError error) => new(null, false, error);
    }

    private async Task<Attempt> SendAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        request.Headers.TryAddWithoutValidation("User-Agent", "claude-code/2.1");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Attempt.Failed(UsageError.Network("시간 초과"));
        }
        catch (HttpRequestException error)
        {
            return Attempt.Failed(UsageError.Network(error.Message));
        }

        using (response)
        {
            switch ((int)response.StatusCode)
            {
                case 200:
                    break;
                case 401:
                case 403:
                    return Attempt.Denied();
                case 429:
                    return Attempt.Failed(UsageError.RateLimited(RetryAfter(response)));
                default:
                    return Attempt.Failed(UsageError.Http((int)response.StatusCode));
            }

            try
            {
                return Attempt.Ok(
                    await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (HttpRequestException error)
            {
                return Attempt.Failed(UsageError.Network(error.Message));
            }
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta is not null) return delta;

        var at = response.Headers.RetryAfter?.Date;
        return at is null ? null : at - DateTimeOffset.UtcNow;
    }

    /// <summary>응답 본문을 스냅숏으로. 형식이 아니면 <see cref="UsageErrorKind.Decode"/>.</summary>
    public static UsageResult Parse(string body, ClaudeCredentials credential, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return UsageResult.Fail(UsageError.Decode());

            return UsageResult.Ok(new UsageSnapshot
            {
                PlanName = ClaudeCredentials.PlanName(credential.SubscriptionType),
                RateLimitTier = credential.RateLimitTier,
                TokenExpiresAt = credential.ExpiresAt,
                FiveHour = Window(root, "five_hour"),
                SevenDay = Window(root, "seven_day"),
                Limits = ParseLimits(root),
                FetchedAt = now,
            });
        }
        catch (JsonException)
        {
            return UsageResult.Fail(UsageError.Decode());
        }
    }

    private static UsageWindow? Window(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty("utilization", out var raw)
            || raw.ValueKind != JsonValueKind.Number
            || !raw.TryGetDouble(out var utilization)
            || double.IsNaN(utilization) || double.IsInfinity(utilization))
        {
            return null;
        }

        return new UsageWindow(Math.Clamp(utilization, 0, 100), ResetsAt(element));
    }

    /// <summary>
    /// <c>resets_at</c> 하나를 읽는다.
    ///
    /// **창과 한도가 같은 함수를 쓴다.** 두 곳에 적어 두면 서버가 형식을 바꿨을 때
    /// 한쪽만 고치게 되고, 그 한쪽은 조용히 null 이 되어 카운트다운만 사라진다.
    /// </summary>
    private static DateTimeOffset? ResetsAt(JsonElement owner)
    {
        // GetString() 은 ValueKind 가 String 이 아니면 **던진다.** 먼저 본다.
        if (!owner.TryGetProperty("resets_at", out var element)
            || element.ValueKind != JsonValueKind.String
            || element.GetString() is not { } text)
        {
            return null;
        }

        // AdjustToUniversal|AssumeUniversal 을 빼면 `Z` 없는 문자열이 로컬 시각으로
        // 읽혀 시간대만큼 통째로 어긋난다.
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// 응답 최상위의 <c>limits</c> 배열.
    ///
    /// **원소 하나가 이상해도 나머지는 살린다** — 서버가 낯선 항목을 하나 끼웠다고 한도가
    /// 통째로 사라지면 측정이 그 순간부터 아무것도 못 센다. <c>kind</c> 와 <c>percent</c>
    /// 를 둘 다 읽은 원소만 받고 나머지는 버린다(맥과 같다).
    /// </summary>
    private static IReadOnlyList<UsageLimit> ParseLimits(JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            // 옛 응답에는 이 배열이 아예 없다. 그것도 정상이다.
            return [];
        }

        var limits = new List<UsageLimit>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            if (!element.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || kind.GetString() is not { Length: > 0 } name)
            {
                continue;
            }

            if (!element.TryGetProperty("percent", out var raw)
                || raw.ValueKind != JsonValueKind.Number
                || !raw.TryGetDouble(out var percent)
                || double.IsNaN(percent) || double.IsInfinity(percent))
            {
                continue;
            }

            limits.Add(new UsageLimit
            {
                Kind = name,
                ModelName = ModelName(element),
                Percent = Math.Clamp(percent, 0, 100),
                ResetsAt = ResetsAt(element),
            });
        }

        return limits;
    }

    /// <summary><c>scope.model.display_name</c>. 모델별로 갈린 한도에만 있다.</summary>
    private static string? ModelName(JsonElement limit)
    {
        if (!limit.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object
            || !scope.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object
            || !model.TryGetProperty("display_name", out var name)
            || name.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        // 빈 문자열은 없는 것으로 눕힌다. 그대로 두면 Id 가 `weekly_scoped/` 로,
        // 제목이 `주간 · ` 로 꼬리가 빈 채 화면에 나간다.
        var text = name.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
