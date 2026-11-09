using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

public class ClaudeCliTests
{
    private const string Home = @"C:\Users\사람";
    private const string AppData = @"C:\Users\사람\AppData\Roaming";
    private const string LocalAppData = @"C:\Users\사람\AppData\Local";

    [Fact]
    public void 공식_설치본_자리를_먼저_본다()
    {
        var found = ClaudeCli.Resolve(Home, AppData, LocalAppData, _ => true, _ => []);

        Assert.Equal(@"C:\Users\사람\.local\bin\claude.exe", found);
    }

    /// <summary>npm 으로 깐 사람은 <c>%APPDATA%\npm</c> 에 <c>.cmd</c> 로 들어 있다.</summary>
    [Fact]
    public void 공식_자리가_비면_npm_전역을_본다()
    {
        var found = ClaudeCli.Resolve(Home, AppData, LocalAppData, path => path.Contains(@"npm\claude.cmd"), _ => []);

        Assert.Equal(@"C:\Users\사람\AppData\Roaming\npm\claude.cmd", found);
    }

    [Fact]
    public void 하나도_없으면_null()
    {
        Assert.Null(ClaudeCli.Resolve(Home, AppData, LocalAppData, _ => false, _ => []));
    }

    /// <summary>
    /// 공식 설치본은 <c>%APPDATA%\Claude\claude-code\&lt;버전&gt;\claude.exe</c> 에 있다.
    ///
    /// **이 자리를 안 보던 것이 실제 문제였다** — 그렇게 깐 기계에서는 후보 다섯이
    /// 모두 없어서 재로그인을 눌러도 "실행 파일을 찾지 못했습니다" 만 떴다.
    /// </summary>
    [Fact]
    public void 공식_설치본은_높은_버전을_먼저_본다()
    {
        var root = Path.Combine(AppData, "Claude", "claude-code");
        var found = ClaudeCli.Resolve(
            Home, AppData, LocalAppData,
            exists: path => path.StartsWith(root, StringComparison.Ordinal),
            listDirectories: _ =>
            [
                Path.Combine(root, "2.9.0"),
                Path.Combine(root, "2.10.0"),
                Path.Combine(root, "2.1.258"),
            ]);

        // **글자로 견주면 2.9.0 이 이긴다.** 자리마다 숫자로 봐야 2.10.0 이 위다.
        Assert.Equal(Path.Combine(root, "2.10.0", "claude.exe"), found);
    }

    /// <summary>
    /// **손으로 깐 것이 이긴다.** 공식 설치본은 맨 뒤라, 둘 다 있으면 앞의 것을 쓴다 —
    /// 손으로 깐 사람에게는 그쪽이 실제로 쓰는 것이다.
    /// </summary>
    [Fact]
    public void 손으로_깐_것이_공식_설치본보다_앞이다()
    {
        var found = ClaudeCli.Resolve(
            Home, AppData, LocalAppData, _ => true,
            listDirectories: _ => [Path.Combine(AppData, "Claude", "claude-code", "9.9.9")]);

        Assert.Equal(@"C:\Users\사람\.local\bin\claude.exe", found);
    }

    /// <summary>
    /// 버전 폴더가 없으면 훑다 던진다. **그걸로 재로그인이 통째로 죽으면 안 된다** —
    /// 공식 설치본을 안 쓰는 사람에게는 늘 일어나는 일이다.
    /// </summary>
    [Fact]
    public void 버전_폴더가_없어도_안_터진다()
    {
        var found = ClaudeCli.Resolve(
            Home, AppData, LocalAppData,
            exists: path => path.Contains(@"npm\claude.cmd"),
            listDirectories: _ => throw new DirectoryNotFoundException());

        Assert.Equal(@"C:\Users\사람\AppData\Roaming\npm\claude.cmd", found);
    }

    /// <summary>
    /// 훑는 함수를 안 주면 옛 목록 그대로다. **부르는 자리를 안 깨뜨린다는 뜻이다.**
    /// </summary>
    [Fact]
    public void 훑는_함수가_없으면_후보가_다섯이다()
    {
        Assert.Equal(5, ClaudeCli.Candidates(Home, AppData, LocalAppData, _ => []).Count());
    }

    /// <summary>
    /// 데스크톱 앱이 깐 폴더를 흉내 낸다. 실제 배치 그대로다 —
    /// <c>Claude-Data\claude_&lt;프로필&gt;\claude-code\&lt;버전&gt;\&lt;해시&gt;\claude.exe</c>.
    /// </summary>
    private static Func<string, IEnumerable<string>> DesktopTree(params string[] builds)
    {
        // builds: "claude_abc/2.1.317/a33aa5518497"
        var root = ClaudeCli.DesktopRoot(LocalAppData);
        var tree = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string parent, string child)
        {
            if (!tree.TryGetValue(parent, out var children)) tree[parent] = children = [];
            if (!children.Contains(child, StringComparer.OrdinalIgnoreCase)) children.Add(child);
        }

        foreach (var build in builds)
        {
            var parts = build.Split('/');
            var profile = Path.Combine(root, parts[0]);
            var versions = Path.Combine(profile, "claude-code");
            var version = Path.Combine(versions, parts[1]);
            Add(root, profile);
            Add(profile, versions);
            Add(versions, version);
            Add(version, Path.Combine(version, parts[2]));
        }

        return directory => tree.TryGetValue(directory, out var children)
            ? children
            : throw new DirectoryNotFoundException(directory);
    }

    /// <summary>
    /// **데스크톱 앱으로만 쓰는 사람은 이것이 유일한 CLI 다.** 그 앱은 로그인을 제 안에 들고
    /// 있어서 <c>.credentials.json</c> 의 로그인이 낡는데, 재로그인이 이 자리를 몰라서
    /// "실행 파일을 찾지 못했습니다" 만 떴다 — 실제로 그렇게 걸렸다.
    /// </summary>
    [Fact]
    public void 데스크톱_앱이_깐_것을_찾는다()
    {
        var expected = Path.Combine(
            ClaudeCli.DesktopRoot(LocalAppData), "claude_pzs8", "claude-code", "2.1.317", "a33aa551", "claude.exe");

        var found = ClaudeCli.Resolve(
            Home, AppData, LocalAppData,
            exists: path => path == expected,
            listDirectories: DesktopTree("claude_pzs8/2.1.317/a33aa551"));

        Assert.Equal(expected, found);
    }

    /// <summary>
    /// 앱이 업데이트되면 옛 판이 한동안 같이 남는다. **높은 버전을 고른다** — 프로필이
    /// 여럿이어도 통틀어 견준다. 자리마다 숫자로 봐야 2.1.317 이 2.1.309 보다 위다.
    /// </summary>
    [Fact]
    public void 데스크톱_앱_것도_높은_버전부터_본다()
    {
        var candidates = ClaudeCli.DesktopCandidates(
            ClaudeCli.DesktopRoot(LocalAppData),
            DesktopTree("claude_a/2.1.309/old", "claude_b/2.1.317/new", "claude_a/2.1.99/older")).ToList();

        Assert.Equal(3, candidates.Count);
        Assert.Contains(Path.Combine("2.1.317", "new"), candidates[0]);
        Assert.Contains(Path.Combine("2.1.309", "old"), candidates[1]);
        Assert.Contains(Path.Combine("2.1.99", "older"), candidates[2]);
    }

    /// <summary>
    /// **따로 깐 CLI 가 있으면 그쪽이 먼저다.** 데스크톱 앱 것은 앱이 저 쓰려고 둔 것이라
    /// 맨 뒤에 둔다.
    /// </summary>
    [Fact]
    public void 따로_깐_CLI_가_데스크톱_앱_것보다_앞이다()
    {
        var found = ClaudeCli.Resolve(
            Home, AppData, LocalAppData,
            exists: _ => true,
            listDirectories: DesktopTree("claude_pzs8/2.1.317/a33aa551"));

        Assert.Equal(@"C:\Users\사람\.local\bin\claude.exe", found);
    }

    /// <summary>데스크톱 앱을 안 쓰는 사람에게는 그 자리가 아예 없다. 그걸로 터지면 안 된다.</summary>
    [Fact]
    public void 데스크톱_앱_자리가_없어도_안_터진다()
    {
        Assert.Empty(ClaudeCli.DesktopCandidates(
            ClaudeCli.DesktopRoot(LocalAppData), _ => throw new DirectoryNotFoundException()));
        Assert.Empty(ClaudeCli.DesktopCandidates(
            ClaudeCli.DesktopRoot(LocalAppData), _ => throw new UnauthorizedAccessException()));
    }

    [Fact]
    public void 못_찾았으면_띄울_명령도_없다()
    {
        Assert.Null(ClaudeCli.LoginCommand(null, insideWsl: false));
    }

    [Fact]
    public void 찾은_실행_파일을_따옴표로_감싼다()
    {
        var command = ClaudeCli.LoginCommand(@"C:\Program Files\claude.exe", insideWsl: false);

        Assert.NotNull(command);
        Assert.Equal("cmd.exe", command.Value.File);
        Assert.Contains(@"""C:\Program Files\claude.exe"" auth login", command.Value.Arguments);
    }

    /// <summary>
    /// **WSL 안에서 쓰던 사람은 거기서 로그인해야 한다.** 윈도우 쪽 claude 로 로그인하면
    /// 윈도우 홈에 새 파일이 생길 뿐, 우리가 읽던 리눅스 홈은 낡은 채로 남는다.
    /// </summary>
    [Theory]
    [InlineData(@"\\wsl$\Ubuntu\home\사람\.claude\.credentials.json", true)]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\사람\.claude\.credentials.json", true)]
    [InlineData(@"C:\Users\사람\.claude\.credentials.json", false)]
    [InlineData(null, false)]
    public void WSL_에서_읽었는지_경로로_가른다(string? path, bool expected)
    {
        Assert.Equal(expected, ClaudeCli.IsInsideWsl(path));
    }

    [Fact]
    public void WSL_이면_실행_파일을_못_찾았어도_wsl_로_넘긴다()
    {
        var command = ClaudeCli.LoginCommand(null, insideWsl: true);

        Assert.NotNull(command);
        Assert.Contains("wsl claude auth login", command.Value.Arguments);
    }
}
