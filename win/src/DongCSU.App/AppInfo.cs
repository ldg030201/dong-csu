using System.Reflection;
using DongCSU.Core;

namespace DongCSU.App;

public static class AppInfo
{
    public const string Name = "DongCSU";

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
}
