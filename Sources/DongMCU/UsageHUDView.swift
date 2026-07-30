import SwiftUI

/// 오른쪽 위에 떠 있는 사용량 HUD.
/// 왼쪽: 이중 링(바깥=주간, 안쪽=세션) + 가운데 Claude 마크.
/// 오른쪽: 세션 / 주간 사용률과 초기화까지 남은 시간.
struct UsageHUDView: View {
    @ObservedObject var store: UsageStore
    var iconStyle: ClaudeIconStyle = .default

    static let size = CGSize(width: 206, height: 88)
    static let cornerRadius: CGFloat = 20

    private let ringDiameter: CGFloat = 62
    private let outerLineWidth: CGFloat = 6
    private let innerLineWidth: CGFloat = 5
    private let staleAmber = Color(red: 0.95, green: 0.72, blue: 0.27)

    var body: some View {
        // 남은 시간 문구를 계속 갱신하려면 주기적으로 다시 그려야 한다.
        TimelineView(.periodic(from: .now, by: 30)) { context in
            HStack(spacing: 13) {
                rings
                VStack(alignment: .leading, spacing: 8) {
                    metric(title: "세션", window: store.snapshot?.fiveHour, now: context.date)
                    metric(title: "주간", window: store.snapshot?.sevenDay, now: context.date)
                }
                .shadow(color: .black.opacity(0.55), radius: 2, y: 0.5)
                Spacer(minLength: 0)
            }
            .padding(.leading, 13)
            .padding(.trailing, 10)
            .frame(width: Self.size.width, height: Self.size.height)
            .overlay(alignment: .topTrailing) { staleIndicator }
        }
    }

    // MARK: - 링

    private var rings: some View {
        // 안쪽 링은 바깥 링 두께(양쪽) + 간격만큼 줄인다.
        let innerDiameter = ringDiameter - outerLineWidth * 2 - 7

        return ZStack {
            ring(window: store.snapshot?.sevenDay, diameter: ringDiameter, lineWidth: outerLineWidth)
            ring(window: store.snapshot?.fiveHour, diameter: innerDiameter, lineWidth: innerLineWidth)
            ClaudeIconView(style: iconStyle, size: innerDiameter - innerLineWidth * 2 - 4)
        }
        .frame(width: ringDiameter, height: ringDiameter)
    }

    private func ring(window: UsageWindow?, diameter: CGFloat, lineWidth: CGFloat) -> some View {
        let fraction = (window?.utilization ?? 0) / 100
        let color = UsageColor.color(for: window?.utilization ?? 0)

        return ZStack {
            Circle()
                .stroke(Color.white.opacity(0.15), lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: max(0.004, fraction))
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .shadow(color: color.opacity(0.30), radius: 1.5)
                .opacity(window == nil ? 0 : 1)
        }
        // 선이 프레임 밖으로 삐져나가지 않게 두께의 절반만큼 안쪽으로 넣는다.
        .padding(lineWidth / 2)
        .frame(width: diameter, height: diameter)
        .animation(.easeOut(duration: 0.5), value: fraction)
    }

    // MARK: - 수치

    private func metric(title: String, window: UsageWindow?, now: Date) -> some View {
        let utilization = window?.utilization
        let color = UsageColor.color(for: utilization ?? 0)

        return VStack(alignment: .leading, spacing: 1) {
            HStack(spacing: 5) {
                Circle()
                    .fill(window == nil ? Color.white.opacity(0.28) : color)
                    .frame(width: 5, height: 5)
                Text(title)
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(.white.opacity(0.68))
                Text(utilization.map { "\(Int($0.rounded()))%" } ?? "—")
                    .font(.system(size: 14, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(.white)
            }
            Text(RemainingTime.text(until: window?.resetsAt, now: now))
                .font(.system(size: 9.5, weight: .medium))
                .foregroundStyle(.white.opacity(0.62))
        }
    }

    /// 값은 있는데 갱신이 실패한 상태 표시(= 화면 숫자가 오래된 값이라는 뜻).
    @ViewBuilder private var staleIndicator: some View {
        if store.snapshot != nil, store.errorText != nil {
            Circle()
                .fill(staleAmber)
                .frame(width: 5, height: 5)
                .padding(8)
        }
    }
}
