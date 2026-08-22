using DongCSU.Core;
using DongCSU.Core.Owl;
using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

public class CredentialTests
{
    /// <summary>
    /// **만료 시각을 아주 먼 미래로 둔다.** 처음에는 몇 달 뒤 시각을 적어 뒀는데,
    /// 그 시각이 지나자 캐시 테스트가 저절로 깨졌다 — 만료된 자격 증명은 캐시에
    /// 담기지 않기 때문이다. 벽시계에 기대는 픽스처는 언젠가 반드시 터진다.
    /// 4102444800000 = 2100-01-01.
    /// </summary>
    private const string Sample = """
        {
          "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-example",
            "subscriptionType": "max",
            "expiresAt": 4102444800000
          }
        }
        """;

    [Fact]
    public void 자격_증명을_읽는다()
    {
        var parsed = ClaudeCredentials.Parse(Sample);

        Assert.NotNull(parsed);
        Assert.Equal("sk-ant-oat01-example", parsed.AccessToken);
        Assert.Equal("max", parsed.SubscriptionType);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(4102444800000), parsed.ExpiresAt);
    }

    /// <summary>expiresAt 은 **밀리초**다. 초로 읽으면 1970년대가 나와 늘 만료로 판정된다.</summary>
    [Fact]
    public void 만료_시각을_밀리초로_읽는다()
    {
        var parsed = ClaudeCredentials.Parse(Sample)!;
        Assert.True(parsed.ExpiresAt!.Value.Year > 2020, "초로 읽으면 1970년대가 나온다");
    }

    [Theory]
    [InlineData("""{"claudeAiOauth":{"accessToken":""}}""")]
    [InlineData("""{"claudeAiOauth":{}}""")]
    [InlineData("""{"other":1}""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void 형식이_아니면_null_이고_던지지_않는다(string json)
    {
        Assert.Null(ClaudeCredentials.Parse(json));
    }

    [Theory]
    [InlineData("max", "Max")]
    [InlineData("claude_max", "Max")]
    [InlineData("pro", "Pro")]
    [InlineData("team", "Team")]
    [InlineData("api", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("enterprise", "Enterprise")]
    public void 플랜_이름을_고른다(string? raw, string? expected)
    {
        Assert.Equal(expected, ClaudeCredentials.PlanName(raw));
    }

    [Fact]
    public void 만료를_판정한다()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new ClaudeCredentials { AccessToken = "t", ExpiresAt = now.AddMinutes(-1) };
        var fresh = new ClaudeCredentials { AccessToken = "t", ExpiresAt = now.AddHours(1) };
        var soon = new ClaudeCredentials { AccessToken = "t", ExpiresAt = now.AddSeconds(30) };

        Assert.True(expired.IsExpired(now));
        Assert.False(fresh.IsExpired(now));
        // 곧 만료될 것은 캐시에 두지 않는다 — 쓰려는 순간 죽어 있으면 헛조회가 된다.
        Assert.True(fresh.IsUsableForAWhile(now));
        Assert.False(soon.IsUsableForAWhile(now));
    }

    [Fact]
    public void 파일에서_읽는다()
    {
        using var file = new TemporaryFile();
        File.WriteAllText(file.Path, Sample);

        var read = new FileCredentialSource([file.Path]).Read();

        Assert.Equal("sk-ant-oat01-example", read?.AccessToken);
    }

    [Fact]
    public void 파일이_없으면_null()
    {
        // 만들지 않는다 — 경로만 받아 온다.
        using var missing = new TemporaryFile();
        Assert.Null(new FileCredentialSource([missing.Path]).Read());
    }

    [Fact]
    public void 캐시는_한_번만_읽고_버리면_다시_읽는다()
    {
        var source = new CountingSource(Sample);
        // 벽시계를 쓰지 않는다. 픽스처가 만료되면 캐시가 통째로 안 도는 것처럼 보인다.
        var store = new CredentialStore(source, new FakeTime());

        store.Current();
        store.Current();
        Assert.Equal(1, source.Reads);

        store.Invalidate();
        store.Current();
        Assert.Equal(2, source.Reads);
    }

    /// <summary>
    /// 만료된 자격 증명. 데스크톱 앱만 쓰는 사용자에게는 <c>.credentials.json</c> 이
    /// 이 상태로 남아 있는 것이 **정상**이다 — 갱신해 줄 사람이 없어서 우리가 갱신했다.
    /// 1704067200000 = 2024-01-01.
    /// </summary>
    private const string Stale = """
        {
          "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-stale",
            "subscriptionType": "max",
            "expiresAt": 1704067200000
          }
        }
        """;

    /// <summary>
    /// 만료를 기준으로 삼으면 조건이 늘 참이라 조회마다 파일을 다시 읽는다. WSL 자리를
    /// 훑는 사용자는 그때마다 배포판이 깨어난다.
    /// </summary>
    [Fact]
    public void 만료된_파일을_조회마다_다시_읽지_않는다()
    {
        var source = new CountingSource(Stale);
        var store = new CredentialStore(source, new FakeTime());

        store.Current();
        store.Current();
        store.Current();

        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public void 한참_지나면_다시_읽는다()
    {
        var source = new CountingSource(Stale);
        var clock = new FakeTime();
        var store = new CredentialStore(source, clock);

        store.Current();
        clock.Advance(CredentialStore.FileRereadInterval + TimeSpan.FromMinutes(1));
        store.Current();

        Assert.Equal(2, source.Reads);
    }

    /// <summary>401 뒤에는 바닥을 무시한다. 안 그러면 재로그인해도 죽은 캐시를 붙들고 있다.</summary>
    [Fact]
    public void 버리면_바닥을_무시하고_곧바로_다시_읽는다()
    {
        var source = new CountingSource(Stale);
        var store = new CredentialStore(source, new FakeTime());

        store.Current();
        Assert.Equal(1, source.Reads);

        // 시간은 안 흘린다 — 바닥이 살아 있으면 여기서 안 늘어난다.
        store.Invalidate();
        store.Current();

        Assert.Equal(2, source.Reads);
    }

    /// <summary>
    /// 못 읽은 상태에 바닥을 걸면 **방금 로그인한 사람이 한 시간을 기다린다.**
    /// 그 경우는 401 도 안 나서 Invalidate() 가 불릴 일조차 없다.
    /// </summary>
    [Fact]
    public void 아직_못_읽었으면_바닥을_두지_않는다()
    {
        var source = new CountingSource("{}");
        var store = new CredentialStore(source, new FakeTime());

        Assert.Null(store.Current());
        Assert.Null(store.Current());

        Assert.Equal(2, source.Reads);
    }

    private sealed class CountingSource(string json) : ICredentialSource
    {
        public int Reads { get; private set; }
        public ClaudeCredentials? Read() { Reads++; return ClaudeCredentials.Parse(json); }
    }
}

public class UsageApiTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>조회 응답을 읽을 때 같이 넘기는 자격 증명. 여기서는 플랜 이름만 쓴다.</summary>
    private static ClaudeCredentials Cred(string? plan) =>
        new() { AccessToken = "x", SubscriptionType = plan };

    [Fact]
    public void 응답을_스냅숏으로_읽는다()
    {
        const string body = """
            {
              "five_hour": { "utilization": 34.2, "resets_at": "2026-08-06T15:30:00Z" },
              "seven_day": { "utilization": 61,   "resets_at": "2026-08-10T00:00:00.000Z" }
            }
            """;

        var result = UsageApi.Parse(body, Cred("max"), Now);

        Assert.True(result.IsSuccess);
        var snapshot = result.Snapshot!;
        Assert.Equal("Max", snapshot.PlanName);
        Assert.Equal(34.2, snapshot.FiveHour!.Value.Utilization, 3);
        Assert.Equal(61, snapshot.SevenDay!.Value.Utilization, 3);
        Assert.Equal(new DateTimeOffset(2026, 8, 6, 15, 30, 0, TimeSpan.Zero), snapshot.FiveHour.Value.ResetsAt);
        // 소수점 초가 붙은 형식도 읽어야 한다.
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero), snapshot.SevenDay.Value.ResetsAt);
    }

    [Fact]
    public void 사용률을_0에서_100_사이로_자른다()
    {
        var result = UsageApi.Parse("""{"five_hour":{"utilization":140}}""", Cred(null), Now);
        Assert.Equal(100, result.Snapshot!.FiveHour!.Value.Utilization);

        result = UsageApi.Parse("""{"five_hour":{"utilization":-5}}""", Cred(null), Now);
        Assert.Equal(0, result.Snapshot!.FiveHour!.Value.Utilization);
    }

    [Theory]
    [InlineData("""{"five_hour":{}}""")]
    [InlineData("""{"five_hour":{"utilization":"많이"}}""")]
    [InlineData("""{}""")]
    public void 창이_없거나_이상하면_null_이지_실패가_아니다(string body)
    {
        var result = UsageApi.Parse(body, Cred(null), Now);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Snapshot!.FiveHour);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public void 본문이_형식이_아니면_실패한다(string body)
    {
        var result = UsageApi.Parse(body, Cred(null), Now);
        Assert.False(result.IsSuccess);
        Assert.Equal(UsageErrorKind.Decode, result.Error!.Kind);
    }

    [Fact]
    public void 다시_걸어도_소용없는_오류를_구분한다()
    {
        Assert.True(UsageError.NoCredentials().IsTerminal);
        Assert.True(UsageError.TokenExpired().IsTerminal);
        Assert.False(UsageError.RateLimited(null).IsTerminal);
        Assert.False(UsageError.Network("끊김").IsTerminal);
        Assert.False(UsageError.Http(500).IsTerminal);
    }

    // MARK: - limits 배열
    //
    // `five_hour`·`seven_day` 두 창만 읽으면 **모델별로 갈린 주간 한도를 놓친다.**
    // 측정이 한도 %p 를 이 배열로 세므로, 여기가 비면 측정이 아무것도 못 센다.

    /// <summary>세 갈래가 다 들어 있는 실제 모양.</summary>
    private const string ThreeLimits = """
        {
          "five_hour": { "utilization": 34.2, "resets_at": "2026-08-06T15:30:00Z" },
          "seven_day": { "utilization": 61,   "resets_at": "2026-08-10T00:00:00.000Z" },
          "limits": [
            { "kind": "session",        "percent": 24, "resets_at": "2026-08-06T15:30:00Z" },
            { "kind": "weekly_all",     "percent": 90 },
            { "kind": "weekly_scoped",  "percent": 15,
              "scope": { "model": { "display_name": "Fable" } } }
          ]
        }
        """;

    [Fact]
    public void 한도_배열을_읽는다()
    {
        var limits = UsageApi.Parse(ThreeLimits, Cred("max"), Now).Snapshot!.Limits;

        Assert.Equal(3, limits.Count);
        Assert.Equal(new[] { "session", "weekly_all", "weekly_scoped/Fable" },
            limits.Select(limit => limit.Id));
        Assert.Equal(new[] { "세션 (5시간)", "주간 (7일)", "주간 · Fable" },
            limits.Select(limit => limit.Title));
        Assert.Equal(new[] { 24d, 90d, 15d }, limits.Select(limit => limit.Percent));
        Assert.Null(limits[1].ModelName);
        Assert.Equal("Fable", limits[2].ModelName);
    }

    /// <summary>
    /// **한도는 창을 대체하지 않고 덧붙는다.** HUD 는 그대로 두 창을 그리고, 측정만
    /// 모델별로 갈린 것까지 필요해서 배열을 본다.
    /// </summary>
    [Fact]
    public void 한도를_읽어도_두_창은_그대로다()
    {
        var snapshot = UsageApi.Parse(ThreeLimits, Cred("max"), Now).Snapshot!;

        Assert.Equal(34.2, snapshot.FiveHour!.Value.Utilization, 3);
        Assert.Equal(61, snapshot.SevenDay!.Value.Utilization, 3);
    }

    /// <summary>옛 응답에는 배열이 아예 없다. 그것도 정상이고, 창은 그대로 나와야 한다.</summary>
    [Theory]
    // 배열 자체가 없다
    [InlineData("""{"five_hour":{"utilization":34.2},"seven_day":{"utilization":61}}""")]
    // 배열 자리에 엉뚱한 것이 왔다
    [InlineData("""{"five_hour":{"utilization":34.2},"seven_day":{"utilization":61},"limits":{}}""")]
    [InlineData("""{"five_hour":{"utilization":34.2},"seven_day":{"utilization":61},"limits":null}""")]
    [InlineData("""{"five_hour":{"utilization":34.2},"seven_day":{"utilization":61},"limits":[]}""")]
    public void 한도가_없으면_빈_목록이고_창은_남는다(string body)
    {
        var snapshot = UsageApi.Parse(body, Cred("max"), Now).Snapshot!;

        Assert.Empty(snapshot.Limits);
        Assert.Equal(34.2, snapshot.FiveHour!.Value.Utilization, 3);
        Assert.Equal(61, snapshot.SevenDay!.Value.Utilization, 3);
    }

    [Fact]
    public void 한도_퍼센트를_0에서_100_사이로_자른다()
    {
        const string body = """
            {"limits":[{"kind":"session","percent":140},{"kind":"weekly_all","percent":-3}]}
            """;

        var limits = UsageApi.Parse(body, Cred(null), Now).Snapshot!.Limits;

        Assert.Equal(100, limits[0].Percent);
        Assert.Equal(0, limits[1].Percent);
    }

    /// <summary>
    /// **원소 하나가 이상해도 나머지는 살린다.** 서버가 낯선 항목을 하나 끼웠다고 한도가
    /// 통째로 사라지면 측정이 그 순간부터 아무것도 못 센다.
    /// </summary>
    [Fact]
    public void 이상한_원소만_버린다()
    {
        const string body = """
            {"limits":[
              {"percent":10},
              {"kind":"weekly_all","percent":"많이"},
              {"kind":"","percent":10},
              {"kind":"weekly_all"},
              "글자",
              {"kind":"session","percent":24}
            ]}
            """;

        var limit = Assert.Single(UsageApi.Parse(body, Cred(null), Now).Snapshot!.Limits);

        Assert.Equal("session", limit.Id);
        Assert.Equal(24, limit.Percent);
    }

    [Theory]
    [InlineData("2026-08-06T15:30:00Z")]
    [InlineData("2026-08-06T15:30:00.000Z")]
    // Z 가 없어도 UTC 로 읽는다 — 로컬로 읽으면 시간대만큼 통째로 어긋난다.
    [InlineData("2026-08-06T15:30:00")]
    [InlineData("2026-08-07T00:30:00+09:00")]
    public void 초기화_시각을_UTC_로_읽는다(string text)
    {
        var body = $$"""{"limits":[{"kind":"session","percent":24,"resets_at":"{{text}}"}]}""";

        var limit = Assert.Single(UsageApi.Parse(body, Cred(null), Now).Snapshot!.Limits);

        Assert.Equal(new DateTimeOffset(2026, 8, 6, 15, 30, 0, TimeSpan.Zero), limit.ResetsAt);
    }

    /// <summary>
    /// <c>GetString()</c> 은 ValueKind 가 String 이 아니면 **던진다.** 서버가 숫자나
    /// 객체를 흘려도 그 한도만 초기화 시각이 비어야지, 조회가 통째로 실패하면 안 된다.
    /// </summary>
    [Theory]
    [InlineData("12345")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"곧\"")]
    public void 초기화_시각이_이상해도_던지지_않는다(string raw)
    {
        var body = $$"""{"limits":[{"kind":"session","percent":24,"resets_at":{{raw}}}]}""";

        var limit = Assert.Single(UsageApi.Parse(body, Cred(null), Now).Snapshot!.Limits);

        Assert.Null(limit.ResetsAt);
        Assert.Equal(24, limit.Percent);
    }

    /// <summary>
    /// 모르는 <c>kind</c> 는 원문을 그대로 쓴다. 서버가 갈래를 하나 늘렸을 때 빈칸이
    /// 되는 것보다, 낯설어도 실제 값이 보이는 편이 낫다.
    /// </summary>
    [Fact]
    public void 모르는_갈래는_원문_그대로다()
    {
        const string body = """{"limits":[{"kind":"monthly_all","percent":5}]}""";

        var limit = Assert.Single(UsageApi.Parse(body, Cred(null), Now).Snapshot!.Limits);

        Assert.Equal("monthly_all", limit.Id);
        Assert.Equal("monthly_all", limit.Title);
    }

    /// <summary>
    /// 모델 이름이 빈 문자열이면 없는 것으로 눕힌다 — 그대로 두면 id 가
    /// <c>weekly_scoped/</c>, 제목이 <c>주간 · </c> 로 꼬리가 빈 채 화면에 나간다.
    /// </summary>
    [Theory]
    [InlineData("""{"model":{"display_name":""}}""")]
    [InlineData("""{"model":{"display_name":null}}""")]
    [InlineData("""{"model":{}}""")]
    [InlineData("""{}""")]
    public void 모델_이름이_없으면_갈래만_남는다(string scope)
    {
        var body = $$"""{"limits":[{"kind":"weekly_scoped","percent":5,"scope":{{scope}}}]}""";

        var limit = Assert.Single(UsageApi.Parse(body, Cred(null), Now).Snapshot!.Limits);

        Assert.Null(limit.ModelName);
        Assert.Equal("weekly_scoped", limit.Id);
    }
}

public class UsageStoreTests
{
    private static UsageStore NewStore(FakeTime time) =>
        new(new UsageApi(new HttpClient(), new CredentialStore(new EmptySource())), time);

    private static UsageSnapshot Snapshot(DateTimeOffset at, double session = 10) => new()
    {
        PlanName = "Max",
        FiveHour = new UsageWindow(session, at.AddHours(2)),
        SevenDay = new UsageWindow(40, at.AddDays(3)),
        FetchedAt = at,
    };

    [Fact]
    public void 성공하면_오류가_지워진다()
    {
        var time = new FakeTime();
        var store = NewStore(time);

        store.Apply(UsageResult.Fail(UsageError.Network("끊김")));
        Assert.True(store.NeedsReauth == false && store.ErrorText is not null);

        store.Apply(UsageResult.Ok(Snapshot(time.GetUtcNow())));
        Assert.Null(store.ErrorText);
        Assert.False(store.IsDisconnected);
    }

    [Fact]
    public void 값이_있는데_실패하면_옛값_상태다()
    {
        var time = new FakeTime();
        var store = NewStore(time);

        store.Apply(UsageResult.Ok(Snapshot(time.GetUtcNow())));
        store.Apply(UsageResult.Fail(UsageError.Network("끊김")));

        Assert.True(store.IsStale);
        Assert.True(store.IsDisconnected);
        Assert.NotNull(store.Snapshot);   // 옛 숫자는 지우지 않는다 — 빈 화면보다 낫다
    }

    [Fact]
    public void 재로그인이_필요한_상태를_알린다()
    {
        var store = NewStore(new FakeTime());
        store.Apply(UsageResult.Fail(UsageError.TokenExpired()));

        Assert.True(store.NeedsReauth);
        Assert.True(store.IsDisconnected);
    }

    [Fact]
    public void 서버가_알려준_만큼_물러난다()
    {
        var time = new FakeTime();
        var store = NewStore(time);

        store.Apply(UsageResult.Fail(UsageError.RateLimited(TimeSpan.FromMinutes(3))));

        Assert.InRange(store.NextPollDelay(), TimeSpan.FromMinutes(2.9), TimeSpan.FromMinutes(3));
    }

    // 잇달아 막혔을 때의 사다리와 상한은 FetchFloorTests 의 "429 물러나기" 구획에 있다.

    [Fact]
    public void 성공하면_물러나기가_풀린다()
    {
        var time = new FakeTime();
        var store = NewStore(time);

        store.Apply(UsageResult.Fail(UsageError.RateLimited(TimeSpan.FromMinutes(10))));
        store.Apply(UsageResult.Ok(Snapshot(time.GetUtcNow())));

        Assert.Equal(store.PollInterval, store.NextPollDelay());
    }

    [Fact]
    public void 메뉴_한_줄을_만든다()
    {
        var time = new FakeTime();
        var store = NewStore(time);
        store.Apply(UsageResult.Ok(Snapshot(time.GetUtcNow(), session: 34)));

        var text = store.SummaryText();

        Assert.Contains("Max", text);
        Assert.Contains("세션 34%", text);
        Assert.Contains("주간 40%", text);
    }

    [Fact]
    public void 값이_없으면_불러오는_중이라고_한다()
    {
        Assert.Equal("사용량 불러오는 중…", NewStore(new FakeTime()).SummaryText());
    }

    /// <summary>
    /// 렌더에서는 조회를 한 번도 안 건다. 그래서 예정 시각을 꽂아 넣지 못하면 상태 탭의
    /// 조회 카운트다운만 통째로 비어서 실제 화면과 달라진다.
    /// </summary>
    [Fact]
    public void 꽂아_넣은_예정_시각을_그대로_돌려준다()
    {
        var time = new FakeTime();
        var store = NewStore(time);
        var at = time.GetUtcNow().AddMinutes(7).AddSeconds(12);

        store.Preview(Snapshot(time.GetUtcNow()), nextPoll: at);

        Assert.Equal(at, store.NextPollAt);
    }

    /// <summary>물러나는 중도 아니고 조회한 적도 없다 — 그래도 꽂은 값이 먼저다.</summary>
    [Fact]
    public void 조회한_적이_없어도_꽂은_값이_우선한다()
    {
        var time = new FakeTime();
        var store = NewStore(time);

        Assert.Null(store.NextPollAt);

        store.Preview(Snapshot(time.GetUtcNow()), nextPoll: time.GetUtcNow().AddMinutes(3));

        Assert.Equal(time.GetUtcNow().AddMinutes(3), store.NextPollAt);
    }

    /// <summary>
    /// **실제 조회가 돌면 고정값은 사라져야 한다.** 안 그러면 평소에 쓰는 저장소에도
    /// 고정 카운트다운이 남아 화면이 영영 같은 시간을 가리킨다.
    /// </summary>
    [Fact]
    public void 실제로_조회하면_꽂은_값이_사라진다()
    {
        var time = new FakeTime();
        var store = NewStore(time);
        store.Preview(Snapshot(time.GetUtcNow()), nextPoll: time.GetUtcNow().AddMinutes(7));

        store.Apply(UsageResult.Ok(Snapshot(time.GetUtcNow())));

        Assert.Equal(time.GetUtcNow() + store.PollInterval, store.NextPollAt);
    }

    // MARK: - 표본 훅 (SnapshotReceived)
    //
    // 측정이 여기 붙어 표본을 받는다. 저장소가 측정 객체를 직접 알지 않으려고 일부러
    // 이벤트로 끊어 뒀다 — 한 덩어리가 되면 조회만 쓰는 미리보기에도 측정이 딸려 온다.

    [Fact]
    public void 성공한_조회에서만_표본이_온다()
    {
        var time = new FakeTime();
        var store = NewStore(time);
        var received = new List<UsageSnapshot>();
        store.SnapshotReceived += snapshot => received.Add(snapshot);

        var sample = Snapshot(time.GetUtcNow());
        store.Apply(UsageResult.Ok(sample));

        Assert.Same(sample, Assert.Single(received));
    }

    /// <summary>**실패한 조회는 표본이 아니다.** 여기서 새면 측정이 빈 값을 기록에 넣는다.</summary>
    [Fact]
    public void 실패한_조회는_표본이_아니다()
    {
        var store = NewStore(new FakeTime());
        var count = 0;
        store.SnapshotReceived += _ => count++;

        store.Apply(UsageResult.Fail(UsageError.RateLimited(null)));
        store.Apply(UsageResult.Fail(UsageError.TokenExpired()));
        store.Apply(UsageResult.Fail(UsageError.NoCredentials()));
        store.Apply(UsageResult.Fail(UsageError.Network("끊김")));

        Assert.Equal(0, count);
    }

    /// <summary>
    /// 값을 잃지 않으려고 옛 스냅숏을 그대로 들고 있는데, 그것을 다시 표본으로 쏘면
    /// 실패할 때마다 같은 값이 한 번 더 기록된다.
    /// </summary>
    [Fact]
    public void 성공_뒤에_실패해도_표본은_한_번뿐이다()
    {
        var time = new FakeTime();
        var store = NewStore(time);
        var count = 0;
        store.SnapshotReceived += _ => count++;

        store.Apply(UsageResult.Ok(Snapshot(time.GetUtcNow())));
        store.Apply(UsageResult.Fail(UsageError.Network("끊김")));

        Assert.Equal(1, count);
    }

    /// <summary>
    /// **미리보기는 표본이 아니다.** 렌더로 꽂은 고정값이 기록에 들어가면, 문서 그림을
    /// 한 장 뽑을 때마다 사용자의 진짜 기록에 가짜 표본이 하나씩 쌓인다.
    /// </summary>
    [Fact]
    public void 미리보기는_표본이_아니다()
    {
        var time = new FakeTime();
        var store = NewStore(time);
        var samples = 0;
        var changes = 0;
        store.SnapshotReceived += _ => samples++;
        store.Changed += () => changes++;

        store.Preview(Snapshot(time.GetUtcNow()));

        Assert.Equal(0, samples);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// 훅 안에서 저장소를 다시 봐도 앞뒤가 맞아야 한다 — 필드를 **전부 정리한 뒤에**
    /// 부르기 때문이다. 앞서 실패해 있던 흔적이 남아 있으면 측정이 옛 오류를 보고
    /// 표본을 버릴 수 있다.
    /// </summary>
    [Fact]
    public void 훅_안에서_본_저장소는_이미_새_값이다()
    {
        var time = new FakeTime();
        var store = NewStore(time);
        store.Apply(UsageResult.Fail(UsageError.TokenExpired()));

        UsageSnapshot? seen = null;
        string? error = "아직 안 봤다";
        var needsReauth = true;
        store.SnapshotReceived += _ =>
        {
            seen = store.Snapshot;
            error = store.ErrorText;
            needsReauth = store.NeedsReauth;
        };

        var sample = Snapshot(time.GetUtcNow());
        store.Apply(UsageResult.Ok(sample));

        Assert.Same(sample, seen);
        Assert.Null(error);
        Assert.False(needsReauth);
    }

    private sealed class EmptySource : ICredentialSource
    {
        public ClaudeCredentials? Read() => null;
    }
}

public class UsageStyleTests
{
    [Fact]
    public void 구간_경계는_그_색_그대로다()
    {
        var document = OwlDocument.Embedded;
        foreach (var stop in document.UsageColors)
        {
            Assert.Equal(Rgb.FromHex(stop.Hex), UsageColor.For(document, stop.At));
        }
    }

    [Fact]
    public void 사이_값은_두_색_사이에_있다()
    {
        var stops = OwlDocument.Embedded.UsageColors;
        var lower = Rgb.FromHex(stops[0].Hex);
        var upper = Rgb.FromHex(stops[1].Hex);
        var middle = UsageColor.For((stops[0].At + stops[1].At) / 2);

        Assert.InRange(middle.R, Math.Min(lower.R, upper.R), Math.Max(lower.R, upper.R));
        Assert.InRange(middle.G, Math.Min(lower.G, upper.G), Math.Max(lower.G, upper.G));
        Assert.InRange(middle.B, Math.Min(lower.B, upper.B), Math.Max(lower.B, upper.B));
    }

    [Fact]
    public void 범위_밖은_양_끝으로_자른다()
    {
        var stops = OwlDocument.Embedded.UsageColors;
        Assert.Equal(Rgb.FromHex(stops[0].Hex), UsageColor.For(-50));
        Assert.Equal(Rgb.FromHex(stops[^1].Hex), UsageColor.For(500));
    }

    [Theory]
    [InlineData(0, "0분 남음")]
    [InlineData(45, "45분 남음")]
    [InlineData(90, "1시간 30분 남음")]
    [InlineData(60 * 25, "1일 1시간 남음")]
    public void 남은_시간을_적는다(int minutes, string expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, RemainingTime.Text(now.AddMinutes(minutes).AddSeconds(1), now));
    }

    [Fact]
    public void 지난_시각은_곧_초기화다()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("곧 초기화", RemainingTime.Text(now.AddMinutes(-1), now));
        Assert.Equal("–", RemainingTime.Text(null, now));
    }

    /// <summary>1일 1시간 59분을 "1일 1시간"으로 깎아 버리면 두 시간 가까이 어긋난다.</summary>
    [Fact]
    public void 하루_넘게_남으면_시간을_반올림한다()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("1일 2시간 남음", RemainingTime.Text(now.AddDays(1).AddMinutes(119), now));
        Assert.Equal("2일 0시간 남음", RemainingTime.Text(now.AddDays(1).AddHours(23).AddMinutes(45), now));
    }

    [Theory]
    [InlineData(30, "방금 값")]
    [InlineData(60 * 5, "5분 전 값")]
    [InlineData(3600 * 3, "3시간 전 값")]
    [InlineData(86400 * 2, "2일 전 값")]
    public void 얼마나_된_값인지_적는다(int seconds, string expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, RemainingTime.AgeText(now.AddSeconds(-seconds), now));
    }

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void 카운트다운을_적는다(int seconds, string expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, RemainingTime.ClockText(now.AddSeconds(seconds).AddMilliseconds(500), now));
    }
}

public class OwlAnimatorTests
{
    private static readonly OwlDocument Document = OwlDocument.Embedded;

    /// <summary>
    /// 사용률로 기분을 고른다.
    ///
    /// **문턱 숫자를 박아 두지 않는다.** 그 값은 <c>owl.json</c> 에서 오고 맥이 이따금
    /// 낮춘다 — 박아 두면 그때마다 여기가 깨진다. 실제로 두 번 깨졌다. 문턱을 읽어서
    /// 그 언저리만 본다.
    /// </summary>
    [Fact]
    public void 사용률로_기분을_고른다()
    {
        var tired = Document.MoodThresholds["tired"];
        var exhausted = Document.MoodThresholds["exhausted"];

        Assert.Equal(OwlMood.Idle, Mood(0));
        Assert.Equal(OwlMood.Idle, Mood(tired - 1));
        Assert.Equal(OwlMood.Tired, Mood(tired));
        Assert.Equal(OwlMood.Tired, Mood(exhausted - 1));
        Assert.Equal(OwlMood.Exhausted, Mood(exhausted));
        Assert.Equal(OwlMood.Exhausted, Mood(100));
    }

    private static OwlMood Mood(double utilization) =>
        OwlMoodResolver.Resolve(Document, utilization, isDisconnected: false);

    /// <summary>옛 숫자로 지친 표정을 지으면 그게 지금 상태인 줄 오해한다.</summary>
    [Fact]
    public void 끊김이_지침보다_세다()
    {
        Assert.Equal(OwlMood.Offline, OwlMoodResolver.Resolve(Document, 99, isDisconnected: true));
        Assert.Equal(OwlMood.Offline, OwlMoodResolver.Resolve(Document, null, isDisconnected: true));
    }

    [Fact]
    public void 값이_없으면_평소다()
    {
        Assert.Equal(OwlMood.Idle, OwlMoodResolver.Resolve(Document, null, isDisconnected: false));
    }

    [Fact]
    public void 기분마다_애니메이션이_실재한다()
    {
        foreach (var mood in Enum.GetValues<OwlMood>())
        {
            var animator = new OwlAnimator(Document);
            animator.SetMood(mood);
            Assert.NotEmpty(animator.CurrentGrid);
            Assert.Equal(Document.Grid.Lines, animator.CurrentGrid.Length);
            Assert.NotEmpty(animator.CurrentPalette);
        }
    }

    [Fact]
    public void 프레임을_돌고_처음으로_돌아온다()
    {
        var animator = new OwlAnimator(Document, new Random(1));
        animator.SetMood(OwlMood.Idle);
        var first = animator.CurrentGrid;

        var count = animator.Animation.Frames.Count;
        for (var i = 0; i < count; i++) animator.Advance();

        Assert.Equal(first, animator.CurrentGrid);
    }

    /// <summary>0초 프레임을 타이머에 그대로 넣으면 쉬지 않고 도는 루프가 된다.</summary>
    [Fact]
    public void 한_장짜리는_타이머를_걸지_않는다()
    {
        var animator = new OwlAnimator(Document);
        animator.SetMood(OwlMood.Offline);

        Assert.Single(animator.Animation.Frames);
        Assert.Null(animator.Advance());
        Assert.Null(animator.CurrentDelay());
    }

    [Fact]
    public void 여러_장짜리는_기다릴_시간을_준다()
    {
        var animator = new OwlAnimator(Document, new Random(1));
        animator.SetMood(OwlMood.Idle);

        var delay = animator.Advance();
        Assert.NotNull(delay);
        Assert.True(delay.Value > TimeSpan.Zero);
    }

    [Fact]
    public void 기분이_바뀌면_처음_프레임부터다()
    {
        var animator = new OwlAnimator(Document, new Random(1));
        animator.SetMood(OwlMood.Idle);
        animator.Advance();

        Assert.True(animator.SetMood(OwlMood.Tired));
        Assert.Equal(animator.Animation.Frames[0].Grid, animator.CurrentGrid);
        Assert.False(animator.SetMood(OwlMood.Tired));   // 같은 기분은 다시 시작하지 않는다
    }

    [Fact]
    public void 끊김은_회색_팔레트를_쓴다()
    {
        var animator = new OwlAnimator(Document);
        animator.SetMood(OwlMood.Offline);
        Assert.Equal("offline", animator.Animation.Palette);

        animator.SetMood(OwlMood.Idle);
        Assert.Equal("normal", animator.Animation.Palette);
    }
}

public class AppSettingsTests
{
    [Fact]
    public void 저장하고_다시_읽는다()
    {
        using var file = new TemporaryFile();
        new AppSettings { Scale = HudScale.Large, Theme = HudTheme.Dark, WindowLeft = 12.5 }
            .Save(file.Path);

        var loaded = AppSettings.Load(file.Path);

        Assert.Equal(HudScale.Large, loaded.Scale);
        Assert.Equal(HudTheme.Dark, loaded.Theme);
        Assert.Equal(12.5, loaded.WindowLeft);
    }

    /// <summary>
    /// **캐시를 뺀 값이 기본이다.** 캐시 읽기가 수천만이라 켜 두면 실제로 주고받은 양이
    /// 묻힌다 — 처음 보는 사람이 자기가 그만큼 쓴 줄 안다.
    /// </summary>
    [Fact]
    public void 측정은_캐시를_빼고_보여주는_것이_기본이다()
    {
        Assert.False(new AppSettings().MeasureIncludesCache);

        using var file = new TemporaryFile();
        new AppSettings { MeasureIncludesCache = true }.Save(file.Path);

        Assert.True(AppSettings.Load(file.Path).MeasureIncludesCache);
    }

    /// <summary>설정 파일이 깨졌다고 앱이 안 뜨면 안 된다.</summary>
    [Fact]
    public void 깨진_파일은_기본값으로_넘어간다()
    {
        using var file = new TemporaryFile();
        File.WriteAllText(file.Path, "{ 반쯤 쓰다 만");

        Assert.Equal(HudScale.Normal, AppSettings.Load(file.Path).Scale);
    }

    [Fact]
    public void 없는_파일도_기본값이다()
    {
        // 만들지 않는다 — 경로만 받아 온다.
        using var missing = new TemporaryFile();
        Assert.Equal(HudMode.Expanded, AppSettings.Load(missing.Path).Mode);
    }

    [Fact]
    public void 조회_주기를_1분에서_30분_사이로_자른다()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), new AppSettings { PollIntervalSeconds = 1 }.PollInterval);
        Assert.Equal(TimeSpan.FromMinutes(30), new AppSettings { PollIntervalSeconds = 99999 }.PollInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), new AppSettings { PollIntervalSeconds = 300 }.PollInterval);
    }

    [Theory]
    [InlineData(HudScale.Small, 0.85)]
    [InlineData(HudScale.Normal, 1.0)]
    [InlineData(HudScale.Large, 1.25)]
    [InlineData(HudScale.ExtraLarge, 1.5)]
    public void 배율이_맥과_같다(HudScale scale, double expected)
    {
        Assert.Equal(expected, scale.Factor());
    }
}

public class ChangelogTests
{
    [Fact]
    public void 뽑은_JSON_을_다시_읽는다()
    {
        var feed = Changelog.Parse(Changelog.Dump());

        Assert.NotNull(feed);
        Assert.Equal(Changelog.Entries.Count, feed.Entries.Count);
        Assert.Equal(Changelog.Entries[0].Version, feed.Entries[0].Version);
        Assert.Equal(Changelog.Entries[0].Notes, feed.Entries[0].Notes);
    }

    [Fact]
    public void 항목마다_버전과_내용이_있다()
    {
        Assert.NotEmpty(Changelog.Entries);
        foreach (var entry in Changelog.Entries)
        {
            Assert.Matches(@"^\d+\.\d+\.\d+(\.\d+)?$", entry.Version);
            Assert.NotEmpty(entry.Notes);
            Assert.All(entry.Notes, note => Assert.False(string.IsNullOrWhiteSpace(note)));
        }
    }

    /// <summary>맨 위는 아직 안 나간 항목이라 날짜가 비어 있고, 나머지는 채워져 있다.</summary>
    [Fact]
    public void 날짜는_맨_위만_비어_있을_수_있다()
    {
        foreach (var entry in Changelog.Entries.Skip(1))
        {
            Assert.NotNull(entry.Date);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", entry.Date);
        }
    }

    [Fact]
    public void 원격_주소가_윈도우_것을_가리킨다()
    {
        Assert.Contains("/win/docs/changelog.json", Changelog.FeedUrl.ToString());
    }
}

/// <summary>테스트가 시계에 기대지 않게 하는 가짜 시계.</summary>
/// <summary>
/// 손으로 돌리는 시계. **테스트는 벽시계에 기대지 않는다** — 언젠가 터진다.
///
/// 시작 시각을 안 주면 아무 날이나 하나로 시작한다. 시각 자체가 중요한 테스트만
/// 값을 넣으면 된다.
/// </summary>
internal sealed class FakeTime(DateTimeOffset? start = null) : TimeProvider
{
    private DateTimeOffset now = start ?? new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan by) => now += by;
}

public class AppVersionTests
{
    [Theory]
    [InlineData(1, 0, 0, 0, "1.0.0")]
    [InlineData(2, 1, 0, 0, "2.1.0")]
    [InlineData(1, 12, 3, 0, "1.12.3")]
    public void 네_번째_자리가_없으면_세_자리로_보인다(int a, int b, int c, int d, string expected)
    {
        Assert.Equal(expected, AppVersion.Format(new Version(a, b, c, d)));
    }

    /// <summary>
    /// 이걸 버리면 앱이 1.0.0.1 인데 자기를 1.0.0 이라고 말한다. 그러면 업데이트를
    /// 마친 뒤에도 "새 버전 1.0.0.1 이 있다"가 영영 사라지지 않는다.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 0, 1, "1.0.0.1")]
    [InlineData(1, 5, 2, 3, "1.5.2.3")]
    public void 긴급_자리는_살려서_보여준다(int a, int b, int c, int d, string expected)
    {
        Assert.Equal(expected, AppVersion.Format(new Version(a, b, c, d)));
    }

    [Fact]
    public void 버전이_없으면_0으로()
    {
        Assert.Equal("0.0.0", AppVersion.Format(null));
        Assert.Equal("1.2.0", AppVersion.Format(new Version(1, 2)));
    }

    [Theory]
    [InlineData("1.0.0.1", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0.1", true)]
    [InlineData("1.0.0", "1.0.0.1", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("2.0.0", "1.9.9", true)]
    public void 어느_쪽이_새_버전인지_안다(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsNewer(candidate, current));
    }

    /// <summary>Velopack 은 `1.0.0+abc` 처럼 꼬리표를 붙여 돌려주기도 한다.</summary>
    [Theory]
    [InlineData("1.0.0.1", "1.0.0.1")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3+build9", "1.2.3")]
    [InlineData("1.2.3-beta", "1.2.3")]
    public void 꼬리표가_붙어도_읽는다(string text, string expected)
    {
        Assert.True(AppVersion.TryParse(text, out var parsed));
        Assert.Equal(expected, AppVersion.Format(parsed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("최신")]
    [InlineData(null)]
    public void 읽을_수_없으면_false(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
        Assert.False(AppVersion.IsNewer(text, "1.0.0"));
    }

    /// <summary>
    /// 릴리스에 올릴 수 있는 자리 수인지. `TryParse` 와 달리 **관대하면 안 된다** —
    /// 여기를 통과한 문자열이 그대로 `vpk --packVersion` 으로 가고, 거기는
    /// SemVer2 세 자리만 받는다. 꼬리표나 `v` 접두어를 봐주면 릴리스 막바지에 죽는다.
    /// </summary>
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.12.3", true)]
    [InlineData("10.0.0", true)]
    [InlineData("1.0.0.1", false)]     // 긴급 자리는 윈도우에서 못 쓴다
    [InlineData("1.0", false)]
    [InlineData("1.2.3+build9", false)]
    [InlineData("v1.2.3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 세_자리인지_가른다(string? text, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsThreePart(text));
    }
}

/// <summary>
/// **파일에 적힌 만료 시각만 보고 조회를 포기하면 안 된다.**
///
/// Claude Code 는 토큰을 메모리에서 갱신하고 `.credentials.json` 을 곧바로 다시 쓰지
/// 않는다. 그래서 Claude 가 멀쩡히 도는 중에도 파일은 만료로 보인다. 1.1.0 이 그 상태를
/// "토큰 만료"로 단정하고 **API 를 한 번도 부르지 않았다.**
/// </summary>
public class ExpiredCredentialTests
{
    private static string Json(long expiresAtMs) => $$"""
        { "claudeAiOauth": { "accessToken": "tok", "subscriptionType": "max", "expiresAt": {{expiresAtMs}} } }
        """;

    [Fact]
    public void 파일이_만료로_보여도_자격_증명은_읽힌다()
    {
        var past = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var parsed = ClaudeCredentials.Parse(Json(past));

        Assert.NotNull(parsed);
        Assert.Equal("tok", parsed.AccessToken);
        Assert.True(parsed.IsExpired(DateTimeOffset.UtcNow));
    }

    /// <summary>맥은 Double 로 읽는다. 이쪽만 정수를 고집하면 같은 파일에서 갈린다.</summary>
    [Theory]
    [InlineData("1786000000000")]
    [InlineData("1786000000000.0")]
    [InlineData("\"1786000000000\"")]
    public void 만료_시각이_정수든_소수든_문자열이든_읽는다(string raw)
    {
        var json = $$"""{ "claudeAiOauth": { "accessToken": "tok", "expiresAt": {{raw}} } }""";
        var parsed = ClaudeCredentials.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(2026, parsed.ExpiresAt!.Value.Year);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"곧\"")]
    [InlineData("-1")]
    public void 만료_시각이_이상하면_없는_것으로_본다(string raw)
    {
        var json = $$"""{ "claudeAiOauth": { "accessToken": "tok", "expiresAt": {{raw}} } }""";
        var parsed = ClaudeCredentials.Parse(json);

        Assert.NotNull(parsed);
        Assert.Null(parsed.ExpiresAt);
        // 만료 시각을 모르면 만료로 단정하지 않는다. 서버가 판단하게 둔다.
        Assert.False(parsed.IsExpired(DateTimeOffset.UtcNow));
    }

    /// <summary>서버가 거절한 것과 파일만 지난 것을 문구로 구분한다.</summary>
    [Fact]
    public void 만료_문구가_원인을_가른다()
    {
        Assert.Contains("서버가 토큰을 거절", UsageError.TokenExpired().Message);
        Assert.Contains("파일·서버 모두", UsageError.TokenExpired(fileAlsoSaidExpired: true).Message);
    }
}
