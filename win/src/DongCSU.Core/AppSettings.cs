using System.Text.Json;
using System.Text.Json.Serialization;

namespace DongCSU.Core;

public enum HudMode { Expanded, Collapsed }

public enum HudTheme { System, Light, Dark }

public enum HudScale { Small, Normal, Large, ExtraLarge }

public enum HudExpandSide { Right, Left }

public static class HudScaleExtensions
{
    public static double Factor(this HudScale scale) => scale switch
    {
        HudScale.Small => 0.85,
        HudScale.Large => 1.25,
        HudScale.ExtraLarge => 1.5,
        _ => 1.0,
    };

    public static string Title(this HudScale scale) => scale switch
    {
        HudScale.Small => "작게",
        HudScale.Large => "크게",
        HudScale.ExtraLarge => "매우 크게",
        _ => "보통",
    };
}

/// <summary>
/// 다음 실행까지 기억할 값.
///
/// 맥은 UserDefaults 를 쓰지만 윈도우에는 그런 게 없다. 레지스트리 대신 **JSON 파일**을
/// 쓴다 — 사용자가 열어서 볼 수 있고, 앱을 지우면 같이 사라지며, 백업하기도 쉽다.
/// </summary>
public sealed class AppSettings
{
    public HudMode Mode { get; set; } = HudMode.Expanded;
    public HudTheme Theme { get; set; } = HudTheme.System;
    public HudScale Scale { get; set; } = HudScale.Normal;
    public HudExpandSide ExpandSide { get; set; } = HudExpandSide.Right;

    /// <summary>조회 주기(초). 너무 조이면 429 가 난다.</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    public bool IsHudVisible { get; set; } = true;
    public bool ShowsVersionBadge { get; set; } = true;
    public bool ChecksForUpdates { get; set; } = true;
    public bool StartsAtLogin { get; set; }

    public double BackdropOpacity { get; set; } = 0.72;

    /// <summary>창 위치. 처음에는 없다 — 그때는 오른쪽 위에 붙인다.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    [JsonIgnore]
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(PollIntervalSeconds, 60, 1800));

    // ── 저장 ──────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><c>%APPDATA%\DongCSU\settings.json</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DongCSU",
        "settings.json");

    /// <summary>읽는다. 없거나 깨졌으면 기본값 — 던지지 않는다.</summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options)
                ?? new AppSettings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // 설정 파일이 깨졌다고 앱이 안 뜨면 안 된다. 기본값으로 계속 간다.
            return new AppSettings();
        }
    }

    /// <summary>
    /// 저장한다. 실패해도 던지지 않는다.
    ///
    /// **임시 파일에 쓰고 바꿔치기한다.** 쓰는 도중에 앱이 죽으면 반쯤 쓰인 JSON 이
    /// 남아서 다음 실행 때 설정이 통째로 초기화된다.
    /// </summary>
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
