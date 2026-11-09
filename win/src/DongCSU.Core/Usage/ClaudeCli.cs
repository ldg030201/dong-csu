namespace DongCSU.Core.Usage;

/// <summary>
/// Claude Code CLI 를 찾아 로그인 창을 띄운다.
///
/// **평소에는 여기까지 오지 않는다.** 토큰이 만료되면 앱이 스스로 갱신한다
/// (<see cref="TokenRefresh"/>). 갱신용 토큰까지 죽었을 때만 이 길이 남는다.
///
/// 대화형 흐름이라 앱 안에서 처리할 수 없다. 콘솔 창에 넘긴다 — 맥이 `.command`
/// 스크립트를 터미널에 던지는 것과 같은 자리다.
/// </summary>
public static class ClaudeCli
{
    /// <summary>로그인 뒤 새 토큰이 파일에 적히기를 기다렸다 다시 조회하는 간격.</summary>
    public static readonly TimeSpan RetryAfterLogin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 실행 파일을 찾을 자리. **설치 방식마다 다르다** — 공식 설치본은 <c>.local\bin</c>,
    /// npm 전역은 <c>%APPDATA%\npm</c>, 옛 자리는 <c>.claude\local</c> 이다.
    ///
    /// 순서가 곧 우선순위다. <c>PATH</c> 는 마지막에 본다 — 거기 걸린 것이 WSL 로
    /// 넘기는 껍데기일 수 있어서, 진짜 실행 파일을 먼저 찾는 편이 안전하다.
    ///
    /// **공식 설치본은 버전 폴더 안에 있어서 못 박을 수가 없다**(<see cref="NativeRoot"/>).
    /// 그 자리는 훑어야 알 수 있어서 <paramref name="listDirectories"/> 를 받는다.
    /// </summary>
    /// <param name="listDirectories">
    /// 폴더 하나의 하위 폴더를 늘어놓는다. 그 자리만 훑어야 알 수 있어서 주입받는다 —
    /// 테스트는 <c>_ => []</c> 로 진짜 디스크 없이 돈다.
    ///
    /// <b>기본값을 두지 않는다.</b> 안 주면 네이티브 설치본을 통째로 못 보는데,
    /// 그게 바로 이 매개변수를 만든 이유였던 버그다 — 공식 설치본으로 깐 사람에게
    /// "실행 파일을 찾지 못했습니다" 만 뜨던 것. 잊어도 컴파일이 되면 언제든 되돌아온다.
    /// </param>
    /// <param name="localAppData">
    /// <c>%LOCALAPPDATA%</c>. Claude 데스크톱 앱이 Claude Code 를 여기 깐다
    /// (<see cref="DesktopRoot"/>). <b>기본값을 두지 않는다</b> — 위와 같은 이유다.
    /// </param>
    public static IEnumerable<string> Candidates(
        string home, string appData, string localAppData,
        Func<string, IEnumerable<string>> listDirectories)
    {
        yield return Path.Combine(home, ".local", "bin", "claude.exe");
        yield return Path.Combine(home, ".claude", "local", "claude.exe");
        yield return Path.Combine(home, ".claude", "local", "claude.cmd");
        yield return Path.Combine(appData, "npm", "claude.cmd");
        yield return Path.Combine(appData, "npm", "claude.exe");

        // **네이티브 설치본은 맨 뒤다.** 손으로 깐 것이 있으면 그쪽이 그 사람이 실제로
        // 쓰는 것이다.
        foreach (var path in VersionedCandidates(NativeRoot(appData), listDirectories))
        {
            yield return path;
        }

        // **데스크톱 앱이 깐 것은 그보다도 뒤다.** 앱이 저 쓰려고 둔 것이라 따로 깐
        // CLI 가 있으면 그쪽이 맞다. 데스크톱 앱만 쓰는 사람에게는 이것이 유일한 CLI 다.
        foreach (var path in DesktopCandidates(DesktopRoot(localAppData), listDirectories))
        {
            yield return path;
        }
    }

    /// <summary>
    /// Claude 데스크톱 앱이 Claude Code 를 깔아 두는 자리.
    /// <c>%LOCALAPPDATA%\Claude-Data\claude_&lt;프로필&gt;\claude-code\&lt;버전&gt;\&lt;해시&gt;\claude.exe</c>.
    ///
    /// **실측으로 확인한 자리다** — 데스크톱 앱 4.23118 에서 <c>2.1.309</c> · <c>2.1.317</c>
    /// 두 벌이 이렇게 깔려 있었고, 그 앱이 띄운 Claude Code 의 <c>CLAUDE_CODE_EXECPATH</c>
    /// 가 그중 새것을 가리켰다. <see cref="NativeRoot"/>(로밍 · 해시 폴더 없음)는 그 전
    /// 배치다 — 앱이 자리를 옮긴 뒤로 거기는 비어 있다.
    ///
    /// **이 자리를 안 보던 것이 실제로 문제였다.** 데스크톱 앱으로만 Claude Code 를 쓰면
    /// 앱이 로그인을 제 안에 들고 있어서 <c>~/.claude/.credentials.json</c> 의 로그인이
    /// 갱신되지 않고 낡는다. 그래서 재로그인이 꼭 필요한데, 정작 그 버튼이 "실행 파일을
    /// 찾지 못했습니다" 만 띄웠다.
    /// </summary>
    public static string DesktopRoot(string localAppData) =>
        Path.Combine(localAppData, "Claude-Data");

    /// <summary>
    /// 데스크톱 앱 자리 안의 실행 파일들. <b>모든 프로필을 통틀어 높은 버전부터</b> 낸다.
    ///
    /// 버전 폴더 안에 해시 폴더가 한 겹 더 있다(같은 버전이 다시 깔리면 해시가 갈린다).
    /// 해시끼리는 어느 쪽이 새것인지 이름으로 알 수 없어서 늘어놓인 차례대로 둔다 —
    /// 부르는 쪽이 있는 것 중 첫째를 고르므로 어느 쪽이든 실제로 있는 것이 잡힌다.
    ///
    /// <see cref="VersionedCandidates"/> 와 같이 <b>못 읽으면 조용히 빈 목록이다.</b>
    /// 데스크톱 앱을 안 쓰는 사람에게는 이 자리가 늘 없다.
    /// </summary>
    public static IEnumerable<string> DesktopCandidates(
        string root, Func<string, IEnumerable<string>> listDirectories)
    {
        var versions = new List<string>();
        foreach (var profile in SafeList(root, listDirectories))
        {
            versions.AddRange(SafeList(Path.Combine(profile, "claude-code"), listDirectories));
        }

        foreach (var version in versions.OrderByDescending(NumericKey, StringComparer.Ordinal))
        {
            foreach (var build in SafeList(version, listDirectories))
            {
                yield return Path.Combine(build, "claude.exe");
            }
        }
    }

    /// <summary>하위 폴더. 없거나 못 읽으면 빈 목록 — 후보를 늘어놓다 던지면 재로그인이 죽는다.</summary>
    private static IReadOnlyList<string> SafeList(
        string directory, Func<string, IEnumerable<string>> listDirectories)
    {
        try
        {
            return [.. listDirectories(directory)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// 네이티브 설치본이 사는 자리. <c>%APPDATA%\Claude\claude-code\&lt;버전&gt;\claude.exe</c>.
    ///
    /// **실측으로 확인한 자리다** — 이 기계에 <c>2.1.247</c> · <c>2.1.258</c> 두 벌이
    /// 그렇게 깔려 있었다. 맥의
    /// <c>~/Library/Application Support/Claude/claude-code/&lt;버전&gt;</c> 과 같은 짝이다.
    ///
    /// **이 자리를 안 보던 것이 실제로 문제였다.** 공식 설치본으로 깐 사람은 위의 다섯
    /// 후보가 하나도 없어서, 재로그인을 눌러도 "실행 파일을 찾지 못했습니다" 만 떴다.
    /// </summary>
    public static string NativeRoot(string appData) =>
        Path.Combine(appData, "Claude", "claude-code");

    /// <summary>
    /// 버전 폴더 안의 실행 파일들. **높은 버전을 먼저 낸다** — 낮은 것을 먼저 잡으면
    /// 옛 CLI 로 로그인한다.
    ///
    /// 폴더가 없거나 못 읽어도 **조용히 빈 목록이다.** 여기서 던지면 재로그인이
    /// 통째로 죽는데, 그건 네이티브 설치본을 안 쓰는 사람에게 늘 일어난다.
    /// </summary>
    public static IEnumerable<string> VersionedCandidates(
        string root, Func<string, IEnumerable<string>> listDirectories)
    {
        IReadOnlyList<string> versions;
        try
        {
            versions = [.. listDirectories(root)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var directory in versions.OrderByDescending(NumericKey, StringComparer.Ordinal))
        {
            yield return Path.Combine(directory, "claude.exe");
        }
    }

    /// <summary>
    /// <c>2.10.0</c> 이 <c>2.9.0</c> 보다 크게 잡히도록 자리마다 0을 채워 견준다.
    /// 맥의 <c>compare(options: .numeric)</c> 과 같은 자리다.
    /// </summary>
    private static string NumericKey(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Concat(name.Split('.').Select(part =>
            int.TryParse(part, out var number) ? number.ToString("D6") : part));
    }

    /// <summary>있는 것 중 첫 번째. 하나도 없으면 null.</summary>
    /// <param name="exists">파일이 있는지. 테스트가 진짜 디스크 없이 돈다.</param>
    /// <param name="listDirectories"><inheritdoc cref="Candidates" path="/param[@name='listDirectories']/node()"/></param>
    public static string? Resolve(
        string home, string appData, string localAppData, Func<string, bool> exists,
        Func<string, IEnumerable<string>> listDirectories) =>
        Candidates(home, appData, localAppData, listDirectories).FirstOrDefault(exists);

    /// <summary>
    /// 자격 증명을 <b>WSL 안에서</b> 찾았는지.
    ///
    /// **거기서 로그인해야 그 파일이 갱신된다.** 윈도우 쪽 `claude` 로 로그인하면
    /// 윈도우 홈에 새 파일이 생길 뿐, 우리가 읽고 있던 리눅스 홈은 그대로 낡아 있다.
    /// </summary>
    public static bool IsInsideWsl(string? credentialPath) =>
        credentialPath is { } path
        && (path.StartsWith(@"\\wsl$", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\\wsl.localhost", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 콘솔 창에 넘길 명령. 부르는 쪽이 그대로 띄운다.
    ///
    /// <c>/k</c> 라 로그인이 끝나도 창이 남는다 — 실패했을 때 무엇이 잘못됐는지
    /// 읽을 자리가 있어야 한다.
    /// </summary>
    public static (string File, string Arguments)? LoginCommand(string? executable, bool insideWsl)
    {
        // WSL 안에서 쓰던 사람은 거기서 로그인한다. 실행 파일도 그쪽 것이라 우리가
        // 찾은 윈도우 경로는 쓸모가 없다.
        if (insideWsl) return ("cmd.exe", "/k wsl claude auth login");

        return executable is null ? null : ("cmd.exe", $"/k \"\"{executable}\" auth login\"");
    }
}
