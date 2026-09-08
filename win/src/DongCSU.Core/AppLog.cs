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

    /// <summary>
    /// <see cref="Start"/> 를 불렀는지. <b><see cref="path"/> 와 갈라 둔다.</b>
    ///
    /// 하나로 묶어 두면 시작할 때 한 번 실패한 것과 아예 안 부른 것이 같아져서,
    /// <b>그 프로세스는 죽을 때까지 한 줄도 안 남긴다.</b> 실제로 그렇게 됐다 —
    /// 앱이 멀쩡히 떠서 돌고 있는데 기록만 통째로 비어 있었고, 그래서 무슨 일이
    /// 있었는지 짚을 수가 없었다. 잠깐 막힌 것이라면 다음 줄에서 다시 된다.
    /// </summary>
    private static bool started;

    public static string DefaultPath => AppPaths.File("log.txt");

    /// <summary>기록을 시작한다. 부르지 않으면 아무것도 남기지 않는다.</summary>
    public static void Start(string? logPath = null)
    {
        lock (Gate)
        {
            path = logPath ?? DefaultPath;
            started = true;
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                // 너무 커졌으면 통째로 버린다. 옛 기록을 보존할 값어치는 없다.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // **자리는 그대로 둔다.** 폴더가 잠깐 잠겼거나 다른 프로세스가 붙들고
                // 있었을 뿐일 수 있어서, 여기서 꺼 버리면 그 뒤로 영영 안 남는다.
                // 진짜로 못 쓰는 자리면 `Write` 가 매번 조용히 실패할 뿐이다.
            }
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            if (!started || path is null) return;
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
