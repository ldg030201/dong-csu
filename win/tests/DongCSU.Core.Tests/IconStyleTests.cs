using DongCSU.Core;

namespace DongCSU.Core.Tests;

/// <summary>
/// 격자 부엉이(<c>Owl</c>)와 시트 부엉이(<c>OwlSheet</c>)가 **둘 다 남아 있는지.**
///
/// 맥 2.4.0 이 새 그림을 들이면서 옛것을 지우지 않고 접어 뒀다. 여기서 하나로 합치면
/// 오리지널을 일부러 고른 사람이 되돌릴 자리가 없어진다.
/// </summary>
public class IconStyleTests
{
    [Fact]
    public void 쓰던_사람을_시트_부엉이로_한_번_옮긴다()
    {
        using var temporary = new TemporaryFile();
        new AppSettings { IconStyle = IconStyle.Owl }.Save(temporary.Path);

        var loaded = AppSettings.Load(temporary.Path);

        Assert.Equal(IconStyle.OwlSheet, loaded.IconStyle);
        Assert.True(loaded.MovedToSheetOwl);
    }

    /// <summary>
    /// 옮긴 뒤에 오리지널로 되돌려 놓았으면 그대로 둔다. 매번 옮기면 고른 것이
    /// 켤 때마다 덮인다.
    /// </summary>
    [Fact]
    public void 되돌려_놓은_오리지널을_다시_덮지_않는다()
    {
        using var temporary = new TemporaryFile();
        new AppSettings { IconStyle = IconStyle.Owl, MovedToSheetOwl = true }.Save(temporary.Path);

        Assert.Equal(IconStyle.Owl, AppSettings.Load(temporary.Path).IconStyle);
    }

    /// <summary>부엉이가 아닌 것을 골라 뒀으면 건드리지 않는다.</summary>
    [Fact]
    public void 다른_그림을_골라_뒀으면_그대로다()
    {
        using var temporary = new TemporaryFile();
        new AppSettings { IconStyle = IconStyle.Clawd }.Save(temporary.Path);

        Assert.Equal(IconStyle.Clawd, AppSettings.Load(temporary.Path).IconStyle);
    }

    /// <summary>둘 다 자세가 있는 그림이다. 정지 그림은 Claude 쪽뿐이다.</summary>
    [Fact]
    public void 부엉이는_둘_다_움직인다()
    {
        Assert.True(IconStyle.Owl.IsAnimated());
        Assert.True(IconStyle.OwlSheet.IsAnimated());
        Assert.False(IconStyle.Clawd.IsAnimated());
    }

    /// <summary>
    /// 저장된 설정이 이름이 아니라 **숫자**라, 목록 가운데에 끼우면 이미 저장된 값이
    /// 다른 그림을 가리킨다. 새것은 끝에 붙인다.
    /// </summary>
    [Fact]
    public void 옛_설정의_숫자가_가리키던_그림이_그대로다()
    {
        Assert.Equal(0, (int)IconStyle.Owl);
        Assert.Equal(1, (int)IconStyle.Clawd);
        Assert.Equal(2, (int)IconStyle.AppIcon);
        Assert.Equal(3, (int)IconStyle.Mark);
    }
}
