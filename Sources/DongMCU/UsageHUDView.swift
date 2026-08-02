import SwiftUI

/// 오른쪽 위에 떠 있는 사용량 HUD.
/// 왼쪽: 이중 링(바깥=주간, 안쪽=세션) + 가운데 Claude 마크.
/// 오른쪽: 세션 / 주간 사용률과 초기화까지 남은 시간.
struct UsageHUDView: View {
    @ObservedObject var store: UsageStore
    var iconStyle: ClaudeIconStyle = .default

    static let size = CGSize(width: 206, height: 88)
    static let cornerRadius: CGFloat = 20

    /// 새로고침 버튼 자리. 이 영역만 드래그 오버레이가 클릭을 통과시킨다.
    static let refreshInset: CGFloat = 4
    static let refreshHitSize: CGFloat = 20

    /// AppKit 좌표(원점 왼쪽 아래) 기준의 버튼 영역.
    static var refreshHitRectInPanel: CGRect {
        CGRect(
            x: size.width - refreshInset - refreshHitSize,
            y: size.height - refreshInset - refreshHitSize,
            width: refreshHitSize,
            height: refreshHitSize
        )
    }

    @State private var isHoveringRefresh = false

    private let ringDiameter: CGFloat = 62
    private let outerLineWidth: CGFloat = 6
    private let innerLineWidth: CGFloat = 5
    private let staleAmber = Color(red: 0.95, green: 0.72, blue: 0.27)

    var body: some View {
        HStack(spacing: 13) {
            rings
            // 남은 시간 문구만 시간에 따라 바뀐다. 링·아이콘은 타임라인 밖에 두어
            // 주기적 갱신 때 다시 평가되지 않게 한다. 표시 단위가 분이라 60초면 충분하다.
            TimelineView(.periodic(from: .now, by: 60)) { context in
                VStack(alignment: .leading, spacing: 8) {
                    metric(title: "세션", window: store.snapshot?.fiveHour, now: context.date)
                    metric(title: "주간", window: store.snapshot?.sevenDay, now: context.date)
                }
                .shadow(color: .black.opacity(0.55), radius: 2, y: 0.5)
            }
            Spacer(minLength: 0)
        }
        .padding(.leading, 13)
        .padding(.trailing, 10)
        .frame(width: Self.size.width, height: Self.size.height)
        .overlay(alignment: .topTrailing) { refreshButton }
    }

    // MARK: - 새로고침 버튼

    private var refreshButton: some View {
        Button {
            store.refresh(force: true)
        } label: {
            Image(systemName: "arrow.clockwise")
                .font(.system(size: 9.5, weight: .bold))
                .foregroundStyle(refreshTint)
                .frame(width: Self.refreshHitSize, height: Self.refreshHitSize)
                .background {
                    Circle().fill(Color.white.opacity(isHoveringRefresh ? 0.13 : 0))
                }
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        // 갱신 중에는 흐리게. 회전 애니메이션은 유휴 상태에서 계속 도는 위험이 있어 쓰지 않는다.
        .opacity(store.isRefreshing ? 0.35 : 1)
        .onHover { isHoveringRefresh = $0 }
        .help(refreshHelp)
        .padding(Self.refreshInset)
    }

    /// 갱신에 실패해 화면 숫자가 오래된 값이면 버튼 자체를 경고색으로 물들인다.
    private var refreshTint: Color {
        if store.errorText != nil { return staleAmber }
        return .white.opacity(isHoveringRefresh ? 0.95 : 0.45)
    }

    private var refreshHelp: String {
        if let error = store.errorText {
            return "갱신 실패: \(error) — 클릭해서 다시 시도"
        }
        return "새로고침"
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

}
