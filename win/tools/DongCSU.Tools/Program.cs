using DongCSU.Core;
using DongCSU.Core.Owl;

if (args.Length == 0)
{
    Console.Error.WriteLine("""
        쓰는 법:
          dotnet run --project tools/DongCSU.Tools -- --dump-changelog [out.json]
          dotnet run --project tools/DongCSU.Tools -- --dump-owl       [out.json]
          dotnet run --project tools/DongCSU.Tools -- --print-owl      [애니메이션 이름]
          dotnet run --project tools/DongCSU.Tools -- --check-icon    <아이콘 경로>
          dotnet run --project tools/DongCSU.Tools -- --check-release <버전>
        """);
    return 2;
}

switch (args[0])
{
    case "--dump-changelog":
        Write(args.ElementAtOrDefault(1), Changelog.Dump());
        return 0;

    case "--dump-owl":
        // 앱에 박아 둔 것을 그대로 뱉는다. CI 가 shared/owl.json 과 대조한다.
        Write(args.ElementAtOrDefault(1), EmbeddedOwl());
        return 0;

    case "--print-owl":
        return PrintOwl(args.ElementAtOrDefault(1) ?? "idle");

    case "--check-icon":
        return CheckIcon(args.ElementAtOrDefault(1)
            ?? "src/DongCSU.App/Resources/DongCSU.ico");

    case "--check-release":
        return CheckRelease(args.ElementAtOrDefault(1));

    default:
        Console.Error.WriteLine($"모르는 인자: {args[0]}");
        return 2;
}

static void Write(string? path, string content)
{
    if (string.IsNullOrEmpty(path)) { Console.WriteLine(content); return; }

    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    File.WriteAllText(path, content);
    Console.WriteLine($"wrote: {path}");
}

static string EmbeddedOwl()
{
    using var stream = typeof(OwlDocument).Assembly.GetManifestResourceStream("owl.json")!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

/// <summary>부엉이를 글자로 찍는다. 그림을 못 보는 자리에서 자세를 확인한다.</summary>
static int PrintOwl(string name)
{
    var document = OwlDocument.Embedded;
    var animation = document.Animations.FirstOrDefault(a => a.Name == name);
    if (animation is null)
    {
        Console.Error.WriteLine($"없는 애니메이션: {name}");
        Console.Error.WriteLine($"있는 것: {string.Join(", ", document.Animations.Select(a => a.Name))}");
        return 1;
    }

    Console.WriteLine($"{animation.Title} ({animation.Name}) · 팔레트 {animation.Palette}");
    var allMatch = true;
    for (var i = 0; i < animation.Frames.Count; i++)
    {
        var frame = animation.Frames[i];
        Console.WriteLine($"\n── {i + 1}/{animation.Frames.Count}  {frame.Duration}s");

        // 우리가 합성한 것을 찍는다. 파일에 실린 맥의 결과와 다르면 여기서 드러난다.
        var composed = OwlComposer.Compose(document, frame.Pose);
        foreach (var row in composed) Console.WriteLine("  " + row);

        if (composed.SequenceEqual(frame.Grid)) continue;
        Console.WriteLine("  ⚠ 맥이 넘긴 그리드와 다르다");
        allMatch = false;
    }
    return allMatch ? 0 : 1;
}

/// <summary>
/// 아이콘이 지금 부엉이에서 나온 것인지.
///
/// **아이콘 파일 자체를 비교하지 않는다.** PNG 압축 결과가 zlib 판마다 달라서 같은
/// 그림인데도 바이트가 달라지고, 그러면 CI 가 아무 이유 없이 빨개진다. 대신 무엇으로
/// 만들었는지를 비교한다 — 부엉이가 바뀌었는데 아이콘을 안 만든 경우만 잡으면 된다.
///
/// 지문을 만드는 방법은 `make-icon.py` 의 `fingerprint()` 와 **똑같아야 한다.**
/// 한쪽을 고치면 다른 쪽도 고친다.
/// </summary>
static int CheckIcon(string iconPath)
{
    var stampPath = iconPath + ".sha256";
    if (!File.Exists(iconPath) || !File.Exists(stampPath))
    {
        Console.Error.WriteLine($"아이콘이나 지문이 없다: {iconPath} — python3 win/make-icon.py 로 만들어라");
        return 1;
    }

    var document = OwlDocument.Embedded;
    var palette = document.Palettes["normal"];
    var grid = document.Animations.Single(a => a.Name == "idle").Frames[0].Grid;
    int[] sizes = [16, 24, 32, 48, 64, 128, 256];

    var material = string.Join("|", [
        string.Join("\n", grid),
        string.Join(",", palette.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => $"{k}={palette[k]}")),
        string.Join(",", sizes),
    ]);

    var expected = Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material)));
    var actual = File.ReadAllText(stampPath).Trim();

    if (expected == actual)
    {
        Console.WriteLine("아이콘이 부엉이 데이터와 맞는다");
        return 0;
    }

    Console.Error.WriteLine($"아이콘이 owl.json 과 다르다 — python3 win/make-icon.py 로 다시 만들어라");
    Console.Error.WriteLine($"  기대: {expected}");
    Console.Error.WriteLine($"  파일: {actual}");
    return 1;
}

/// <summary>
/// 이 버전을 내보내도 되는지.
///
/// **태그만 붙이고 변경 내역을 안 적는 실수**를 막는다. 그러면 사용자는 새 버전을
/// 받았는데 설정 창의 버전 탭에는 아무것도 안 뜬다. 날짜도 함께 본다 — 비어 있으면
/// "아직 안 나간 항목"이라는 뜻이라 릴리스와 앞뒤가 안 맞는다.
/// </summary>
static int CheckRelease(string? version)
{
    if (string.IsNullOrWhiteSpace(version))
    {
        Console.Error.WriteLine("버전을 넘겨라: --check-release 1.0.0");
        return 2;
    }

    var newest = Changelog.Entries[0];
    var problems = new List<string>();

    if (newest.Version != version)
    {
        problems.Add($"변경 내역 맨 위가 {newest.Version} 인데 내려는 것은 {version} 이다");
    }
    if (string.IsNullOrEmpty(newest.Date))
    {
        problems.Add($"{newest.Version} 에 날짜가 없다 — Changelog.cs 에서 확정해라");
    }

    if (problems.Count == 0)
    {
        Console.WriteLine($"{version} 을 내보낼 수 있다 ({newest.Notes.Count}줄, {newest.Date})");
        return 0;
    }

    foreach (var problem in problems) Console.Error.WriteLine($"  {problem}");
    return 1;
}
