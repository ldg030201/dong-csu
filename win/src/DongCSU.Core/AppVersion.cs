namespace DongCSU.Core;

/// <summary>
/// 버전 문자열을 만들고 견준다.
///
/// **네 번째 자리를 버리면 안 된다.** 이 저장소는 긴급 수정에 네 번째 자리를 쓴다
/// (`1.0.0.1`). 세 자리로만 조립하면 앱이 자기를 `1.0.0` 이라고 말하게 되고,
/// 그러면 업데이트를 마친 뒤에도 "새 버전 1.0.0.1 이 있다"가 영영 사라지지 않는다.
///
/// 화면이 없는 로직이라 여기 둔다 — WPF 쪽에 두면 맥에서 테스트할 수 없다.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// 어셈블리 버전을 사람이 읽는 문자열로.
    ///
    /// 네 번째 자리는 **있을 때만** 붙인다. 늘 붙이면 평범한 `1.2.0` 이 `1.2.0.0` 으로
    /// 보여서, 긴급 수정이 붙은 버전과 눈으로 구분되지 않는다.
    /// </summary>
    public static string Format(Version? version)
    {
        if (version is null) return "0.0.0";

        var build = Math.Max(0, version.Build);
        var basePart = $"{version.Major}.{version.Minor}.{build}";
        return version.Revision > 0 ? $"{basePart}.{version.Revision}" : basePart;
    }

    /// <summary><paramref name="candidate"/> 가 <paramref name="current"/> 보다 새 버전인가.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out var next) || !TryParse(current, out var now)) return false;
        return next > now;
    }

    /// <summary>
    /// 버전 문자열을 읽는다.
    ///
    /// Velopack 이 돌려주는 문자열에는 `1.0.0+abc` 처럼 빌드 꼬리표가 붙을 수 있다.
    /// 그대로 넘기면 파싱이 실패해서 **새 버전이 있어도 없는 것처럼 조용히 넘어간다.**
    /// </summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim().TrimStart('v', 'V');
        foreach (var separator in new[] { '+', '-' })
        {
            var at = trimmed.IndexOf(separator);
            if (at > 0) trimmed = trimmed[..at];
        }

        return Version.TryParse(trimmed, out version!);
    }
}
