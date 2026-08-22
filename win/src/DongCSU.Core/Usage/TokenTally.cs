using System.Text.Json.Serialization;

namespace DongCSU.Core.Usage;

/// <summary>
/// 토큰 수 묶음 하나.
///
/// **칸이 전부 <c>long</c> 이다.** 맥의 <c>Int</c> 는 64비트라 신경 쓸 일이 없었지만
/// C# 의 <c>int</c> 는 21.4억에서 끝난다 — 응답 하나의 캐시 읽기가 20만을 예사로 넘어서
/// 몇천 응답만 쌓여도 조용히 음수가 된다.
///
/// 값 타입(<c>readonly record struct</c>)인 것도 일부러다. 사전에 담아 더할 때 참조가
/// 공유되면 엉뚱한 자리가 같이 바뀐다.
/// </summary>
/// <param name="Responses">응답 수. 토큰이 아니라 이것으로 비어 있는지를 판단한다.</param>
public readonly record struct TokenTally(
    long Responses,
    long Input,
    long Output,
    long CacheCreation,
    long CacheRead)
{
    [JsonIgnore]
    public long Total => Input + Output + CacheCreation + CacheRead;

    /// <summary>
    /// 캐시를 뺀 합계.
    ///
    /// **캐시가 합계를 통째로 가린다.** 실제로는 여기 몇십만이 드나드는데 캐시 읽기가
    /// 수백만~수천만이라, <see cref="Total"/> 만 보면 "내가 이렇게 썼다고?" 가 된다.
    /// 캐시 읽기는 같은 글을 다시 보내지 않으려고 서버가 들고 있는 것이라 단가도 입력의
    /// 1/10이다. 두 숫자를 나란히 둬서 어느 쪽이 무엇인지 갈라 보이게 한다.
    /// </summary>
    [JsonIgnore]
    public long WithoutCache => Input + Output;

    /// <summary>토큰이 아니라 <see cref="Responses"/> 를 본다 — 응답은 왔는데 값이 전부
    /// 0인 것과, 아무것도 안 온 것은 다르다.</summary>
    [JsonIgnore]
    public bool IsEmpty => Responses == 0;

    public static TokenTally operator +(TokenTally a, TokenTally b) => new(
        a.Responses + b.Responses,
        a.Input + b.Input,
        a.Output + b.Output,
        a.CacheCreation + b.CacheCreation,
        a.CacheRead + b.CacheRead);
}
