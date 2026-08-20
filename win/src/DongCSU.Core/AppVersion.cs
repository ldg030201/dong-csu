using System.Text.RegularExpressions;

namespace DongCSU.Core;

/// <summary>
/// 버전 문자열을 만들고 견준다.
///
/// **네 번째 자리는 맥의 규칙이다.** 공통 규칙에 긴급 수정용 네 번째 자리(`1.0.0.1`)가
/// 있지만 **윈도우 앱 자기 버전에는 그 번호가 올 수 없다.** 버전은
/// `DongCSU.App.csproj` 의 `&lt;Version&gt;` 한 곳에서 오고 그 값은 릴리스 워크플로가
/// 태그에서 넘기는데, 설치본이 NuGet 패키지 형식이라 SemVer2 **세 자리**여야 해서
/// 거기서 막힌다 (<see cref="IsThreePart"/>). 자세한 건 `win/CLAUDE.md` 의
/// "네 번째 자리를 쓸 수 없다" 절에 있다.
///
/// **그래도 네 자리 처리를 지우지 않는다.** <see cref="TryParse"/> 와
/// <see cref="IsNewer"/> 는 **밖에서 온 문자열**을 읽는 자리다 — 업데이트 피드,
/// 변경 내역 피드, 맥 쪽 목록이 섞여 들어올 수도 있다. 네 자리를 못 읽으면
/// <see cref="TryParse"/> 가 false 를 돌려 <see cref="IsNewer"/> 가 조용히
/// "새 버전 없음"이 된다. **화면에 아무 오류도 안 뜨는 종류의 실패**라 특히 나쁘다.
///
/// 화면이 없는 로직이라 여기 둔다 — WPF 쪽에 두면 맥에서 테스트할 수 없다.
/// 그리고 이 `Core` 는 맥에서도 컴파일되는 공용 코드라, **윈도우 사정만으로
/// 잘라 낼 자리가 아니다.**
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// 릴리스 버전으로 쓸 수 있는 세 자리인가. `1.2.3` 만 참이고 `1.0.0.1`·`1.0` 은 거짓이다.
    ///
    /// 설치본이 NuGet 패키지 형식이라 버전이 SemVer2 세 자리여야 하고, 네 자리를 밀면
    /// `vpk pack` 이 'it must be a 3-part SemVer2 compliant version string' 으로 죽는다.
    /// **그때는 태그가 이미 원격에 올라간 뒤라 되돌리기가 비싸다** — 그래서 릴리스
    /// 워크플로 첫 단계와 `--check-release` 가 미리 이걸로 걸러 낸다.
    ///
    /// <see cref="TryParse"/> 위에 얹지 않고 문자열을 그대로 본다. 그쪽은 밖에서 온
    /// 것을 너그럽게 읽어 주는 자리라 `v1.2.3`·`1.2.3+build` 까지 통과시키는데,
    /// 그런 문자열은 릴리스 버전에 올 수 없다. 자릿수 판정은 엄해야 한다.
    ///
    /// 자릿수 규칙을 네 자리를 다루는 코드 바로 옆에 두려고 여기 둔다.
    /// </summary>
    // `\d` 는 .NET 에서 전각 숫자까지 잡으므로 `[0-9]` 로 못 박는다.
    public static bool IsThreePart(string? text)
    {
        if (text is null) return false;
        return Regex.IsMatch(text.Trim(), @"^[0-9]+\.[0-9]+\.[0-9]+$");
    }

    /// <summary>
    /// 어셈블리 버전을 사람이 읽는 문자열로.
    ///
    /// 네 번째 자리는 **있을 때만** 붙인다. 늘 붙이면 평범한 `1.2.0` 이 `1.2.0.0` 으로
    /// 보여서, 긴급 수정이 붙은 버전과 눈으로 구분되지 않는다.
    ///
    /// **앱 제 버전으로는 세 자리만 온다** — 네 자리 분기는 남의 버전(피드에서 읽어 온
    /// 문자열, 맥 쪽 번호)을 <see cref="TryParse"/> 로 읽어 그대로 보여줄 때를 위한 것이다.
    /// </summary>
    public static string Format(Version? version)
    {
        if (version is null) return "0.0.0";

        var build = Math.Max(0, version.Build);
        var basePart = $"{version.Major}.{version.Minor}.{build}";
        return version.Revision > 0 ? $"{basePart}.{version.Revision}" : basePart;
    }

    /// <summary>
    /// <paramref name="candidate"/> 가 <paramref name="current"/> 보다 새 버전인가.
    ///
    /// **여기로는 네 자리가 실제로 들어온다** — 견주는 대상이 밖에서 온 번호다.
    /// </summary>
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
    ///
    /// **여기로는 네 자리가 실제로 들어온다.** 윈도우 앱이 제 번호로 달지 못할 뿐,
    /// 읽어야 하는 문자열에는 얼마든지 나온다.
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
