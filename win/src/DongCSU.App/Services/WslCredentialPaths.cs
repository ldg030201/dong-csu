using System.IO;
using DongCSU.Core;
using DongCSU.Core.Usage;
using Microsoft.Win32;

namespace DongCSU.App.Services;

/// <summary>
/// WSL 안에서 Claude Code 를 쓰는 사람을 위한 자리.
///
/// **윈도우 홈에는 아무것도 없다.** 리눅스 쪽 홈에 <c>.claude/.credentials.json</c> 이
/// 있고, 윈도우에서는 <c>\\wsl.localhost\&lt;배포판&gt;\...</c> 으로 닿는다.
///
/// **배포판 이름을 모르면 못 연다.** <c>\\wsl.localhost\</c> 자체는 디렉터리로 나열되지
/// 않아서(이름을 알아야 열린다) 목록을 레지스트리에서 읽는다 — WSL 을 깨우지 않고
/// 이름만 알아낼 수 있는 유일한 길이다.
///
/// 그래도 **파일을 실제로 열어 보는 순간 배포판이 깨어난다.** 그래서 이 자리는
/// 윈도우 쪽에서 못 찾았을 때만 본다(<c>FileCredentialSource</c> 의 fallback).
/// </summary>
public static class WslCredentialPaths
{
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    public static IEnumerable<string> All()
    {
        var distros = Distros();
        if (distros.Count == 0) yield break;

        AppLog.Write($"윈도우 쪽에서 못 찾아 WSL 을 본다: {string.Join(", ", distros)}");

        foreach (var distro in distros)
        {
            // 최신 윈도우는 wsl.localhost, 예전 것은 wsl$ 를 쓴다. 둘 다 본다.
            foreach (var prefix in new[] { @"\\wsl.localhost\", @"\\wsl$\" })
            {
                var root = prefix + distro;
                if (!Reachable(root)) continue;

                foreach (var path in FileCredentialSource.WslPathsUnder(root)) yield return path;

                // 한쪽 이름으로 닿았으면 다른 이름은 같은 곳이다.
                break;
            }
        }
    }

    /// <summary>설치된 배포판 이름. 없으면 빈 목록 — WSL 이 안 깔린 기계가 대부분이다.</summary>
    private static List<string> Distros()
    {
        var names = new List<string>();
        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
            if (lxss is null) return names;

            foreach (var id in lxss.GetSubKeyNames())
            {
                using var entry = lxss.OpenSubKey(id);
                if (entry?.GetValue("DistributionName") is string name && name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }
        catch (Exception error) when (error is System.Security.SecurityException
                                          or UnauthorizedAccessException
                                          or IOException)
        {
        }
        return names;
    }

    /// <summary>
    /// 그 이름으로 닿는지. **여기서 배포판이 깨어날 수 있다.**
    ///
    /// 꺼져 있으면 몇 초 걸리기도 해서, 부르는 쪽이 마지막 수단으로만 쓴다.
    /// </summary>
    private static bool Reachable(string root)
    {
        try
        {
            return Directory.Exists(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
