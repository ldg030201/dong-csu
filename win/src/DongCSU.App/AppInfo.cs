using System.Reflection;

namespace DongCSU.App;

public static class AppInfo
{
    public const string Name = "DongCSU";

    /// <summary>
    /// 어셈블리에서 읽는다. csproj 의 <c>&lt;Version&gt;</c> 한 곳만 고치면 된다 —
    /// 소스에 또 적어 두면 릴리스할 때 한쪽을 빠뜨린다.
    /// </summary>
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";

    public static string DisplayVersion => $"{Name} {Version}";
}
