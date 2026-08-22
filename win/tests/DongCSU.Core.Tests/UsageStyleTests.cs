using System.Globalization;
using DongCSU.Core.Usage;

namespace DongCSU.Core.Tests;

// 이 파일의 클래스 이름이 `UsageStyleTests` 가 아닌 것은 그 이름을 UsageTests.cs 가
// 이미 쓰고 있어서다(링 색·남은 시간). 측정이 새로 들여온 두 가지만 여기 모은다.

/// <summary>
/// 잰 시간. 카운트다운과 달리 자릿수를 안 맞추는 것이 이 함수의 전부라, 갈래 네 개와
/// 그 경계를 표로 못 박아 둔다.
/// </summary>
public class ElapsedTextTests
{
    [Theory]
    [InlineData(0, "0초")]
    [InlineData(59, "59초")]
    // 1분이 되는 순간 갈래가 바뀐다.
    [InlineData(60, "1분 0초")]
    [InlineData(90, "1분 30초")]
    [InlineData(3599, "59분 59초")]
    // 한 시간부터는 초를 버린다. 0분도 그대로 적는다.
    [InlineData(3600, "1시간 0분")]
    [InlineData(3723, "1시간 2분")]
    [InlineData(86399, "23시간 59분")]
    [InlineData(86400, "1일 0시간")]
    [InlineData(90000, "1일 1시간")]
    public void 잰_시간을_적는다(int seconds, string expected)
    {
        Assert.Equal(expected, RemainingTime.ElapsedText(TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>
    /// 시계가 뒤로 가면(절전 복귀·시간대 변경) 잰 시간이 음수가 된다. 화면에 "-3초"가
    /// 뜨면 고장으로 보이므로 0으로 눕힌다.
    /// </summary>
    [Fact]
    public void 음수는_0초다()
    {
        Assert.Equal("0초", RemainingTime.ElapsedText(TimeSpan.FromSeconds(-1)));
        Assert.Equal("0초", RemainingTime.ElapsedText(TimeSpan.FromHours(-5)));
    }

    /// <summary>초 미만은 버린다 — 올림하면 시작하자마자 "1초"가 뜬다.</summary>
    [Fact]
    public void 소수점_초는_내린다()
    {
        Assert.Equal("0초", RemainingTime.ElapsedText(TimeSpan.FromMilliseconds(999)));
        Assert.Equal("1분 59초", RemainingTime.ElapsedText(TimeSpan.FromSeconds(119.9)));
    }
}

/// <summary>토큰 축약. 억·만 문턱과 소수점 다듬기가 전부다.</summary>
public class TokenFormatTests
{
    [Theory]
    [InlineData(0L, "0")]
    [InlineData(1L, "1")]
    [InlineData(9_999L, "9,999")]
    // 만 문턱. `1.0만` 이 아니라 `1만` 이어야 한다.
    [InlineData(10_000L, "1만")]
    [InlineData(12_345L, "1.2만")]
    // 100을 넘으면 소수점이 의미가 없다.
    [InlineData(1_234_567L, "123만")]
    // **억 문턱 바로 아래는 아직 만이다.** 여기서 갈래가 새면 4.5억이 45000만으로 나간다.
    [InlineData(99_999_999L, "10000만")]
    [InlineData(100_000_000L, "1억")]
    [InlineData(452_846_994L, "4.5억")]
    [InlineData(4_528_469_940L, "45.3억")]
    // `int` 로 짰으면 여기서 음수가 되어 걸린다.
    [InlineData(3_000_000_000L, "30억")]
    public void 억과_만으로_줄인다(long value, string expected)
    {
        Assert.Equal(expected, TokenFormat.Short(value));
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(1_234_567L, "1,234,567")]
    [InlineData(3_000_000_000L, "3,000,000,000")]
    public void 자릿점을_찍는다(long value, string expected)
    {
        Assert.Equal(expected, TokenFormat.Exact(value));
    }

    /// <summary>문턱 상수를 화면 쪽이 따로 적지 않게 밖으로 내놓은 값이다.</summary>
    [Fact]
    public void 문턱은_억과_만이다()
    {
        Assert.Equal(100_000_000L, TokenFormat.HundredMillion);
        Assert.Equal(10_000L, TokenFormat.TenThousand);
    }

    /// <summary>
    /// **문화권을 바꿔도 같은 답이 나와야 한다.**
    ///
    /// 이 기계는 ko-KR 이라 마침 자릿점·소수점이 InvariantCulture 와 같지만, de-DE 는
    /// 소수점이 `,` 이고 천단위가 `.` 이다. 서식에 문화권을 안 못 박았으면 `"F1"` 이
    /// <c>4,5</c> 를 내놓아 `.0` 떼기가 안 먹고, `"N0"` 이 <c>1.234.567</c> 을 내놓아
    /// 자릿점이 소수점처럼 읽힌다.
    /// </summary>
    [Fact]
    public void 문화권이_달라도_같은_글자다()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("9,999", TokenFormat.Short(9_999));
            Assert.Equal("1.2만", TokenFormat.Short(12_345));
            Assert.Equal("123만", TokenFormat.Short(1_234_567));
            Assert.Equal("4.5억", TokenFormat.Short(452_846_994));
            Assert.Equal("1,234,567", TokenFormat.Exact(1_234_567));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>잰 시간에는 서식이 안 들어가지만, 같이 흔들리지 않는지 함께 본다.</summary>
    [Fact]
    public void 문화권이_달라도_잰_시간은_같다()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1시간 2분", RemainingTime.ElapsedText(TimeSpan.FromSeconds(3723)));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
