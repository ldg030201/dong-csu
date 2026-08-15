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
    /// </summary>
    public static IEnumerable<string> Candidates(string home, string appData)
    {
        yield return Path.Combine(home, ".local", "bin", "claude.exe");
        yield return Path.Combine(home, ".claude", "local", "claude.exe");
        yield return Path.Combine(home, ".claude", "local", "claude.cmd");
        yield return Path.Combine(appData, "npm", "claude.cmd");
        yield return Path.Combine(appData, "npm", "claude.exe");
    }

    /// <summary>있는 것 중 첫 번째. 하나도 없으면 null.</summary>
    /// <param name="exists">파일이 있는지. 테스트가 진짜 디스크 없이 돈다.</param>
    public static string? Resolve(string home, string appData, Func<string, bool> exists) =>
        Candidates(home, appData).FirstOrDefault(exists);

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
