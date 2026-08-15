using System.Security.AccessControl;
using System.Security.Principal;

namespace DongCSU.Core;

/// <summary>
/// 앱이 쓰는 파일이 어디로 가는지.
///
/// **테스트판과 정식판이 갈리는 자리는 여기 하나다.** 설정·기록·갱신한 토큰이
/// 저마다 경로를 계산하면, 판을 가를 때 한 곳을 빠뜨려서 **테스트판이 정식판의
/// 토큰이나 창 위치를 덮어쓴다.** 맥은 번들 ID 하나만 바꾸면 UserDefaults 도메인이
/// 통째로 갈리지만, 윈도우에는 그런 게 없어서 우리가 직접 갈라야 한다.
/// </summary>
public static class AppPaths
{
    public const string DefaultFolderName = "DongCSU";

    private static string folderName = DefaultFolderName;


    /// <summary>
    /// 폴더를 갈아 끼운다. **뜨자마자, 무엇이든 읽거나 쓰기 전에 부른다.**
    ///
    /// 늦게 부르면 앞서 읽은 것은 옛 폴더에서 온 것이라 두 폴더가 섞인다.
    /// </summary>
    public static void UseFolder(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return;

        // 경로 구분자가 섞여 들어오면 %APPDATA% 밖으로 나간다.
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            if (trimmed.Contains(invalid)) return;
        }

        folderName = trimmed;
    }

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        folderName);

    public static string File(string name) => Path.Combine(Root, name);

    /// <summary>
    /// 판이 갈려도 **함께 쓰는** 자리.
    ///
    /// 갱신한 토큰이 여기 들어간다. 판마다 따로 두면 안 된다 — 서버가 갱신할 때마다
    /// 리프레시 토큰을 회전시키기 때문에, 두 판이 각자 갱신하면 **서로가 들고 있는
    /// 토큰을 죽여서** 둘 다 재로그인으로 떨어진다. 자격 증명은 앱의 상태가 아니라
    /// 사용자의 것이고, 맥도 두 판이 같은 키체인 항목을 읽는다.
    /// </summary>
    public static string SharedFile(string name) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        DefaultFolderName,
        name);

    /// <summary>
    /// 폴더를 만들고 **본인 말고는 못 열게 해 둔다.** 갱신한 토큰이 여기 들어간다.
    ///
    /// **폴더부터 본다.** 파일 권한은 파일을 쓰고 난 뒤에야 바꿀 수 있어서 그 사이가
    /// 잠깐 열리는데, 폴더가 닫혀 있으면 그 틈에도 남이 들어오지 못한다.
    ///
    /// **평소에는 아무것도 안 한다.** <c>%APPDATA%</c> 가 물려주는 권한은 이미
    /// {본인 · SYSTEM · Administrators} 셋뿐이고, 그건 유닉스의 <c>0700</c> 과 같다
    /// (거기서도 root 는 그냥 읽는다). 상속을 무턱대고 끊으면 **로밍 프로필 동기화와
    /// 백업이 깨진다** — 얻는 것 없이 잃기만 한다.
    ///
    /// 조이는 것은 **남이 끼어 있을 때뿐이다.** 옛 판이 만들어 둔 폴더나 손으로 권한을
    /// 늘려 놓은 자리가 그렇다. 그때는 상속을 끊고 셋만 남긴다.
    ///
    /// 못 조여도 던지지 않는다. 권한을 못 바꾸는 환경(정책으로 잠긴 기계)에서 앱이
    /// 안 뜨는 것보다는 낫다.
    /// </summary>
    /// <returns>준비된 폴더 경로. 만들지도 못했으면 null.</returns>
    public static string? Prepared(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (OperatingSystem.IsWindows()) TightenIfShared(folder);
        return folder;
    }

    /// <summary>낯선 권한이 끼어 있으면 지운다. 위 <see cref="Prepared"/> 의 뒷부분이다.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TightenIfShared(string folder)
    {
        try
        {
            var info = new DirectoryInfo(folder);
            var security = info.GetAccessControl();
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));

            var me = WindowsIdentity.GetCurrent().User;
            if (me is null) return;

            var allowed = new HashSet<SecurityIdentifier>([
                me,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            ]);

            var strangers = rules
                .Cast<FileSystemAccessRule>()
                .Where(rule => rule.AccessControlType == AccessControlType.Allow)
                .Select(rule => rule.IdentityReference)
                .OfType<SecurityIdentifier>()
                .Where(sid => !allowed.Contains(sid))
                .Distinct()
                .ToList();

            if (strangers.Count == 0) return;

            // 남이 끼어 있다. 상속을 끊고 셋만 남긴다.
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            foreach (var sid in strangers)
            {
                security.PurgeAccessRules(sid);
            }
            info.SetAccessControl(security);

            AppLog.Write($"토큰 폴더에서 낯선 권한 {strangers.Count}개를 지웠다: {folder}");
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException or IOException
                or PlatformNotSupportedException or InvalidOperationException)
        {
            // 못 조였다. 파일은 그대로 쓴다 — 앱이 안 뜨는 것보다는 낫다.
        }
    }
}
