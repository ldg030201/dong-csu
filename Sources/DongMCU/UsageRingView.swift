import SwiftUI

/// 오른쪽 위에 떠 있는 원형 사용량 게이지.
struct UsageRingView: View {
    @ObservedObject var store: UsageStore

    static let diameter: CGFloat = 92
    private let lineWidth: CGFloat = 8

    private var window: UsageWindow? { store.snapshot?.fiveHour }
    private var hasStaleData: Bool { store.snapshot != nil && store.errorText != nil }

    var body: some View {
        ZStack {
            Circle()
                .stroke(Color.white.opacity(0.12), lineWidth: lineWidth)

            Circle()
                .trim(from: 0, to: max(0.001, fraction))
                .stroke(
                    AngularGradient(
                        colors: [levelColor.opacity(0.55), levelColor],
                        center: .center,
                        startAngle: .degrees(-90),
                        endAngle: .degrees(270)
                    ),
                    style: StrokeStyle(lineWidth: lineWidth, lineCap: .round)
                )
                .rotationEffect(.degrees(-90))
                .opacity(window == nil ? 0 : 1)
                .animation(.easeOut(duration: 0.45), value: fraction)

            VStack(spacing: 1) {
                Text(centerText)
                    .font(.system(size: 21, weight: .semibold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(.white)
                Text("5H")
                    .font(.system(size: 9, weight: .bold, design: .rounded))
                    .tracking(1.2)
                    .foregroundStyle(.white.opacity(0.42))
            }

            if hasStaleData {
                Circle()
                    .fill(Color(red: 0.90, green: 0.66, blue: 0.24))
                    .frame(width: 4, height: 4)
                    .offset(y: Self.diameter / 2 - lineWidth - 12)
            }
        }
        .padding(lineWidth / 2 + 4)
        .frame(width: Self.diameter, height: Self.diameter)
    }

    private var fraction: Double {
        guard let window else { return 0 }
        return window.utilization / 100
    }

    private var centerText: String {
        guard let window else {
            return store.errorText == nil ? "··" : "—"
        }
        return "\(Int(window.utilization.rounded()))%"
    }

    private var levelColor: Color {
        let value = window?.utilization ?? 0
        if value >= 80 { return Color(red: 0.90, green: 0.28, blue: 0.30) }
        if value >= 50 { return Color(red: 0.90, green: 0.66, blue: 0.24) }
        return Color(red: 0.35, green: 0.80, blue: 0.53)
    }
}
