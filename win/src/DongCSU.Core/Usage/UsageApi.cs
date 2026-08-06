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
public sealed class UsageApi(HttpClient http, CredentialStore credentials, TimeProvider? time = null)
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
        if (credential.IsExpired(time.GetUtcNow())) return UsageResult.Fail(UsageError.TokenExpired());

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
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
            return UsageResult.Fail(UsageError.Network("시간 초과"));
        }
        catch (HttpRequestException error)
        {
            return UsageResult.Fail(UsageError.Network(error.Message));
        }

        using (response)
        {
            switch ((int)response.StatusCode)
            {
                case 200:
                    break;
                case 401:
                case 403:
                    // 서버가 거절했으면 들고 있던 토큰은 죽은 것이다. 다음엔 파일을 다시 읽는다.
                    credentials.Invalidate();
                    return UsageResult.Fail(UsageError.TokenExpired());
                case 429:
                    return UsageResult.Fail(UsageError.RateLimited(RetryAfter(response)));
                default:
                    return UsageResult.Fail(UsageError.Http((int)response.StatusCode));
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException error)
            {
                return UsageResult.Fail(UsageError.Network(error.Message));
            }

            return Parse(body, credential.SubscriptionType, time.GetUtcNow());
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
