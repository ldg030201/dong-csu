using System.Text.Json;

namespace DongCSU.Core.Usage;

/// <summary>
/// 측정 상태를 <c>meter.json</c> 한 장에 통째로 넣고 뺀다.
///
/// **설정 파일(<c>settings.json</c>)에 섞지 않는 이유가 둘이다.** 중복 제거용 id 가 몇천
/// 개까지 쌓이는 데이터라 설정과 성격이 다르고, 설정을 통째로 되돌리는 "모든 설정
/// 초기화"에 딸려 지워지면 곤란하다 — 초기화는 설정을 되돌리는 것이지 재던 것을 버리는
/// 게 아니다.
///
/// 읽기도 쓰기도 **실패해도 던지지 않는다.** 기록을 못 읽었다고 앱이 안 뜨는 것보다
/// 처음부터 재는 편이 낫다.
/// </summary>
public sealed class MeterStore
{
    /// <summary>
    /// **PascalCase · 들여쓰기 없음.**
    ///
    /// 나가는 피드(<c>changelog.json</c>)와 달리 이건 우리만 읽는 내부 상태 파일이라
    /// 이름 규칙을 맞출 상대가 없고, <c>SeenIds</c> 가 수천 줄이라 들여쓰기는 파일만 불린다.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly string path;

    /// <param name="path">안 주면 <see cref="DefaultPath"/>. 검사가 임시 파일을 꽂는 자리다.</param>
    public MeterStore(string? path = null) => this.path = path ?? DefaultPath;

    /// <summary>
    /// <c>%APPDATA%\DongCSU\meter.json</c>.
    ///
    /// **<see cref="AppPaths.SharedFile"/> 가 아니라 <see cref="AppPaths.File"/> 다** —
    /// 함께 쓰는 것은 자격 증명(<c>token.json</c>)뿐이고, 측정 기록은 사용자의 것이 아니라
    /// 앱의 데이터라 판마다 갈리는 것이 맞다. 테스트판이 정식판 기록을 덮으면 안 된다.
    ///
    /// **굳히지 않고 매번 계산한다.** <c>AppPaths.UseFolder</c> 가 <c>Program.Main</c> 초입에서
    /// 폴더를 갈아 끼우는데, <c>static readonly</c> 로 잡아 두면 그보다 먼저 굳어 테스트판이
    /// 정식판 파일을 쓴다.
    /// </summary>
    public static string DefaultPath => AppPaths.File("meter.json");

    /// <summary>없거나 깨졌으면 null. 그때는 부르는 쪽이 빈 상태로 시작한다.</summary>
    public MeterState? Read()
    {
        try
        {
            if (!File.Exists(path)) return null;

            var state = JsonSerializer.Deserialize<MeterState>(File.ReadAllText(path), Options);
            // **읽은 직후에 반드시 다시 싼다.** 직렬화를 건너온 사전은 비교자가 기본값이라,
            // 대소문자만 다른 경로가 새 항목이 되어 같은 파일을 처음부터 다시 센다.
            return state?.Copy();
        }
        catch (Exception error) when (error is IOException or JsonException
            or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public void Write(MeterState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && AppPaths.Prepared(directory) is null) return;

            // 쓰는 도중에 앱이 죽으면 반쯤 쓰인 JSON 이 남는다. 옆에 쓰고 바꿔치기한다.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or JsonException
            or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // 다음 변화에서 다시 쓴다. 못 남겼다고 재던 것을 버릴 이유는 없다.
        }
    }
}
