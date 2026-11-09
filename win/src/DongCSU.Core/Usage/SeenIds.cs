using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DongCSU.Core.Usage;

/// <summary>
/// 이미 센 응답 id. **들어온 차례를 들고 있다가 넘치면 오래된 것부터 버린다.**
///
/// 예전에는 <c>HashSet</c> 하나였고 상한에 닿으면 **새 id 를 아예 안 받았다.** 그러면 그
/// 순간부터 중복 제거가 통째로 멈추는데, 같은 응답이 두세 줄에 걸쳐 적히는 것이 이 기록의
/// 전제라 **그 뒤로 합계가 2배가 된다.** 맥 실측으로 하루에 서로 다른 id 가 8,300개씩
/// 생기므로, 상한 5만은 측정을 켜 둔 채 6~14일이면 닿는다 — "몇 주씩 켜 두는 사람" 이
/// 바로 거기다.
///
/// 버리는 쪽은 **제일 오래된 것**이다. 방금 본 id 가 바로 다음 줄에서 또 나오므로,
/// 새 것을 버리는 것은 중복 제거를 끄는 것과 같다.
///
/// **파일에는 차례만 적는다**(<see cref="SeenIdsConverter"/>). 집합은 읽을 때 되만든다 —
/// 같은 것을 두 벌 적을 이유가 없고, 옛 <c>meter.json</c> 이 집합을 배열로 적어 둔 것과도
/// 그대로 맞는다.
/// </summary>
[JsonConverter(typeof(SeenIdsConverter))]
public sealed class SeenIds : IReadOnlyCollection<string>
{
    /// <summary>들고 있을 수 있는 최대 개수.</summary>
    public const int Limit = 50_000;

    /// <summary>
    /// 넘칠 때 한 번에 버리는 양.
    ///
    /// **한 개씩 버리면 안 된다** — 목록 앞을 지우면 뒤쪽 전체가 당겨지므로, 상한에 닿은
    /// 뒤로는 줄마다 5만 칸을 옮기게 된다.
    /// </summary>
    public const int EvictChunk = 4_096;

    /// <summary>id 는 **대소문자를 가린다**(경로와 다르다).</summary>
    private readonly HashSet<string> ids;

    /// <summary>들어온 차례. 넘칠 때 버릴 쪽을 고르는 데만 쓴다.</summary>
    private readonly List<string> order;

    public SeenIds()
    {
        ids = new HashSet<string>(StringComparer.Ordinal);
        order = [];
    }

    /// <summary>차례가 있는 목록에서 되만든다. 겹치거나 null 인 것은 버린다.</summary>
    public SeenIds(IEnumerable<string?>? ids) : this()
    {
        if (ids is null) return;
        foreach (var id in ids)
        {
            if (!string.IsNullOrEmpty(id)) Add(id);
        }
    }

    private SeenIds(SeenIds source)
    {
        // **상한 계산을 다시 돌리지 않는다.** 복사는 같은 것을 그대로 옮기는 일이라,
        // 하나씩 넣으면 버릴 쪽을 또 고르면서 값만 든다.
        ids = new HashSet<string>(source.ids, StringComparer.Ordinal);
        order = [.. source.order];
    }

    public int Count => ids.Count;

    public bool Contains(string id) => ids.Contains(id);

    /// <summary>처음 보는 id 면 들이고 true. 넘쳤으면 오래된 한 덩이를 버린다.</summary>
    public bool Add(string id)
    {
        if (!ids.Add(id)) return false;

        order.Add(id);
        if (order.Count <= Limit + EvictChunk) return true;

        for (var index = 0; index < EvictChunk; index++) ids.Remove(order[index]);
        order.RemoveRange(0, EvictChunk);
        return true;
    }

    /// <summary>깊은 복사. 넘겨받은 쪽이 고쳐도 원본이 안 움직인다.</summary>
    public SeenIds Copy() => new(this);

    /// <summary>**들어온 차례 그대로** 돌려준다 — 파일에 적는 차례이기도 하다.</summary>
    public IEnumerator<string> GetEnumerator() => order.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// <see cref="SeenIds"/> 를 **문자열 배열 그대로** 싣는다.
///
/// 옛 <c>meter.json</c> 은 이 자리에 집합을 배열로 적어 두었다. 같은 꼴을 쓰므로 판을
/// 올려도 기록이 그대로 읽히고, 내려도(옛 판으로 돌아가도) 읽힌다.
/// </summary>
internal sealed class SeenIdsConverter : JsonConverter<SeenIds>
{
    /// <summary>
    /// <c>null</c> 도 우리가 받는다.
    ///
    /// **안 받으면 그 칸이 아예 <c>null</c> 로 꽂힌다** — <c>"seenIds": null</c> 이 든
    /// <c>meter.json</c> 하나로 그 뒤 모든 훑기가 터진다(기본값은 칸에 적혀 있어도
    /// 직렬화가 덮는다). 빈 것으로 읽으면 중복 제거만 처음부터 다시 쌓인다.
    /// </summary>
    public override bool HandleNull => true;

    public override SeenIds Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return new SeenIds();
        // **배열이 아니면 빈 것으로 본다.** 손으로 고친 파일 하나 때문에 측정 기록이
        // 통째로 안 읽히면 안 된다 — 중복 제거만 느슨해지고 합계는 계속 쌓인다.
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return new SeenIds();
        }
        return new SeenIds(JsonSerializer.Deserialize<List<string?>>(ref reader, options));
    }

    public override void Write(Utf8JsonWriter writer, SeenIds value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var id in value) writer.WriteStringValue(id);
        writer.WriteEndArray();
    }
}
