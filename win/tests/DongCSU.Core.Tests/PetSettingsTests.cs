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
}
