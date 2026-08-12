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
}
