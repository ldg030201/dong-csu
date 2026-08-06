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
}
