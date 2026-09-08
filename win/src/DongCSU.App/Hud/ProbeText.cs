namespace DongCSU.App.Hud;

/// <summary>
/// 진단 통로가 표를 그릴 때 쓰는 것.
///
/// <b>표가 여럿이라 따로 두면 어긋난다.</b> 같은 터미널에서 나란히 읽는 표들이라
/// 칸 맞추는 규칙이 갈리면 한쪽만 삐뚤어진다.
/// </summary>
internal static class ProbeText
{
    /// <summary>
    /// 한글이 두 칸을 먹는 것을 세어서 폭을 맞춘다.
    ///
    /// <c>string.Format</c> 의 폭은 <b>글자 수</b>로 세는데, 한글은 터미널에서 두 칸을
    /// 차지해서 그대로 두면 한글이 섞인 줄만 오른쪽으로 밀린다.
    /// </summary>
    public static string Pad(string text, int width)
    {
        var used = text.Sum(c => c >= 0x1100 ? 2 : 1);
        return text + new string(' ', Math.Max(0, width - used));
    }
}
