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
        var path = Path.Combine(Path.GetTempPath(), $"dongcsu-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, Sample);
            var read = new FileCredentialSource([path]).Read();
            Assert.Equal("sk-ant-oat01-example", read?.AccessToken);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 파일이_없으면_null()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dongcsu-none-{Guid.NewGuid():N}.json");
        Assert.Null(new FileCredentialSource([missing]).Read());
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

    private sealed class CountingSource(string json) : ICredentialSource
    {
        public int Reads { get; private set; }
        public ClaudeCredentials? Read() { Reads++; return ClaudeCredentials.Parse(json); }
    }
}

public class UsageApiTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 응답을_스냅숏으로_읽는다()
    {
        const string body = """
            {
              "five_hour": { "utilization": 34.2, "resets_at": "2026-08-06T15:30:00Z" },
              "seven_day": { "utilization": 61,   "resets_at": "2026-08-10T00:00:00.000Z" }
            }
            """;

        var result = UsageApi.Parse(body, "max", Now);

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
        var result = UsageApi.Parse("""{"five_hour":{"utilization":140}}""", null, Now);
        Assert.Equal(100, result.Snapshot!.FiveHour!.Value.Utilization);

        result = UsageApi.Parse("""{"five_hour":{"utilization":-5}}""", null, Now);
        Assert.Equal(0, result.Snapshot!.FiveHour!.Value.Utilization);
    }

    [Theory]
    [InlineData("""{"five_hour":{}}""")]
    [InlineData("""{"five_hour":{"utilization":"많이"}}""")]
    [InlineData("""{}""")]
    public void 창이_없거나_이상하면_null_이지_실패가_아니다(string body)
    {
        var result = UsageApi.Parse(body, null, Now);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Snapshot!.FiveHour);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public void 본문이_형식이_아니면_실패한다(string body)
    {
        var result = UsageApi.Parse(body, null, Now);
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

    /// <summary>상한이 없으면 한 번 막혔을 때 영영 안 돌아온다.</summary>
    [Fact]
    public void 연달아_막히면_점점_물러나되_30분을_넘지_않는다()
    {
        var time = new FakeTime();
        var store = NewStore(time);

        for (var i = 0; i < 20; i++)
        {
            store.Apply(UsageResult.Fail(UsageError.RateLimited(null)));
        }

        Assert.True(store.NextPollDelay() <= TimeSpan.FromMinutes(30));
        Assert.True(store.NextPollDelay() > TimeSpan.FromMinutes(5));
    }

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
        var path = Path.Combine(Path.GetTempPath(), $"dongcsu-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { Scale = HudScale.Large, Theme = HudTheme.Dark, WindowLeft = 12.5 }
                .Save(path);

            var loaded = AppSettings.Load(path);

            Assert.Equal(HudScale.Large, loaded.Scale);
            Assert.Equal(HudTheme.Dark, loaded.Theme);
            Assert.Equal(12.5, loaded.WindowLeft);
        }
        finally { File.Delete(path); }
    }

    /// <summary>설정 파일이 깨졌다고 앱이 안 뜨면 안 된다.</summary>
    [Fact]
    public void 깨진_파일은_기본값으로_넘어간다()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dongcsu-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ 반쯤 쓰다 만");
            var loaded = AppSettings.Load(path);
            Assert.Equal(HudScale.Normal, loaded.Scale);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 없는_파일도_기본값이다()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dongcsu-none-{Guid.NewGuid():N}.json");
        Assert.Equal(HudMode.Expanded, AppSettings.Load(missing).Mode);
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
internal sealed class FakeTime : TimeProvider
{
    private DateTimeOffset now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
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
