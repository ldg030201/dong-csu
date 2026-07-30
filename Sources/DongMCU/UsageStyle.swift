import SwiftUI

/// 사용률에 따라 연속적으로 변하는 게이지 색.
/// 초록 → 라임 → 노랑 → 주황 → 빨강 구간을 선형 보간한다.
enum UsageColor {
    private static let stops: [(threshold: Double, rgb: (Double, Double, Double))] = [
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
}
