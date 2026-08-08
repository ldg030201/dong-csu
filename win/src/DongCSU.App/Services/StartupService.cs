using System.IO;
using Microsoft.Win32;

namespace DongCSU.App.Services;

/// <summary>
/// 로그인할 때 저절로 뜨게 한다.
///
/// 맥은 <c>SMAppService</c> 를 쓰지만 윈도우는 레지스트리 <c>Run</c> 키다.
/// **HKEY_CURRENT_USER 라 관리자 권한이 필요 없고 물어보지도 않는다.**
///
/// 맥판과 같은 이유로 값을 따로 저장하지 않는다 — 사용자가 작업 관리자 &gt; 시작 프로그램
/// 에서 끌 수 있어서, 우리가 적어 두면 껐는데도 켜진 것으로 보인다. 항상 레지스트리를
/// 읽는다.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// 등록 이름. **테스트판과 갈라야 한다.**
    ///
    /// 같은 이름을 쓰면 테스트판을 한 번 띄우는 것만으로 정식판의 자동 시작 등록이
    /// 테스트판 경로로 덮어써져서, 다음 로그인 때 사용자가 쓰던 앱 대신 개발 빌드가 뜬다.
    /// </summary>
    private static string ValueName => AppInfo.Name;

    /// <summary>
    /// 켜져 있나. **경로가 달라도 켜진 것으로 본다.**
    ///
    /// 등록이 남아 있으면 윈도우는 그 경로로 띄운다 — 우리가 보기에 옛 경로라고 해서
    /// 사용자에게 "꺼짐"이라고 말하면 거짓말이다. 업데이트로 경로가 바뀐 경우는
    /// <see cref="RepairIfEnabled"/> 가 뜰 때마다 맞춰 주고, 그게 실패하는 환경
    /// (정책으로 잠긴 기계 등)에서는 켜진 채로 옛 경로가 남는다.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string stored && !string.IsNullOrEmpty(stored);
            }
            catch (Exception error) when (error is System.Security.SecurityException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>켜거나 끈다. 성공했으면 true.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (enabled) key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception error) when (error is System.Security.SecurityException
                                          or UnauthorizedAccessException
                                          or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// 켜져 있으면 지금 실행 파일 경로로 다시 써 둔다.
    ///
    /// 업데이트하면 앱이 새 폴더로 들어가는데, 옛 경로가 남아 있으면 **로그인할 때
    /// 아무것도 안 뜬다.** 뜰 때마다 맞춰 둔다.
    /// </summary>
    public static void RepairIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string stored || string.IsNullOrEmpty(stored)) return;
            if (string.Equals(stored, CommandLine, StringComparison.OrdinalIgnoreCase)) return;

            key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
        }
        catch (Exception error) when (error is System.Security.SecurityException
                                          or UnauthorizedAccessException
                                          or IOException)
        {
        }
    }

    /// <summary>경로에 공백이 있으면 따옴표 없이는 잘린다.</summary>
    private static string CommandLine => $"\"{Environment.ProcessPath}\"";
}
