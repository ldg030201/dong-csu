using System.Text.Json;
using System.Text.Json.Serialization;

namespace DongCSU.Core;

/// <summary>
/// HUD 를 얼마나 보여줄지.
///
/// <see cref="Pet"/> 은 배경도 숫자도 없이 마스코트만 남기는 보기다. 이름으로 저장하므로
/// 값이 늘어도 옛 <c>settings.json</c> 은 그대로 읽힌다.
/// </summary>
public enum HudMode { Expanded, Collapsed, Pet }

public enum HudTheme { System, Light, Dark }

public enum HudScale { Small, Normal, Large, ExtraLarge }

public enum HudExpandSide { Right, Left }

/// <summary>펫 모드에서 뒤에 두르는 사용량 링을 언제 보여줄지.</summary>
public enum PetRingDisplay { Hover, Always, Never }

public static class HudModeExtensions
{

    /// <summary>
    /// 둥근 배경을 깔지.
    ///
    /// **펫은 안 깐다.** 마스코트만 떠 있어야 하는데 배경이 있으면 네모가 따라다닌다.
    /// 모서리를 깎을 일도 없어진다 — 남겨 두면 링 가장자리가 잘린다.
    /// </summary>
    public static bool ShowsBackdrop(this HudMode mode) => mode != HudMode.Pet;

    public static string Title(this PetRingDisplay display) => display switch
    {
        PetRingDisplay.Always => "항상 표시",
        PetRingDisplay.Never => "표시 안 함",
        _ => "마우스를 올리면",
    };
}

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

    /// <summary>링 한가운데에 그릴 그림. 부엉이 둘 말고는 정지 그림이다.</summary>
    public IconStyle IconStyle { get; set; } = IconStyle.OwlSheet;

    /// <summary>
    /// 그림 부엉이로 한 번 옮겼는지. **한 번만 옮긴다** — 두 번 옮기면 그 사이에
    /// 오리지널로 되돌려 놓은 사람의 선택을 매번 덮는다.
    ///
    /// 기본값만 바꿔서는 아무도 안 바뀐다. 설정 파일에 <c>IconStyle</c> 이 이미 적혀
    /// 있어서, "고른 것"과 "그냥 깔린 것"을 가릴 방법이 없기 때문이다. 오리지널을
    /// 일부러 쓰던 사람은 아이콘 탭의 접힌 묶음에서 한 번에 되돌린다.
    /// </summary>
    public bool MovedToSheetOwl { get; set; }

    /// <summary>조회 주기(초). 너무 조이면 429 가 난다. **맥과 같은 10분이다.**</summary>
    public int PollIntervalSeconds { get; set; } = 600;

    public bool IsHudVisible { get; set; } = true;
    public bool ShowsVersionBadge { get; set; } = true;
    public bool ChecksForUpdates { get; set; } = true;

    /// <summary>
    /// HUD 아래에 이 앱 자신의 CPU·메모리를 붙일지. **기본은 꺼짐이다.**
    ///
    /// 대부분은 궁금해하지 않고, 켜면 카드가 17만큼 길어진다. 항상 떠 있는 앱이
    /// 얼마나 먹는지 확인하고 싶을 때만 켠다.
    /// </summary>
    public bool ShowsProcessStats { get; set; }

    /// <summary>
    /// 마스코트를 움직일지. 끄면 평소 자세로 멈추되 기분에 따른 색은 그대로다.
    ///
    /// 움직임이 거슬리거나 노트북에서 배터리를 아끼고 싶을 때 쓴다.
    /// </summary>
    public bool AnimatesMascot { get; set; } = true;

    /// <summary>
    /// 배경 불투명도. **맥과 같은 0.92 다.**
    ///
    /// 값 자체는 그대로 두고 <see cref="Backdrop"/> 에서 잘라 쓴다. 파일을 손으로
    /// 고쳐 0.05 같은 값을 넣으면 글자가 배경에 묻혀 아무것도 안 읽힌다.
    /// </summary>
    public double BackdropOpacity { get; set; } = DefaultBackdropOpacity;

    public const double DefaultBackdropOpacity = 1.0;
    public const double MinBackdropOpacity = 0.35;

    // ── 펫 모드 ───────────────────────────────────────────────────

    /// <summary>
    /// 펫에서 나올 때 돌아갈 보기.
    ///
    /// **맥은 이걸 저장하지 않는다** — UserDefaults 에 굳이 넣을 값이 아니라고 봤다.
    /// 그래서 펫 상태로 껐다 켜면 복귀 지점이 펼침으로 초기화된다. 파일 설정에서는
    /// 한 줄 더 적는 것이 공짜라 저장한다. 의도한 차이다.
    /// </summary>
    public HudMode ModeBeforePet { get; set; } = HudMode.Expanded;

    public PetRingDisplay PetRingDisplay { get; set; } = PetRingDisplay.Hover;

    /// <summary>혼자 돌아다닐지.</summary>
    public bool PetWanders { get; set; } = true;

    /// <summary>커서가 위에 머물면 비켜설지.</summary>
    public bool PetDodgesCursor { get; set; } = true;

    /// <summary>창 위치. 처음에는 없다 — 그때는 오른쪽 위에 붙인다.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    [JsonIgnore]
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(PollIntervalSeconds, 60, 1800));

    /// <summary>실제로 그릴 때 쓸 불투명도. 글자가 읽히는 아래쪽에서 막는다.</summary>
    [JsonIgnore]
    public double Backdrop => Math.Clamp(BackdropOpacity, MinBackdropOpacity, 1.0);

    // ── 저장 ──────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><c>%APPDATA%\DongCSU\settings.json</c>. 테스트판은 폴더가 다르다.</summary>
    public static string DefaultPath => AppPaths.File("settings.json");

    /// <summary>읽는다. 없거나 깨졌으면 기본값 — 던지지 않는다.</summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options)
                ?? new AppSettings();
            loaded.MoveToSheetOwlOnce();
            return loaded;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // 설정 파일이 깨졌다고 앱이 안 뜨면 안 된다. 기본값으로 계속 간다.
            return new AppSettings();
        }
    }

    /// <summary>
    /// 쓰던 사람도 새 부엉이로 한 번 옮긴다. 이미 옮겼거나 다른 그림을 골라 뒀으면
    /// 아무것도 안 한다.
    /// </summary>
    internal void MoveToSheetOwlOnce()
    {
        if (MovedToSheetOwl) return;
        MovedToSheetOwl = true;
        if (IconStyle == IconStyle.Owl) IconStyle = IconStyle.OwlSheet;
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
