using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 자격 증명을 못 읽었을 때 **왜인지**를 짚는다.
///
/// 2.0.0 에서 실제로 난 일이다 — 사용자 기록에 "파일 있음 / 읽기 실패" 두 줄만 남아서
/// 원인을 알 수 없었다. 셋은 사용자가 할 일이 전혀 다르다.
/// </summary>
public class CredentialLookupTests
{
    [Fact]
    public void 제대로_된_파일은_읽힌다()
    {
        var (credentials, problem, _) = ClaudeCredentials.Examine("""
            { "claudeAiOauth": { "accessToken": "t", "subscriptionType": "max" } }
            """);

        Assert.NotNull(credentials);
        Assert.Equal(CredentialProblem.None, problem);
    }

    /// <summary>
    /// 앱만 쓰는 사람에게서 온 모습이다. 파일은 있는데 Claude 로그인이 아니라
    /// MCP 토큰만 들어 있다.
    /// </summary>
    [Fact]
    public void Claude_로그인이_없으면_그렇다고_알려준다()
    {
        var (credentials, problem, keys) = ClaudeCredentials.Examine("""
            { "mcpOAuth": { "some-server": { "accessToken": "x" } } }
            """);

        Assert.Null(credentials);
        Assert.Equal(CredentialProblem.NoClaudeLogin, problem);
        Assert.Equal("mcpOAuth", keys);
    }

    [Fact]
    public void 토큰이_비어_있으면_구분한다()
    {
        var (_, problem, _) = ClaudeCredentials.Examine("""{ "claudeAiOauth": { "accessToken": "" } }""");
        Assert.Equal(CredentialProblem.NoAccessToken, problem);

        var (_, missing, _) = ClaudeCredentials.Examine("""{ "claudeAiOauth": { "expiresAt": 1 } }""");
        Assert.Equal(CredentialProblem.NoAccessToken, missing);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("")]
    public void 형식이_아니면_그렇다고_알려준다(string json)
    {
        var (credentials, problem, _) = ClaudeCredentials.Examine(json);

        Assert.Null(credentials);
        Assert.Equal(CredentialProblem.NotJson, problem);
    }

    /// <summary>
    /// **키 이름만 담고 값은 절대 담지 않는다.** 이 문자열이 그대로 기록 파일로 나간다.
    /// </summary>
    [Fact]
    public void 키_이름만_담고_토큰은_담지_않는다()
    {
        var (_, _, keys) = ClaudeCredentials.Examine("""
            { "mcpOAuth": {}, "somethingElse": { "accessToken": "sk-ant-secret-value" } }
            """);

        Assert.NotNull(keys);
        Assert.Contains("mcpOAuth", keys);
        Assert.Contains("somethingElse", keys);
        Assert.DoesNotContain("sk-ant", keys);
        Assert.DoesNotContain("secret", keys);
    }

    [Fact]
    public void 파일이_없으면_없다고_한다()
    {
        using var temporary = new TemporaryFile();
        var source = new FileCredentialSource([temporary.Path]);

        var attempts = source.Inspect();

        Assert.Single(attempts);
        Assert.Equal(CredentialProblem.NotFound, attempts[0].Problem);
        Assert.Null(source.Read());
    }

    /// <summary>앞의 자리가 비었어도 뒤에서 찾으면 된다.</summary>
    [Fact]
    public void 여러_자리를_차례로_본다()
    {
        using var empty = new TemporaryFile();
        using var broken = new TemporaryFile();
        using var good = new TemporaryFile();

        File.WriteAllText(broken.Path, """{ "mcpOAuth": {} }""");
        File.WriteAllText(good.Path, """{ "claudeAiOauth": { "accessToken": "found" } }""");

        var source = new FileCredentialSource([empty.Path, broken.Path, good.Path]);
        var attempts = source.Inspect();

        Assert.Equal(3, attempts.Count);
        Assert.Equal(CredentialProblem.NotFound, attempts[0].Problem);
        Assert.Equal(CredentialProblem.NoClaudeLogin, attempts[1].Problem);
        Assert.True(attempts[2].Found);
        Assert.Equal("found", source.Read()!.AccessToken);
    }

    /// <summary>찾으면 거기서 멈춘다. 뒤의 자리는 건드리지 않는다.</summary>
    [Fact]
    public void 찾으면_뒤는_보지_않는다()
    {
        using var good = new TemporaryFile();
        using var later = new TemporaryFile();
        File.WriteAllText(good.Path, """{ "claudeAiOauth": { "accessToken": "first" } }""");
        File.WriteAllText(later.Path, """{ "claudeAiOauth": { "accessToken": "second" } }""");

        var attempts = new FileCredentialSource([good.Path, later.Path]).Inspect();

        Assert.Single(attempts);
        Assert.Equal("first", attempts[0].Credentials!.AccessToken);
    }

    /// <summary>
    /// WSL 배포판 하나 안에서 볼 자리. <c>/root</c> 는 늘 후보이고, <c>/home</c> 아래는
    /// 닿지 않으면(없는 기계) 그냥 빠진다 — 던지면 안 된다.
    /// </summary>
    [Fact]
    public void WSL_배포판_안의_자리를_만든다()
    {
        var paths = FileCredentialSource.WslPathsUnder(@"\\wsl.localhost\없는배포판").ToList();

        Assert.Contains(paths, path => path.Contains("root"));
        Assert.All(paths, path => Assert.EndsWith(".credentials.json", path));
    }

    /// <summary>뒷자리는 앞에서 못 찾았을 때만 본다. WSL 을 괜히 깨우지 않는다.</summary>
    [Fact]
    public void 앞에서_찾으면_뒷자리는_보지_않는다()
    {
        using var good = new TemporaryFile();
        File.WriteAllText(good.Path, """{ "claudeAiOauth": { "accessToken": "t" } }""");

        var asked = false;
        var source = new FileCredentialSource([good.Path], () => { asked = true; return []; });

        Assert.NotNull(source.Read());
        Assert.False(asked);
    }

    [Fact]
    public void 앞에서_못_찾으면_뒷자리를_본다()
    {
        using var missing = new TemporaryFile();
        using var wsl = new TemporaryFile();
        File.WriteAllText(wsl.Path, """{ "claudeAiOauth": { "accessToken": "from-wsl" } }""");

        var source = new FileCredentialSource([missing.Path], () => [wsl.Path]);

        Assert.Equal("from-wsl", source.Read()!.AccessToken);
    }

    [Fact]
    public void 기본_자리에_윈도우_홈이_들어_있다()
    {
        var paths = FileCredentialSource.DefaultPaths().ToList();

        Assert.Contains(paths, path => path.Contains(".claude"));
        Assert.All(paths, path => Assert.EndsWith(".credentials.json", path));
    }
}
