using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DongCSU.Core.Usage;

/// <summary>
/// 훑기 한 번의 결과.
///
/// **델타가 아니다** — <see cref="Offsets"/> 와 <see cref="SeenIds"/> 는 갱신된 **전체**다
/// (맥과 같다). 그래서 옛 훑기의 결과를 새 측정 위에 얹으면 오프셋이 옛 자리로
/// **되감기고**, 한 번 되감기면 그 뒤 모든 훑기가 같은 구간을 다시 읽어 값이 계속 커진다.
/// 얹기 전에 부르는 쪽이 표식(<c>SessionStamp</c> = 시작 시각 + 멈춰 있던 시간)을
/// 대조해서, 훑는 사이에 다시 시작·계속이 눌렸으면 결과를 통째로 버려야 한다.
/// </summary>
public sealed record TokenScanResult
{
    public TokenTally Added { get; init; }

    /// <summary>모델 이름(<see cref="ClaudeCodeUsage.DisplayName"/> 를 태운 것) → 더한 몫.</summary>
    public Dictionary<string, TokenTally> AddedByModel { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, long> Offsets { get; init; } = new(ClaudeCodeUsage.PathComparer);

    public HashSet<string> SeenIds { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 기록 파일을 **덧붙은 부분만** 읽어서 토큰을 더한다.
///
/// 파일이 30MB 씩 되기 때문에 폴링마다 통째로 다시 읽을 수 없다. 기록은 줄 단위로
/// 덧붙기만 하므로 파일마다 어디까지 읽었는지 **바이트** 오프셋을 기억해 두고 그 뒤만 읽는다.
///
/// <see cref="Run"/> 은 파일을 백여 개 열 수 있어 초 단위로 늘어난다. **화면 스레드에서
/// 부르지 않는다** — <c>Task.Run</c> 위에 올린다. 대신 아무 공유 상태도 안 건드리는 순수
/// 계산이라, 돌아와서 결과를 버려도 아무 부작용이 안 남는다.
/// </summary>
public sealed class TokenScan
{
    /// <summary>
    /// 중복 제거용 id 를 이만큼까지만 들고 있는다.
    ///
    /// 측정 구간 안의 응답 수만큼만 쌓이므로 실제로는 몇천을 넘지 않는다. 그래도 몇 주씩
    /// 켜 두는 사람이 있을 수 있어서 위를 막아 둔다 — 넘으면 중복 제거만 느슨해지고
    /// 합계는 계속 쌓인다.
    /// </summary>
    public const int SeenLimit = 50_000;

    private readonly DateTimeOffset since;
    private readonly Dictionary<string, long> offsets;
    private readonly HashSet<string> seenIds;
    private readonly string? root;

    /// <param name="since">
    /// 이 시각보다 앞선 기록은 세지 않는다.
    ///
    /// 오프셋만으로는 부족하다 — 세션을 이어가면 **옛 응답이 새 파일로 통째로 복사**되고,
    /// 그건 새로 쓴 토큰이 아니다. 복사본은 원래 시각을 그대로 달고 오므로 여기서 걸린다.
    /// </param>
    /// <param name="offsets">파일마다 어디까지 읽었는지. 없으면 처음부터 읽는다.</param>
    /// <param name="seenIds">이미 센 <c>message.id</c>.</param>
    /// <param name="root">안 주면 <see cref="ClaudeCodeUsage.ProjectsDirectory"/>.</param>
    /// <remarks>
    /// **받은 사전과 집합을 여기서 곧바로 복사한다.** 스위프트의 <c>Dictionary</c>·<c>Set</c>
    /// 은 값 타입이라 저절로 복사되지만 C# 은 참조다 — 그대로 들고 쓰면 배경 스레드가
    /// **살아 있는 측정 상태를 직접 갈아엎고**, 표식 대조가 결과를 버려도 이미 늦는다.
    /// (게다가 화면 스레드가 같은 사전을 읽는 중이면 그 자리에서 던진다.)
    /// </remarks>
    public TokenScan(
        DateTimeOffset since,
        IReadOnlyDictionary<string, long>? offsets,
        IReadOnlySet<string>? seenIds,
        string? root = null)
    {
        this.since = since;
        this.offsets = ClaudeCodeUsage.WithPathComparer(offsets);
        // 경로는 대소문자를 안 가리지만 id 는 가린다. 비교자가 서로 다르다.
        this.seenIds = seenIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(seenIds, StringComparer.Ordinal);
        this.root = root;
    }

    /// <summary>줄 하나에서 뽑아 내는 것. 그 밖의 칸은 보지 않는다.</summary>
    public readonly record struct Entry(string Id, string Model, TokenTally Tally);

    /// <summary>한 번 훑는다. 같은 인스턴스를 두 번 불러도 입력이 그대로라 같은 답이 나온다.</summary>
    public TokenScanResult Run()
    {
        // 인스턴스가 들고 있는 것도 다시 복사한다 — Run 이 제 입력을 갉아먹으면 두 번째
        // 훑기가 첫 번째와 다른 답을 낸다.
        var offsets = new Dictionary<string, long>(this.offsets, ClaudeCodeUsage.PathComparer);
        var seen = new HashSet<string>(this.seenIds, StringComparer.Ordinal);
        var added = default(TokenTally);
        var byModel = new Dictionary<string, TokenTally>(StringComparer.Ordinal);

        foreach (var file in ClaudeCodeUsage.Transcripts(root))
        {
            var offset = offsets.GetValueOrDefault(file.Path);

            // **덧붙은 게 없으면 열지도 않는다.** 기록 폴더에 파일이 수백 개씩 쌓이는데
            // 그걸 1분마다 전부 열면 열고 닫는 값만으로도 비싸진다. 크기는 훑을 때
            // 이미 받아 둔 값이라 공짜다.
            if (file.Length <= offset)
            {
                // 파일이 줄었으면(지워졌다 다시 만들어졌다) 지금 끝을 새 기준으로 내려
                // 둔다. **맥은 이 빠른 경로에서 안 내려서**, 잘렸던 파일이 옛 크기를
                // 넘길 때까지 그 사이에 적힌 것을 통째로 놓친다. 정상 경로의 동작은
                // 똑같고 잘림에서만 나아지는 것이라 고쳐서 옮긴다.
                if (file.Length < offset) offsets[file.Path] = Math.Max(0, file.Length);
                continue;
            }

            if (ReadAppended(file.Path, offset) is not { } chunk) continue;
            offsets[file.Path] = chunk.Next;
            if (chunk.Data.Length == 0) continue;

            // 문자열로 바꾸지 않고 바이트째 자른다 — 30MB 짜리 첫 훑기를
            // `GetString` + `Split('\n')` 로 하면 큰 개체 힙을 통째로 때린다.
            var rest = new ReadOnlyMemory<byte>(chunk.Data);
            while (!rest.IsEmpty)
            {
                var end = rest.Span.IndexOf((byte)'\n');
                var line = end < 0 ? rest : rest[..end];
                rest = end < 0 ? default : rest[(end + 1)..];

                // 실제 기록에 `\r` 은 없지만, 있으면 파싱이 통째로 실패하므로 걷어낸다.
                if (!line.IsEmpty && line.Span[^1] == (byte)'\r') line = line[..^1];
                if (line.IsEmpty) continue;

                if (Parse(line, since) is not { } entry) continue;

                // **같은 응답이 두세 줄에 걸쳐 적힌다.** 값은 매번 같으므로 처음 것만 센다.
                if (seen.Contains(entry.Id)) continue;
                if (seen.Count < SeenLimit) seen.Add(entry.Id);

                added += entry.Tally;
                byModel[entry.Model] = byModel.GetValueOrDefault(entry.Model) + entry.Tally;
            }
        }

        return new TokenScanResult
        {
            Added = added,
            AddedByModel = byModel,
            Offsets = offsets,
            SeenIds = seen,
        };
    }

    /// <summary>
    /// 오프셋 뒤에 덧붙은 부분. **완성된 줄까지만** 돌려주고 그만큼만 오프셋을 옮긴다.
    /// 마침 쓰는 중이면 마지막 줄이 잘려 있는데, 그걸 파싱하면 그 응답을 영영 놓친다.
    /// </summary>
    /// <returns>못 열었으면 <c>null</c> — 그 파일은 이번 라운드만 건너뛰고 오프셋도 그대로 둔다.</returns>
    private static (byte[] Data, long Next)? ReadAppended(string path, long offset)
    {
        try
        {
            // **`FileShare` 를 틀리면 Claude Code 가 막힌다.** 우리 핸들의 공유 모드는
            // *남이 무엇을 해도 되는지*를 정한다 — `Read` 로 열면 우리가 읽는 동안
            // Node 가 쓰기로 여는 것이 공유 위반으로 실패해서 **기록이 끊긴다.**
            // `Delete` 를 빼면 그 사이에 지우거나 이름을 바꾸는 것도 막힌다.
            using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                BufferSize = 0,
                Options = FileOptions.SequentialScan,
            });

            // 크기는 **한 번만** 잰다. 여는 사이에 또 덧붙었다면 그건 다음 라운드 몫이다.
            var size = stream.Length;

            // 파일이 줄었다. 지워졌다 다시 만들어진 것이니 지금 끝을 새 기준으로 잡는다.
            // 0부터 다시 읽으면 이미 센 것을 또 센다.
            if (size <= offset) return (Array.Empty<byte>(), Math.Min(offset, size));

            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[size - offset];
            // `Read` 한 번으로는 다 안 온다 — 부분 읽기가 정상이다.
            stream.ReadExactly(buffer);

            // UTF-8 은 자기동기화 코드라 0x0A 가 멀티바이트 글자 안에 절대 안 들어간다.
            // 그래서 날바이트를 개행으로 자르는 것이 안전하다. 반대로 `StreamReader` 로
            // 읽고 글자 수를 세면, 한국어 한 글자가 3바이트라 오프셋이 어긋나 다음
            // 라운드가 줄 한가운데로 건너뛴다(`BaseStream.Position` 도 버퍼링 때문에
            // 못 믿는다).
            var lastNewline = Array.LastIndexOf(buffer, (byte)'\n');
            if (lastNewline < 0) return (Array.Empty<byte>(), offset);

            // `JsonDocument.Parse` 는 BOM 을 안 건너뛰고 0xEF 를 만나면 던진다. 파싱에
            // 넘기기 전에 잘라내되 **오프셋 계산에는 그대로 넣는다** — 파일 안에 있는
            // 바이트다.
            var start = offset == 0 && buffer.Length >= 3
                && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;

            return (buffer[start..(lastNewline + 1)], offset + lastNewline + 1);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>줄 하나를 글자로 넣어 본다. 검사·진단이 파일을 안 만들고 쓰는 통로다.</summary>
    public static Entry? ParseLine(string line, DateTimeOffset since)
        => Parse(Encoding.UTF8.GetBytes(line), since);

    /// <summary>
    /// 줄 하나. <c>message.usage</c> · <c>message.id</c> · <c>timestamp</c> 가 다 있어야
    /// 통과하고, 하나라도 없으면 조용히 버린다(오프셋은 그대로 넘어간다).
    ///
    /// <c>type == "assistant"</c> 로 거르고 싶어지지만 **맥이 안 거르므로 거르지 않는다** —
    /// 구조 검사만으로 충분하고, 서버가 새 타입을 쓰기 시작해도 안 놓친다.
    /// </summary>
    private static Entry? Parse(ReadOnlyMemory<byte> line, DateTimeOffset since)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || idElement.GetString() is not { Length: > 0 } id)
            {
                return null;
            }

            if (Stamp(root) is not { } stamp || stamp < since) return null;

            var model = message.TryGetProperty("model", out var modelElement)
                && modelElement.ValueKind == JsonValueKind.String
                && modelElement.GetString() is { Length: > 0 } raw
                ? ClaudeCodeUsage.DisplayName(raw)
                : ClaudeCodeUsage.UnknownModel;

            // **최상위 값을 읽는다.** `usage.iterations` 배열도 있지만 실제 기록에서
            // 최상위가 0인데 iterations 만 든 것은 6,089 레코드 중 1건뿐이고 합계도
            // 최상위가 더 크다.
            var tally = new TokenTally(
                Responses: 1,
                Input: Number(usage, "input_tokens"),
                Output: Number(usage, "output_tokens"),
                CacheCreation: Number(usage, "cache_creation_input_tokens"),
                CacheRead: Number(usage, "cache_read_input_tokens"));

            return new Entry(id, model, tally);
        }
        catch (JsonException)
        {
            // 줄이 JSON 이 아니어도 **던지지 않는다.** 그 줄만 버리고 계속 간다.
            return null;
        }
    }

    /// <summary>없거나 숫자가 아니면 0. 서버가 칸 하나를 안 주는 일이 흔하다.</summary>
    private static long Number(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        if (element.TryGetInt64(out var number)) return number;
        return element.TryGetDouble(out var real) ? (long)real : 0;
    }

    /// <summary>
    /// 최상위 <c>timestamp</c>. 실제 모양은 <c>2026-07-26T12:19:00.573Z</c> 다.
    ///
    /// <see cref="JsonElement.TryGetDateTimeOffset"/> 하나가 맥의 포매터 둘(소수점 있는
    /// 것·없는 것)을 대신한다. 다만 소수 자릿수가 많으면 그쪽이 실패하므로 뒷길을 남긴다 —
    /// 그때 <c>AdjustToUniversal|AssumeUniversal</c> 을 빼면 <c>Z</c> 없는 문자열이 로컬
    /// 시각으로 읽혀 시간대만큼 통째로 어긋나고, 문화권을 안 못 박으면 기계마다 답이 갈린다.
    /// </summary>
    private static DateTimeOffset? Stamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (element.TryGetDateTimeOffset(out var stamp)) return stamp;

        return element.GetString() is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>
/// 훑은 결과를 여태 쌓은 것 위에 얹는 셈.
///
/// **일부러 떼어 놨다** — 진단 통로(<c>--probe-meter scan</c>)와 실제 동작이 같은 코드를
/// 쓰게 하려는 것이다. 두 벌이 되면 반드시 갈린다. 시계도 파일도 안 타는 순수 계산이라
/// 아무 데서나 부를 수 있다.
///
/// 오프셋과 본 id 는 결과의 것으로 **통째로 갈아 끼우는 것**이라 여기서 셈할 것이 없다
/// (<see cref="TokenScanResult"/> 주석 참고). 그래서 이 함수는 합계 둘만 더한다.
/// </summary>
public static class TokenScanApply
{
    public static (TokenTally Tokens, Dictionary<string, TokenTally> ByModel) Applying(
        TokenScanResult result,
        TokenTally tokens,
        IReadOnlyDictionary<string, TokenTally>? byModel)
    {
        // 받은 사전을 고치지 않고 새것을 돌려준다 — 부르는 쪽이 표식 대조에서 결과를
        // 버리기로 해도 아무것도 안 남아 있어야 한다.
        var merged = byModel is null
            ? new Dictionary<string, TokenTally>(StringComparer.Ordinal)
            : new Dictionary<string, TokenTally>(byModel, StringComparer.Ordinal);

        foreach (var (model, tally) in result.AddedByModel)
        {
            merged[model] = merged.GetValueOrDefault(model) + tally;
        }

        return (tokens + result.Added, merged);
    }
}
