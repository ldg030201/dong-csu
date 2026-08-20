using System.IO;
using DongCSU.App.Services;
using DongCSU.Core;
using DongCSU.Core.Owl;
using DongCSU.Core.Usage;

namespace DongCSU.App;

/// <summary>
/// 창을 띄우지 않고 확인만 하는 통로.
///
/// 맥판의 <c>--render</c> · <c>--dump-changelog</c> 와 같은 자리다. **CI 가 이걸 부른다** —
/// 화면을 볼 수 없는 곳에서 앱이 멀쩡한지 알아내는 유일한 방법이다.
/// </summary>
public static partial class Diagnostics
{
    /// <summary>진단 인자를 처리했으면 true. 그러면 창을 띄우지 않고 끝낸다.</summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0) return false;

        // **Velopack 인자는 콘솔에 붙기 전에 흘려보낸다.** 설치·업데이트 훅은 조용히
        // 돌아야 하는데, `AttachToConsole()` 이 먼저 불리면 붙을 부모 콘솔이 없어
        // `AllocConsole()` 이 검은 창을 하나 띄운다 — 조용한 설치 중에 창이 번쩍인다.
        //
        // **`args[0]` 만 보지 않는다.** Velopack 이 인자 순서를 바꿔도 설치가 안 깨져야 한다.
        if (args.Any(IsVelopackArgument)) return false;

        AttachToConsole();

        switch (args[0])
        {
            // 화면을 띄우지 않고 PNG 로 뽑는다. 배치를 눈으로 볼 유일한 싼 방법이다.
            case "--render":
                exitCode = Rendering.RenderProbe.Hud(args);
                return true;

            case "--render-settings":
                exitCode = Rendering.RenderProbe.SettingsTab(args);
                return true;

            case "--render-owl":
                exitCode = Rendering.RenderProbe.Owl(args);
                return true;

            // 트레이 아이콘을 기분마다 한 줄, 프레임마다 한 칸으로 늘어놓는다.
            // 한 칸이 1px까지 작아지는 자리라 눈으로 확인할 통로가 필요하다.
            case "--render-menubar":
                exitCode = Rendering.RenderProbe.Menubar(args);
                return true;

            case "--version":
                // 정식판의 출력 형식은 건드리지 않는다 — CI 와 문서가 이 줄을 본다.
                // 테스트판만 꼬리표를 단다.
                Console.WriteLine($"dong-csu {AppInfo.Version}{(AppInfo.IsTestBuild ? " (test)" : "")}");
                return true;

            case "--where":
                // 두 판을 같이 띄워 놓고 "지금 이건 어느 쪽 설정을 보나"를 확인하는 자리.
                Console.WriteLine($"name:     {AppInfo.Name}");
                Console.WriteLine($"test:     {AppInfo.IsTestBuild}");
                Console.WriteLine($"data:     {AppPaths.Root}");
                Console.WriteLine($"settings: {AppSettings.DefaultPath}");
                Console.WriteLine($"log:      {AppLog.DefaultPath}");
                Console.WriteLine($"token:    {RefreshedTokenStore.DefaultPath}");

                Console.WriteLine("credential candidates:");
                foreach (var path in FileCredentialSource.DefaultPaths()) Console.WriteLine($"  {path}");

                // 윈도우 쪽에서 못 찾았을 때만 실제로 들여다보는 자리. 여기 찍는 것만으로도
                // 배포판이 깨어날 수 있어서 진단 통로에서만 훑는다.
                Console.WriteLine("  (wsl fallback)");
                var wsl = WslCredentialPaths.All().ToList();
                if (wsl.Count == 0) Console.WriteLine("    없음 (WSL 이 없거나 닿지 않음)");
                foreach (var path in wsl) Console.WriteLine($"    {path}");
                return true;

            // 뽑아내는 일은 **실행 중인 앱의 상태가 필요 없어서** tools/DongCSU.Tools 가 한다
            // (CI 도 그쪽을 부른다). 여기 두면 같은 코드가 두 벌이 되고 조용히 갈린다.
            //
            // 그래도 **조용히 앱을 띄우지는 않는다.** 옛 습관으로 쳤을 때 창이 떠 버리면
            // 무슨 일이 난 건지 알 수 없다.
            case "--dump-changelog":
            case "--dump-owl":
                Console.Error.WriteLine($"{args[0]} 은 여기서 빠졌다. 도구 프로젝트에서 뽑는다:");
                Console.Error.WriteLine(
                    $"  dotnet run --project tools/DongCSU.Tools -- {args[0]} <파일>");
                exitCode = 2;
                return true;

            case "--probe":
                exitCode = Probe().GetAwaiter().GetResult();
                return true;

            case "--probe-owl":
                PrintOwl(args.ElementAtOrDefault(1) ?? "idle");
                return true;

            // 설정 창을 화면 밖에서 만들어 탭마다 얼마나 차지하는지 잰다. 가로로
            // 잘리는 탭이 있으면 0 이 아닌 값으로 끝난다 — CI 가 이걸 본다.
            case "--probe-layout":
                exitCode = Settings.ProbeLayout.Run(args);
                return true;

            case "--log":
                // 로그 파일을 그대로 찍는다. 사용자가 파일을 찾아 헤매지 않게.
                Console.WriteLine(AppLog.DefaultPath);
                Console.WriteLine(File.Exists(AppLog.DefaultPath)
                    ? File.ReadAllText(AppLog.DefaultPath)
                    : "(아직 기록이 없다)");
                return true;

            case "--help":
            case "-h":
            case "-?":
                PrintUsage();
                return true;

            default:
                // **모르는 플래그에 창을 띄우지 않는다.** 여기서 false 를 돌려주면
                // `Program.Main` 이 그대로 내려가 창이 하나 더 뜨고, 무슨 일이 난 건지
                // 알 수 없다. CI 는 프로세스가 안 끝나 매달린다.
                //
                // **`--` 로 시작하지 않는 인자는 지금처럼 흘려보낸다** — 나중에 파일
                // 연결이나 프로토콜 실행으로 들어올 수 있는데, 그것까지 막으면 앱이
                // 조용히 안 뜬다.
                if (args[0].StartsWith("--", StringComparison.Ordinal))
                {
                    PrintUsage();
                    exitCode = 2;
                    return true;
                }
                return false;
        }
    }

    /// <summary>
    /// Velopack 이 설치·업데이트 때 넘기는 인자인지.
    ///
    /// **여기를 좁게 잡으면 설치와 자체 업데이트가 통째로 깨진다.** 훅이 exit 2 로 죽으면
    /// 바로가기가 안 만들어지고, 업데이트 훅이 죽으면 갈아 끼운 뒤 앱이 안 뜬다.
    ///
    /// 앱이 실제로 받는 훅은 <c>--veloapp-install</c> · <c>--veloapp-updated</c> ·
    /// <c>--veloapp-obsolete</c> · <c>--veloapp-uninstall</c> 과 옛 이름 <c>--squirrel-*</c>
    /// 넷뿐이고(각각 뒤에 버전 문자열이 하나 붙는다), 첫 실행·재시작 신호는 인자가 아니라
    /// 환경변수 <c>VELOPACK_FIRSTRUN</c> · <c>VELOPACK_RESTART</c> 로 온다. 그래서 이
    /// 두 접두어만 열어 주면 설치 경로가 온전하다 — 이름을 하나씩 적으면 Velopack 이
    /// 훅을 하나 늘렸을 때 조용히 막힌다.
    /// </summary>
    private static bool IsVelopackArgument(string arg) =>
        arg.StartsWith("--veloapp-", StringComparison.OrdinalIgnoreCase)
        || arg.StartsWith("--squirrel-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 통로 목록을 찍는다. 모르는 <c>--</c> 인자와 <c>--help</c> 가 여기로 온다.
    ///
    /// **새 통로를 더하면 이 목록에도 한 줄 더한다.** 여기 없는 통로는 있어도 아무도
    /// 못 찾는다 — 창이 없는 앱이라 메뉴로도 안 보인다.
    ///
    /// `Console.Error` 가 아니라 `Console.WriteLine` 으로 찍는다. 사용법은 오류 내용이
    /// 아니라 **물어본 것에 대한 답**이라, 파이프로 넘길 때 같이 따라와야 한다.
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine($"사용법: {AppInfo.Name}.exe <통로>");
        Console.WriteLine();
        Console.WriteLine("  --version");
        Console.WriteLine("  --where");
        Console.WriteLine("  --probe");
        Console.WriteLine("  --probe-owl [기분]");
        Console.WriteLine("  --probe-layout");
        Console.WriteLine("  --log");
        Console.WriteLine("  --render <out.png> [세션%] [주간%] [보기] [아이콘] [배율] [테마]");
        Console.WriteLine("  --render-settings <out.png> [탭] [너비x높이] [dark|light]");
        Console.WriteLine("  --render-owl <out.png> [칸 크기]");
        Console.WriteLine("  --render-menubar <out.png> [아이콘 크기] [확대] [기분] [test]");
        Console.WriteLine("  --help");
        Console.WriteLine();
        Console.WriteLine("--dump-changelog · --dump-owl 은 도구 프로젝트에서 뽑는다:");
        Console.WriteLine("  dotnet run --project tools/DongCSU.Tools -- --dump-changelog <파일>");
    }

    /// <summary>
    /// 부모 터미널에 붙는다.
    ///
    /// **이 앱은 `WinExe` 라 콘솔이 없다.** 그냥 `Console.WriteLine` 을 하면 PowerShell
    /// 에서 실행해도 **아무것도 안 찍힌다.** 진단 통로를 만들어 놓고 정작 못 보는 일이
    /// 실제로 있었다(1.1.0). 붙은 뒤에는 표준 출력을 다시 열어 줘야 한다 —
    /// .NET 이 이미 빈 스트림을 잡아 놨기 때문이다.
    /// </summary>
    private static void AttachToConsole()
    {
        // 터미널에서 부른 게 아니면(더블클릭 등) 창을 하나 띄운다. 안 그러면 볼 수가 없다.
        if (!NativeConsole.AttachConsole(NativeConsole.AttachParentProcess)
            && !NativeConsole.AllocConsole())
        {
            return;
        }

        try
        {
            var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(output);
            Console.SetError(output);
        }
        catch (IOException)
        {
        }
    }

    private static partial class NativeConsole
    {
        /// <summary>부모 프로세스의 콘솔에 붙는다(-1).</summary>
        public const uint AttachParentProcess = 0xFFFFFFFF;

        [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool AttachConsole(uint processId);

        [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool AllocConsole();
    }

    /// <summary>사용량 조회만 해 본다. 자격 증명이 제대로 읽히는지 확인하는 자리다.</summary>
    private static async Task<int> Probe()
    {
        using var http = UsageApi.CreateHttpClient();
        var source = new FileCredentialSource(fallbackPaths: WslCredentialPaths.All);

        // **찾아본 자리를 전부 찍는다.** "못 읽었다" 한 줄만으로는 사용자가 보낸
        // 기록에서 원인을 짚을 수 없다 — 파일이 없는 것과, 있는데 Claude 로그인이
        // 안 들어 있는 것은 할 일이 전혀 다르다.
        foreach (var attempt in source.Inspect())
        {
            Console.WriteLine($"  {attempt.Path}");
            Console.WriteLine($"    → {attempt.Describe()}");
        }

        var credentials = new CredentialStore(source, refreshedTokens: new RefreshedTokenStore());

        if (credentials.Current() is not { } credential)
        {
            Console.WriteLine("credentials: not found");
            return 1;
        }
        Console.WriteLine("credentials: found");
        Console.WriteLine($"expires_at: {credential.ExpiresAt?.ToString("u") ?? "-"}"
            + (credential.IsExpired(DateTimeOffset.UtcNow) ? " (expired — will refresh)" : ""));
        Console.WriteLine($"refresh_token: {(credential.RefreshToken is null ? "missing" : "present")}");

        var api = new UsageApi(http, credentials, refresher: new OAuthTokenRefresher(http));
        var result = await api.FetchAsync().ConfigureAwait(false);
        if (result.Error is { } error)
        {
            Console.WriteLine($"error: {error.Kind} {error.Message}");
            return 1;
        }

        var snapshot = result.Snapshot!;
        Console.WriteLine($"plan: {snapshot.PlanName ?? "(불명)"}");
        Console.WriteLine($"five_hour: {Show(snapshot.FiveHour)}");
        Console.WriteLine($"seven_day: {Show(snapshot.SevenDay)}");
        return 0;

        static string Show(UsageWindow? window) => window is { } value
            ? $"{value.Utilization}% resets_at={value.ResetsAt?.ToString("O") ?? "-"}"
            : "-";
    }

    /// <summary>부엉이를 글자로 찍는다. 그림을 못 보는 자리에서 자세를 확인한다.</summary>
    private static void PrintOwl(string animationName)
    {
        var document = OwlDocument.Embedded;
        var animation = document.Animations.FirstOrDefault(a => a.Name == animationName);
        if (animation is null)
        {
            Console.WriteLine($"없는 애니메이션: {animationName}");
            Console.WriteLine($"있는 것: {string.Join(", ", document.Animations.Select(a => a.Name))}");
            return;
        }

        Console.WriteLine($"{animation.Title} ({animation.Name}) · 팔레트 {animation.Palette}");
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            var frame = animation.Frames[i];
            Console.WriteLine($"\n── {i + 1}/{animation.Frames.Count}  {frame.Duration}s");

            // 우리가 합성한 것을 찍는다. 파일에 실린 것과 다르면 여기서 눈에 띈다.
            var composed = OwlComposer.Compose(document, frame.Pose);
            var matches = composed.SequenceEqual(frame.Grid);
            foreach (var row in composed) Console.WriteLine("  " + row);
            if (!matches) Console.WriteLine("  ⚠ 맥이 넘긴 그리드와 다르다");
        }
    }
}
