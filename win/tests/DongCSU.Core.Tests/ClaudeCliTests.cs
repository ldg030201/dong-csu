using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

public class ClaudeCliTests
{
    private const string Home = @"C:\Users\사람";
    private const string AppData = @"C:\Users\사람\AppData\Roaming";

    [Fact]
    public void 공식_설치본_자리를_먼저_본다()
    {
        var found = ClaudeCli.Resolve(Home, AppData, _ => true);

        Assert.Equal(@"C:\Users\사람\.local\bin\claude.exe", found);
    }

    /// <summary>npm 으로 깐 사람은 <c>%APPDATA%\npm</c> 에 <c>.cmd</c> 로 들어 있다.</summary>
    [Fact]
    public void 공식_자리가_비면_npm_전역을_본다()
    {
        var found = ClaudeCli.Resolve(Home, AppData, path => path.Contains(@"npm\claude.cmd"));

        Assert.Equal(@"C:\Users\사람\AppData\Roaming\npm\claude.cmd", found);
    }

    [Fact]
    public void 하나도_없으면_null()
    {
        Assert.Null(ClaudeCli.Resolve(Home, AppData, _ => false));
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
