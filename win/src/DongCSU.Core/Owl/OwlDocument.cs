using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DongCSU.Core.Owl;

/// <summary>
/// <c>shared/owl.json</c> 을 그대로 담는다.
///
/// **이 파일은 맥 소스에서 뽑아낸 것이고 손으로 고치지 않는다.** 부엉이를 고치려면
/// 맥 쪽 <c>OwlMark.swift</c> 를 고치고 <c>dump_owl</c> 을 다시 돌린다.
/// 여기서 직접 값을 바꾸면 다음 릴리스에 조용히 되돌아간다.
/// </summary>
public sealed record OwlDocument
{
    /// <summary>이 코드가 읽을 줄 아는 형식. 파일이 더 높으면 읽지 않는다.</summary>
    public const int SupportedFormatVersion = 1;

    public required int FormatVersion { get; init; }
    public required OwlGrid Grid { get; init; }

    /// <summary>이름 → 13줄짜리 그림. <c>.</c> 은 빈 칸이다.</summary>
    public required IReadOnlyDictionary<string, string[]> Layers { get; init; }

    /// <summary>팔레트 이름 → (글자 종류 → <c>#RRGGBB</c>).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Palettes { get; init; }

    /// <summary>기분이 바뀌는 사용률. <c>tired</c> · <c>exhausted</c>.</summary>
    public required IReadOnlyDictionary<string, double> MoodThresholds { get; init; }

    /// <summary>링 색 구간. 사이는 선형 보간한다.</summary>
    public required IReadOnlyList<OwlUsageStop> UsageColors { get; init; }

    public required IReadOnlyList<OwlAnimation> Animations { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>앱에 박아 둔 <c>owl.json</c>. 빌드할 때 저장소의 것이 그대로 들어간다.</summary>
    public static OwlDocument Embedded { get; } = LoadEmbedded();

    public static OwlDocument Parse(string json)
    {
        var document = JsonSerializer.Deserialize<OwlDocument>(json, Options)
            ?? throw new InvalidDataException("owl.json 을 읽지 못했다.");

        // 형식이 올라갔다는 건 모르는 항목이 생겼다는 뜻이다. 조용히 잘못 그리는 것보다
        // 여기서 멈추는 게 낫다 — 빌드가 깨지면 바로 알아챈다.
        if (document.FormatVersion > SupportedFormatVersion)
        {
            throw new InvalidDataException(
                $"owl.json 형식이 {document.FormatVersion} 인데 이 코드는 {SupportedFormatVersion} 까지만 안다.");
        }

        return document;
    }

    private static OwlDocument LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("owl.json")
            ?? throw new InvalidOperationException("owl.json 이 앱에 들어 있지 않다.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}

/// <summary>칸 수. 좌우 여백은 날개를 펼 자리라 몸통보다 넓다.</summary>
public sealed record OwlGrid
{
    public required int Columns { get; init; }
    public required int Lines { get; init; }
    public required int BodyColumns { get; init; }
}

public sealed record OwlUsageStop
{
    /// <summary>이 색이 되는 사용률 (0–100).</summary>
    public required double At { get; init; }
    public required string Hex { get; init; }
}

public sealed record OwlAnimation
{
    /// <summary><c>idle</c> · <c>walk</c> 처럼 코드에서 가리키는 이름.</summary>
    public required string Name { get; init; }

    /// <summary>사람이 읽는 이름. 문서와 미리보기에 쓴다.</summary>
    public required string Title { get; init; }

    /// <summary><see cref="OwlDocument.Palettes"/> 의 키.</summary>
    public required string Palette { get; init; }

    public required IReadOnlyList<OwlFrame> Frames { get; init; }
}

public sealed record OwlFrame
{
    /// <summary>이 프레임을 보여줄 시간(초).</summary>
    public required double Duration { get; init; }

    /// <summary>같은 자세가 기계처럼 반복되지 않게 더하는 흔들림(초).</summary>
    public required double Jitter { get; init; }

    public required OwlPose Pose { get; init; }

    /// <summary>
    /// 맥이 합성해 둔 결과. 한 줄이 한 행이고 <c>.</c> 은 빈 칸이다.
    ///
    /// <see cref="OwlComposer"/> 가 <see cref="Pose"/> 로 만든 것과 **반드시 같아야 한다.**
    /// 테스트가 전 프레임을 이것과 대조한다.
    /// </summary>
    public required string[] Grid { get; init; }
}

/// <summary>레이어를 어떻게 골라 어디로 밀지.</summary>
public sealed record OwlPose
{
    public required OwlEyes Eyes { get; init; }
    public required OwlWings Wings { get; init; }
    public required OwlFeet Feet { get; init; }

    /// <summary>몸 전체를 좌우로 미는 칸 수.</summary>
    public required int Lean { get; init; }

    /// <summary>몸 전체를 위아래로 미는 칸 수.</summary>
    public required int Bob { get; init; }

    /// <summary>얼굴만 몸에서 더 미는 칸 수.</summary>
    public required int FaceLean { get; init; }

    /// <summary>발만 따로 미는 칸 수. 몸의 기울임·오르내림을 따르지 않는다.</summary>
    public required int FeetLean { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<OwlEyes>))]
public enum OwlEyes { Open, Half, Closed, Dizzy }

[JsonConverter(typeof(JsonStringEnumConverter<OwlWings>))]
public enum OwlWings { Folded, Spread, Droop, Lift }

[JsonConverter(typeof(JsonStringEnumConverter<OwlFeet>))]
public enum OwlFeet { Stand, StepA, StepB, Dangle }
