using System.Text.Json;

namespace DongCSU.Core.Usage;

/// <summary>Claude Code 가 저장해 둔 OAuth 자격 증명.</summary>
public sealed record ClaudeCredentials
{
    public required string AccessToken { get; init; }
    public string? SubscriptionType { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// 만료된 accessToken 을 되살릴 때 쓰는 것. 없을 수도 있다.
    ///
    /// **이게 있어야 Claude Code 없이도 계속 돈다.** 데스크톱 앱만 쓰는 사용자에게는
    /// <c>.credentials.json</c> 을 갱신해 줄 사람이 아무도 없어서, 이것 없이는 파일이
    /// 한 번 만료되면 사용량이 다시는 안 나온다.
    /// </summary>
    public string? RefreshToken { get; init; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } at && at <= now;

    /// <summary>곧 만료될 것은 캐시에 두지 않는다. 쓰려는 순간 만료돼 있으면 헛조회가 된다.</summary>
    public bool IsUsableForAWhile(DateTimeOffset now) =>
        ExpiresAt is not { } at || at - now > TimeSpan.FromMinutes(1);

    /// <summary>subscriptionType → 화면에 쓸 플랜 이름. API 사용자는 null.</summary>
    public static string? PlanName(string? subscriptionType)
    {
        var raw = subscriptionType?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;

        var lower = raw.ToLowerInvariant();
        if (lower.Contains("max")) return "Max";
        if (lower.Contains("pro")) return "Pro";
        if (lower.Contains("team")) return "Team";
        if (lower.Contains("api")) return null;
        return char.ToUpperInvariant(raw[0]) + raw[1..];
    }

    /// <summary>
    /// 자격 증명 JSON 을 읽는다. 형식이 아니면 null — 던지지 않는다.
    ///
    /// 맥은 keychain 에, 윈도우는 **파일**에 들어 있다. 담긴 JSON 모양은 같다.
    /// </summary>
    public static ClaudeCredentials? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth)) return null;
            if (!oauth.TryGetProperty("accessToken", out var tokenElement)) return null;

            var token = tokenElement.GetString();
            if (string.IsNullOrEmpty(token)) return null;

            // 밀리초다. 초로 읽으면 1970년대가 나와서 항상 만료로 판정된다.
            // 정수로만 받으면 안 된다 — 맥 쪽은 Double 로 읽고 있어서, 소수점이 붙어
            // 오면 이쪽만 터진다. 문자열로 오는 경우까지 함께 받아 준다.
            DateTimeOffset? expiresAt = null;
            if (oauth.TryGetProperty("expiresAt", out var expires))
            {
                double? milliseconds = expires.ValueKind switch
                {
                    JsonValueKind.Number when expires.TryGetDouble(out var number) => number,
                    JsonValueKind.String when double.TryParse(
                        expires.GetString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var text) => text,
                    _ => null,
                };

                // FromUnixTimeMilliseconds 가 받는 범위 밖이면 던진다. 걸러 낸다.
                const double maxMilliseconds = 2.5e14;   // 서기 9999년쯤
                if (milliseconds is { } value && value > 0 && value < maxMilliseconds)
                {
                    expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)value);
                }
            }

            return new ClaudeCredentials
            {
                AccessToken = token,
                SubscriptionType = oauth.TryGetProperty("subscriptionType", out var sub)
                    ? sub.GetString()
                    : null,
                ExpiresAt = expiresAt,
                RefreshToken = oauth.TryGetProperty("refreshToken", out var refresh)
                    ? refresh.GetString()
                    : null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// 자격 증명을 어디서 읽을지.
///
/// 맥은 keychain 을 쓰지만 윈도우에는 그런 게 없어서 Claude Code 가 **파일**에 적어 둔다.
/// 그래서 이쪽은 훨씬 단순하다 — 프로세스를 띄울 필요도, 접근 허용을 물을 필요도 없다.
/// </summary>
public interface ICredentialSource
{
    ClaudeCredentials? Read();
}

/// <summary>Claude Code 설정 폴더의 <c>.credentials.json</c> 을 읽는다.</summary>
public sealed class FileCredentialSource(IEnumerable<string>? searchPaths = null) : ICredentialSource
{
    private readonly string[] paths = (searchPaths ?? DefaultPaths()).ToArray();

    /// <summary>찾아볼 자리. 앞에 있는 것이 이긴다.</summary>
    public static IEnumerable<string> DefaultPaths()
    {
        // CLAUDE_CONFIG_DIR 을 쓰면 설정 폴더 자체가 옮겨진다. 그쪽이 우선이다.
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")?.Trim();
        if (!string.IsNullOrEmpty(configDir))
        {
            yield return Path.Combine(configDir, ".credentials.json");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".claude", ".credentials.json");
        }
    }

    public ClaudeCredentials? Read()
    {
        foreach (var path in paths)
        {
            string json;
            try
            {
                if (!File.Exists(path)) continue;
                json = File.ReadAllText(path);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            if (ClaudeCredentials.Parse(json) is { } credentials) return credentials;
        }
        return null;
    }
}

/// <summary>
/// 지금 쓸 자격 증명을 들고 있는다.
///
/// 두 곳에서 온다. **Claude Code 가 적어 둔 파일**(읽기만 한다)과, **우리가 갱신해서
/// 따로 저장해 둔 토큰**이다. 갱신해 둔 것이 살아 있으면 그쪽이 이긴다 — 파일이 만료된
/// 채로 남아 있는 것이 오히려 정상이다. 갱신해 줄 사람이 없어서 우리가 갱신한 것이다.
///
/// 파일 읽기가 비싸지는 않지만 폴링마다 디스크를 두드릴 이유는 없다.
/// 서버가 401 을 주면 <see cref="Invalidate"/> 로 버린다.
/// </summary>
public sealed class CredentialStore(
    ICredentialSource source,
    TimeProvider? time = null,
    RefreshedTokenStore? refreshedTokens = null)
{
    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly Lock gate = new();
    private ClaudeCredentials? cachedFile;
    private RefreshedToken? refreshed;
    private bool refreshedLoaded;

    public ClaudeCredentials? Current()
    {
        var file = FromFile();
        var saved = Refreshed();

        if (saved is { } token && token.IsUsableForAWhile(time.GetUtcNow()))
        {
            // 플랜 이름은 갱신 응답에 안 온다. 파일 쪽에서 가져다 붙인다.
            return new ClaudeCredentials
            {
                AccessToken = token.AccessToken,
                SubscriptionType = file?.SubscriptionType,
                ExpiresAt = token.ExpiresAt,
                RefreshToken = token.RefreshToken ?? file?.RefreshToken,
            };
        }

        if (file is null) return null;

        // 갱신해 둔 토큰이 만료됐더라도 **refreshToken 만은 그쪽이 더 새것이다**
        // (서버가 회전시켰다면 파일의 것은 이미 못 쓴다).
        return saved?.RefreshToken is { } newer ? file with { RefreshToken = newer } : file;
    }

    /// <summary>갱신해서 받은 것을 저장하고 곧바로 쓰기 시작한다.</summary>
    public void ApplyRefreshed(RefreshedToken token)
    {
        refreshedTokens?.Write(token);
        lock (gate)
        {
            refreshed = token;
            refreshedLoaded = true;
        }
    }

    /// <summary>들고 있던 것을 버린다. 다음에 다시 읽는다.</summary>
    public void Invalidate()
    {
        lock (gate)
        {
            cachedFile = null;
            refreshed = null;
            refreshedLoaded = false;
        }
    }

    /// <summary>
    /// 갱신해 둔 토큰을 버린다.
    ///
    /// 갱신한 토큰까지 서버가 거절했을 때 부른다. 지워 두지 않으면 만료 시각만 보고
    /// 살아 있다고 믿어서 **죽은 토큰으로 영원히 헛조회한다.**
    ///
    /// 다만 **파일까지 지우는 것은 우리가 쓰던 그 토큰이 아직 거기 있을 때만** 이다.
    /// 두 판(정식·테스트)이 같이 떠 있으면 그 사이에 다른 쪽이 새로 갱신해 뒀을 수
    /// 있는데, 그것까지 지우면 멀쩡한 토큰을 버리고 둘 다 재로그인으로 떨어진다.
    /// </summary>
    public void DiscardRefreshed()
    {
        RefreshedToken? dead;
        lock (gate) { dead = refreshed; }

        if (dead is not null
            && refreshedTokens?.Read() is { } stored
            && stored.AccessToken == dead.AccessToken)
        {
            refreshedTokens.Clear();
        }

        lock (gate)
        {
            refreshed = null;
            // 다음에 파일을 다시 읽는다 — 다른 쪽이 새로 적어 뒀으면 그것을 쓴다.
            refreshedLoaded = false;
        }
    }

    private ClaudeCredentials? FromFile()
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            if (cachedFile is { } value && !value.IsExpired(now) && value.IsUsableForAWhile(now))
            {
                return cachedFile;
            }
        }

        var fresh = source.Read();
        lock (gate) { cachedFile = fresh; }
        return fresh;
    }

    private RefreshedToken? Refreshed()
    {
        lock (gate)
        {
            if (!refreshedLoaded)
            {
                refreshed = refreshedTokens?.Read();
                refreshedLoaded = true;
            }
            return refreshed;
        }
    }
}
