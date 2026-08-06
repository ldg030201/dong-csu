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
    private const string ValueName = "DongCSU";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                var stored = key?.GetValue(ValueName) as string;
                if (string.IsNullOrEmpty(stored)) return false;

                // 앱을 업데이트하면 경로가 바뀐다. 옛 경로가 남아 있으면 켜진 게 아니다.
                return string.Equals(stored, CommandLine, StringComparison.OrdinalIgnoreCase);
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
