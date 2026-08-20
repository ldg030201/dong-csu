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
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

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

    /// <summary>
    /// **통신이 잠깐 끊긴 것은 거절이 아니다.** 서버가 갱신용 토큰을 회전시킨 뒤라면
    /// 갱신해 둔 것이 유일한 갱신 수단이라, 여기서 버리면 살아 있는 로그인이 재로그인으로
    /// 떨어진다. 노트북이 깨어나는 순간처럼 조회와 갱신이 함께 못 닿을 때 실제로 난다.
    /// </summary>
    [Fact]
    public async Task 통신_오류로_못_닿으면_저장한_토큰을_버리지_않는다()
    {
        using var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));
        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        tokens.Write(new RefreshedToken
        {
            AccessToken = "renewed-access",
            RefreshToken = "rotated",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        });
        var credentials = new CredentialStore(new FixedSource(ExpiredFile), null, tokens);
        var api = new UsageApi(http, credentials, null, new StubRefresher(RefreshOutcome.Unreachable()));

        var result = await api.FetchAsync();

        // Network 는 IsTerminal 이 false 다 — 재로그인 안내가 뜨지 않고 다음 폴링에 다시 건다.
        Assert.Equal(UsageErrorKind.Network, result.Error!.Kind);
        Assert.Equal("renewed-access", tokens.Read()?.AccessToken);
        Assert.Equal("renewed-access", credentials.Current()!.AccessToken);
    }

    /// <summary>갱신 서버가 흔들린 것뿐이다. 토큰을 거절한 것이 아니다.</summary>
    [Fact]
    public async Task 갱신_서버가_5xx_면_버리지_않는다()
    {
        using var handler = new StubHandler(request =>
            request.RequestUri == OAuthTokenRefresher.Endpoint
                ? Json(HttpStatusCode.ServiceUnavailable, "{}")
                : Json(HttpStatusCode.Unauthorized, "{}"));

        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        tokens.Write(new RefreshedToken
        {
            AccessToken = "renewed-access",
            RefreshToken = "rotated",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        });
        var credentials = new CredentialStore(new FixedSource(ExpiredFile), null, tokens);
        var api = new UsageApi(http, credentials, null, new OAuthTokenRefresher(http));

        var result = await api.FetchAsync();

        Assert.Equal(UsageErrorKind.Network, result.Error!.Kind);
        Assert.Equal("renewed-access", tokens.Read()?.AccessToken);
    }

    /// <summary>
    /// 거절은 다르다. 갱신용 토큰이 죽은 것이 확실하므로 저장해 둔 것까지 지운다 —
    /// 남겨 두면 만료 시각만 보고 살아 있다고 믿어서 죽은 토큰으로 헛조회한다.
    /// </summary>
    [Fact]
    public async Task 갱신을_거절당하면_저장한_것을_버린다()
    {
        using var handler = new StubHandler(request =>
            request.RequestUri == OAuthTokenRefresher.Endpoint
                ? Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")
                : Json(HttpStatusCode.Unauthorized, "{}"));

        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var tokens = new RefreshedTokenStore(temporary.Path);
        tokens.Write(new RefreshedToken
        {
            AccessToken = "renewed-access",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        });
        var credentials = new CredentialStore(new FixedSource(ExpiredFile), null, tokens);
        var api = new UsageApi(http, credentials, null, new OAuthTokenRefresher(http));

        var result = await api.FetchAsync();

        Assert.Equal(UsageErrorKind.TokenExpired, result.Error!.Kind);
        Assert.Null(tokens.Read());
    }

    /// <summary>
    /// 갱신에 성공한 조회의 스냅숏에는 **갱신한 만료 시각**이 실려야 한다. 파일에서 읽은
    /// 것을 그대로 넘기면 계정 탭이 방금 갱신한 토큰을 두고 다음 조회(기본 10분)까지
    /// "만료됨 (곧 갱신)" 이라고 말한다 — 실제로는 갱신이 끝났고 사용량도 정상으로 나온다.
    /// </summary>
    [Fact]
    public async Task 갱신하면_새_만료_시각이_스냅숏에_실린다()
    {
        var usageCalls = 0;
        using var handler = new StubHandler(_ =>
        {
            usageCalls++;
            return usageCalls == 1
                ? Json(HttpStatusCode.Unauthorized, "{}")
                : Json(HttpStatusCode.OK, UsageBody);
        });

        using var http = new HttpClient(handler);
        using var temporary = new TemporaryFile();
        var time = new FixedTime(Now);
        var credentials = new CredentialStore(
            new FixedSource(ExpiredFile), time, new RefreshedTokenStore(temporary.Path));

        // 진짜 갱신기는 안에서 TimeProvider.System 을 써서 만료 시각을 고정할 수 없다.
        var renewed = new RefreshedToken
        {
            AccessToken = "renewed-access",
            RefreshToken = "rotated",
            ExpiresAt = Now.AddHours(8),
        };
        var api = new UsageApi(
            http, credentials, time, new StubRefresher(RefreshOutcome.Renewed(renewed)));

        var result = await api.FetchAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddHours(8), result.Snapshot!.TokenExpiresAt);
        // 파일의 2023년 값이 그대로 실리면 안 된다.
        Assert.NotEqual(
            DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), result.Snapshot.TokenExpiresAt);
        // 플랜·한도 등급은 갱신 응답에 안 온다. `with` 를 안 쓰고 새로 만들면 여기서 깨진다.
        Assert.Equal("Max", result.Snapshot.PlanName);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>
/// 갱신 실패를 **거절**과 **못 닿음**으로 가르는 자리다. 반대로 잡으면 둘 다 나쁘다 —
/// 400 을 못 닿음으로 두면 죽은 토큰을 붙들고 조회마다 갱신을 다시 걸며 재로그인 안내가
/// 영영 안 뜨고, 5xx 를 거절로 두면 서버가 잠깐 흔들린 것만으로 살아 있던 토큰을 버린다.
/// </summary>
public class OAuthTokenRefresherTests
{
    [Fact]
    public async Task 갱신에_성공하면_토큰이_온다()
    {
        using var handler = new StubHandler(_ =>
            Json(HttpStatusCode.OK, """{"access_token":"renewed","expires_in":28800}"""));
        using var http = new HttpClient(handler);

        var outcome = await new OAuthTokenRefresher(http).RefreshAsync("file-refresh");

        Assert.Equal("renewed", outcome.Token?.AccessToken);
        Assert.False(outcome.Rejected);
    }

    /// <summary>서버가 그 갱신용 토큰을 거절했다. 이때만 버려도 된다.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task 서버가_거절하면_거절이다(HttpStatusCode status)
    {
        using var handler = new StubHandler(_ => Json(status, """{"error":"invalid_grant"}"""));
        using var http = new HttpClient(handler);

        var outcome = await new OAuthTokenRefresher(http).RefreshAsync("file-refresh");

        Assert.Null(outcome.Token);
        Assert.True(outcome.Rejected);
    }

    /// <summary>408·429·5xx 는 대답을 못 한 것이지 토큰을 거절한 것이 아니다.</summary>
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task 서버가_대답을_못_하면_닿지_못한_것이다(HttpStatusCode status)
    {
        using var handler = new StubHandler(_ => Json(status, "{}"));
        using var http = new HttpClient(handler);

        var outcome = await new OAuthTokenRefresher(http).RefreshAsync("file-refresh");

        Assert.Null(outcome.Token);
        Assert.False(outcome.Rejected);
    }

    [Fact]
    public async Task 통신이_끊기면_닿지_못한_것이다()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("연결 실패"));
        using var http = new HttpClient(handler);

        var outcome = await new OAuthTokenRefresher(http).RefreshAsync("file-refresh");

        Assert.Null(outcome.Token);
        Assert.False(outcome.Rejected);
    }

    /// <summary>200 을 받았으니 토큰은 살아 있을 가능성이 크다. 형식이 어긋났다고 버리지 않는다.</summary>
    [Fact]
    public async Task 형식이_아닌_응답은_닿지_못한_것으로_둔다()
    {
        using var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "그런 형식이 아니다"));
        using var http = new HttpClient(handler);

        var outcome = await new OAuthTokenRefresher(http).RefreshAsync("file-refresh");

        Assert.Null(outcome.Token);
        Assert.False(outcome.Rejected);
    }

    /// <summary>갱신용 토큰이 없으면 살릴 것이 애초에 없다.</summary>
    [Fact]
    public async Task 갱신용_토큰이_없으면_거절이다()
    {
        using var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var http = new HttpClient(handler);

        var outcome = await new OAuthTokenRefresher(http).RefreshAsync("   ");

        Assert.True(outcome.Rejected);
        Assert.Empty(handler.Calls);
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

/// <summary>
/// 갱신 갈래를 결정적으로 먹인다. 통신을 흉내 내지 않고 결과만 주므로,
/// **부르는 쪽이 거절과 못 닿음을 어떻게 다루는지**만 따로 잴 수 있다.
/// </summary>
internal sealed class StubRefresher(RefreshOutcome outcome) : ITokenRefresher
{
    public int Calls { get; private set; }

    public Task<RefreshOutcome> RefreshAsync(
        string refreshToken, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(outcome);
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
