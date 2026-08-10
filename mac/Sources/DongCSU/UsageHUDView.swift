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
    /// 얼마나 보여줄지. 링만 남기거나(collapsed) 마스코트만 남긴다(pet).
    var mode: HUDMode = .expanded
    /// 펫 모드에서 마우스가 위에 있는지. `petRingDisplay`가 `.hover`일 때만 쓰인다.
    var isHovered: Bool = false
    /// 펫 모드에서 뒤에 두르는 링을 언제 보여줄지.
    var petRingDisplay: PetRingDisplay = .default
    var palette = HUDPalette(isDark: true)
    /// 설정 창 열기. HUDController가 꽂아준다.
    var onOpenSettings: (() -> Void)?
    /// 측정 화면을 연다. **여기서 바로 재기 시작하지 않는다** — 손이 스치면 재던 것이
    /// 끊기고, 무엇이 시작됐는지도 화면에 안 보인다.
    var onOpenMeasure: (() -> Void)?
    /// 지금 재는 중인지. 버튼 모양이 이걸 따른다.
    var isMeasuring = false
    /// 접기/펼치기 토글.
    var onToggleCollapse: (() -> Void)?
    /// 펼쳐지는 방향. 손잡이(링·버튼)가 붙는 쪽이 반대편이 된다.
    var expandSide: HUDExpandSide = .default
    /// 왼쪽 아래에 이 앱의 CPU·메모리를 표시할지.
    var usageMonitor: ProcessUsageMonitor?
    /// HUD 전체 배율. 치수와 글자 크기에 곱한다.
    var scale: CGFloat = 1
    /// 새 버전이 나와 있으면 버튼 반대편 위 모서리에 표시를 띄운다.
    var showsUpdateBadge: Bool = false
    /// 같은 모서리에 붙일 버전 딱지. nil이면 그리지 않는다.
    var versionBadge: String?
    /// 그 딱지를 테스트판 색으로 그릴지. 렌더 통로가 실제 빌드와 무관하게 넘길 수 있게
    /// `AppInfo`를 직접 읽지 않고 받는다.
    var versionBadgeIsTest: Bool = false
    /// 그 표시를 눌렀을 때. 버전 화면을 연다.
    var onOpenUpdates: (() -> Void)?
    /// 가운데 부엉이를 움직이게 할 애니메이터. 없으면 정지 자세로 그린다.
    var owlAnimator: OwlAnimator?

    /// 배율 1 기준 길이를 실제 길이로.
    private func s(_ value: CGFloat) -> CGFloat { value * scale }

    /// 배율 1 기준 글자 크기를 실제 폰트로.
    private func font(
        _ size: CGFloat,
        weight: Font.Weight = .regular,
        design: Font.Design = .default
    ) -> Font {
        .system(size: size * scale, weight: weight, design: design)
    }

    // 아래는 모두 배율 1 기준 치수다. 실제 값은 scale을 곱해서 쓴다.
    static let baseExpandedSize = CGSize(width: 240, height: 88)
    /// 자원 사용량 줄을 붙일 때 늘어나는 높이.
    static let baseStatsRowHeight: CGFloat = 17
    /// 접은 모습: 링 + 오른쪽에 버튼 세 개가 세로로 붙는다.
    static let baseCollapsedSize = CGSize(width: 108, height: 88)
    /// 펫 마스코트의 높이. 펫에서는 캐릭터가 주인공이라 크게 잡는다.
    static let basePetOwlHeight: CGFloat = 84
    /// 뒤에 두르는 링의 바깥 지름. 마스코트가 링 안쪽에 여유 있게 들어가야 한다.
    /// 안쪽 지름 = 바깥 − 두께 2겹(10) − 간격(7) 이고, 마스코트 폭은 높이 × 15/13 이다.
    static let basePetRingDiameter: CGFloat = 124
    /// 창은 링을 담을 만큼. 링을 감추고 있을 때도 크기는 그대로다 —
    /// 호버할 때 창을 늘리면 커서가 창 밖으로 밀려나 호버가 끊긴다.
    /// 링 아래에 붙는 버튼 줄의 높이.
    static let basePetButtonRow: CGFloat = 32

    /// 창은 링을 담을 만큼 + 아래 버튼 줄. 링을 감추고 있을 때도 크기는 그대로다 —
    /// 호버할 때 창을 늘리면 커서가 창 밖으로 밀려나 호버가 끊긴다.
    static let basePetSize = CGSize(width: 128, height: 128 + basePetButtonRow)

    static func size(mode: HUDMode, showsStats: Bool = false, scale: CGFloat = 1) -> CGSize {
        func scaled(_ size: CGSize) -> CGSize {
            CGSize(width: size.width * scale, height: size.height * scale)
        }
        switch mode {
        case .pet: return scaled(basePetSize)
        // 접은 상태에는 자리가 없어서 자원 사용량을 붙이지 않는다.
        case .collapsed: return scaled(baseCollapsedSize)
        case .expanded:
            let expanded = scaled(baseExpandedSize)
            guard showsStats else { return expanded }
            return CGSize(
                width: expanded.width,
                height: expanded.height + baseStatsRowHeight * scale
            )
        }
    }

    static func cornerRadius(mode: HUDMode, scale: CGFloat = 1) -> CGFloat {
        switch mode {
        // 배경이 없으니 깎을 모서리도 없다. 남겨 두면 링 가장자리가 잘린다.
        case .pet: return 0
        case .collapsed: return 26 * scale
        case .expanded: return 20 * scale
        }
    }

    /// 펫 모드에서 클릭·호버를 받는 자리. 창 가운데의 링만큼이다.
    ///
    /// 창 전체를 받으면 투명한 네 귀퉁이에서도 클릭이 먹혀서 뒤에 있는 창을 누를 수 없다.
    /// 반대로 마스코트 크기로만 잡으면, 호버해서 링이 뜬 순간 커서를 링 쪽으로 조금만
    /// 옮겨도 영역을 벗어나 링이 사라진다.
    static func petHitRect(scale: CGFloat) -> CGRect {
        let panel = size(mode: .pet, scale: scale)
        let side = basePetRingDiameter * scale
        let row = basePetButtonRow * scale
        // 뷰 좌표는 아래가 0이다. 버튼 줄이 아래에 깔리고 링은 그 **위** 영역의 가운데다.
        return CGRect(
            x: (panel.width - side) / 2,
            y: row + (panel.height - row - side) / 2,
            width: side,
            height: side
        )
    }

    /// 펫 모드에서 새 버전 배지가 앉는 자리 — **창 오른쪽 위**.
    ///
    /// 링(원)의 바깥 모서리라 마스코트를 가리지 않는다. 다만 커서 피하기를 거는
    /// `petHitRect`(사각형)와는 겹치므로, 여기 마우스가 올라오면 도망을 막아야 한다.
    /// `HUDController` 가 이 자리에 추적 영역을 따로 걸어 그렇게 한다.
    static func petUpdateRect(scale: CGFloat) -> CGRect {
        let panel = size(mode: .pet, scale: scale)
        let side = updateBadgeSize(scale: scale)
        let inset = 2 * scale
        return CGRect(
            x: panel.width - side - inset,
            y: panel.height - side - inset,
            width: side,
            height: side
        )
    }

    /// 링 아래 버튼 줄이 차지하는 자리.
    ///
    /// **커서 피하기를 거는 추적 영역(`petHitRect`)과 겹치지 않는다.** 버튼을 누르러
    /// 다가갔는데 펫이 달아나면 영영 못 누른다. 자리를 갈라 두면 특별히 예외를 두지
    /// 않아도 그 일이 생기지 않는다.
    static func petButtonsRect(scale: CGFloat) -> CGRect {
        let panel = size(mode: .pet, scale: scale)
        return CGRect(x: 0, y: 0, width: panel.width, height: basePetButtonRow * scale)
    }

    static func petOwlHeight(scale: CGFloat) -> CGFloat { basePetOwlHeight * scale }

    /// 마스코트 그림이 실제로 덮는 자리(뷰 좌표, 아래가 0).
    ///
    /// **`petHitRect` 와 같은 셈을 쓴다.** 둘 다 "버튼 줄 위 영역의 가운데"인데,
    /// 예전에는 이 계산이 `HUDPanel` 에 따로 적혀 있어서 버튼 줄이 생겼을 때 한쪽만
    /// 고쳐졌다 — 판정이 배율 1에서 16pt 아래로 밀렸다. 같은 파일에 나란히 둔다.
    static func petMascotRect(scale: CGFloat) -> CGRect {
        let panel = size(mode: .pet, scale: scale)
        let height = petOwlHeight(scale: scale)
        // 그리드 15열 중 몸통이 쓰는 건 가운데 11열이다. 나머지는 날개를 펼 여백이라
        // 평소에는 비어 있어서, 그 폭까지 가린다고 치면 쓸데없이 멀리 비킨다.
        let width = height * CGFloat(OwlMark.bodyColumns) / CGFloat(OwlMark.lines)
        let row = basePetButtonRow * scale
        return CGRect(
            x: (panel.width - width) / 2,
            y: row + (panel.height - row - height) / 2,
            width: width,
            height: height
        )
    }

    /// 새로고침 버튼 자리. 이 영역만 드래그 오버레이가 클릭을 통과시킨다.
    static func refreshInset(scale: CGFloat) -> CGFloat { 4 * scale }
    static func refreshHitSize(scale: CGFloat) -> CGFloat { 20 * scale }

    /// 카드 위 버튼 수(접기 · 측정 · 설정 · 새로고침).
    ///
    /// **여기를 안 늘리면 버튼을 더해도 클릭이 통과하지 않는다.** 버튼은 SwiftUI가
    /// 그리지만 클릭을 흘려보낼 자리는 AppKit 쪽에서 따로 재기 때문이다.
    static let controlButtonCount = 4

    /// AppKit 좌표(원점 왼쪽 아래) 기준의 버튼 영역.
    /// 펼친 상태는 위쪽 가로 한 줄, 접은 상태는 옆쪽 세로 한 줄이다.
    ///
    /// 높이는 반드시 "실제 창 크기"에서 가져와야 한다. 자원 사용량 줄이 붙으면 창이
    /// 17pt 커지는데, 그때 펼친 기본 높이(88)로 계산하면 영역이 그만큼 아래로 밀려서
    /// 버튼을 눌러도 클릭이 통과되지 않는다.
    static func controlsHitRectInPanel(
        mode: HUDMode,
        side: HUDExpandSide,
        showsStats: Bool,
        scale: CGFloat = 1
    ) -> CGRect {
        // 펫에는 버튼이 없다. 빈 사각형을 주면 어떤 클릭도 여기 걸리지 않는다.
        guard mode != .pet else { return .zero }

        let button = refreshHitSize(scale: scale)
        let panel = size(mode: mode, showsStats: showsStats, scale: scale)
        let trailing = collapsedTrailing(scale: scale)
        let inset = refreshInset(scale: scale)

        if mode == .collapsed {
            let height = button * CGFloat(controlButtonCount)
            let x = side == .right ? panel.width - trailing - button : trailing
            return CGRect(
                x: x,
                y: (panel.height - height) / 2,
                width: button,
                height: height
            )
        }

        let width = button * CGFloat(controlButtonCount)
        let x = side == .right ? panel.width - inset - width : inset
        return CGRect(
            x: x,
            y: panel.height - inset - button,
            width: width,
            height: button
        )
    }

    static func collapsedTrailing(scale: CGFloat) -> CGFloat { 6 * scale }

    /// 마스코트가 놓인 자리(AppKit 좌표, 원점 왼쪽 아래).
    ///
    /// 여기를 더블클릭하면 펫 모드로 들어간다. 링과 마스코트가 겹쳐 있으므로 링 전체를
    /// 잡는다. 펫에서는 이미 마스코트뿐이라 창 전체가 그 자리다.
    static func characterRectInPanel(
        mode: HUDMode,
        side: HUDExpandSide,
        showsStats: Bool,
        scale: CGFloat = 1
    ) -> CGRect {
        let panel = size(mode: mode, showsStats: showsStats, scale: scale)
        guard mode != .pet else { return CGRect(origin: .zero, size: panel) }

        let ring = 62 * scale
        // 펼친 상태의 링은 위쪽 88pt 줄 안에서 세로 가운데에 놓인다.
        // 자원 사용량 줄이 붙어 창이 커져도 링은 그대로 위에 남는다.
        let rowHeight = mode == .collapsed ? panel.height : baseExpandedSize.height * scale
        let y = panel.height - rowHeight + (rowHeight - ring) / 2

        // 접힌 상태에서 왼쪽으로 펼치는 설정이면 버튼 열이 링 앞에 온다.
        let leading: CGFloat
        switch (mode, side) {
        case (.collapsed, .right): leading = 12 * scale
        case (.collapsed, .left):
            leading = collapsedTrailing(scale: scale) + refreshHitSize(scale: scale) + 8 * scale
        case (_, .right): leading = 13 * scale
        case (_, .left): leading = panel.width - 13 * scale - ring
        }
        return CGRect(x: leading, y: y, width: ring, height: ring)
    }

    /// 업데이트 표시 한 변.
    static func updateBadgeSize(scale: CGFloat) -> CGFloat { 18 * scale }

    /// 업데이트 표시 자리. 버튼 묶음 반대편 위 모서리에 둔다.
    /// 기본 설정(오른쪽으로 펼치기)에서는 왼쪽 위가 된다.
    static func updateBadgeRectInPanel(
        mode: HUDMode,
        side: HUDExpandSide,
        showsStats: Bool,
        scale: CGFloat = 1
    ) -> CGRect {
        // 펫은 배지를 그리지 않는다. 그런데도 자리를 돌려주면 그만큼이 클릭 통과
        // 구멍이 되어, 마스코트 한 귀퉁이를 눌러도 끌리지 않는다.
        guard mode != .pet else { return .zero }

        let badge = updateBadgeSize(scale: scale)
        let inset = refreshInset(scale: scale)
        let panel = size(mode: mode, showsStats: showsStats, scale: scale)
        let x = side == .right ? inset : panel.width - inset - badge
        return CGRect(x: x, y: panel.height - inset - badge, width: badge, height: badge)
    }

    @State private var isHoveringRefresh = false
    @State private var isHoveringSettings = false
    @State private var isHoveringMeasure = false
    @State private var isHoveringCollapse = false

    /// 지금 링을 그릴지.
    private var showsPetRing: Bool {
        switch petRingDisplay {
        case .always: return true
        case .hover: return isHovered
        case .never: return false
        }
    }

    private var isDisconnected: Bool { store.isDisconnected }

    private var ringDiameter: CGFloat { s(62) }
    private var outerLineWidth: CGFloat { s(6) }
    private var innerLineWidth: CGFloat { s(5) }

    var body: some View {
        switch mode {
        case .pet: petBody
        case .collapsed: collapsedBody
        case .expanded: expandedBody
        }
    }

    /// 펫 모습: 마스코트만. 마우스를 올리면 뒤에서 링이 떠오른다.
    ///
    /// 창 크기는 링에 맞춰 두고 마스코트를 가운데 놓는다. 호버할 때 창을 늘리면
    /// 커서가 창 밖으로 밀려나 호버가 끊기고, 그 자리에서 켜졌다 꺼졌다 한다.
    private var petBody: some View {
        VStack(spacing: 0) {
            petRingArea
            petButtonRow
        }
        .frame(
            width: Self.size(mode: .pet, scale: scale).width,
            height: Self.size(mode: .pet, scale: scale).height
        )
        // **여기에 사용량 요약을 붙이지 않는다.** 카드 전체를 덮는 설명이라 버튼 위에
        // 올려도 같이 떠서 버튼 설명과 겹친다. 마우스를 올리면 링이 떠오르므로
        // 사용량은 이미 눈에 보인다 — 같은 말을 글로 한 번 더 할 이유가 없다.
    }

    private var petRingArea: some View {
        ZStack(alignment: .topTrailing) {
            petRingStack
            // 새 버전이 있을 때만. 링 바깥 모서리라 마스코트를 가리지 않는다.
            if showsUpdateBadge {
                updateBadge.padding(s(2))
            }
        }
    }

    private var petRingStack: some View {
        ZStack {
            ringPair(
                diameter: s(Self.basePetRingDiameter),
                outerWidth: s(5),
                innerWidth: s(4)
            )
            .opacity(showsPetRing ? (isDisconnected ? 0.4 : 0.95) : 0)
            .animation(.easeOut(duration: 0.18), value: showsPetRing)

            ClaudeIconView(
                style: iconStyle,
                size: Self.petOwlHeight(scale: scale),
                eyeColor: palette.markEye,
                owlAnimator: owlAnimator
            )
            // 움직이는 캐릭터는 팔레트가 회색으로 바뀌어 스스로 드러낸다. 정지 그림은
            // 그럴 수 없는데, 펫에는 링도 안 보여서 조회가 끊긴 걸 알 방법이 없어진다.
            // 링에 이미 쓰는 방식대로 흐리게 해서 지금 값이 아님을 알린다.
            .opacity(!iconStyle.isAnimated && isDisconnected ? 0.4 : 1)
        }
        .frame(
            width: Self.size(mode: .pet, scale: scale).width,
            height: Self.size(mode: .pet, scale: scale).height - s(Self.basePetButtonRow)
        )
    }

    /// 링 밖 아래에 붙는 동그란 아이콘 버튼들.
    ///
    /// 링과 마찬가지로 **마우스를 올렸을 때만** 보인다 — 펫은 마스코트만 띄우는 보기라
    /// 버튼이 늘 떠 있으면 그 뜻이 사라진다.
    private var petButtonRow: some View {
        HStack(spacing: s(8)) {
            // **그림만 있는 버튼이라 설명을 붙인다.** 펫 모드에는 글자가 하나도 없어서,
            // 마우스를 올렸을 때 말해 주지 않으면 눌러 보는 수밖에 없다.
            PetCircleButton(
                systemName: isMeasuring ? "stopwatch.fill" : "stopwatch",
                palette: palette,
                scale: scale,
                tint: isMeasuring ? .red : nil
            ) {
                onOpenMeasure?()
            }
            .help(measureHelp)
            PetCircleButton(systemName: "gearshape.fill", palette: palette, scale: scale) {
                onOpenSettings?()
            }
            .help("설정")
            PetCircleButton(systemName: "arrow.clockwise", palette: palette, scale: scale) {
                store.refresh(force: true)
            }
            .opacity(store.isRefreshing ? 0.35 : 1)
            .help(refreshHelp)
        }
        .frame(height: s(Self.basePetButtonRow))
        .opacity(showsPetRing ? 1 : 0)
        .animation(.easeOut(duration: 0.18), value: showsPetRing)
    }

    /// 접힌 모습: 링 + 세로 버튼 열. 버튼은 펼쳐질 방향 쪽에 붙는다.
    private var collapsedBody: some View {
        HStack(spacing: s(8)) {
            if expandSide == .right {
                ringsView
                buttonColumn
            } else {
                buttonColumn
                ringsView
            }
        }
        .padding(.leading, expandSide == .right ? s(12) : Self.collapsedTrailing(scale: scale))
        .padding(.trailing, expandSide == .right ? Self.collapsedTrailing(scale: scale) : s(12))
        .frame(
            width: Self.size(mode: .collapsed, scale: scale).width,
            height: Self.size(mode: .collapsed, scale: scale).height
        )
        // 접은 카드는 108pt뿐이라 버전 딱지를 붙이면 링 위에 겹친다.
        // 테스트판인지는 마스코트 색(보라)이 알려준다.
        .overlay(alignment: badgeAlignment) { cornerBadges(showsVersion: false) }
    }

    private var buttonColumn: some View {
        VStack(spacing: 0) {
            collapseButton
            measureButton
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
                    .frame(height: s(Self.baseStatsRowHeight))
            }
        }
        .frame(
            width: s(Self.baseExpandedSize.width),
            height: Self.size(mode: .expanded, showsStats: usageMonitor != nil, scale: scale).height
        )
    }

    private var mainRow: some View {
        HStack(spacing: s(13)) {
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
        .padding(.leading, expandSide == .right ? s(13) : s(10))
        .padding(.trailing, expandSide == .right ? s(10) : s(13))
        .frame(width: s(Self.baseExpandedSize.width), height: s(Self.baseExpandedSize.height))
        .overlay(alignment: expandSide == .right ? .topTrailing : .topLeading) { controlButtons }
        .overlay(alignment: badgeAlignment) { cornerBadges(showsVersion: true) }
        // 아래 줄이 생기면 카운트다운도 거기로 내려가 자원 사용량과 같은 높이에 놓인다.
        .overlay(alignment: expandSide == .right ? .bottomTrailing : .bottomLeading) {
            if usageMonitor == nil { resetCountdown }
        }
    }

    /// 자원 사용량과 조회 카운트다운을 한 줄에 놓는다. 방향 설정에 따라 좌우가 뒤집힌다.
    private func bottomBar(monitor: ProcessUsageMonitor) -> some View {
        HStack(spacing: 0) {
            if expandSide == .right {
                ProcessStatsRow(monitor: monitor, palette: palette, scale: scale)
                Spacer(minLength: s(8))
                countdownContent
            } else {
                countdownContent
                Spacer(minLength: s(8))
                ProcessStatsRow(monitor: monitor, palette: palette, scale: scale)
            }
        }
        .padding(.horizontal, s(13))
        .padding(.bottom, s(4))
    }

    /// 남은 시간 문구만 시간에 따라 바뀐다. 링·아이콘은 타임라인 밖에 두어
    /// 주기적 갱신 때 다시 평가되지 않게 한다. 표시 단위가 분이라 60초면 충분하다.
    private var metricsView: some View {
        TimelineView(.periodic(from: .now, by: 60)) { context in
            VStack(alignment: .leading, spacing: s(8)) {
                metric(
                    title: "세션",
                    window: store.snapshot?.fiveHour,
                    now: context.date,
                    isSpent: store.isWeeklySpent
                )
                metric(
                    title: "주간",
                    window: store.snapshot?.sevenDay,
                    now: context.date,
                    isSpent: store.isWeeklySpent
                )
            }
            .shadow(color: palette.textShadow, radius: s(2), y: s(0.5))
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
                        .font(font(9.5, weight: .semibold, design: .rounded))
                        .foregroundStyle(palette.warning)
                } else {
                    HStack(spacing: s(4)) {
                        Text("조회")
                            .font(font(8.5, weight: .semibold))
                            .foregroundStyle(palette.faintText)
                        Text(countdownText(now: context.date))
                            .font(font(9.5, weight: .medium, design: .rounded))
                            .monospacedDigit()
                            .foregroundStyle(palette.tertiaryText.opacity(store.isRefreshing ? 0.55 : 1))
                    }
                }
            }
        }
        // help 문구는 시간과 무관하다. 안에 두면 1초마다 문자열을 새로 만든다.
        .help(countdownHelp)
        .padding(.horizontal, s(10))
        .padding(.bottom, s(7))
    }

    /// 아래 줄에 들어갈 때 쓰는, 패딩 없는 카운트다운.
    @ViewBuilder private var countdownContent: some View {
        if showsCountdown { countdownBody.padding(.bottom, s(-7)).padding(.horizontal, s(-10)) }
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

    // MARK: - 위 모서리 표시 (업데이트 · 버전)

    private var badgeAlignment: Alignment {
        expandSide == .right ? .topLeading : .topTrailing
    }

    /// 버튼 묶음 반대편 위 모서리에 붙는 것들.
    ///
    /// 업데이트 표시와 버전 딱지가 같은 자리를 노려서, 각자 오버레이로 얹으면 겹친다.
    /// 한 줄에 묶어 두면 새 버전이 잡히는 순간 버전 딱지가 옆으로 밀려난다.
    /// **업데이트 표시가 늘 바깥쪽이다** — 클릭 통과 영역이 모서리 기준으로 계산된다
    /// (`updateBadgeRectInPanel`).
    @ViewBuilder private func cornerBadges(showsVersion: Bool) -> some View {
        let version = showsVersion ? versionBadge : nil
        if showsUpdateBadge || version != nil {
            HStack(spacing: s(3)) {
                if expandSide == .right {
                    updateBadge
                    versionLabel(version)
                } else {
                    versionLabel(version)
                    updateBadge
                }
            }
            .padding(Self.refreshInset(scale: scale))
        }
    }

    @ViewBuilder private var updateBadge: some View {
        if showsUpdateBadge {
            Button {
                onOpenUpdates?()
            } label: {
                Image(systemName: "arrow.down.circle.fill")
                    .font(font(13, weight: .semibold))
                    .symbolRenderingMode(.palette)
                    .foregroundStyle(.white, palette.updateBadge)
                    .frame(
                        width: Self.updateBadgeSize(scale: scale),
                        height: Self.updateBadgeSize(scale: scale)
                    )
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .help("새 버전이 나왔다 — 눌러서 확인")
        }
    }

    /// 지금 버전. 테스트판은 색을 입힌 알약으로 그려서 곁눈으로도 걸린다.
    @ViewBuilder private func versionLabel(_ text: String?) -> some View {
        if let text {
            Text(text)
                .font(font(9, weight: .semibold, design: .rounded))
                .monospacedDigit()
                .lineLimit(1)
                // 좁은 카드에서 줄바꿈되거나 말줄임표가 뜨지 않게 제 크기를 지킨다.
                .fixedSize()
                .foregroundStyle(versionBadgeIsTest ? palette.testBadge : palette.faintText)
                .shadow(color: palette.textShadow, radius: s(1.5))
                // 카드의 둥근 모서리가 글자를 깎지 않게 띄운다. 모서리 반지름이
                // 20pt라 글자 높이쯤에서 카드 경계가 x≈5.7pt까지 들어와 있는데,
                // 여백 없이 두면 **첫 글자 왼쪽이 잘린다.** 테스트판은 알약 배경
                // 덕에 우연히 여백이 있어서 멀쩡했고, 정식판만 깨져 보였다.
                .padding(.horizontal, s(5))
                .padding(.vertical, s(1))
                .background {
                    if versionBadgeIsTest {
                        Capsule().fill(palette.testBadge.opacity(0.18))
                    }
                }
                .help(versionBadgeIsTest ? "테스트 빌드다 — 배포본이 아니다" : "지금 버전")
        }
    }

    // MARK: - 우측 상단 버튼

    private var controlButtons: some View {
        HStack(spacing: 0) {
            collapseButton
            measureButton
            settingsButton
            refreshButton
        }
        .padding(Self.refreshInset(scale: scale))
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
        .help(mode == .collapsed ? "펼치기" : "접기")
    }

    /// 측정 시작·중지. 재는 동안에는 빨갛게 채워 둔다.
    private var measureButton: some View {
        Button {
            onOpenMeasure?()
        } label: {
            controlLabel(
                systemName: isMeasuring ? "stopwatch.fill" : "stopwatch",
                tint: measureTint,
                hovering: isHoveringMeasure
            )
        }
        .buttonStyle(.plain)
        .onHover { isHoveringMeasure = $0 }
        .help(measureHelp)
    }

    private var measureTint: Color {
        if isMeasuring { return .red }
        return isHoveringMeasure ? palette.controlActive : palette.controlIdle
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
        ((mode == .collapsed) == (expandSide == .right)) ? "chevron.right" : "chevron.left"
    }

    private func controlLabel(systemName: String, tint: Color, hovering: Bool) -> some View {
        Image(systemName: systemName)
            .font(font(9.5, weight: .bold))
            .foregroundStyle(tint)
            .frame(
                width: Self.refreshHitSize(scale: scale),
                height: Self.refreshHitSize(scale: scale)
            )
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

    /// 펼친 보기와 펫 모드가 같이 쓴다. 두 곳에 따로 적으면 한쪽만 고쳐진다.
    private var measureHelp: String {
        isMeasuring ? "측정 중 — 측정 화면 열기" : "측정"
    }

    private var refreshHelp: String {
        if let error = store.errorText {
            return "갱신 실패: \(error) — 클릭해서 다시 시도"
        }
        // **잇달아 누르면 요청 제한에 걸린다.** 그 사이에는 눌러도 안 나가므로,
        // 마우스를 올렸을 때 몇 초 남았는지 알려 준다.
        let remaining = Int(store.fetchCooldown().rounded(.up))
        return remaining > 0 ? "새로고침 — \(remaining)초 뒤에 가능" : "새로고침"
    }

    // MARK: - 링

    private var rings: some View {
        ZStack {
            ringPair(diameter: ringDiameter, outerWidth: outerLineWidth, innerWidth: innerLineWidth)
            ClaudeIconView(
                style: iconStyle,
                size: Self.innerDiameter(
                    outer: ringDiameter,
                    outerWidth: outerLineWidth,
                    gap: s(7)
                ) - innerLineWidth * 2 - s(4),
                eyeColor: palette.markEye,
                owlAnimator: owlAnimator
            )
        }
        .frame(width: ringDiameter, height: ringDiameter)
    }

    /// 안쪽 링은 바깥 링 두께(양쪽) + 간격만큼 줄인다.
    private static func innerDiameter(
        outer: CGFloat,
        outerWidth: CGFloat,
        gap: CGFloat
    ) -> CGFloat {
        outer - outerWidth * 2 - gap
    }

    /// 링 두 개만. 펫 모드는 가운데 아이콘을 따로 크게 그리므로 여기 붙이지 않는다.
    private func ringPair(
        diameter: CGFloat,
        outerWidth: CGFloat,
        innerWidth: CGFloat
    ) -> some View {
        let inner = Self.innerDiameter(outer: diameter, outerWidth: outerWidth, gap: s(7))
        return ZStack {
            // 주간을 다 썼으면 **둘 다** 색을 뺀다. 세션은 쓸 수 없어서고, 주간은
            // 그 자신이 죽은 이유라서다. 하나만 빨갛게 남으면 마스코트는 죽었는데
            // 링은 살아 있어서, 아직 뭔가 되는 것처럼 읽힌다.
            ring(
                window: store.snapshot?.fiveHour,
                diameter: diameter,
                lineWidth: outerWidth,
                isSpent: store.isWeeklySpent
            )
            ring(
                window: store.snapshot?.sevenDay,
                diameter: inner,
                lineWidth: innerWidth,
                isSpent: store.isWeeklySpent
            )
        }
        .frame(width: diameter, height: diameter)
    }

    private func ring(
        window: UsageWindow?,
        diameter: CGFloat,
        lineWidth: CGFloat,
        isSpent: Bool = false
    ) -> some View {
        let fraction = (window?.utilization ?? 0) / 100
        // 다 쓴 링은 사용률 색 대신 회색이다. 숫자는 그대로 두고 색만 뺀다 —
        // 얼마나 썼는지는 여전히 알아야 하고, 쓸 수 없다는 것만 더 알려주면 된다.
        let color = isSpent ? palette.ringSpent : UsageColor.color(for: window?.utilization ?? 0)

        return ZStack {
            Circle()
                .stroke(palette.ringTrack, lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: max(0.004, fraction))
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .shadow(color: color.opacity(0.30), radius: s(1.5))
                .opacity(window == nil ? 0 : 1)
        }
        // 선이 프레임 밖으로 삐져나가지 않게 두께의 절반만큼 안쪽으로 넣는다.
        .padding(lineWidth / 2)
        .frame(width: diameter, height: diameter)
        .animation(.easeOut(duration: 0.5), value: fraction)
    }

    // MARK: - 수치

    private func metric(
        title: String,
        window: UsageWindow?,
        now: Date,
        isSpent: Bool = false
    ) -> some View {
        let utilization = window?.utilization
        // 링과 같은 규칙이다. 링만 회색이고 점은 초록이면 앞뒤가 안 맞는다.
        let color = isSpent ? palette.ringSpent : UsageColor.color(for: utilization ?? 0)

        return VStack(alignment: .leading, spacing: s(1)) {
            HStack(spacing: s(5)) {
                Circle()
                    .fill(window == nil ? palette.mutedDot : color)
                    .frame(width: s(5), height: s(5))
                Text(title)
                    .font(font(10, weight: .semibold))
                    .foregroundStyle(palette.secondaryText)
                Text(utilization.map { "\(Int($0.rounded()))%" } ?? "—")
                    .font(font(14, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(palette.primaryText)
            }
            Text(RemainingTime.text(until: window?.resetsAt, now: now))
                .font(font(9.5, weight: .medium))
                .foregroundStyle(palette.tertiaryText)
        }
    }

}

/// HUD 왼쪽 아래에 붙는 이 앱의 자원 사용량.
/// 링 밖 아래에 붙는 동그란 아이콘 버튼.
///
/// **호버 상태를 제가 들고 있다.** 펼침 보기의 버튼들과 `@State` 를 나눠 쓰면, 그
/// 상태를 적어 주는 `.onHover` 가 펼침 보기에만 달려 있어서 펫 모드에서는 영영
/// 바뀌지 않는다 — 마우스를 올려도 표시가 안 바뀌고, 펼침에서 올린 채로 펫으로
/// 넘어오면 켜진 채 굳는다.
private struct PetCircleButton: View {
    let systemName: String
    let palette: HUDPalette
    let scale: CGFloat
    /// 색을 못 박고 싶을 때. 재는 중인 측정 버튼이 빨갛게 남는 자리다.
    var tint: Color?
    let action: () -> Void

    @State private var isHovering = false

    private func s(_ value: CGFloat) -> CGFloat { value * scale }

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: s(11), weight: .semibold))
                .foregroundStyle(tint ?? (isHovering ? palette.controlActive : palette.controlIdle))
                .frame(width: s(24), height: s(24))
                .background {
                    // 투명한 배경 위에 뜨는 버튼이라 제 바탕이 있어야 읽힌다.
                    Circle().fill(Color(nsColor: palette.backdrop(opacity: 0.92)))
                }
                .overlay {
                    Circle().strokeBorder(palette.ringTrack, lineWidth: s(1))
                }
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .onHover { isHovering = $0 }
    }
}

struct ProcessStatsRow: View {
    @ObservedObject var monitor: ProcessUsageMonitor
    let palette: HUDPalette
    var scale: CGFloat = 1

    var body: some View {
        HStack(spacing: 6 * scale) {
            label("CPU", value: monitor.usage.cpuText)
            label("MEM", value: monitor.usage.footprintText)
            Spacer(minLength: 0)
        }
        .help("dong-csu 자신이 쓰는 CPU와 메모리")
    }

    private func label(_ title: String, value: String) -> some View {
        HStack(spacing: 3 * scale) {
            Text(title)
                .font(.system(size: 8 * scale, weight: .semibold))
                .foregroundStyle(palette.faintText)
            Text(value)
                .font(.system(size: 9 * scale, weight: .medium, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(palette.tertiaryText)
        }
    }
}
