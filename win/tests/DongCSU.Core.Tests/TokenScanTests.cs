using System.Globalization;
using System.Text;
using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

/// <summary>
/// 검사가 만들어 쓰는 임시 기록 폴더.
///
/// **이 기계의 진짜 기록(<c>~/.claude/projects</c>)에 기대지 않는다.** 그러면 값이
/// 사람마다·날마다 달라지고, 기록이 없는 맥·CI 에서는 통째로 못 돌린다. 훑기 함수들이
/// 하나같이 <c>root</c> 를 받는 이유가 이거다.
/// </summary>
internal sealed class TempTranscripts : IDisposable
{
    public string Root { get; } = Path.Combine(
        Path.GetTempPath(), $"dongcsu-scan-{Guid.NewGuid():N}");

    public TempTranscripts() => Directory.CreateDirectory(Root);

    public string Full(string relative) => Path.Combine(Root, relative);

    /// <summary>파일 하나를 새로 쓴다. 하위 폴더는 알아서 만든다.</summary>
    public string Write(string relative, string content, Encoding? encoding = null)
    {
        var path = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    public string Append(string relative, string content)
    {
        var path = Full(relative);
        File.AppendAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>기록 한 줄을 짓는다. 검사마다 JSON 을 손으로 적으면 오타가 검사를 죽인다.</summary>
internal static class Jsonl
{
    /// <summary>실제 기록의 모양 그대로 — <c>2026-07-26T12:19:00.573Z</c>.</summary>
    public static string Stamp(DateTimeOffset at) => at.ToUniversalTime()
        .ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z";

    /// <summary>
    /// 자리표를 갈아 끼우는 식으로 짓는다 — JSON 은 중괄호가 많아서 보간 문자열에 그대로
    /// 넣으면 어디까지가 보간인지 눈으로 안 잡힌다.
    /// </summary>
    public static string Line(
        string id,
        DateTimeOffset at,
        string? model = "claude-opus-5",
        long input = 0,
        long output = 0,
        long cacheCreation = 0,
        long cacheRead = 0)
    {
        const string template = """
            {"type":"assistant","timestamp":"@STAMP","message":{"id":"@ID",@MODEL"usage":{"input_tokens":@IN,"output_tokens":@OUT,"cache_creation_input_tokens":@CC,"cache_read_input_tokens":@CR}}}
            """;

        return template
            .Replace("@STAMP", Stamp(at))
            .Replace("@MODEL", model is null ? "" : "\"model\":\"" + model + "\",")
            .Replace("@ID", id)
            .Replace("@IN", Digits(input))
            .Replace("@OUT", Digits(output))
            .Replace("@CC", Digits(cacheCreation))
            .Replace("@CR", Digits(cacheRead)) + "\n";
    }

    private static string Digits(long value) => value.ToString(CultureInfo.InvariantCulture);
}

public class TokenTallyTests
{
    [Fact]
    public void 합계와_캐시를_뺀_합계를_따로_센다()
    {
        var tally = new TokenTally(Responses: 3, Input: 10, Output: 20,
            CacheCreation: 300, CacheRead: 4000);

        Assert.Equal(4330, tally.Total);
        Assert.Equal(30, tally.WithoutCache);
    }

    /// <summary>토큰이 아니라 응답 수를 본다 — 값이 전부 0인 응답도 온 것은 온 것이다.</summary>
    [Fact]
    public void 빈지_아닌지는_응답_수로_판단한다()
    {
        Assert.True(default(TokenTally).IsEmpty);
        Assert.False(new TokenTally(Responses: 1, 0, 0, 0, 0).IsEmpty);
        Assert.True(new TokenTally(Responses: 0, Input: 999, 0, 0, 0).IsEmpty);
    }

    [Fact]
    public void 칸마다_더한다()
    {
        var sum = new TokenTally(1, 2, 3, 4, 5) + new TokenTally(10, 20, 30, 40, 50);

        Assert.Equal(new TokenTally(11, 22, 33, 44, 55), sum);
    }

    /// <summary>
    /// **<c>int</c> 로 짰으면 여기서 걸린다.** 캐시 읽기는 한 측정에서 이미 4.5억이라
    /// 몇 번 쌓이면 21.4억을 넘긴다 — 맥의 <c>Int</c> 는 64비트라 안 겪던 일이다.
    /// </summary>
    [Fact]
    public void 캐시_읽기가_30억을_넘어도_안_깨진다()
    {
        var big = new TokenTally(1, 0, 0, 0, CacheRead: 3_000_000_000L);

        Assert.Equal(3_000_000_000L, big.CacheRead);
        Assert.Equal(3_000_000_000L, big.Total);
        Assert.Equal(6_000_000_000L, (big + big).CacheRead);
        Assert.True((big + big).Total > int.MaxValue);
    }

    /// <summary>값 타입이라야 사전에 담아 더할 때 참조가 안 얽힌다.</summary>
    [Fact]
    public void 사전에_담아_더해도_원본이_안_바뀐다()
    {
        var first = new TokenTally(1, 1, 1, 1, 1);
        var byModel = new Dictionary<string, TokenTally> { ["Opus 5"] = first };

        byModel["Opus 5"] = byModel["Opus 5"] + new TokenTally(1, 1, 1, 1, 1);

        Assert.Equal(new TokenTally(1, 1, 1, 1, 1), first);
        Assert.Equal(new TokenTally(2, 2, 2, 2, 2), byModel["Opus 5"]);
    }
}

/// <summary>
/// 환경변수를 만지는 검사를 한 줄로 세운다.
///
/// **xUnit 은 같은 어셈블리의 클래스를 병렬로 돌린다.** 프로세스 환경변수는 하나뿐이라
/// 두 클래스가 동시에 <c>CLAUDE_CONFIG_DIR</c> 을 세우면 서로의 답을 갈아엎는다.
/// </summary>
[CollectionDefinition("환경변수")]
public sealed class EnvironmentCollection;

[Collection("환경변수")]
public class ClaudeConfigDirTests
{
    private const string Variable = "CLAUDE_CONFIG_DIR";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>환경변수를 세워 두고 잰 뒤 반드시 되돌린다.</summary>
    private static T With<T>(string? value, Func<T> body)
    {
        var original = Environment.GetEnvironmentVariable(Variable);
        Environment.SetEnvironmentVariable(Variable, value);
        try { return body(); }
        finally { Environment.SetEnvironmentVariable(Variable, original); }
    }

    [Fact]
    public void 환경변수가_없으면_홈_아래를_본다()
    {
        var expected = Path.Combine(Home, ".claude", "projects");

        Assert.Equal(expected, With<string>(null, () => ClaudeCodeUsage.ProjectsDirectory));
    }

    [Fact]
    public void 환경변수가_있으면_그_아래_projects_다()
    {
        using var temp = new TempTranscripts();

        var actual = With(temp.Root, () => ClaudeCodeUsage.ProjectsDirectory);

        Assert.Equal(Path.Combine(temp.Root, "projects"), actual);
    }

    /// <summary>공백만 적어 둔 것은 안 적은 것과 같다. 안 그러면 경로가 `\projects` 가 된다.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void 공백만_있으면_없는_것으로_본다(string value)
    {
        var expected = Path.Combine(Home, ".claude", "projects");

        Assert.Equal(expected, With(value, () => ClaudeCodeUsage.ProjectsDirectory));
    }

    /// <summary>
    /// 맥의 틸드 전개 자리. 윈도우 사람도 <c>~/.claude</c> 를 그대로 적어 두는 일이 있다.
    /// </summary>
    [Theory]
    [InlineData("~")]
    [InlineData("~/.claude")]
    public void 틸드를_홈으로_편다(string value)
    {
        var actual = With(value, () => ClaudeCodeUsage.ProjectsDirectory);

        Assert.StartsWith(Home, actual, StringComparison.Ordinal);
        Assert.EndsWith("projects", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("~", actual, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>%USERPROFILE%\.claude</c> 처럼 환경변수를 적어 두는 사람이 윈도우에는 실제로 있다.
    /// (맥에는 이 전개가 없어서 윈도우에서만 본다.)
    /// </summary>
    [Fact]
    public void 환경변수_표기를_편다()
    {
        if (!OperatingSystem.IsWindows()) return;

        var actual = With(@"%USERPROFILE%\.claude", () => ClaudeCodeUsage.ProjectsDirectory);

        Assert.Equal(Path.Combine(Home, ".claude", "projects"), actual);
    }

    /// <summary>
    /// **후보에 WSL 이 하나도 없어야 한다.** 기록 훑기는 측정이 도는 동안 60초마다 도는데,
    /// <c>\\wsl.localhost\…</c> 를 그 주기로 건드리면 꺼져 있던 배포판이 계속 깨어 있게 된다.
    /// </summary>
    [Fact]
    public void 후보에_WSL_이_없다()
    {
        Assert.DoesNotContain("wsl", With<string>(null, () => ClaudeCodeUsage.ProjectsDirectory),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>못 찾았다는 사실을 밖으로 알려야 화면이 0 대신 "찾지 못했다"를 띄운다.</summary>
    [Fact]
    public void 폴더가_없으면_못_찾은_것이다()
    {
        using var temp = new TempTranscripts();

        Assert.False(With(temp.Root, () => ClaudeCodeUsage.IsAvailable));

        Directory.CreateDirectory(Path.Combine(temp.Root, "projects"));

        Assert.True(With(temp.Root, () => ClaudeCodeUsage.IsAvailable));
    }
}

public class TranscriptWalkTests
{
    [Fact]
    public void 깊이_상관없이_jsonl_만_모은다()
    {
        using var temp = new TempTranscripts();
        temp.Write("z.jsonl", "1\n");
        temp.Write(Path.Combine("a", "b", "c", "x.jsonl"), "12\n");
        temp.Write("note.txt", "이건 기록이 아니다\n");

        var files = ClaudeCodeUsage.Transcripts(temp.Root);

        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.EndsWith(".jsonl", file.Path, StringComparison.Ordinal));
    }

    /// <summary>크기는 훑을 때 이미 받아 둔 값이다. 틀리면 훑기가 파일을 건너뛰거나 두 번 센다.</summary>
    [Fact]
    public void 크기를_함께_들고_온다()
    {
        using var temp = new TempTranscripts();
        var path = temp.Write("a.jsonl", "0123456789\n");

        var file = Assert.Single(ClaudeCodeUsage.Transcripts(temp.Root));

        Assert.Equal(new FileInfo(path).Length, file.Length);
    }

    [Fact]
    public void 숨김_파일은_빼놓는다()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempTranscripts();
        temp.Write("보임.jsonl", "1\n");
        var hidden = temp.Write("숨김.jsonl", "1\n");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var file = Assert.Single(ClaudeCodeUsage.Transcripts(temp.Root));

        Assert.EndsWith("보임.jsonl", file.Path, StringComparison.Ordinal);
    }

    /// <summary>폴더가 통째로 사라졌다고 측정이 죽으면 안 된다.</summary>
    [Fact]
    public void 없는_폴더는_빈_목록이고_안_던진다()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dongcsu-none-{Guid.NewGuid():N}");

        Assert.Empty(ClaudeCodeUsage.Transcripts(missing));
        Assert.Empty(ClaudeCodeUsage.EndOffsets(missing));
    }

    [Fact]
    public void 지금_끝을_경로별로_담는다()
    {
        using var temp = new TempTranscripts();
        var one = temp.Write("one.jsonl", "1234\n");
        var two = temp.Write(Path.Combine("깊이", "two.jsonl"), "12345678\n");

        var offsets = ClaudeCodeUsage.EndOffsets(temp.Root);

        Assert.Equal(2, offsets.Count);
        Assert.Equal(new FileInfo(one).Length, offsets[one]);
        Assert.Equal(new FileInfo(two).Length, offsets[two]);
    }

    /// <summary>
    /// 윈도우 파일 시스템은 대소문자를 안 가린다. 사전이 가리면 같은 파일이 두 항목으로
    /// 갈리고, 한쪽 오프셋이 0 인 채로 남아 그 파일을 통째로 다시 센다.
    /// </summary>
    [Fact]
    public void 경로_사전은_윈도우에서_대소문자를_안_가린다()
    {
        using var temp = new TempTranscripts();
        var path = temp.Write("Case.jsonl", "1234\n");

        var offsets = ClaudeCodeUsage.EndOffsets(temp.Root);

        if (OperatingSystem.IsWindows())
        {
            Assert.True(offsets.ContainsKey(path.ToUpperInvariant()));
            Assert.True(offsets.ContainsKey(path.ToLowerInvariant()));
        }
        else
        {
            Assert.True(offsets.ContainsKey(path));
        }
    }

    /// <summary>
    /// System.Text.Json 이 읽어 온 사전의 비교자는 기본값이다. 다시 싸지 않으면 위와
    /// 같은 이유로 오프셋이 되감긴다.
    /// </summary>
    [Fact]
    public void 읽어_온_사전을_다시_싼다()
    {
        var plain = new Dictionary<string, long>(StringComparer.Ordinal) { [@"C:\A\x.jsonl"] = 7 };

        var wrapped = ClaudeCodeUsage.WithPathComparer(plain);

        Assert.Equal(7, wrapped[@"C:\A\x.jsonl"]);
        Assert.Equal(OperatingSystem.IsWindows(), wrapped.ContainsKey(@"c:\a\x.jsonl"));
        Assert.Empty(ClaudeCodeUsage.WithPathComparer(null));
    }
}

public class ModelNameTests
{
    [Theory]
    [InlineData("claude-opus-5", "Opus 5")]
    [InlineData("claude-haiku-4-5", "Haiku 4.5")]
    [InlineData("claude-sonnet-4-5-20250929", "Sonnet 4.5")]
    [InlineData("claude-3-5-sonnet-20241022", "3.5 Sonnet")]
    // 앞머리가 없어도 그대로 다듬는다.
    [InlineData("opus-5", "Opus 5")]
    [InlineData("gpt-4", "Gpt 4")]
    // 날짜만 남으면 조각이 없다 — 원문을 그대로 둔다.
    [InlineData("claude-20251001", "claude-20251001")]
    [InlineData("claude-", "claude-")]
    [InlineData("", "")]
    // 7자리는 날짜가 아니다. 길이 8을 정확히 봐야 한다.
    [InlineData("claude-opus-5-1234567", "Opus 5.1234567")]
    public void 모델_이름을_다듬는다(string raw, string expected)
    {
        Assert.Equal(expected, ClaudeCodeUsage.DisplayName(raw));
    }
}

public class TokenScanTests
{
    private static readonly DateTimeOffset Since = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private static TokenScanResult Scan(TempTranscripts temp, TokenScanResult? previous = null)
        => new TokenScan(Since, previous?.Offsets, previous?.SeenIds, temp.Root).Run();

    [Fact]
    public void 줄마다_한_응답으로_센다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl",
            Jsonl.Line("a1", Since.AddMinutes(1), input: 10, output: 20,
                cacheCreation: 300, cacheRead: 4000)
            + Jsonl.Line("a2", Since.AddMinutes(2), input: 1, output: 2,
                cacheCreation: 3, cacheRead: 4));

        var result = Scan(temp);

        Assert.Equal(new TokenTally(2, 11, 22, 303, 4004), result.Added);
    }

    /// <summary>
    /// **두 번째 훑기는 덧붙은 것만 읽어야 한다.** 파일이 30MB 씩 되기 때문에 1분마다
    /// 통째로 다시 읽을 수 없고, 다시 읽으면 값도 두 배가 된다.
    /// </summary>
    [Fact]
    public void 두_번째_훑기는_덧붙은_것만_읽는다()
    {
        using var temp = new TempTranscripts();
        var path = temp.Write("a.jsonl",
            Jsonl.Line("a1", Since.AddMinutes(1), output: 100)
            + Jsonl.Line("a2", Since.AddMinutes(2), output: 200));

        var first = Scan(temp);
        Assert.Equal(2, first.Added.Responses);
        Assert.Equal(new FileInfo(path).Length, first.Offsets[path]);

        temp.Append("a.jsonl", Jsonl.Line("a3", Since.AddMinutes(3), output: 7));
        var second = Scan(temp, first);

        Assert.Equal(1, second.Added.Responses);
        Assert.Equal(7, second.Added.Output);
        Assert.Equal(new FileInfo(path).Length, second.Offsets[path]);
    }

    /// <summary>덧붙은 게 없으면 아무것도 안 더한다(파일을 열지도 않는 빠른 길이다).</summary>
    [Fact]
    public void 덧붙은_게_없으면_0_이다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl", Jsonl.Line("a1", Since.AddMinutes(1), output: 5));

        var first = Scan(temp);
        var second = Scan(temp, first);

        Assert.True(second.Added.IsEmpty);
        Assert.Equal(first.Offsets, second.Offsets);
    }

    /// <summary>
    /// 마침 쓰는 중이면 마지막 줄이 잘려 있다. **그걸 파싱하면 그 응답을 영영 놓친다** —
    /// 오프셋만 넘어가고 값은 안 들어오기 때문이다. 개행이 올 때까지 미룬다.
    /// </summary>
    [Fact]
    public void 개행이_없는_꼬리는_다음_훑기로_미룬다()
    {
        using var temp = new TempTranscripts();
        var whole = Jsonl.Line("b", Since.AddMinutes(2), output: 999);
        var head = whole[..20];
        var tail = whole[20..];

        var path = temp.Write("a.jsonl", Jsonl.Line("a", Since.AddMinutes(1), output: 1) + head);

        var first = Scan(temp);
        Assert.Equal(1, first.Added.Responses);
        Assert.Equal(1, first.Added.Output);
        // 마지막 개행 다음에서 멈춘다 — 잘린 꼬리만큼은 안 넘어간다.
        Assert.Equal(new FileInfo(path).Length - head.Length, first.Offsets[path]);

        temp.Append("a.jsonl", tail);
        var second = Scan(temp, first);

        Assert.Equal(1, second.Added.Responses);
        Assert.Equal(999, second.Added.Output);
        Assert.Equal(new FileInfo(path).Length, second.Offsets[path]);
    }

    /// <summary>
    /// 세션을 이어가면 **옛 응답이 새 파일로 통째로 복사된다.** 복사본은 원래 시각을
    /// 그대로 달고 오므로 여기서 걸러야 한다 — 오프셋만으로는 못 막는다.
    /// </summary>
    [Fact]
    public void 기준보다_앞선_기록은_버린다()
    {
        using var temp = new TempTranscripts();
        temp.Write("이어감.jsonl",
            Jsonl.Line("옛것", Since.AddHours(-3), output: 5000)
            + Jsonl.Line("새것", Since.AddMinutes(1), output: 7));

        var result = Scan(temp);

        Assert.Equal(1, result.Added.Responses);
        Assert.Equal(7, result.Added.Output);
    }

    /// <summary>경계는 포함이다 — 시작한 그 순간의 응답을 놓치면 첫 표본이 통째로 빈다.</summary>
    [Fact]
    public void 기준과_같은_시각은_센다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl", Jsonl.Line("a", Since, output: 3));

        Assert.Equal(1, Scan(temp).Added.Responses);
    }

    /// <summary>
    /// **같은 응답이 두세 줄에 걸쳐 적힌다.** 값이 매번 같아서 그냥 세면 두세 배가 된다.
    /// 파일이 갈려도 마찬가지다.
    /// </summary>
    [Fact]
    public void 같은_id_는_한_번만_센다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl",
            Jsonl.Line("같은id", Since.AddMinutes(1), output: 100)
            + Jsonl.Line("같은id", Since.AddMinutes(1), output: 100));
        temp.Write(Path.Combine("다른", "b.jsonl"), Jsonl.Line("같은id", Since.AddMinutes(2), output: 100));

        var result = Scan(temp);

        Assert.Equal(1, result.Added.Responses);
        Assert.Equal(100, result.Added.Output);
        Assert.Single(result.SeenIds);
    }

    /// <summary>앞선 훑기에서 본 id 는 다음 훑기에도 안 센다.</summary>
    [Fact]
    public void 앞_훑기에서_본_id_도_안_센다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl", Jsonl.Line("dup", Since.AddMinutes(1), output: 100));
        var first = Scan(temp);

        // 같은 응답이 다른 파일에 다시 적혔다.
        temp.Write("b.jsonl", Jsonl.Line("dup", Since.AddMinutes(2), output: 100));
        var second = Scan(temp, first);

        Assert.True(second.Added.IsEmpty);
    }

    /// <summary>
    /// 파일이 지워졌다 다시 만들어졌다. **0부터 다시 읽으면 이미 센 것을 또 센다** —
    /// 지금 끝을 새 기준으로 삼는다.
    /// </summary>
    [Fact]
    public void 파일이_줄면_오프셋을_새_크기로_내린다()
    {
        using var temp = new TempTranscripts();
        var path = temp.Write("a.jsonl",
            Jsonl.Line("a1", Since.AddMinutes(1), output: 1)
            + Jsonl.Line("a2", Since.AddMinutes(2), output: 2)
            + Jsonl.Line("a3", Since.AddMinutes(3), output: 3));
        var first = Scan(temp);

        temp.Write("a.jsonl", Jsonl.Line("새것", Since.AddMinutes(4), output: 9));
        var shrunk = new FileInfo(path).Length;
        Assert.True(shrunk < first.Offsets[path], "다시 쓴 파일이 더 작아야 하는 검사다");

        var second = Scan(temp, first);

        Assert.True(second.Added.IsEmpty);
        Assert.Equal(shrunk, second.Offsets[path]);

        // 내려 둔 자리부터라야 그다음에 덧붙은 것을 놓치지 않는다.
        temp.Append("a.jsonl", Jsonl.Line("그다음", Since.AddMinutes(5), output: 11));
        var third = Scan(temp, second);

        Assert.Equal(1, third.Added.Responses);
        Assert.Equal(11, third.Added.Output);
    }

    /// <summary>줄 하나가 깨졌다고 그 뒤를 통째로 버리면 안 된다.</summary>
    [Fact]
    public void 깨진_줄은_건너뛰고_계속_간다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl",
            Jsonl.Line("a1", Since.AddMinutes(1), output: 1)
            + "이건 JSON 이 아니다\n"
            + "{\"message\":{\n"
            + "[1,2,3]\n"
            + "\n"
            + Jsonl.Line("a2", Since.AddMinutes(2), output: 2));

        var result = Scan(temp);

        Assert.Equal(2, result.Added.Responses);
        Assert.Equal(3, result.Added.Output);
    }

    /// <summary><c>JsonDocument.Parse</c> 는 BOM 을 안 건너뛰고 0xEF 를 만나면 던진다.</summary>
    [Fact]
    public void BOM_이_있어도_첫_줄을_읽는다()
    {
        using var temp = new TempTranscripts();
        var path = temp.Write("a.jsonl", Jsonl.Line("a", Since.AddMinutes(1), output: 42),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = Scan(temp);

        Assert.Equal(1, result.Added.Responses);
        Assert.Equal(42, result.Added.Output);
        // BOM 세 바이트도 파일 안의 바이트다 — 오프셋에는 그대로 들어간다.
        Assert.Equal(new FileInfo(path).Length, result.Offsets[path]);
    }

    /// <summary>
    /// **오프셋은 글자가 아니라 바이트다.** 한국어 한 글자가 UTF-8 로 3바이트라, 글자 수로
    /// 세면 다음 라운드가 줄 한가운데로 건너뛴다.
    /// </summary>
    [Fact]
    public void 오프셋은_글자가_아니라_바이트다()
    {
        using var temp = new TempTranscripts();
        // 보간 원시 문자열은 닫는 중괄호가 세 개 이어지면 못 쓴다(JSON 이 딱 그렇다).
        // 그래서 여기도 자리표를 갈아 끼운다.
        var line = """
            {"cwd":"C:/사용자/바탕화면/한글폴더","timestamp":"@STAMP","message":{"id":"a","model":"claude-opus-5","usage":{"output_tokens":5}}}
            """.Replace("@STAMP", Jsonl.Stamp(Since.AddMinutes(1))) + "\n";
        var path = temp.Write("a.jsonl", line);

        var result = Scan(temp);

        Assert.Equal(1, result.Added.Responses);
        Assert.Equal(new FileInfo(path).Length, result.Offsets[path]);
        Assert.True(result.Offsets[path] > line.Length, "바이트가 글자 수보다 커야 한다");
    }

    [Fact]
    public void 모델별로_갈라_센다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl",
            Jsonl.Line("a1", Since.AddMinutes(1), model: "claude-opus-5", output: 10)
            + Jsonl.Line("a2", Since.AddMinutes(2), model: "claude-haiku-4-5-20251001", output: 20)
            + Jsonl.Line("a3", Since.AddMinutes(3), model: null, output: 30));

        var byModel = Scan(temp).AddedByModel;

        Assert.Equal(10, byModel["Opus 5"].Output);
        Assert.Equal(20, byModel["Haiku 4.5"].Output);
        Assert.Equal(30, byModel[ClaudeCodeUsage.UnknownModel].Output);
    }

    /// <summary>
    /// **받은 사전·집합을 갈아엎으면 안 된다.**
    ///
    /// 스위프트의 사전·집합은 값 타입이라 저절로 복사되지만 C# 은 참조다. 그대로 들고
    /// 쓰면 배경 스레드가 살아 있는 측정 상태를 직접 고치고, 표식 대조가 결과를 버려도
    /// 이미 늦는다.
    /// </summary>
    [Fact]
    public void 받은_사전과_집합을_안_건드린다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl", Jsonl.Line("a1", Since.AddMinutes(1), output: 1));

        var offsets = new Dictionary<string, long>();
        var seen = new HashSet<string>();

        var result = new TokenScan(Since, offsets, seen, temp.Root).Run();

        Assert.Empty(offsets);
        Assert.Empty(seen);
        Assert.NotEmpty(result.Offsets);
        Assert.Single(result.SeenIds);
    }

    /// <summary>순수 계산이라 같은 인스턴스를 두 번 불러도 답이 같다.</summary>
    [Fact]
    public void 두_번_불러도_같은_답이다()
    {
        using var temp = new TempTranscripts();
        temp.Write("a.jsonl",
            Jsonl.Line("a1", Since.AddMinutes(1), output: 1)
            + Jsonl.Line("a2", Since.AddMinutes(2), output: 2));

        var scan = new TokenScan(Since, null, null, temp.Root);

        var first = scan.Run();
        var second = scan.Run();

        Assert.Equal(first.Added, second.Added);
        Assert.Equal(first.Offsets, second.Offsets);
        Assert.Equal(first.SeenIds, second.SeenIds);
    }

    [Fact]
    public void 없는_폴더에서도_던지지_않는다()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dongcsu-none-{Guid.NewGuid():N}");

        var result = new TokenScan(Since, null, null, missing).Run();

        Assert.True(result.Added.IsEmpty);
        Assert.Empty(result.Offsets);
    }

    [Fact]
    public void 상한은_5만이다()
    {
        Assert.Equal(50_000, TokenScan.SeenLimit);
    }
}

public class TokenScanParseTests
{
    private static readonly DateTimeOffset Since = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>실제 기록에서 그대로 옮긴 모양.</summary>
    [Fact]
    public void 네_칸을_다_잡는다()
    {
        const string line = """
            {"parentUuid":"x","type":"assistant","timestamp":"2026-08-06T12:19:00.573Z","message":{"id":"msg_01","model":"claude-opus-5","usage":{"input_tokens":4,"output_tokens":1234,"cache_creation_input_tokens":5678,"cache_read_input_tokens":90123}}}
            """;

        var entry = TokenScan.ParseLine(line, Since);

        Assert.NotNull(entry);
        Assert.Equal("msg_01", entry.Value.Id);
        Assert.Equal("Opus 5", entry.Value.Model);
        Assert.Equal(new TokenTally(1, 4, 1234, 5678, 90123), entry.Value.Tally);
    }

    /// <summary>서버가 칸 하나를 안 주는 일이 흔하다. 그 칸만 0 이면 된다.</summary>
    [Fact]
    public void 빠진_칸은_0_이다()
    {
        const string line = """
            {"timestamp":"2026-08-06T12:19:00.573Z","message":{"id":"m","model":"claude-opus-5","usage":{"output_tokens":7}}}
            """;

        var tally = TokenScan.ParseLine(line, Since)!.Value.Tally;

        Assert.Equal(new TokenTally(1, 0, 7, 0, 0), tally);
    }

    /// <summary>소수점 초가 없는 모양도 읽어야 한다(맥이 포매터 둘을 두는 이유다).</summary>
    [Theory]
    [InlineData("2026-08-06T12:19:00Z")]
    [InlineData("2026-08-06T12:19:00.573Z")]
    [InlineData("2026-08-06T12:19:00.5730000Z")]
    [InlineData("2026-08-06T21:19:00+09:00")]
    public void 여러_시각_모양을_읽는다(string stamp)
    {
        var line = """
            {"timestamp":"@STAMP","message":{"id":"m","usage":{"output_tokens":1}}}
            """.Replace("@STAMP", stamp);

        Assert.NotNull(TokenScan.ParseLine(line, Since));
    }

    [Fact]
    public void 기준보다_이르면_버리고_같으면_통과한다()
    {
        const string template = """
            {"timestamp":"@STAMP","message":{"id":"m","usage":{}}}
            """;

        Assert.Null(TokenScan.ParseLine(
            template.Replace("@STAMP", Jsonl.Stamp(Since.AddSeconds(-1))), Since));
        Assert.NotNull(TokenScan.ParseLine(
            template.Replace("@STAMP", Jsonl.Stamp(Since)), Since));
    }

    /// <summary>구조가 안 맞으면 조용히 버린다. **던지지 않는다.**</summary>
    [Theory]
    // usage 가 없다
    [InlineData("""{"timestamp":"2026-08-06T12:19:00Z","message":{"id":"m"}}""")]
    // id 가 없다
    [InlineData("""{"timestamp":"2026-08-06T12:19:00Z","message":{"usage":{}}}""")]
    // id 가 빈 문자열이다
    [InlineData("""{"timestamp":"2026-08-06T12:19:00Z","message":{"id":"","usage":{}}}""")]
    // message 가 없다
    [InlineData("""{"timestamp":"2026-08-06T12:19:00Z","usage":{}}""")]
    // timestamp 가 없다
    [InlineData("""{"message":{"id":"m","usage":{}}}""")]
    // timestamp 가 문자열이 아니다
    [InlineData("""{"timestamp":12345,"message":{"id":"m","usage":{}}}""")]
    // timestamp 가 시각이 아니다
    [InlineData("""{"timestamp":"어제","message":{"id":"m","usage":{}}}""")]
    // 최상위가 객체가 아니다
    [InlineData("[1,2,3]")]
    [InlineData("\"글자\"")]
    // 아예 JSON 이 아니다
    [InlineData("이건 JSON 이 아니다")]
    [InlineData("{\"message\":{")]
    [InlineData("")]
    public void 형식이_아니면_null_이고_안_던진다(string line)
    {
        Assert.Null(TokenScan.ParseLine(line, Since));
    }

    /// <summary>모델을 못 읽으면 "(불명)" 으로 묶는다 — 그 응답을 버리지는 않는다.</summary>
    [Theory]
    [InlineData("""{"id":"m","usage":{"output_tokens":1}}""")]
    [InlineData("""{"id":"m","model":"","usage":{"output_tokens":1}}""")]
    [InlineData("""{"id":"m","model":123,"usage":{"output_tokens":1}}""")]
    public void 모델이_없으면_불명이다(string message)
    {
        var line = $$"""{"timestamp":"2026-08-06T12:19:00Z","message":{{message}}}""";

        var entry = TokenScan.ParseLine(line, Since);

        Assert.NotNull(entry);
        Assert.Equal(ClaudeCodeUsage.UnknownModel, entry.Value.Model);
    }

    /// <summary>숫자가 아닌 칸은 0 으로 눕힌다. 소수로 와도 받는다.</summary>
    [Fact]
    public void 숫자가_아닌_칸은_0_이다()
    {
        const string line = """
            {"timestamp":"2026-08-06T12:19:00Z","message":{"id":"m","usage":{"input_tokens":"많이","output_tokens":null,"cache_creation_input_tokens":12.0,"cache_read_input_tokens":3000000000}}}
            """;

        var tally = TokenScan.ParseLine(line, Since)!.Value.Tally;

        Assert.Equal(new TokenTally(1, 0, 0, 12, 3_000_000_000L), tally);
    }

    /// <summary>
    /// <c>usage.iterations</c> 가 같이 있어도 **최상위 값을 본다** — 맥이 그렇게 센다.
    /// </summary>
    [Fact]
    public void iterations_가_있어도_최상위를_본다()
    {
        const string line = """
            {"timestamp":"2026-08-06T12:19:00Z","message":{"id":"m","usage":{"output_tokens":100,"iterations":[{"output_tokens":1},{"output_tokens":2}]}}}
            """;

        Assert.Equal(100, TokenScan.ParseLine(line, Since)!.Value.Tally.Output);
    }

    /// <summary><c>type</c> 은 보지 않는다 — 서버가 새 타입을 쓰기 시작해도 안 놓친다.</summary>
    [Fact]
    public void type_으로_거르지_않는다()
    {
        const string line = """
            {"type":"낯선것","timestamp":"2026-08-06T12:19:00Z","message":{"id":"m","usage":{"output_tokens":1}}}
            """;

        Assert.NotNull(TokenScan.ParseLine(line, Since));
    }
}

/// <summary>
/// 얹는 셈. 진단 통로와 실제 동작이 **같은 코드**를 쓰게 떼어 놓은 자리다.
/// </summary>
public class TokenScanApplyTests
{
    private static TokenScanResult Result() => new()
    {
        Added = new TokenTally(2, 10, 20, 30, 40),
        AddedByModel = new Dictionary<string, TokenTally>(StringComparer.Ordinal)
        {
            ["Opus 5"] = new(1, 10, 20, 30, 40),
            ["Haiku 4.5"] = new(1, 0, 0, 0, 0),
        },
    };

    [Fact]
    public void 합계와_모델별을_함께_얹는다()
    {
        var (tokens, byModel) = TokenScanApply.Applying(Result(),
            new TokenTally(5, 1, 1, 1, 1),
            new Dictionary<string, TokenTally> { ["Opus 5"] = new(5, 1, 1, 1, 1) });

        Assert.Equal(new TokenTally(7, 11, 21, 31, 41), tokens);
        Assert.Equal(new TokenTally(6, 11, 21, 31, 41), byModel["Opus 5"]);
        Assert.Equal(new TokenTally(1, 0, 0, 0, 0), byModel["Haiku 4.5"]);
    }

    [Fact]
    public void 아무것도_없던_자리에도_얹힌다()
    {
        var (tokens, byModel) = TokenScanApply.Applying(Result(), default, null);

        Assert.Equal(new TokenTally(2, 10, 20, 30, 40), tokens);
        Assert.Equal(2, byModel.Count);
    }

    /// <summary>받은 사전을 고치지 않는다 — 부르는 쪽이 결과를 버려도 아무것도 안 남아야 한다.</summary>
    [Fact]
    public void 받은_사전을_안_건드린다()
    {
        var before = new Dictionary<string, TokenTally> { ["Opus 5"] = new(5, 1, 1, 1, 1) };

        TokenScanApply.Applying(Result(), default, before);

        Assert.Single(before);
        Assert.Equal(new TokenTally(5, 1, 1, 1, 1), before["Opus 5"]);
    }

    /// <summary>
    /// **같은 결과를 두 번 얹으면 값이 두 배가 된다.** 얹기 자체는 못 막는 일이고,
    /// 부르는 쪽이 표식(시작 시각 + 멈춰 있던 시간)을 대조해서 옛 결과를 버려야 한다는
    /// 사실을 여기에 못 박아 둔다 — 측정 쪽 검사가 이 위에 얹힌다.
    /// </summary>
    [Fact]
    public void 두_번_얹으면_두_배가_된다()
    {
        var result = Result();

        var (once, onceByModel) = TokenScanApply.Applying(result, default, null);
        var (twice, twiceByModel) = TokenScanApply.Applying(result, once, onceByModel);

        Assert.Equal(once.Responses * 2, twice.Responses);
        Assert.Equal(once.Total * 2, twice.Total);
        Assert.Equal(onceByModel["Opus 5"].Output * 2, twiceByModel["Opus 5"].Output);
    }
}
