using DongCSU.Core;

namespace DongCSU.Core.Tests;

/// <summary>
/// 격자 부엉이(<c>Owl</c>)와 시트 부엉이(<c>OwlSheet</c>)가 **둘 다 남아 있는지**,
/// 그리고 **시트로 도는 캐릭터의 표가 맞는지.**
///
/// 맥 2.4.0 이 새 그림을 들이면서 옛것을 지우지 않고 접어 뒀다. 여기서 하나로 합치면
/// 오리지널을 일부러 고른 사람이 되돌릴 자리가 없어진다.
///
/// **그림 자체는 여기서 못 본다** — 시트는 <c>App</c> 의 EmbeddedResource 이고 이
/// 테스트는 <c>Core</c> 만 본다. 그림이 실제로 박혔는지는 <c>--probe-mascot</c> 이 본다.
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

    /// <summary>라쿤을 골라 둔 사람을 시트 부엉이로 끌어오지 않는다.</summary>
    [Fact]
    public void 라쿤을_골라_뒀으면_그대로다()
    {
        using var temporary = new TemporaryFile();
        new AppSettings { IconStyle = IconStyle.RaccoonSheet }.Save(temporary.Path);

        var loaded = AppSettings.Load(temporary.Path);

        Assert.Equal(IconStyle.RaccoonSheet, loaded.IconStyle);
        Assert.True(loaded.MovedToSheetOwl);
    }

    /// <summary>둘 다 자세가 있는 그림이다. 정지 그림은 Claude 쪽뿐이다.</summary>
    [Fact]
    public void 부엉이는_둘_다_움직인다()
    {
        Assert.True(IconStyle.Owl.IsAnimated());
        Assert.True(IconStyle.OwlSheet.IsAnimated());
        Assert.False(IconStyle.Clawd.IsAnimated());
    }

    /// <summary>라쿤도 캐릭터 묶음이고 자세가 있다.</summary>
    [Fact]
    public void 라쿤은_캐릭터_묶음이고_움직인다()
    {
        Assert.Equal(IconStyleGroup.Character, IconStyle.RaccoonSheet.Group());
        Assert.True(IconStyle.RaccoonSheet.IsAnimated());
    }

    /// <summary>
    /// 시트로 도는 캐릭터마다 읽을 그림 이름이 있어야 한다. **격자 부엉이는 없다** —
    /// 저쪽은 파츠를 겹쳐 코드로 그리는 갈래다.
    /// </summary>
    [Fact]
    public void 시트로_도는_것만_그림_이름이_있다()
    {
        Assert.Equal("mascot", IconStyle.OwlSheet.SheetResource());
        Assert.Equal("raccoon", IconStyle.RaccoonSheet.SheetResource());

        Assert.Null(IconStyle.Owl.SheetResource());
        Assert.Null(IconStyle.Clawd.SheetResource());
        Assert.Null(IconStyle.AppIcon.SheetResource());
        Assert.Null(IconStyle.Mark.SheetResource());
    }

    /// <summary><c>UsesSheet</c> 가 <c>SheetResource</c> 와 언제나 같은 답을 내는지.</summary>
    [Theory]
    [InlineData(IconStyle.OwlSheet, true)]
    [InlineData(IconStyle.RaccoonSheet, true)]
    [InlineData(IconStyle.Owl, false)]
    [InlineData(IconStyle.Clawd, false)]
    [InlineData(IconStyle.AppIcon, false)]
    [InlineData(IconStyle.Mark, false)]
    public void 시트로_도는지가_그림_이름과_맞는다(IconStyle style, bool expected)
    {
        Assert.Equal(expected, style.UsesSheet());
        Assert.Equal(expected, style.SheetResource() is not null);
    }

    /// <summary>
    /// **이름이 겹치면 두 캐릭터가 같은 그림을 그린다.** 케이스를 복사해 붙이고
    /// 문자열만 안 고치는 실수가 정확히 이 꼴인데, 화면에는 그럴듯한 것이 나와서
    /// 안 드러난다.
    /// </summary>
    [Fact]
    public void 시트_이름은_서로_겹치지_않는다()
    {
        var names = Enum.GetValues<IconStyle>()
            .Select(style => style.SheetResource())
            .Where(name => name is not null)
            .ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    /// <summary>
    /// 다듬는 중인 것은 지금 라쿤 하나뿐이다. **다 다듬으면 여기가 먼저 빨개진다** —
    /// 딱지를 떼면서 이 줄을 같이 고치라는 뜻이다.
    /// </summary>
    [Fact]
    public void 아직_다듬는_중인_캐릭터는_라쿤_하나다()
    {
        Assert.Equal(
            [IconStyle.RaccoonSheet],
            Enum.GetValues<IconStyle>().Where(style => style.IsBeta()));
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
        Assert.Equal(4, (int)IconStyle.OwlSheet);
        Assert.Equal(5, (int)IconStyle.RaccoonSheet);
    }
}
