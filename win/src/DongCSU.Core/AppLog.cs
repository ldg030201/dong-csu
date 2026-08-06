using System.Text;

namespace DongCSU.Core;

/// <summary>
/// 파일 한 장에 남기는 기록.
///
/// **화면만 있는 앱은 조용히 실패한다.** 사용량이 안 나올 때 사용자가 볼 수 있는 게
/// "안 나온다"뿐이면 아무도 원인을 못 찾는다. 무엇을 어디서 읽었고 무엇이 실패했는지를
/// 남겨 두면, 로그 한 장만 받아 보면 된다.
///
/// **토큰이나 자격 증명 내용은 절대 남기지 않는다.** 경로와 성공·실패만 적는다.
/// </summary>
public static class AppLog
{
    /// <summary>이만큼 커지면 한 번 갈아엎는다. 켜 둔 채로 며칠 지나도 부담이 없어야 한다.</summary>
    private const long MaxBytes = 512 * 1024;

    private static readonly Lock Gate = new();
    private static string? path;

    public static string DefaultPath => AppPaths.File("log.txt");

    /// <summary>기록을 시작한다. 부르지 않으면 아무것도 남기지 않는다.</summary>
    public static void Start(string? logPath = null)
    {
        lock (Gate)
        {
            path = logPath ?? DefaultPath;
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                // 너무 커졌으면 통째로 버린다. 옛 기록을 보존할 값어치는 없다.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                path = null;
            }
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            if (path is null) return;
            try
            {
                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 기록을 못 남긴다고 앱이 죽으면 안 된다.
            }
        }
    }
}
