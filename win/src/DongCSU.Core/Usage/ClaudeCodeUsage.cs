namespace DongCSU.Core.Usage;

/// <summary>기록 파일 하나. 크기를 함께 든다.</summary>
/// <param name="Path">전체 경로. 오프셋 사전의 열쇠다.</param>
/// <param name="Length">
/// 지금 크기(바이트). **훑을 때 이미 받아 둔 값이라 공짜다** — 이걸 들고 다녀야
/// 훑기가 파일마다 크기를 다시 묻지 않는다.
/// </param>
public readonly record struct TranscriptFile(string Path, long Length);

/// <summary>
/// Claude Code 가 남긴 기록에서 실제로 쓴 토큰을 센다.
///
/// **여기서 세는 것은 Claude Code 것뿐이다.** 클로드 앱·웹은 이 기계에 아무것도
/// 남기지 않는다. 계정 전체를 보려면 한도 %(사용량 API)를 봐야 한다 — 그쪽은
/// 어디서 쓰든 같은 창을 깎는다. 측정 화면이 두 숫자를 나란히 두는 이유가 이거다.
/// </summary>
public static class ClaudeCodeUsage
{
    /// <summary>모델 이름을 못 읽었을 때 쓰는 이름.</summary>
    public const string UnknownModel = "(불명)";

    private const string ConfigDirVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>
    /// <c>%USERPROFILE%\.claude\projects</c>. <c>CLAUDE_CONFIG_DIR</c> 을 쓰면 그쪽을
    /// 본다(자격 증명 쪽과 같은 규칙).
    ///
    /// **후보는 이 둘뿐이고 WSL 은 보지 않는다.** 자격 증명은 못 찾았을 때 한 번,
    /// 그것도 한 시간 캐시를 두고 <c>\\wsl.localhost\…</c> 를 들여다보지만, 기록은
    /// 측정이 도는 동안 **60초마다 파일 백여 개를 훑는다.** 그 주기로 건드리면 꺼져
    /// 있던 배포판이 계속 깨어 있게 되고, 9P 공유 너머의 조회는 로컬보다 수십 배
    /// 느려서 훑기 한 번이 초 단위로 늘어난다. 그래서 WSL 안에서만 Claude Code 를 쓰는
    /// 사람은 토큰이 안 잡히는데, 그때 화면이 0 을 보여주면 안 된다 —
    /// <see cref="IsAvailable"/> 로 "찾지 못했다"를 따로 말한다.
    ///
    /// **환경변수가 있으면 있든 없든 무조건 그쪽이다**(맥과 글자 그대로 같다).
    /// "있는 첫 후보를 고른다" 같은 개선은 넣지 않는다 — 맥과 갈리는 자리를 늘리면
    /// 같은 기계에서 두 판이 다른 값을 세게 된다.
    ///
    /// 매번 계산한다. 환경변수는 바뀔 수 있고 문자열 몇 개 잇는 값은 싸다.
    /// </summary>
    public static string ProjectsDirectory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configured = Environment.GetEnvironmentVariable(ConfigDirVariable)?.Trim();
            if (string.IsNullOrEmpty(configured))
            {
                return Path.Combine(home, ".claude", "projects");
            }

            // 윈도우에는 `%USERPROFILE%\.claude` 처럼 환경변수를 적어 두는 사람이 실제로
            // 있다. 맥의 틸드 전개 자리라 `~` 도 함께 편다.
            var expanded = Environment.ExpandEnvironmentVariables(configured);
            var root = expanded switch
            {
                "~" => home,
                _ when expanded.StartsWith("~/", StringComparison.Ordinal)
                    || expanded.StartsWith("~\\", StringComparison.Ordinal)
                    => Path.Combine(home, expanded[2..]),
                _ => expanded,
            };
            return Path.Combine(root, "projects");
        }
    }

    /// <summary>기록 폴더가 실제로 있는지. <see cref="Directory.Exists(string)"/> 는 던지지 않는다.</summary>
    public static bool IsAvailable => Directory.Exists(ProjectsDirectory);

    /// <summary>
    /// 경로를 열쇠로 쓰는 사전의 비교자.
    ///
    /// **윈도우 파일 시스템은 대소문자를 안 가린다.** 안 주면 <c>C:\Users</c> 와
    /// <c>C:\users</c> 가 다른 항목이 되어 한쪽 오프셋이 0 인 채로 남고, 그 파일을
    /// 통째로 다시 읽어 토큰이 두 배가 된다.
    /// </summary>
    public static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// 직렬화해서 돌아온 오프셋 사전을 다시 싼다.
    ///
    /// **System.Text.Json 이 읽은 사전의 비교자는 기본값(Ordinal)이다.** 그대로 쓰면
    /// 위와 같은 이유로 같은 파일이 두 항목으로 갈린다 — <c>meter.json</c> 을 읽은
    /// 직후에 반드시 한 번 태운다.
    /// </summary>
    public static Dictionary<string, long> WithPathComparer(IReadOnlyDictionary<string, long>? source)
        => source is null
            ? new Dictionary<string, long>(PathComparer)
            : new Dictionary<string, long>(source, PathComparer);

    /// <summary>
    /// 기록 파일 전부. 프로젝트마다 폴더가 갈려 있고 그 아래로 더 깊이 들어가서
    /// (<c>&lt;프로젝트&gt;/&lt;세션&gt;/subagents/…</c>) 훑어 내려간다.
    /// </summary>
    /// <param name="root">안 주면 <see cref="ProjectsDirectory"/>. 검사가 임시 폴더를 꽂는 자리다.</param>
    public static IReadOnlyList<TranscriptFile> Transcripts(string? root = null)
    {
        var files = new List<TranscriptFile>();

        // **열거는 지연 실행이라 `foreach` 도중에 던진다.** 폴더가 통째로 사라졌다고
        // 측정이 죽으면 안 되므로 순회 전체를 감싸고, 잡히면 그때까지 모은 것을 준다.
        // (폴더를 여는 것도 안에 둔다 — `CLAUDE_CONFIG_DIR` 에 이상한 글자가 들어 있으면
        // 그 자리에서 던진다.)
        try
        {
            var directory = new DirectoryInfo(root ?? ProjectsDirectory);
            foreach (var file in directory.EnumerateFiles("*.jsonl", Walk))
            {
                // `MatchType.Win32` 은 8.3 짧은 이름 때문에 엉뚱한 확장자까지 무는 일이
                // 있어서 `Simple` 로 두고 여기서 한 번 더 본다.
                if (!Path.GetExtension(file.Name).Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // `FileInfo.Length` 는 열거가 이미 받아 둔 값이다. `Refresh()` 를 부르면
                // 그때부터 파일마다 다시 물으므로 부르지 않는다.
                files.Add(new TranscriptFile(file.FullName, file.Length));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            return files;
        }

        return files;
    }

    /// <summary>
    /// 지금 파일 끝.
    ///
    /// **측정을 시작할 때 이걸로 기준을 잡는다.** 0에서 읽기 시작하면 며칠 치 옛 기록을
    /// 전부 훑어야 하고, 시각으로 걸러도 수십 MB를 읽는 값이 그대로 나간다.
    ///
    /// 부르는 자리는 둘뿐이다 — 측정을 시작할 때와 일시정지에서 돌아올 때(세워 둔 동안
    /// 쓴 것을 이번 측정에 안 넣으려는 것이다). 중지는 오프셋을 안 건드린다.
    /// </summary>
    public static Dictionary<string, long> EndOffsets(string? root = null)
    {
        var offsets = new Dictionary<string, long>(PathComparer);
        foreach (var file in Transcripts(root))
        {
            offsets[file.Path] = Math.Max(0, file.Length);
        }
        return offsets;
    }

    /// <summary><c>claude-opus-5</c> → <c>Opus 5</c>. 화면에 그대로 쓰기엔 길고 안 예쁘다.</summary>
    public static string DisplayName(string raw)
    {
        var name = raw;
        if (name.StartsWith("claude-", StringComparison.Ordinal))
        {
            name = name["claude-".Length..];
        }

        // 끝에 붙는 날짜(`-20251001`)는 사람에게 의미가 없다.
        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[^1].Length == 8 && parts[^1].All(char.IsAsciiDigit))
        {
            parts = parts[..^1];
        }
        if (parts.Length == 0) return raw;

        // 숫자가 이어지면 판 번호다. `haiku 4 5` 보다 `Haiku 4.5` 가 읽힌다.
        var words = new List<string>();
        foreach (var part in parts)
        {
            // `char.IsDigit` 은 아랍·전각 숫자까지 참이다. 모델 이름은 ASCII 뿐이라
            // 기계·문화권에 안 흔들리게 `IsAsciiDigit` 으로 못 박는다.
            if (part.All(char.IsAsciiDigit) && words.Count > 0
                && words[^1].All(c => char.IsAsciiDigit(c) || c == '.'))
            {
                words[^1] = words[^1] + "." + part;
            }
            else
            {
                // `ToUpper()` 는 현재 문화권을 타서 터키어 로캘이면 `i` 가 `İ` 가 된다.
                words.Add(char.ToUpperInvariant(part[0]) + part[1..]);
            }
        }
        return string.Join(" ", words);
    }

    /// <summary>
    /// 훑는 방법.
    ///
    /// <c>Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)</c> 를
    /// 쓰면 안 된다 — 그 오버로드는 <c>IgnoreInaccessible = false</c> 라 **접근 거부
    /// 폴더 하나가 훑기 전체를 날린다.** 여기서 직접 못 박는다.
    ///
    /// <c>ReparsePoint</c> 를 더한 것은 맥의 <c>skipsPackageDescendants</c> 자리에 놓는
    /// 방어다. .NET 은 디렉터리 심볼릭 링크·정션을 기본으로 따라가서, 순환이 하나라도
    /// 있으면 훑기가 안 끝난다.
    /// </summary>
    private static EnumerationOptions Walk => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        MatchType = MatchType.Simple,
        MatchCasing = MatchCasing.CaseInsensitive,
        ReturnSpecialDirectories = false,
    };
}
