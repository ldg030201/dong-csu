using System.Reflection;
using DongCSU.Core;

namespace DongCSU.App;

public static class AppInfo
{
    /// <summary>
    /// 화면에 보이는 이름. **어셈블리 이름에서 읽는다.**
    ///
    /// 테스트판은 <c>DongCSU-Test</c> 로 빌드되므로, 이 한 값이 갈리면 설정 폴더·
    /// 자동 시작 등록 이름·트레이 메뉴 문구가 함께 갈린다. 맥이 번들 ID 하나로
    /// 모든 것을 가르는 것과 같은 자리다.
    /// </summary>
    public static string Name { get; } =
        Assembly.GetExecutingAssembly().GetName().Name ?? AppPaths.DefaultFolderName;

    /// <summary>
    /// 테스트판인지.
    ///
    /// 조건부 컴파일을 쓰지 않는 이유는 **진단 통로도 같은 답을 봐야 하기 때문**이다.
    /// 이름으로 판정하면 <c>--version</c> 이든 화면이든 늘 같은 값이 나온다.
    /// </summary>
    public static bool IsTestBuild => Name.EndsWith("-Test", StringComparison.Ordinal);

    /// <summary>
    /// 어셈블리에서 읽는다. csproj 의 <c>&lt;Version&gt;</c> 한 곳만 고치면 된다 —
    /// 소스에 또 적어 두면 릴리스할 때 한쪽을 빠뜨린다.
    ///
    /// 조립은 <see cref="AppVersion.Format"/> 이 한다. 네 번째 자리(긴급 수정)를
    /// 버리지 않기 위한 규칙이 거기 테스트와 함께 있다.
    /// </summary>
    public static string Version { get; } =
        AppVersion.Format(Assembly.GetExecutingAssembly().GetName().Version);

    public static string DisplayVersion => $"{Name} {Version}";

    /// <summary>
    /// HUD 왼쪽 위 딱지에 넣을 글자.
    ///
    /// 색만으로 가르지 않는다 — 두 판을 나란히 놓고 비교하는 중이면 색은 눈에 익어
    /// 버리고, 스크린샷으로 남기면 어느 쪽이었는지 알 방법이 사라진다.
    /// </summary>
    public static string BadgeText => IsTestBuild ? $"{Version} test" : Version;
}
