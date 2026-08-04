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
    /// 설정 창 열기. HUDController가 꽂아준다.
    var onOpenSettings: (() -> Void)?
    /// 접기/펼치기 토글.
    var onToggleCollapse: (() -> Void)?
    /// 펼쳐지는 방향. 손잡이(링·버튼)가 붙는 쪽이 반대편이 된다.
    var expandSide: HUDExpandSide = .default
    /// 왼쪽 아래에 이 앱의 CPU·메모리를 표시할지.
    var usageMonitor: ProcessUsageMonitor?

    static let expandedSize = CGSize(width: 240, height: 88)
    /// 자원 사용량 줄을 붙일 때 늘어나는 높이.
    static let statsRowHeight: CGFloat = 17
    /// 접은 모습: 링 + 오른쪽에 버튼 세 개가 세로로 붙는다.
    static let collapsedSize = CGSize(width: 108, height: 88)

    static func size(collapsed: Bool, showsStats: Bool = false) -> CGSize {
        // 접은 상태에는 자리가 없어서 자원 사용량을 붙이지 않는다.
        guard !collapsed else { return collapsedSize }
        guard showsStats else { return expandedSize }
        return CGSize(width: expandedSize.width, height: expandedSize.height + statsRowHeight)
    }

    static func cornerRadius(collapsed: Bool) -> CGFloat {
        collapsed ? 26 : 20
    }

    /// 새로고침 버튼 자리. 이 영역만 드래그 오버레이가 클릭을 통과시킨다.
    static let refreshInset: CGFloat = 4
    static let refreshHitSize: CGFloat = 20

    /// AppKit 좌표(원점 왼쪽 아래) 기준의 버튼 영역.
    /// 펼친 상태는 오른쪽 위 가로 세 칸, 접은 상태는 오른쪽 세로 세 칸이다.
    static func controlsHitRectInPanel(collapsed: Bool, side: HUDExpandSide) -> CGRect {
        let button = refreshHitSize
        if collapsed {
            let height = button * 3
            let x = side == .right
                ? collapsedSize.width - collapsedTrailing - button
                : collapsedTrailing
            return CGRect(
                x: x,
                y: (collapsedSize.height - height) / 2,
                width: button,
                height: height
            )
        }
        let width = button * 3
        let x = side == .right ? expandedSize.width - refreshInset - width : refreshInset
        return CGRect(
            x: x,
            y: expandedSize.height - refreshInset - button,
            width: width,
            height: button
        )
    }

    static let collapsedTrailing: CGFloat = 6

    @State private var isHoveringRefresh = false
    @State private var isHoveringSettings = false
    @State private var isHoveringCollapse = false

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

    /// 접힌 모습: 링 + 세로 버튼 열. 버튼은 펼쳐질 방향 쪽에 붙는다.
    private var collapsedBody: some View {
        HStack(spacing: 8) {
            if expandSide == .right {
                ringsView
                buttonColumn
            } else {
                buttonColumn
                ringsView
            }
        }
        .padding(.leading, expandSide == .right ? 12 : Self.collapsedTrailing)
        .padding(.trailing, expandSide == .right ? Self.collapsedTrailing : 12)
        .frame(width: Self.collapsedSize.width, height: Self.collapsedSize.height)
    }

    private var buttonColumn: some View {
        VStack(spacing: 0) {
            collapseButton
            settingsButton
            refreshButton
        }
    }

    /// 마지막 성공값을 보여주는 중이면 링·숫자를 흐리게 해서 지금 값이 아님을 드러낸다.
    private var ringsView: some View {
        rings.opacity(store.isStale ? 0.45 : 1)
    }

    private var expandedBody: some View {
        VStack(spacing: 0) {
            mainRow
            if let usageMonitor {
                bottomBar(monitor: usageMonitor)
                    .frame(height: Self.statsRowHeight)
            }
        }
        .frame(
            width: Self.expandedSize.width,
            height: Self.size(collapsed: false, showsStats: usageMonitor != nil).height
        )
    }

    private var mainRow: some View {
        HStack(spacing: 13) {
            if expandSide == .right {
                ringsView
                metricsView
                Spacer(minLength: 0)
            } else {
                Spacer(minLength: 0)
                metricsView
                ringsView
            }
        }
        .padding(.leading, expandSide == .right ? 13 : 10)
        .padding(.trailing, expandSide == .right ? 10 : 13)
        .frame(width: Self.expandedSize.width, height: Self.expandedSize.height)
        .overlay(alignment: expandSide == .right ? .topTrailing : .topLeading) { controlButtons }
        // 아래 줄이 생기면 카운트다운도 거기로 내려가 자원 사용량과 같은 높이에 놓인다.
        .overlay(alignment: expandSide == .right ? .bottomTrailing : .bottomLeading) {
            if usageMonitor == nil { resetCountdown }
        }
    }

    /// 자원 사용량과 조회 카운트다운을 한 줄에 놓는다. 방향 설정에 따라 좌우가 뒤집힌다.
    private func bottomBar(monitor: ProcessUsageMonitor) -> some View {
        HStack(spacing: 0) {
            if expandSide == .right {
                ProcessStatsRow(monitor: monitor, palette: palette)
                Spacer(minLength: 8)
                countdownContent
            } else {
                countdownContent
                Spacer(minLength: 8)
                ProcessStatsRow(monitor: monitor, palette: palette)
            }
        }
        .padding(.horizontal, 13)
        .padding(.bottom, 4)
    }

    /// 남은 시간 문구만 시간에 따라 바뀐다. 링·아이콘은 타임라인 밖에 두어
    /// 주기적 갱신 때 다시 평가되지 않게 한다. 표시 단위가 분이라 60초면 충분하다.
    private var metricsView: some View {
        TimelineView(.periodic(from: .now, by: 60)) { context in
            VStack(alignment: .leading, spacing: 8) {
                metric(title: "세션", window: store.snapshot?.fiveHour, now: context.date)
                metric(title: "주간", window: store.snapshot?.sevenDay, now: context.date)
            }
            .shadow(color: palette.textShadow, radius: 2, y: 0.5)
            .opacity(store.isStale ? 0.45 : 1)
        }
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
        }
        // help 문구는 시간과 무관하다. 안에 두면 1초마다 문자열을 새로 만든다.
        .help(countdownHelp)
        .padding(.horizontal, 10)
        .padding(.bottom, 7)
    }

    /// 아래 줄에 들어갈 때 쓰는, 패딩 없는 카운트다운.
    @ViewBuilder private var countdownContent: some View {
        if showsCountdown { countdownBody.padding(.bottom, -7).padding(.horizontal, -10) }
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

    // MARK: - 우측 상단 버튼

    private var controlButtons: some View {
        HStack(spacing: 0) {
            collapseButton
            settingsButton
            refreshButton
        }
        .padding(Self.refreshInset)
    }

    private var collapseButton: some View {
        Button {
            onToggleCollapse?()
        } label: {
            controlLabel(
                systemName: chevronName,
                tint: isHoveringCollapse ? palette.controlActive : palette.controlIdle,
                hovering: isHoveringCollapse
            )
        }
        .buttonStyle(.plain)
        .onHover { isHoveringCollapse = $0 }
        .help(isCollapsed ? "펼치기" : "접기")
    }

    private var settingsButton: some View {
        Button {
            onOpenSettings?()
        } label: {
            controlLabel(
                systemName: "gearshape.fill",
                tint: isHoveringSettings ? palette.controlActive : palette.controlIdle,
                hovering: isHoveringSettings
            )
        }
        .buttonStyle(.plain)
        .onHover { isHoveringSettings = $0 }
        .help("설정")
    }

    /// 눌렀을 때 패널이 움직일 방향을 가리킨다.
    private var chevronName: String {
        (isCollapsed == (expandSide == .right)) ? "chevron.right" : "chevron.left"
    }

    private func controlLabel(systemName: String, tint: Color, hovering: Bool) -> some View {
        Image(systemName: systemName)
            .font(.system(size: 9.5, weight: .bold))
            .foregroundStyle(tint)
            .frame(width: Self.refreshHitSize, height: Self.refreshHitSize)
            .background {
                Circle().fill(hovering ? palette.controlHoverFill : .clear)
            }
            .contentShape(Circle())
    }

    private var refreshButton: some View {
        Button {
            store.refresh(force: true)
        } label: {
            controlLabel(
                systemName: "arrow.clockwise",
                tint: refreshTint,
                hovering: isHoveringRefresh
            )
        }
        .buttonStyle(.plain)
        // 갱신 중에는 흐리게. 회전 애니메이션은 유휴 상태에서 계속 도는 위험이 있어 쓰지 않는다.
        .opacity(store.isRefreshing ? 0.35 : 1)
        .onHover { isHoveringRefresh = $0 }
        .help(refreshHelp)
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

/// HUD 왼쪽 아래에 붙는 이 앱의 자원 사용량.
struct ProcessStatsRow: View {
    @ObservedObject var monitor: ProcessUsageMonitor
    let palette: HUDPalette

    var body: some View {
        HStack(spacing: 6) {
            label("CPU", value: monitor.usage.cpuText)
            label("MEM", value: monitor.usage.footprintText)
            Spacer(minLength: 0)
        }
        .help("dong-mcu 자신이 쓰는 CPU와 메모리")
    }

    private func label(_ title: String, value: String) -> some View {
        HStack(spacing: 3) {
            Text(title)
                .font(.system(size: 8, weight: .semibold))
                .foregroundStyle(palette.faintText)
            Text(value)
                .font(.system(size: 9, weight: .medium, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(palette.tertiaryText)
        }
    }
}
