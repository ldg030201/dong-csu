using System.Text.Json;

namespace DongCSU.Core.Usage;

/// <summary>Claude Code 가 저장해 둔 OAuth 자격 증명.</summary>
public sealed record ClaudeCredentials
{
    public required string AccessToken { get; init; }
    public string? SubscriptionType { get; init; }

    /// <summary><c>default_claude_max_5x</c> 처럼 몇 배 플랜인지까지 들어 있다. 계정 탭이 쓴다.</summary>
    public string? RateLimitTier { get; init; }

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
    public static ClaudeCredentials? Parse(string json) => Examine(json).Credentials;

    /// <summary>
    /// 읽어 보고 **안 되면 왜 안 되는지**까지 돌려준다.
    ///
    /// 돌려주는 <c>Keys</c> 는 최상위 키 **이름**뿐이다. 값은 담지 않는다 — 그대로
    /// 기록 파일로 나가는 값이라 토큰이 섞이면 안 된다.
    /// </summary>
    public static (ClaudeCredentials? Credentials, CredentialProblem Problem, string? Keys) Examine(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, CredentialProblem.NotJson, null);
            }

            var keys = string.Join(", ", document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Take(8));

            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                // MCP 토큰만 든 파일이 이렇다 — 파일은 있는데 Claude 로그인은 없다.
                return (null, CredentialProblem.NoClaudeLogin, keys);
            }

            if (!oauth.TryGetProperty("accessToken", out var tokenElement))
            {
                return (null, CredentialProblem.NoAccessToken, keys);
            }

            var token = tokenElement.GetString();
            if (string.IsNullOrEmpty(token))
            {
                return (null, CredentialProblem.NoAccessToken, keys);
            }

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

            var credentials = new ClaudeCredentials
            {
                AccessToken = token,
                SubscriptionType = oauth.TryGetProperty("subscriptionType", out var sub)
                    ? sub.GetString()
                    : null,
                RateLimitTier = oauth.TryGetProperty("rateLimitTier", out var tier)
                    ? tier.GetString()
                    : null,
                ExpiresAt = expiresAt,
                RefreshToken = oauth.TryGetProperty("refreshToken", out var refresh)
                    ? refresh.GetString()
                    : null,
            };
            return (credentials, CredentialProblem.None, keys);
        }
        catch (JsonException)
        {
            return (null, CredentialProblem.NotJson, null);
        }
    }
}

/// <summary>
/// 자격 증명 파일 하나를 살펴본 결과.
///
/// **왜 실패했는지를 들고 다닌다.** 예전에는 못 읽으면 그냥 null 이라, 사용자가 보낸
/// 기록에 "자격 증명 읽기 실패" 한 줄만 남았다. 파일이 없는 것과, 있는데 Claude 로그인이
/// 안 들어 있는 것과, 형식이 깨진 것은 사용자가 할 일이 전혀 다르다.
/// </summary>
public enum CredentialProblem
{
    None,
    /// <summary>그 자리에 파일이 없다.</summary>
    NotFound,
    /// <summary>열지 못했다(권한·잠김). WSL 이 꺼져 있을 때도 여기로 온다.</summary>
    Unreadable,
    /// <summary>JSON 이 아니다.</summary>
    NotJson,
    /// <summary>JSON 이긴 한데 <c>claudeAiOauth</c> 가 없다. MCP 토큰만 든 파일이 이렇다.</summary>
    NoClaudeLogin,
    /// <summary><c>claudeAiOauth</c> 는 있는데 <c>accessToken</c> 이 비어 있다.</summary>
    NoAccessToken,
}

/// <param name="Path">살펴본 자리.</param>
/// <param name="Keys">파일에 있던 최상위 키 이름들. **값은 절대 담지 않는다.**</param>
public sealed record CredentialAttempt(
    string Path,
    CredentialProblem Problem,
    ClaudeCredentials? Credentials = null,
    string? Keys = null)
{
    public bool Found => Credentials is not null;

    /// <summary>사용자에게 보여줄 한마디.</summary>
    public string Describe() => Problem switch
    {
        CredentialProblem.None => "읽었습니다",
        CredentialProblem.NotFound => "파일이 없습니다",
        CredentialProblem.Unreadable => "파일을 열지 못했습니다",
        CredentialProblem.NotJson => "형식이 JSON 이 아닙니다",
        CredentialProblem.NoClaudeLogin => Keys is { Length: > 0 }
            ? $"Claude 로그인이 안 들어 있습니다 (있는 항목: {Keys})"
            : "Claude 로그인이 안 들어 있습니다",
        _ => "로그인 정보가 비어 있습니다",
    };
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

    /// <summary>살펴본 자리를 전부 돌려준다. 기록과 계정 화면이 이걸 쓴다.</summary>
    IReadOnlyList<CredentialAttempt> Inspect() => [];
}

/// <summary>
/// Claude Code 설정 폴더의 <c>.credentials.json</c> 을 읽는다.
///
/// **한 자리만 보지 않는다.** 같은 앱을 쓰는 방식이 사람마다 달라서 파일이 사는 곳도
/// 갈린다 — 윈도우 홈, <c>CLAUDE_CONFIG_DIR</c> 로 옮긴 자리, 그리고 **WSL 안의 리눅스 홈**.
/// WSL 에서 Claude Code 를 쓰는 사람은 윈도우 홈에 아무것도 없다.
/// </summary>
/// <param name="searchPaths">먼저 볼 자리. 안 주면 <see cref="FileCredentialSource.DefaultPaths"/>.</param>
/// <param name="fallbackPaths">
/// 앞에서 못 찾았을 때만 볼 자리. WSL 처럼 **들여다보는 것 자체가 비싼** 곳을 여기 둔다.
/// Core 는 WSL 배포판 이름을 알 방법이 없어서(레지스트리는 윈도우 전용) 화면 쪽이 넣어 준다.
/// </param>
public sealed class FileCredentialSource(
    IEnumerable<string>? searchPaths = null,
    Func<IEnumerable<string>>? fallbackPaths = null) : ICredentialSource
{
    private readonly string[]? fixedPaths = searchPaths?.ToArray();

    /// <summary>
    /// 찾아볼 자리. 앞에 있는 것이 이긴다.
    ///
    /// WSL 은 **맨 뒤**다. <c>\\wsl.localhost</c> 를 건드리면 꺼져 있던 배포판이 깨어나므로,
    /// 윈도우 쪽에서 찾으면 아예 들여다보지 않는다.
    /// </summary>
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

    /// <summary>
    /// WSL 배포판 하나(<c>\\wsl.localhost\Ubuntu</c>) 안에서 찾아볼 자리들.
    ///
    /// **배포판 이름은 여기서 알아낼 수 없다.** <c>\\wsl.localhost\</c> 는 디렉터리로
    /// 나열되지 않아서(이름을 알아야만 열린다) 목록은 레지스트리에 있고, 그건 윈도우
    /// 전용이라 화면 쪽이 넘겨준다.
    ///
    /// 리눅스 쪽 사용자 이름도 모르므로 <c>/root</c> 와 <c>/home</c> 아래를 다 본다.
    /// </summary>
    public static IEnumerable<string> WslPathsUnder(string distroRoot)
    {
        yield return Path.Combine(distroRoot, "root", ".claude", ".credentials.json");

        foreach (var userHome in Subdirectories(Path.Combine(distroRoot, "home")))
        {
            yield return Path.Combine(userHome, ".claude", ".credentials.json");
        }
    }

    /// <summary>없거나 못 들어가면 빈 목록. WSL 이 안 깔린 기계에서 던지면 안 된다.</summary>
    private static string[] Subdirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
        }
        catch (Exception error) when (error is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException)
        {
            return [];
        }
    }

    public ClaudeCredentials? Read() =>
        Inspect().FirstOrDefault(attempt => attempt.Found)?.Credentials;

    public IReadOnlyList<CredentialAttempt> Inspect()
    {
        var attempts = new List<CredentialAttempt>();

        foreach (var path in fixedPaths ?? [.. DefaultPaths()])
        {
            var attempt = Look(path);
            attempts.Add(attempt);
            if (attempt.Found) return attempts;
        }

        // 여기까지 못 찾았을 때만 뒷자리(WSL)를 들여다본다. 그쪽은 꺼져 있던 배포판을
        // 깨우기 때문에, 윈도우 쪽에서 찾으면 아예 건드리지 않는다.
        if (fallbackPaths is null) return attempts;

        foreach (var path in fallbackPaths())
        {
            var attempt = Look(path);
            // 홈이 여러 개일 수 있다. 없는 자리까지 다 적으면 기록이 지저분해진다.
            if (attempt.Problem == CredentialProblem.NotFound) continue;

            attempts.Add(attempt);
            if (attempt.Found) return attempts;
        }

        return attempts;
    }

    private static CredentialAttempt Look(string path)
    {
        string json;
        try
        {
            if (!File.Exists(path)) return new CredentialAttempt(path, CredentialProblem.NotFound);
            json = File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException)
        {
            return new CredentialAttempt(path, CredentialProblem.Unreadable);
        }

        var (credentials, problem, keys) = ClaudeCredentials.Examine(json);
        return new CredentialAttempt(path, problem, credentials, keys);
    }
}

/// <summary>
/// 지금 쓸 자격 증명을 들고 있는다.
///
/// 두 곳에서 온다. **Claude Code 가 적어 둔 파일**(읽기만 한다)과, **우리가 갱신해서
/// 따로 저장해 둔 토큰**이다. 갱신해 둔 것이 살아 있으면 그쪽이 이긴다 — 파일이 만료된
/// 채로 남아 있는 것이 오히려 정상이다. 갱신해 줄 사람이 없어서 우리가 갱신한 것이다.
///
/// **폴링마다 파일을 다시 읽지 않는다.** 윈도우 쪽에서 못 찾으면 WSL 자리까지 훑는데,
/// 거기를 건드리는 것은 꺼져 있던 배포판을 깨우는 **실제로 비싼** 읽기다. 그래서
/// <see cref="FileRereadInterval"/> 이 지난 뒤에만 다시 읽는다.
/// 서버가 401 을 주면 <see cref="Invalidate"/> 로 버린다 — 그때는 바닥도 같이 치운다.
/// </summary>
public sealed class CredentialStore(
    ICredentialSource source,
    TimeProvider? time = null,
    RefreshedTokenStore? refreshedTokens = null)
{
    /// <summary>
    /// 자격 증명 파일을 다시 읽기까지 두는 바닥.
    ///
    /// **파일이 만료돼 있어도 내용은 안 바뀐다** — 갱신해 줄 사람이 없어서 우리가 갱신한
    /// 것이라, 만료됐다는 이유로 다시 읽으면 같은 것을 또 읽을 뿐이다. 조회 주기는
    /// <see cref="AppSettings.PollInterval"/> 에서 최대 30분으로 잘리므로 **한 시간이면
    /// 어떤 설정에서도** 파일 읽기가 실제로 줄어든다.
    ///
    /// 맥은 갱신까지 얹은 결과를 캐시에 담아서 사실상 토큰 수명(여덟 시간)에 한 번만
    /// 키체인을 본다. 우리는 그보다 짧게 잡는다 — 그 사이에 **Claude Code 가 파일을
    /// 갱신해 뒀을 수 있다.**
    /// </summary>
    public static readonly TimeSpan FileRereadInterval = TimeSpan.FromHours(1);

    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly Lock gate = new();
    private ClaudeCredentials? cachedFile;
    private DateTimeOffset? fileReadAt;
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
                RateLimitTier = file?.RateLimitTier,
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
            // 바닥까지 치운다. 401 뒤에는 곧바로 다시 읽어야 Claude Code 가 회전시켜 둔
            // 새 refreshToken 을 늦지 않게 집는다.
            fileReadAt = null;
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
            // **만료 여부가 아니라 마지막으로 읽은 시각으로 판단한다.** 데스크톱 앱만
            // 쓰는 사용자에게는 파일이 늘 만료된 채로 남아 있는 것이 정상이라, 만료를
            // 기준으로 삼으면 조건이 항상 참이 되어 폴링마다 파일을 다시 읽는다.
            if (cachedFile is not null && fileReadAt is { } at && now - at < FileRereadInterval)
            {
                return cachedFile;
            }
        }

        var fresh = source.Read();
        lock (gate)
        {
            cachedFile = fresh;
            // 아직 아무것도 못 읽었으면 바닥을 두지 않는다. 방금 Claude Code 로 로그인한
            // 사람이 한 시간을 기다리게 되는데, 그 경우는 401 도 안 나서 Invalidate() 가
            // 불릴 일조차 없다.
            fileReadAt = fresh is null ? null : now;
        }
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
