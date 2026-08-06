using System.Net;
using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

public class RefreshedTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 갱신_응답을_읽는다()
    {
        const string body = """
            { "access_token": "new-access", "refresh_token": "new-refresh", "expires_in": 28800 }
            """;

        var token = RefreshedToken.Parse(body, Now);

        Assert.NotNull(token);
        Assert.Equal("new-access", token.AccessToken);
        Assert.Equal("new-refresh", token.RefreshToken);
        Assert.Equal(Now.AddHours(8), token.ExpiresAt);
    }

    /// <summary>
    /// <c>expires_in</c> 은 **초**다. 밀리초로 읽으면 만료가 한참 뒤로 밀려서,
    /// 죽은 토큰을 살아 있다고 믿고 갱신하지 않은 채 계속 헛조회한다.
    /// </summary>
    [Fact]
    public void 만료까지_남은_시간을_초로_읽는다()
    {
        var token = RefreshedToken.Parse("""{"access_token":"t","expires_in":3600}""", Now)!;
        Assert.Equal(Now.AddHours(1), token.ExpiresAt);
    }

    [Fact]
    public void 갱신용_토큰이_안_와도_받아들인다()
    {
        var token = RefreshedToken.Parse("""{"access_token":"t","expires_in":60}""", Now);

        Assert.NotNull(token);
        Assert.Null(token.RefreshToken);
    }

    [Theory]
    [InlineData("""{"access_token":""}""")]
    [InlineData("""{"refresh_token":"only-refresh"}""")]
    [InlineData("""{"error":"invalid_grant"}""")]
    [InlineData("[]")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void 형식이_아니면_null_이고_던지지_않는다(string json)
    {
        Assert.Null(RefreshedToken.Parse(json, Now));
    }

    [Fact]
    public void 곧_만료될_것은_쓰지_않는다()
    {
        var soon = new RefreshedToken { AccessToken = "t", ExpiresAt = Now.AddSeconds(30) };
        var fresh = new RefreshedToken { AccessToken = "t", ExpiresAt = Now.AddHours(1) };
        var forever = new RefreshedToken { AccessToken = "t", ExpiresAt = null };

        Assert.False(soon.IsUsableForAWhile(Now));
        Assert.True(fresh.IsUsableForAWhile(Now));
        Assert.True(forever.IsUsableForAWhile(Now));
    }
}

public class RefreshedTokenStoreTests
{
    [Fact]
    public void 저장하고_다시_읽는다()
    {
        using var temporary = new TemporaryFile();
        var store = new RefreshedTokenStore(temporary.Path);
        var token = new RefreshedToken
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = new DateTimeOffset(2026, 8, 6, 20, 0, 0, TimeSpan.Zero),
        };

        store.Write(token);

        Assert.Equal(token, store.Read());
    }

    [Fact]
    public void 파일이_없으면_null_이다()
    {
        using var temporary = new TemporaryFile();
        Assert.Null(new RefreshedTokenStore(temporary.Path).Read());
    }

    [Fact]
    public void 깨진_파일이어도_던지지_않는다()
    {
        using var temporary = new TemporaryFile();
        File.WriteAllText(temporary.Path, "{ 반쯤 쓰이다 만 것");

        Assert.Null(new RefreshedTokenStore(temporary.Path).Read());
    }

    [Fact]
    public void 지우면_사라진다()
    {
        using var temporary = new TemporaryFile();
        var store = new RefreshedTokenStore(temporary.Path);
        store.Write(new RefreshedToken { AccessToken = "access" });

        store.Clear();

        Assert.Null(store.Read());
        Assert.False(File.Exists(temporary.Path));
    }
}

public class CredentialStoreRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>만료 시각이 2023년이다. 파일 쪽은 죽어 있는 상태를 흉내 낸다.</summary>
    private const string ExpiredFile = """
        {
          "claudeAiOauth": {
            "accessToken": "file-access",
            "refreshToken": "file-refresh",
            "subscriptionType": "max",
            "expiresAt": 1700000000000
          }
        }
        """;

    [Fact]
    public void 파일에서_갱신용_토큰을_읽는다()
    {
        var parsed = ClaudeCredentials.Parse(ExpiredFile);

        Assert.NotNull(parsed);
        Assert.Equal("file-refresh", parsed.RefreshToken);
    }

    /// <summary>
    /// **파일이 만료돼 있는 것이 정상이다.** 갱신해 줄 사람이 없어서 우리가 갱신한 것이고,
    /// 갱신해 둔 것이 살아 있으면 그쪽을 써야 한다.
    /// </summary>
    [Fact]
    public void 갱신해_둔_토큰이_파일보다_우선한다()
    {
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        tokens.Write(new RefreshedToken
        {
            AccessToken = "renewed-access",
            RefreshToken = "renewed-refresh",
            ExpiresAt = Now.AddHours(8),
        });

        var store = new CredentialStore(
            new FixedSource(ExpiredFile), new FixedTime(Now), tokens);

        var current = store.Current();

        Assert.NotNull(current);
        Assert.Equal("renewed-access", current.AccessToken);
        Assert.Equal("renewed-refresh", current.RefreshToken);
        // 플랜 이름은 갱신 응답에 안 온다. 파일 쪽에서 가져다 붙여야 한다.
        Assert.Equal("max", current.SubscriptionType);
    }

    [Fact]
    public void 갱신해_둔_토큰이_만료면_파일로_돌아간다()
    {
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        tokens.Write(new RefreshedToken { AccessToken = "stale", ExpiresAt = Now.AddMinutes(-5) });

        var store = new CredentialStore(
            new FixedSource(ExpiredFile), new FixedTime(Now), tokens);

        Assert.Equal("file-access", store.Current()!.AccessToken);
    }

    /// <summary>서버가 갱신용 토큰을 회전시켰다면 파일에 남은 것은 이미 못 쓴다.</summary>
    [Fact]
    public void 갱신용_토큰은_새것이_이긴다()
    {
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        tokens.Write(new RefreshedToken
        {
            AccessToken = "stale",
            RefreshToken = "rotated-refresh",
            ExpiresAt = Now.AddMinutes(-5),
        });

        var store = new CredentialStore(
            new FixedSource(ExpiredFile), new FixedTime(Now), tokens);

        var current = store.Current()!;
        Assert.Equal("file-access", current.AccessToken);
        Assert.Equal("rotated-refresh", current.RefreshToken);
    }

    [Fact]
    public void 버리면_파일_것으로_돌아간다()
    {
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        var store = new CredentialStore(
            new FixedSource(ExpiredFile), new FixedTime(Now), tokens);

        store.ApplyRefreshed(new RefreshedToken { AccessToken = "renewed", ExpiresAt = Now.AddHours(8) });
        Assert.Equal("renewed", store.Current()!.AccessToken);

        store.DiscardRefreshed();

        Assert.Equal("file-access", store.Current()!.AccessToken);
        Assert.Null(tokens.Read());
    }

    /// <summary>
    /// 두 판(정식·테스트)이 같이 떠 있는 상황이다. 우리가 쓰던 토큰이 죽었더라도,
    /// 그 사이 다른 쪽이 새로 갱신해 뒀으면 그것까지 지우면 안 된다.
    /// </summary>
    [Fact]
    public void 다른_쪽이_새로_갱신해_뒀으면_지우지_않는다()
    {
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        var store = new CredentialStore(
            new FixedSource(ExpiredFile), new FixedTime(Now), tokens);

        store.ApplyRefreshed(new RefreshedToken { AccessToken = "mine", ExpiresAt = Now.AddHours(8) });

        // 다른 프로세스가 그 사이에 새로 갱신해서 파일을 바꿔 놓았다.
        tokens.Write(new RefreshedToken { AccessToken = "theirs", ExpiresAt = Now.AddHours(8) });

        store.DiscardRefreshed();

        Assert.Equal("theirs", tokens.Read()?.AccessToken);
        // 다음 조회는 그 새 토큰으로 걸어야 한다.
        Assert.Equal("theirs", store.Current()!.AccessToken);
    }

    [Fact]
    public void 갱신한_것을_저장한다()
    {
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        var store = new CredentialStore(
            new FixedSource(ExpiredFile), new FixedTime(Now), tokens);

        store.ApplyRefreshed(new RefreshedToken { AccessToken = "renewed", ExpiresAt = Now.AddHours(8) });

        // 다음 실행에서도 살아 있어야 한다. 안 그러면 뜰 때마다 갱신한다.
        Assert.Equal("renewed", new RefreshedTokenStore(temporary.Path).Read()!.AccessToken);
    }
}

public class UsageApiRefreshTests
{
    private const string ExpiredFile = """
        {
          "claudeAiOauth": {
            "accessToken": "dead-access",
            "refreshToken": "file-refresh",
            "subscriptionType": "max",
            "expiresAt": 1700000000000
          }
        }
        """;

    private const string UsageBody = """
        { "five_hour": { "utilization": 12 }, "seven_day": { "utilization": 34 } }
        """;

    /// <summary>
    /// 이 저장소가 겪은 실제 상황이다 — 파일의 토큰은 죽었고 갱신해 줄 사람이 없다.
    /// 갱신해서 다시 걸지 않으면 사용량이 **다시는** 안 나온다.
    /// </summary>
    [Fact]
    public async Task 서버가_거절하면_갱신해서_다시_건다()
    {
        var usageCalls = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri == OAuthTokenRefresher.Endpoint)
            {
                return Json(HttpStatusCode.OK,
                    """{"access_token":"renewed-access","refresh_token":"rotated","expires_in":28800}""");
            }

            usageCalls++;
            return usageCalls == 1
                ? Json(HttpStatusCode.Unauthorized, "{}")
                : Json(HttpStatusCode.OK, UsageBody);
        });

        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        var credentials = new CredentialStore(new FixedSource(ExpiredFile), null, tokens);
        var api = new UsageApi(http, credentials, null, new OAuthTokenRefresher(http));

        var result = await api.FetchAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Snapshot!.FiveHour!.Value.Utilization);
        Assert.Equal(2, usageCalls);

        // 두 번째 조회는 갱신한 토큰으로 걸어야 한다.
        Assert.Contains("Bearer renewed-access", handler.Authorizations);

        // 다음 실행에서 또 갱신하지 않도록 저장해 둬야 한다.
        Assert.Equal("renewed-access", tokens.Read()!.AccessToken);
        Assert.Equal("rotated", tokens.Read()!.RefreshToken);
    }

    [Fact]
    public async Task 갱신이_실패하면_만료로_끝난다()
    {
        using var handler = new StubHandler(request =>
            request.RequestUri == OAuthTokenRefresher.Endpoint
                ? Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")
                : Json(HttpStatusCode.Unauthorized, "{}"));

        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var credentials = new CredentialStore(
            new FixedSource(ExpiredFile), null, new RefreshedTokenStore(temporary.Path));
        var api = new UsageApi(http, credentials, null, new OAuthTokenRefresher(http));

        var result = await api.FetchAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageErrorKind.TokenExpired, result.Error!.Kind);
    }

    /// <summary>
    /// 갓 갱신한 토큰까지 거절당했으면 그것을 지워야 한다. 남겨 두면 만료 시각만 보고
    /// 살아 있다고 믿어서 **죽은 토큰으로 영원히 헛조회한다.**
    /// </summary>
    [Fact]
    public async Task 갱신한_토큰까지_거절당하면_저장한_것을_버린다()
    {
        using var handler = new StubHandler(request =>
            request.RequestUri == OAuthTokenRefresher.Endpoint
                ? Json(HttpStatusCode.OK, """{"access_token":"renewed","expires_in":28800}""")
                : Json(HttpStatusCode.Unauthorized, "{}"));

        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        var credentials = new CredentialStore(new FixedSource(ExpiredFile), null, tokens);
        var api = new UsageApi(http, credentials, null, new OAuthTokenRefresher(http));

        var result = await api.FetchAsync();

        Assert.Equal(UsageErrorKind.TokenExpired, result.Error!.Kind);
        Assert.Null(tokens.Read());
    }

    /// <summary>갱신을 두 번 세 번 걸지 않는다. 거절이 이어지면 그냥 만료다.</summary>
    [Fact]
    public async Task 갱신은_한_번만_건다()
    {
        var refreshCalls = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri == OAuthTokenRefresher.Endpoint)
            {
                refreshCalls++;
                return Json(HttpStatusCode.OK, """{"access_token":"renewed","expires_in":28800}""");
            }
            return Json(HttpStatusCode.Unauthorized, "{}");
        });

        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var credentials = new CredentialStore(
            new FixedSource(ExpiredFile), null, new RefreshedTokenStore(temporary.Path));
        var api = new UsageApi(http, credentials, null, new OAuthTokenRefresher(http));

        await api.FetchAsync();

        Assert.Equal(1, refreshCalls);
    }

    [Fact]
    public async Task 갱신기가_없으면_예전처럼_만료로_끝난다()
    {
        using var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));
        using var http = new HttpClient(handler);
        var credentials = new CredentialStore(new FixedSource(ExpiredFile));
        var api = new UsageApi(http, credentials);

        var result = await api.FetchAsync();

        Assert.Equal(UsageErrorKind.TokenExpired, result.Error!.Kind);
        Assert.DoesNotContain(handler.Calls, uri => uri == OAuthTokenRefresher.Endpoint);
    }

    /// <summary>429 는 거절이 아니다. 갱신하지 않고 물러나야 한다.</summary>
    [Fact]
    public async Task 요청_제한에는_갱신하지_않는다()
    {
        using var handler = new StubHandler(_ => Json(HttpStatusCode.TooManyRequests, "{}"));
        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var credentials = new CredentialStore(
            new FixedSource(ExpiredFile), null, new RefreshedTokenStore(temporary.Path));
        var api = new UsageApi(http, credentials, null, new OAuthTokenRefresher(http));

        var result = await api.FetchAsync();

        Assert.Equal(UsageErrorKind.RateLimited, result.Error!.Kind);
        Assert.DoesNotContain(handler.Calls, uri => uri == OAuthTokenRefresher.Endpoint);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

// ── 테스트용 도구 ────────────────────────────────────────────────

internal sealed class FixedSource(string json) : ICredentialSource
{
    public ClaudeCredentials? Read() => ClaudeCredentials.Parse(json);
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>쓰고 나면 지우는 임시 파일 경로. 만들어 두지는 않는다.</summary>
internal sealed class TemporaryFile : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"dongcsu-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri?> Calls { get; } = [];
    public List<string> Authorizations { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add(request.RequestUri);
        if (request.Headers.Authorization is { } authorization)
        {
            Authorizations.Add($"{authorization.Scheme} {authorization.Parameter}");
        }
        return Task.FromResult(respond(request));
    }
}
