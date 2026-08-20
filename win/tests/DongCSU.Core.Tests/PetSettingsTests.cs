using System.Reflection;
using System.Text.Json.Serialization;
using DongCSU.Core;

namespace DongCSU.Core.Tests;

public class PetSettingsTests
{
    [Fact]
    public void 펫_설정을_저장하고_다시_읽는다()
    {
        using var temporary = new TemporaryFile();
        new AppSettings
        {
            Mode = HudMode.Pet,
            ModeBeforePet = HudMode.Collapsed,
            PetRingDisplay = PetRingDisplay.Always,
            PetWanders = false,
            PetDodgesCursor = false,
        }.Save(temporary.Path);

        var loaded = AppSettings.Load(temporary.Path);

        Assert.Equal(HudMode.Pet, loaded.Mode);
        Assert.Equal(HudMode.Collapsed, loaded.ModeBeforePet);
        Assert.Equal(PetRingDisplay.Always, loaded.PetRingDisplay);
        Assert.False(loaded.PetWanders);
        Assert.False(loaded.PetDodgesCursor);
    }

    /// <summary>
    /// 이미 쓰던 사람의 파일에는 펫 항목이 없다. 그것 때문에 설정이 통째로
    /// 초기화되면 안 된다.
    /// </summary>
    [Fact]
    public void 펫_항목이_없는_옛_파일도_읽힌다()
    {
        using var temporary = new TemporaryFile();
        File.WriteAllText(temporary.Path, """
            {
              "Mode": "Collapsed",
              "Theme": "Dark",
              "PollIntervalSeconds": 300,
              "BackdropOpacity": 0.72
            }
            """);

        var loaded = AppSettings.Load(temporary.Path);

        Assert.Equal(HudMode.Collapsed, loaded.Mode);
        Assert.Equal(HudTheme.Dark, loaded.Theme);

        // 없던 항목은 기본값으로 채워진다.
        Assert.Equal(HudMode.Expanded, loaded.ModeBeforePet);
        Assert.Equal(PetRingDisplay.Hover, loaded.PetRingDisplay);
        Assert.True(loaded.PetWanders);
        Assert.True(loaded.PetDodgesCursor);
    }

    /// <summary>값이 아니라 이름으로 저장한다 — 나중에 항목을 끼워 넣어도 안 어긋난다.</summary>
    [Fact]
    public void 모드를_이름으로_저장한다()
    {
        using var temporary = new TemporaryFile();
        new AppSettings { Mode = HudMode.Pet }.Save(temporary.Path);

        Assert.Contains("\"Pet\"", File.ReadAllText(temporary.Path));
    }

    [Fact]
    public void 펫만_배경을_깔지_않는다()
    {
        Assert.True(HudMode.Expanded.ShowsBackdrop());
        Assert.True(HudMode.Collapsed.ShowsBackdrop());
        Assert.False(HudMode.Pet.ShowsBackdrop());
    }

    [Fact]
    public void 불투명도는_읽히지_않는_아래쪽에서_막힌다()
    {
        Assert.Equal(AppSettings.MinBackdropOpacity, new AppSettings { BackdropOpacity = 0.01 }.Backdrop);
        Assert.Equal(1.0, new AppSettings { BackdropOpacity = 5 }.Backdrop);
        Assert.Equal(0.5, new AppSettings { BackdropOpacity = 0.5 }.Backdrop);
    }

    // ── 초기화 ──────────────────────────────────────────────────────

    /// <summary>
    /// 설정 창이 되돌릴 목록을 손으로 적던 시절 <c>PetHidesRingWhileHeld</c> 가 빠져
    /// 있었다. 그 한 줄이 실제로 돌아오는지 못 박는다.
    /// </summary>
    [Fact]
    public void 초기화하면_손으로_적지_않은_설정도_돌아온다()
    {
        var settings = new AppSettings
        {
            PetHidesRingWhileHeld = false,
            Mode = HudMode.Pet,
            Scale = HudScale.ExtraLarge,
            PetWanders = false,
            BackdropOpacity = 0.5,
            WindowLeft = 12,
            WindowTop = 34,
        };

        settings.ResetToDefaults();

        Assert.True(settings.PetHidesRingWhileHeld);
        Assert.Equal(HudMode.Expanded, settings.Mode);
        Assert.Equal(HudScale.Normal, settings.Scale);
        Assert.True(settings.PetWanders);
        Assert.Equal(AppSettings.DefaultBackdropOpacity, settings.BackdropOpacity);
        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
    }

    /// <summary>
    /// 이주 표식까지 되돌리면 다음 실행의 <c>Load</c> 가 또 옮긴다 — 초기화한 뒤
    /// 오리지널 부엉이를 고른 사람이 다음 실행에 그림 부엉이로 끌려간다.
    /// </summary>
    [Fact]
    public void 초기화해도_이주_표식은_남는다()
    {
        var settings = new AppSettings { MovedToSheetOwl = true, IconStyle = IconStyle.Owl };

        settings.ResetToDefaults();

        Assert.True(settings.MovedToSheetOwl);
    }

    /// <summary>
    /// 되돌릴 목록을 사람이 적지 않으므로, 설정을 하나 더해도 이 검사가 저절로 따라온다.
    /// 저장되는 속성 전부에 기본값과 **다른** 값을 넣고 하나씩 견준다.
    /// </summary>
    [Fact]
    public void 초기화가_빠뜨린_설정이_없다()
    {
        // 이주 표식만 일부러 남는다. AppSettings.KeptOnReset 과 같은 목록이다.
        string[] kept = [nameof(AppSettings.MovedToSheetOwl)];

        var settings = new AppSettings();
        var saved = Savable().ToArray();
        Assert.NotEmpty(saved);

        foreach (var property in saved)
        {
            property.SetValue(settings, Different(property.PropertyType, property.GetValue(settings)));
            Assert.NotEqual(property.GetValue(new AppSettings()), property.GetValue(settings));
        }

        settings.ResetToDefaults();

        var fresh = new AppSettings();
        foreach (var property in saved)
        {
            if (kept.Contains(property.Name)) continue;
            Assert.Equal(property.GetValue(fresh), property.GetValue(settings));
        }
    }

    /// <summary>파일에 적히는 — 그러니까 초기화가 되돌려야 하는 — 속성들.</summary>
    private static IEnumerable<PropertyInfo> Savable() =>
        typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() is null);

    /// <summary>지금 값과 다른 값 하나. 타입마다 어긋나게만 만들면 된다.</summary>
    private static object Different(Type type, object? current) => type switch
    {
        _ when type == typeof(bool) => !(bool)current!,
        _ when type == typeof(int) => (int)current! + 1,
        _ when type == typeof(double) => (double)current! + 0.123,
        // double? 은 처음에 비어 있다(창 위치). 숫자를 넣으면 그것만으로 다르다.
        _ when type == typeof(double?) => 123.0,
        _ when type.IsEnum => Enum.GetValues(type).Cast<object>().First(v => !v.Equals(current)),
        _ => throw new NotSupportedException($"{type} 을 어떻게 어긋내야 할지 모른다"),
    };

    /// <summary>보던 탭은 창을 닫았다 열 때까지만 산다 — 앱을 껐다 켜면 상태 탭이다.</summary>
    [Fact]
    public void 보던_탭은_파일에_남지_않는다()
    {
        using var temporary = new TemporaryFile();
        new AppSettings { SettingsTab = "pet" }.Save(temporary.Path);

        Assert.DoesNotContain("SettingsTab", File.ReadAllText(temporary.Path));
        Assert.Equal("status", AppSettings.Load(temporary.Path).SettingsTab);
    }
}
