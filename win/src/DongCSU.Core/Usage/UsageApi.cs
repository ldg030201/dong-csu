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

        var attempt = await SendAsync(credential.AccessToken, cancellationToken).ConfigureAwait(false);
        if (attempt.Error is { } failure) return UsageResult.Fail(failure);

        // **서버가 거절하면 갱신해서 딱 한 번만 다시 건다.**
        //
        // 데스크톱 앱만 쓰는 사용자에게는 `.credentials.json` 을 갱신해 줄 사람이 없어서,
        // 여기서 갱신하지 않으면 그 파일이 만료된 뒤로 사용량이 **다시는** 안 나온다.
        if (attempt.Rejected)
        {
            var renewed = credential.RefreshToken is { } refreshToken && refresher is not null
                ? await refresher.RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false)
                : null;

            if (renewed is null)
            {
                credentials.DiscardRefreshed();
                credentials.Invalidate();
                return UsageResult.Fail(UsageError.TokenExpired(looksExpired));
            }

            credentials.ApplyRefreshed(renewed);

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

        return Parse(attempt.Body ?? "", credential.SubscriptionType, time.GetUtcNow());
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
    public static UsageResult Parse(string body, string? subscriptionType, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return UsageResult.Fail(UsageError.Decode());

            return UsageResult.Ok(new UsageSnapshot
            {
                PlanName = ClaudeCredentials.PlanName(subscriptionType),
                FiveHour = Window(root, "five_hour"),
                SevenDay = Window(root, "seven_day"),
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

        DateTimeOffset? resetsAt = null;
        if (element.TryGetProperty("resets_at", out var reset) && reset.GetString() is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            resetsAt = parsed;
        }

        return new UsageWindow(Math.Clamp(utilization, 0, 100), resetsAt);
    }
}
