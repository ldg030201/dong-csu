using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DongCSU.Core.Usage;

/// <summary>
/// 갱신해서 받은 토큰 한 벌.
///
/// <see cref="ClaudeCredentials"/> 와 따로 두는 이유는 **출처가 다르기 때문**이다.
/// 저쪽은 Claude Code 가 적어 둔 파일에서 읽은 것이고, 이쪽은 우리가 서버에 물어서
/// 받아 온 것이다. 섞어 두면 어느 것을 저장해야 하는지가 흐려진다.
/// </summary>
public sealed record RefreshedToken
{
    public required string AccessToken { get; init; }

    /// <summary>다음 갱신에 쓸 것. 서버가 회전시키면 새 값이 온다.</summary>
    public string? RefreshToken { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>곧 만료될 것은 쓰지 않는다. 쓰려는 순간 만료돼 있으면 헛조회가 된다.</summary>
    public bool IsUsableForAWhile(DateTimeOffset now) =>
        ExpiresAt is not { } at || at - now > TimeSpan.FromMinutes(1);

    /// <summary>토큰 응답 JSON 을 읽는다. 형식이 아니면 null — 던지지 않는다.</summary>
    public static RefreshedToken? Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("access_token", out var accessElement)) return null;
            if (accessElement.GetString() is not { Length: > 0 } accessToken) return null;

            string? refreshToken = null;
            if (root.TryGetProperty("refresh_token", out var refreshElement)
                && refreshElement.GetString() is { Length: > 0 } value)
            {
                refreshToken = value;
            }

            // expires_in 은 **초**다. 밀리초로 읽으면 만료가 한참 뒤로 밀려서,
            // 죽은 토큰을 살아 있다고 믿고 계속 헛조회한다.
            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("expires_in", out var expiresElement)
                && expiresElement.ValueKind == JsonValueKind.Number
                && expiresElement.TryGetDouble(out var seconds)
                && seconds > 0 && seconds < TimeSpan.FromDays(365).TotalSeconds)
            {
                expiresAt = now.AddSeconds(seconds);
            }

            return new RefreshedToken
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// 갱신을 걸어 본 결과. 셋 중 하나다 — 새 토큰을 받았거나, 거절당했거나, 닿지 못했다.
///
/// **왜 실패를 둘로 가르나.** 부르는 쪽은 <see cref="Rejected"/> 일 때만 저장해 둔 토큰을
/// 버려야 한다. 서버가 리프레시 토큰을 회전시킨 뒤라면 우리가 갱신해 둔 것이 **유일한
/// 갱신 수단**인데, 잠깐 끊긴 통신까지 거절로 뭉개면 그것을 버리고 재로그인으로 떨어진다
/// (노트북이 깨어날 때 잘 난다). null 하나로는 그 둘을 가릴 수 없다.
/// </summary>
public readonly record struct RefreshOutcome(RefreshedToken? Token, bool Rejected)
{
    /// <summary>새 토큰을 받았다.</summary>
    public static RefreshOutcome Renewed(RefreshedToken token) => new(token, false);

    /// <summary>
    /// **서버가 그 갱신용 토큰을 거절했다.** 토큰이 죽은 것이 확실하므로 버려도 된다 —
    /// 재로그인 말고는 길이 없다.
    ///
    /// 이름이 <c>Rejected</c> 가 아닌 이유는 같은 이름의 속성이 이미 있어서다.
    /// <c>UsageApi.Attempt.Denied</c> 도 같은 자리에서 같은 이름을 골랐다.
    /// </summary>
    public static RefreshOutcome Denied() => new(null, true);

    /// <summary>
    /// **닿지 못해서 토큰의 생사를 모른다.** 아무것도 버리지 않고 다음에 다시 건다.
    /// </summary>
    public static RefreshOutcome Unreachable() => new(null, false);
}

/// <summary>refreshToken 으로 새 accessToken 을 받아 온다.</summary>
public interface ITokenRefresher
{
    Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Anthropic OAuth 토큰 엔드포인트.
///
/// **왜 필요한가.** Claude Code 가 적어 둔 <c>.credentials.json</c> 의 accessToken 은
/// 몇 시간이면 죽는다. 터미널에서 Claude Code 를 계속 쓰면 그쪽이 파일을 갱신해 주지만,
/// **Claude 데스크톱 앱만 쓰는 사용자에게는 갱신해 줄 사람이 아무도 없다.** 그러면 파일은
/// 영영 만료 상태로 남고 사용량이 다시는 안 나온다. 파일에 refreshToken 이 같이 들어
/// 있으니, 우리가 직접 갱신한다.
///
/// **토큰은 절대 기록하지 않는다.** 성공·실패만 남긴다.
/// </summary>
public sealed class OAuthTokenRefresher(HttpClient http) : ITokenRefresher
{
    public static readonly Uri Endpoint = new("https://console.anthropic.com/v1/oauth/token");

    /// <summary>Claude Code 의 공개 OAuth 클라이언트 ID. 비밀이 아니다.</summary>
    public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private readonly TimeProvider time = TimeProvider.System;

    public async Task<RefreshOutcome> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        // 갱신용 토큰이 애초에 없다. 살릴 것이 없으니 거절과 같이 다룬다.
        if (string.IsNullOrWhiteSpace(refreshToken)) return RefreshOutcome.Denied();

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new RefreshRequest(refreshToken, ClientId)),
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            // 보내지도 못했다. 토큰이 죽었는지 어떤지는 서버만 안다.
            AppLog.Write($"토큰 갱신 실패: 통신 오류 ({error.GetType().Name}) (닿지 못함)");
            return RefreshOutcome.Unreachable();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // **상태 코드로 가른다.** refreshToken 까지 죽었으면 400 이 오고 그때는
                // 재로그인 말고 길이 없다. 다만 408·429 와 5xx 는 서버가 토큰을 거절한
                // 것이 아니라 지금 대답을 못 하는 것이다 — 거절로 뭉개면 서버가 잠깐
                // 흔들린 것만으로 살아 있던 토큰을 버린다.
                var status = (int)response.StatusCode;
                var rejected = status is >= 400 and < 500 and not 408 and not 429;
                AppLog.Write($"토큰 갱신 실패: HTTP {status} ({(rejected ? "거절" : "닿지 못함")})");
                return rejected ? RefreshOutcome.Denied() : RefreshOutcome.Unreachable();
            }

            string body;
            try
            {
                // 200 까지 받았으니 토큰은 살아 있을 가능성이 크다. 버리지 않는다.
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                AppLog.Write("토큰 갱신 실패: 응답을 못 읽음 (닿지 못함)");
                return RefreshOutcome.Unreachable();
            }

            var token = RefreshedToken.Parse(body, time.GetUtcNow());
            if (token is null)
            {
                // 형식이 어긋난 것은 우리 잘못이거나 서버가 바뀐 것이지, 토큰이 죽었다는
                // 뜻이 아니다. 여기서도 버리지 않는다.
                AppLog.Write("토큰 갱신 실패: 응답 형식이 아님 (닿지 못함)");
                return RefreshOutcome.Unreachable();
            }

            AppLog.Write($"토큰 갱신 성공 · 만료 {token.ExpiresAt?.ToString("u") ?? "없음"}");
            return RefreshOutcome.Renewed(token);
        }
    }

    /// <summary>보내는 본문. 토큰이 들어가므로 <c>ToString</c> 을 타지 않게 record 로 두지 않는다.</summary>
    private sealed class RefreshRequest(string refreshToken, string clientId)
    {
        [JsonPropertyName("grant_type")]
        public string GrantType => "refresh_token";

        [JsonPropertyName("refresh_token")]
        public string RefreshToken => refreshToken;

        [JsonPropertyName("client_id")]
        public string ClientId => clientId;
    }
}

/// <summary>
/// 갱신해 둔 토큰을 우리 폴더에 둔다.
///
/// **Claude Code 의 <c>.credentials.json</c> 을 고치지 않는다.** 그쪽은 Claude Code 의
/// 것이고, 둘이 동시에 쓰면 파일이 섞여서 로그인이 통째로 날아갈 수 있다. 우리는 거기서
/// **읽기만** 하고, 갱신한 결과는 여기에 따로 쌓는다.
/// </summary>
public sealed class RefreshedTokenStore(string? path = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string path = path ?? DefaultPath;

    /// <summary>
    /// <c>%APPDATA%\DongCSU\token.json</c>.
    ///
    /// **테스트판도 같은 파일을 쓴다.** 갱신할 때마다 리프레시 토큰이 회전하므로,
    /// 판마다 따로 두면 두 판이 서로의 토큰을 죽인다. 자세한 이유는
    /// <see cref="AppPaths.SharedFile"/> 에 적어 뒀다.
    /// </summary>
    public static string DefaultPath => AppPaths.SharedFile("token.json");

    public RefreshedToken? Read()
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<RefreshedToken>(File.ReadAllText(path), Options);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>저장한다. 실패해도 던지지 않는다 — 다음 실행에서 다시 갱신하면 그만이다.</summary>
    public void Write(RefreshedToken token)
    {
        try
        {
            // **폴더부터 조인다.** 파일 권한은 쓰고 난 뒤에야 바꿀 수 있어서 그 사이가
            // 잠깐 열리는데, 폴더가 닫혀 있으면 그 틈에도 남이 들어오지 못한다.
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && AppPaths.Prepared(directory) is null) return;

            // 쓰는 도중에 앱이 죽으면 반쯤 쓰인 JSON 이 남는다. 바꿔치기한다.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(token, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
