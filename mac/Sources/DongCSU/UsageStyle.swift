import SwiftUI

/// 사용률에 따라 연속적으로 변하는 게이지 색.
/// 초록 → 라임 → 노랑 → 주황 → 빨강 구간을 선형 보간한다.
enum UsageColor {
    /// 구간 경계와 그 색. 파일로 내보낼 때도 이 목록을 그대로 쓴다.
    static let stops: [(threshold: Double, rgb: (Double, Double, Double))] = [
        (0, (0.34, 0.80, 0.52)),
        (40, (0.60, 0.81, 0.35)),
        (60, (0.95, 0.75, 0.26)),
        (80, (0.96, 0.52, 0.22)),
        (100, (0.93, 0.26, 0.29)),
    ]

    static func color(for utilization: Double) -> Color {
        let value = min(100, max(0, utilization))
        guard let upperIndex = stops.firstIndex(where: { $0.threshold >= value }) else {
            let last = stops[stops.count - 1].rgb
            return Color(red: last.0, green: last.1, blue: last.2)
        }
        guard upperIndex > 0 else {
            let first = stops[0].rgb
            return Color(red: first.0, green: first.1, blue: first.2)
        }

        let lower = stops[upperIndex - 1]
        let upper = stops[upperIndex]
        let span = upper.threshold - lower.threshold
        let ratio = span > 0 ? (value - lower.threshold) / span : 0
        return Color(
            red: lower.rgb.0 + (upper.rgb.0 - lower.rgb.0) * ratio,
            green: lower.rgb.1 + (upper.rgb.1 - lower.rgb.1) * ratio,
            blue: lower.rgb.2 + (upper.rgb.2 - lower.rgb.2) * ratio
        )
    }
}

/// 초기화까지 남은 시간을 사람이 읽는 문구로.
enum RemainingTime {
    static func text(until date: Date?, now: Date) -> String {
        guard let date else { return "–" }
        let remaining = date.timeIntervalSince(now)
        guard remaining > 0 else { return "곧 초기화" }

        let totalMinutes = Int(remaining) / 60
        let hours = (totalMinutes % (24 * 60)) / 60
        let minutes = totalMinutes % 60

        // 하루 넘게 남았으면 분을 버리는 대신 시간 단위로 반올림한다.
        // (1일 1시간 59분을 "1일 1시간"으로 보여주는 오차를 막는다.)
        var days = totalMinutes / (24 * 60)
        if days > 0 {
            var roundedHours = Int((Double(totalMinutes % (24 * 60)) / 60).rounded())
            if roundedHours >= 24 {
                days += 1
                roundedHours = 0
            }
            return "\(days)일 \(roundedHours)시간 남음"
        }
        if hours > 0 { return "\(hours)시간 \(minutes)분 남음" }
        return "\(minutes)분 남음"
    }

    /// 값을 가져온 지 얼마나 지났는지. 화면 숫자가 언제 것인지 알려줄 때 쓴다.
    static func ageText(since date: Date, now: Date) -> String {
        let elapsed = Int(max(0, now.timeIntervalSince(date)))
        if elapsed < 60 { return "방금 값" }
        if elapsed < 3600 { return "\(elapsed / 60)분 전 값" }
        if elapsed < 24 * 3600 { return "\(elapsed / 3600)시간 전 값" }
        return "\(elapsed / (24 * 3600))일 전 값"
    }

    /// 잰 시간. 카운트다운(`clockText`)과 달리 자릿수를 맞추지 않는다 —
    /// 측정 화면에서는 "얼마나 쟀나"가 한눈에 읽히는 편이 낫다.
    static func elapsedText(_ seconds: TimeInterval) -> String {
        let total = max(0, Int(seconds))
        let days = total / (24 * 3600)
        let hours = (total % (24 * 3600)) / 3600
        let minutes = (total % 3600) / 60

        if days > 0 { return "\(days)일 \(hours)시간" }
        if hours > 0 { return "\(hours)시간 \(minutes)분" }
        if minutes > 0 { return "\(minutes)분 \(total % 60)초" }
        return "\(total)초"
    }

    /// 초까지 보이는 카운트다운. 1시간 미만이면 `분:초`, 넘으면 `시:분:초`.
    static func clockText(until date: Date?, now: Date) -> String {
        guard let date else { return "--:--" }
        let remaining = max(0, Int(date.timeIntervalSince(now)))
        let hours = remaining / 3600
        let minutes = (remaining % 3600) / 60
        let seconds = remaining % 60

        if hours > 0 { return String(format: "%d:%02d:%02d", hours, minutes, seconds) }
        return String(format: "%d:%02d", minutes, seconds)
    }
}

/// 토큰 수를 사람이 읽는 형태로.
enum TokenFormat {
    /// 한눈에 크기를 잡는 용도. `452,846,994` 는 세어 봐야 알지만 `4.5억`은 안 세도 된다.
    static func short(_ value: Int) -> String {
        let magnitude = abs(value)
        if magnitude >= 100_000_000 { return trim(Double(value) / 100_000_000) + "억" }
        if magnitude >= 10_000 { return trim(Double(value) / 10_000) + "만" }
        return exact(value)
    }

    /// 자릿점만 찍은 그대로의 값.
    static func exact(_ value: Int) -> String {
        grouping.string(from: NSNumber(value: value)) ?? "\(value)"
    }

    private static func trim(_ value: Double) -> String {
        // 100을 넘으면 소수점이 의미가 없다(123.4만 → 123만).
        let text = abs(value) >= 100
            ? String(format: "%.0f", value)
            : String(format: "%.1f", value)
        return text.hasSuffix(".0") ? String(text.dropLast(2)) : text
    }

    private static let grouping: NumberFormatter = {
        let formatter = NumberFormatter()
        formatter.numberStyle = .decimal
        return formatter
    }()
}
