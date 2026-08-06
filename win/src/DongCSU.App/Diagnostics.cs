using System.IO;
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
public static class Diagnostics
{
    /// <summary>진단 인자를 처리했으면 true. 그러면 창을 띄우지 않고 끝낸다.</summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0) return false;

        switch (args[0])
        {
            case "--version":
                Console.WriteLine($"dong-csu {AppInfo.Version}");
                return true;

            case "--dump-changelog":
                Write(args.ElementAtOrDefault(1), Changelog.Dump());
                return true;

            case "--dump-owl":
                // 맥이 뽑은 것을 그대로 다시 뱉는다. CI 가 shared/owl.json 과 대조해서,
                // 앱에 박힌 데이터가 저장소의 것과 같은지 확인한다.
                Write(args.ElementAtOrDefault(1), EmbeddedOwlJson());
                return true;

            case "--probe":
                exitCode = Probe().GetAwaiter().GetResult();
                return true;

            case "--probe-owl":
                PrintOwl(args.ElementAtOrDefault(1) ?? "idle");
                return true;

            default:
                return false;
        }
    }

    private static void Write(string? path, string content)
    {
        if (string.IsNullOrEmpty(path)) { Console.WriteLine(content); return; }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, content);
        Console.WriteLine($"wrote: {path}");
    }

    private static string EmbeddedOwlJson()
    {
        using var stream = typeof(OwlDocument).Assembly.GetManifestResourceStream("owl.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>사용량 조회만 해 본다. 자격 증명이 제대로 읽히는지 확인하는 자리다.</summary>
    private static async Task<int> Probe()
    {
        using var http = UsageApi.CreateHttpClient();
        var credentials = new CredentialStore(new FileCredentialSource());

        if (credentials.Current() is null)
        {
            Console.WriteLine("credentials: not found");
            foreach (var path in FileCredentialSource.DefaultPaths()) Console.WriteLine($"  looked at: {path}");
            return 1;
        }
        Console.WriteLine("credentials: found");

        var result = await new UsageApi(http, credentials).FetchAsync().ConfigureAwait(false);
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
