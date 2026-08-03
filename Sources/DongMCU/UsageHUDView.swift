import SwiftUI

/// 오른쪽 위에 떠 있는 사용량 HUD.
/// 왼쪽: 이중 링(바깥=세션, 안쪽=주간) + 가운데 Claude 마크.
/// 오른쪽: 세션 / 주간 사용률과 초기화까지 남은 시간.
/// 오른쪽 아래: 다음 사용량 조회까지 남은 시간(초 단위).
struct UsageHUDView: View {
    @ObservedObject var store: UsageStore
    var iconStyle: ClaudeIconStyle = .default
    /// HUD가 숨겨져 있으면 1초 타이머가 돌 이유가 없다.
    var showsCountdown: Bool = true
    /// 접힌 상태에서는 링만 남기고 전부 감춘다.
    var isCollapsed: Bool = false
    var palette = HUDPalette(isDark: true)

    static let expandedSize = CGSize(width: 240, height: 88)
    static let collapsedSize = CGSize(width: 86, height: 86)

    static func size(collapsed: Bool) -> CGSize {
        collapsed ? collapsedSize : expandedSize
    }

    /// 접으면 원형으로 만든다.
    static func cornerRadius(collapsed: Bool) -> CGFloat {
        collapsed ? collapsedSize.height / 2 : 20
    }

    /// 새로고침 버튼 자리. 이 영역만 드래그 오버레이가 클릭을 통과시킨다.
    static let refreshInset: CGFloat = 4
    static let refreshHitSize: CGFloat = 20

    /// AppKit 좌표(원점 왼쪽 아래) 기준의 버튼 영역. 접힌 상태에는 버튼이 없다.
    static func refreshHitRectInPanel(collapsed: Bool) -> CGRect {
        guard !collapsed else { return .zero }
        let size = expandedSize
        return CGRect(
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

    var body: some View {
        if isCollapsed {
            collapsedBody
        } else {
            expandedBody
        }
    }

    /// 접힌 모습: 링만.
    private var collapsedBody: some View {
        rings
            .opacity(store.isStale ? 0.45 : 1)
            .frame(width: Self.collapsedSize.width, height: Self.collapsedSize.height)
    }

    private var expandedBody: some View {
        HStack(spacing: 13) {
            // 마지막 성공값을 보여주는 중이면 링·숫자를 흐리게 해서 지금 값이 아님을 드러낸다.
            rings
                .opacity(store.isStale ? 0.45 : 1)
            // 남은 시간 문구만 시간에 따라 바뀐다. 링·아이콘은 타임라인 밖에 두어
            // 주기적 갱신 때 다시 평가되지 않게 한다. 표시 단위가 분이라 60초면 충분하다.
            TimelineView(.periodic(from: .now, by: 60)) { context in
                VStack(alignment: .leading, spacing: 8) {
                    metric(title: "세션", window: store.snapshot?.fiveHour, now: context.date)
                    metric(title: "주간", window: store.snapshot?.sevenDay, now: context.date)
                }
                .shadow(color: palette.textShadow, radius: 2, y: 0.5)
                .opacity(store.isStale ? 0.45 : 1)
            }
            Spacer(minLength: 0)
        }
        .padding(.leading, 13)
        .padding(.trailing, 10)
        .frame(width: Self.expandedSize.width, height: Self.expandedSize.height)
        .overlay(alignment: .topTrailing) { refreshButton }
        .overlay(alignment: .bottomTrailing) { resetCountdown }
    }

    // MARK: - 다음 조회 카운트다운

    /// 다음 사용량 조회까지 남은 시간.
    /// 초까지 움직여야 하므로 1초 주기지만, 이 작은 텍스트만 다시 그린다.
    @ViewBuilder private var resetCountdown: some View {
        if showsCountdown {
            countdownBody
        }
    }

    private var countdownBody: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            Group {
                if let warning = staleLabel(now: context.date) {
                    // 화면 숫자가 지금 값이 아니면, 남은 시간 대신 그 사실을 알린다.
                    Text(warning)
                        .font(.system(size: 9.5, weight: .semibold, design: .rounded))
                        .foregroundStyle(palette.warning)
                } else {
                    HStack(spacing: 4) {
                        Text("조회")
                            .font(.system(size: 8.5, weight: .semibold))
                            .foregroundStyle(palette.faintText)
                        Text(countdownText(now: context.date))
                            .font(.system(size: 9.5, weight: .medium, design: .rounded))
                            .monospacedDigit()
                            .foregroundStyle(palette.tertiaryText.opacity(store.isRefreshing ? 0.55 : 1))
                    }
                }
            }
            .help(countdownHelp)
        }
        .padding(.trailing, 10)
        .padding(.bottom, 7)
    }

    /// 재로그인이 필요하거나 마지막 성공값을 보여주는 중이면 그 문구를 돌려준다.
    private func staleLabel(now: Date) -> String? {
        if store.needsReauth { return "재로그인 필요" }
        guard store.isStale, let fetchedAt = store.snapshot?.fetchedAt else { return nil }
        return RemainingTime.ageText(since: fetchedAt, now: now)
    }

    private func countdownText(now: Date) -> String {
        guard let next = store.nextPollDate else { return "멈춤" }
        // 타이머에 tolerance를 크게 줬기 때문에 예정 시각이 지나도 잠시 뒤에 울린다.
        // 그동안 0:00으로 멈춘 것처럼 보이지 않게 한다.
        guard next.timeIntervalSince(now) > 0 else { return "곧" }
        return RemainingTime.clockText(until: next, now: now)
    }

    private var countdownHelp: String {
        if store.needsReauth {
            return "Claude Code 재로그인이 필요하다 — \(store.errorText ?? "")"
        }
        if let error = store.errorText {
            return "갱신 실패로 마지막 성공값을 보여주는 중 — \(error)"
        }
        guard store.nextPollDate != nil else { return "화면이 꺼져 있어 조회를 멈춘 상태" }
        return "다음 사용량 조회까지 남은 시간"
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
                    Circle().fill(isHoveringRefresh ? palette.controlHoverFill : .clear)
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
        if store.errorText != nil { return palette.warning }
        return isHoveringRefresh ? palette.controlActive : palette.controlIdle
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
            ring(window: store.snapshot?.fiveHour, diameter: ringDiameter, lineWidth: outerLineWidth)
            ring(window: store.snapshot?.sevenDay, diameter: innerDiameter, lineWidth: innerLineWidth)
            ClaudeIconView(style: iconStyle, size: innerDiameter - innerLineWidth * 2 - 4, eyeColor: palette.markEye)
        }
        .frame(width: ringDiameter, height: ringDiameter)
    }

    private func ring(window: UsageWindow?, diameter: CGFloat, lineWidth: CGFloat) -> some View {
        let fraction = (window?.utilization ?? 0) / 100
        let color = UsageColor.color(for: window?.utilization ?? 0)

        return ZStack {
            Circle()
                .stroke(palette.ringTrack, lineWidth: lineWidth)
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
                    .fill(window == nil ? palette.mutedDot : color)
                    .frame(width: 5, height: 5)
                Text(title)
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(palette.secondaryText)
                Text(utilization.map { "\(Int($0.rounded()))%" } ?? "—")
                    .font(.system(size: 14, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(palette.primaryText)
            }
            Text(RemainingTime.text(until: window?.resetsAt, now: now))
                .font(.system(size: 9.5, weight: .medium))
                .foregroundStyle(palette.tertiaryText)
        }
    }

}
