using System.Globalization;
using DongCSU.Core.Owl;

namespace DongCSU.Core.Usage;

/// <summary>색 하나. 화면 쪽 타입에 얽히지 않게 성분만 들고 있는다.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length != 6 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"색이 #RRGGBB 형식이 아니다: {hex}");
        }
        return new Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// 사용률에 따라 연속적으로 변하는 링 색.
///
/// **구간은 <c>owl.json</c> 에서 온다.** 여기 숫자를 적어 두면 맥에서 색을 바꿀 때
/// 윈도우만 옛 색으로 남는다.
/// </summary>
public static class UsageColor
{
    public static Rgb For(double utilization) => For(OwlDocument.Embedded, utilization);

    public static Rgb For(OwlDocument document, double utilization)
    {
        var stops = document.UsageColors;
        if (stops.Count == 0) return new Rgb(0x3A, 0x72, 0xC4);

        var value = Math.Clamp(utilization, stops[0].At, stops[^1].At);

        for (var i = 1; i < stops.Count; i++)
        {
            if (value > stops[i].At) continue;

            var lower = stops[i - 1];
            var upper = stops[i];
            var span = upper.At - lower.At;
            var ratio = span > 0 ? (value - lower.At) / span : 0;
            return Lerp(Rgb.FromHex(lower.Hex), Rgb.FromHex(upper.Hex), ratio);
        }

        return Rgb.FromHex(stops[^1].Hex);
    }

    private static Rgb Lerp(Rgb from, Rgb to, double ratio) => new(
        (byte)Math.Round(from.R + (to.R - from.R) * ratio),
        (byte)Math.Round(from.G + (to.G - from.G) * ratio),
        (byte)Math.Round(from.B + (to.B - from.B) * ratio));
}

/// <summary>초기화까지 남은 시간을 사람이 읽는 문구로.</summary>
public static class RemainingTime
{
    public static string Text(DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is not { } date) return "–";

        var remaining = date - now;
        if (remaining <= TimeSpan.Zero) return "곧 초기화";

        var totalMinutes = (int)remaining.TotalMinutes;
        var days = totalMinutes / (24 * 60);

        if (days > 0)
        {
            // 하루 넘게 남았으면 분을 버리는 대신 시간 단위로 반올림한다.
            // (1일 1시간 59분을 "1일 1시간"으로 보여주는 오차를 막는다.)
            var roundedHours = (int)Math.Round((totalMinutes % (24 * 60)) / 60.0);
            if (roundedHours >= 24) { days += 1; roundedHours = 0; }
            return $"{days}일 {roundedHours}시간 남음";
        }

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0 ? $"{hours}시간 {minutes}분 남음" : $"{minutes}분 남음";
    }

    /// <summary>값을 가져온 지 얼마나 지났는지. 화면 숫자가 언제 것인지 알려줄 때 쓴다.</summary>
    public static string AgeText(DateTimeOffset since, DateTimeOffset now)
    {
        var elapsed = (int)Math.Max(0, (now - since).TotalSeconds);
        if (elapsed < 60) return "방금 값";
        if (elapsed < 3600) return $"{elapsed / 60}분 전 값";
        if (elapsed < 24 * 3600) return $"{elapsed / 3600}시간 전 값";
        return $"{elapsed / (24 * 3600)}일 전 값";
    }

    /// <summary>
    /// 다음 조회까지. **HUD 와 설정 창이 같은 글자를 보여야 한다.**
    ///
    /// 갈래가 셋이다 — 멈춰 있거나(조회 예정이 없음), 예정 시각이 지났거나, 아직 남았거나.
    /// 두 곳에서 따로 판단하면 같은 순간에 한쪽은 "곧", 다른 쪽은 "0:00" 을 띄운다.
    /// </summary>
    /// <param name="stopped">조회가 멈춰 있을 때 보여줄 글자.</param>
    public static string CountdownText(DateTimeOffset? next, DateTimeOffset now, string stopped = "멈춤")
    {
        if (next is not { } at) return stopped;
        // 타이머에 여유가 있어 예정 시각이 지나도 0:00 으로 굳지 않게 한다.
        return at <= now ? "곧" : ClockText(at, now);
    }

    /// <summary>초까지 보이는 카운트다운. 1시간 미만이면 <c>분:초</c>, 넘으면 <c>시:분:초</c>.</summary>
    public static string ClockText(DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is not { } date) return "--:--";

        var remaining = (int)Math.Max(0, (date - now).TotalSeconds);
        var hours = remaining / 3600;
        var minutes = remaining % 3600 / 60;
        var seconds = remaining % 60;

        return hours > 0 ? $"{hours}:{minutes:D2}:{seconds:D2}" : $"{minutes}:{seconds:D2}";
    }

    /// <summary>
    /// 잰 시간. 카운트다운(<see cref="ClockText"/>)과 달리 자릿수를 맞추지 않는다 —
    /// 측정 화면에서는 "얼마나 쟀나"가 한눈에 읽히는 편이 낫다.
    /// </summary>
    public static string ElapsedText(TimeSpan elapsed)
    {
        // **TimeSpan.Days·Hours 를 쓰지 않는다.** 답은 같지만, 맥 소스와 눈으로 대조할 수
        // 있게 같은 식으로 적는다.
        var total = (long)Math.Max(0, Math.Floor(elapsed.TotalSeconds));
        var days = total / (24 * 3600);
        var hours = total % (24 * 3600) / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;

        if (days > 0) return $"{days}일 {hours}시간";
        if (hours > 0) return $"{hours}시간 {minutes}분";
        if (minutes > 0) return $"{minutes}분 {seconds}초";
        return $"{seconds}초";
    }
}

/// <summary>토큰 수를 사람이 읽는 형태로.</summary>
public static class TokenFormat
{
    /// <summary>억 문턱.</summary>
    public const long HundredMillion = 100_000_000;

    /// <summary>만 문턱.</summary>
    public const long TenThousand = 10_000;

    /// <summary>
    /// 한눈에 크기를 잡는 용도. <c>452,846,994</c> 는 세어 봐야 알지만 <c>4.5억</c> 은
    /// 안 세도 된다.
    ///
    /// 받는 값이 <c>long</c> 인 것은 <c>TokenTally</c> 가 <c>long</c> 이라서다 —
    /// 캐시 읽기는 한 측정에서 이미 4.5억이라 <c>int</c> 로는 몇 번 만에 넘친다.
    /// </summary>
    public static string Short(long value)
    {
        var magnitude = Math.Abs(value);
        if (magnitude >= HundredMillion) return Trim(value / (double)HundredMillion) + "억";
        if (magnitude >= TenThousand) return Trim(value / (double)TenThousand) + "만";
        return Exact(value);
    }

    /// <summary>자릿점만 찍은 그대로의 값.</summary>
    public static string Exact(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Trim(double value)
    {
        // 100 을 넘으면 소수점이 의미가 없다(123.4만 → 123만).
        //
        // **서식마다 InvariantCulture 를 준다.** 이 기계는 ko-KR 이라 마침 결과가 같지만,
        // 유럽 로케일에서는 "F1" 이 `12,3` 을 내놓아 아래의 ".0" 떼기가 안 먹고 자릿점이
        // 소수점처럼 보인다.
        var text = Math.Abs(value) >= 100
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : value.ToString("F1", CultureInfo.InvariantCulture);
        return text.EndsWith(".0", StringComparison.Ordinal) ? text[..^2] : text;
    }
}
