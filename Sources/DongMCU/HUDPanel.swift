import AppKit
import Combine
import SwiftUI

/// 포커스를 절대 가져가지 않는 항상-위 패널.
final class HUDPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    /// AppKit은 창이 현재 화면을 벗어나지 않도록 프레임을 자동으로 잡아당긴다.
    /// 그 보정 때문에 HUD를 다른 모니터로 끌어다 놓을 수 없어서, 제안된 위치를 그대로 쓴다.
    /// 화면 밖으로 완전히 나가는 건 HUDController의 clampedOrigin이 따로 막는다.
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        frameRect
    }
}

/// 패널이 key window가 되지 않기 때문에, 비활성 창의 첫 클릭이 삼켜지지 않도록
/// 명시적으로 첫 클릭을 받는다. 이게 없으면 새로고침 버튼이 한 번에 안 눌린다.
final class FirstMouseHostingView<Content: View>: NSHostingView<Content> {
    required init(rootView: Content) {
        super.init(rootView: rootView)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not used")
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// 링을 감싸고 드래그 이동 / 우클릭 메뉴를 처리하는 투명 오버레이.
/// 링 자체는 조작할 게 없으므로 마우스 이벤트를 전부 여기서 받는다.
final class HUDInteractionView: NSView {
    var onDragTo: (@MainActor (NSPoint) -> Void)?
    var onDragEnded: (@MainActor () -> Void)?
    var menuBuilder: (@MainActor () -> NSMenu)?
    var onDoubleClick: (@MainActor () -> Void)?

    /// 이 영역의 마우스 이벤트는 아래(SwiftUI)로 흘려보낸다. 새로고침 버튼용.
    var passThroughRect: CGRect = .zero

    /// 마우스와 창 원점 사이의 간격. 드래그 내내 이 값을 유지한다.
    private var dragOffset: CGSize?

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        // point는 superview 좌표계로 들어온다.
        if passThroughRect.contains(convert(point, from: superview)) { return nil }
        return super.hitTest(point)
    }

    override func mouseDown(with event: NSEvent) {
        // 더블클릭으로 접었다 폈다 한다. 이때는 드래그를 시작하지 않는다.
        if event.clickCount == 2 {
            dragOffset = nil
            onDoubleClick?()
            return
        }
        guard let origin = window?.frame.origin else { return }
        let mouse = NSEvent.mouseLocation
        dragOffset = CGSize(width: mouse.x - origin.x, height: mouse.y - origin.y)
    }

    /// 창을 절대 좌표로 옮긴다.
    /// 이동량(델타)을 더해 나가면 이벤트가 하나만 누락돼도 그만큼 어긋난 채로 남고,
    /// 그 오차가 계속 쌓여서 창이 커서에서 점점 멀어진다.
    override func mouseDragged(with event: NSEvent) {
        guard let offset = dragOffset else { return }
        let mouse = NSEvent.mouseLocation
        onDragTo?(NSPoint(x: mouse.x - offset.width, y: mouse.y - offset.height))
    }

    override func mouseUp(with event: NSEvent) {
        dragOffset = nil
        onDragEnded?()
    }

    override func rightMouseDown(with event: NSEvent) {
        guard let menu = menuBuilder?() else { return }
        NSMenu.popUpContextMenu(menu, with: event, for: self)
    }
}

/// 패널 생성 · 위치 기억 · 컨텍스트 메뉴를 담당한다.
@MainActor
final class HUDController {
    private let store: UsageStore
    private let panel: HUDPanel
    private let interactionView = HUDInteractionView()
    private var cancellables: Set<AnyCancellable> = []

    private let hosting: FirstMouseHostingView<UsageHUDView>
    private let container: NSView
    let settings: HUDSettings
    /// 설정 창을 여는 동작. AppDelegate가 꽂아준다.
    var onOpenSettings: (@MainActor () -> Void)?
    private let backdrop = NSView()
    private let usageMonitor = ProcessUsageMonitor()

    private static let originXKey = "hud.origin.x"
    private static let originYKey = "hud.origin.y"
    private static let margin: CGFloat = 16

    private var isCollapsed: Bool { settings.isCollapsed }
    private var iconStyle: ClaudeIconStyle { settings.iconStyle }
    private var appearance: HUDAppearance { settings.appearance }

    init(store: UsageStore, settings: HUDSettings) {
        self.store = store
        self.settings = settings

        let size = UsageHUDView.size(
            collapsed: settings.isCollapsed,
            showsStats: settings.showsProcessStats
        )
        panel = HUDPanel(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isFloatingPanel = true
        panel.level = .floating
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]
        panel.appearance = settings.appearance.nsAppearance

        container = NSView(frame: NSRect(origin: .zero, size: size))
        container.wantsLayer = true
        container.layer?.cornerRadius = UsageHUDView.cornerRadius(collapsed: settings.isCollapsed)
        container.layer?.masksToBounds = true
        container.layer?.borderWidth = 1

        // 배경은 단색 반투명. NSVisualEffectView의 behindWindow 블러를 쓰면
        // 항상 위에 떠 있는 창이라 뒤 내용이 바뀔 때마다 WindowServer가 계속
        // 블러를 다시 합성한다. 어두운 카드에서는 눈에 차이가 없고 비용만 든다.
        backdrop.frame = container.bounds
        backdrop.wantsLayer = true
        backdrop.autoresizingMask = [.width, .height]
        container.addSubview(backdrop)

        hosting = FirstMouseHostingView(
            rootView: UsageHUDView(
                store: store,
                iconStyle: settings.iconStyle,
                showsCountdown: false,
                isCollapsed: settings.isCollapsed,
                palette: HUDPalette(isDark: true)
            )
        )
        container.addSubview(hosting)

        interactionView.frame = container.bounds
        interactionView.autoresizingMask = [.width, .height]
        interactionView.passThroughRect = UsageHUDView.controlsHitRectInPanel(
            collapsed: settings.isCollapsed,
            side: settings.expandSide
        )
        container.addSubview(interactionView)

        panel.contentView = container

        interactionView.onDragTo = { [weak self] origin in self?.panel.setFrameOrigin(origin) }
        interactionView.onDragEnded = { [weak self] in self?.saveOrigin() }
        interactionView.menuBuilder = { [weak self] in self?.makeMenu() ?? NSMenu() }
        interactionView.onDoubleClick = { [weak self] in self?.handleToggleCollapse() }

        applyAppearance()
        layoutHosting(for: size)
        syncUsageMonitor(visible: true)

        panel.setFrameOrigin(restoredOrigin())
        // 시스템 테마가 바뀌면 .system 설정일 때만 따라간다.
        DistributedNotificationCenter.default.addObserver(
            self,
            selector: #selector(handleSystemThemeChange),
            name: NSNotification.Name("AppleInterfaceThemeChangedNotification"),
            object: nil
        )
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(handleScreenChange),
            name: NSApplication.didChangeScreenParametersNotification,
            object: nil
        )
        observeStore()
        observeSettings()
    }

    /// @Published는 값이 바뀌기 "직전"에 알림을 보낸다. 그래서 한 턴 미뤄서 읽어야
    /// 새 값이 들어와 있다. 이때 RunLoop.main을 쓰면 안 된다 — 기본 모드에서만 돌기 때문에
    /// 마우스를 누르고 있는 동안(이벤트 추적 모드)에는 실행이 미뤄져서, 버튼을 눌러도
    /// 손을 뗄 때까지 반응이 없다. DispatchQueue.main은 모드와 무관하게 처리된다.
    private func observeSettings() {
        settings.$appearance
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.applyAppearance() }
            .store(in: &cancellables)

        settings.$iconStyle
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.rebuildRootView() }
            .store(in: &cancellables)

        settings.$isCollapsed
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.applyCollapsed() }
            .store(in: &cancellables)

        settings.$isHUDVisible
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.applyHUDVisible() }
            .store(in: &cancellables)

        settings.$expandSide
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.applyExpandSide() }
            .store(in: &cancellables)

        settings.$backdropOpacity
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.applyAppearance() }
            .store(in: &cancellables)

        settings.$showsProcessStats
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.applyProcessStats() }
            .store(in: &cancellables)
    }

    /// 자원 사용량 표시를 켜고 끈다. 크기까지 바뀌므로 레이아웃도 다시 잡는다.
    private func applyProcessStats() {
        syncUsageMonitor()
        applyCollapsed()
    }

    /// 보이지도 않는데 표본을 뜰 이유가 없다. 조건이 바뀌는 자리마다 이걸 부른다.
    private func syncUsageMonitor(collapsed: Bool? = nil, visible: Bool? = nil) {
        let isCollapsed = collapsed ?? settings.isCollapsed
        let isVisible = visible ?? panel.isVisible

        if settings.showsProcessStats, isVisible, !isCollapsed {
            usageMonitor.start()
        } else {
            usageMonitor.stop()
        }
    }

    /// 펼침 방향이 바뀌면 손잡이(링·버튼)가 반대쪽으로 옮겨간다.
    private func applyExpandSide() {
        interactionView.passThroughRect = UsageHUDView.controlsHitRectInPanel(
            collapsed: settings.isCollapsed,
            side: settings.expandSide
        )
        rebuildRootView()
        layoutHosting(for: UsageHUDView.size(collapsed: settings.isCollapsed, showsStats: settings.showsProcessStats))
    }

    /// 크기가 바뀔 때 고정할 모서리를 정한다.
    /// 오른쪽으로 펼치면 왼쪽 변을, 왼쪽으로 펼치면 오른쪽 변을 붙잡는다. 위쪽은 항상 고정.
    private func targetFrame(for size: CGSize) -> NSRect {
        let old = panel.frame
        let x = settings.expandSide == .right ? old.minX : old.maxX - size.width
        let origin = NSPoint(x: x, y: old.maxY - size.height)
        return NSRect(origin: clampedOrigin(origin, size: size) ?? origin, size: size)
    }

    /// 내용 뷰를 고정 크기로 두고 컨테이너가 잘라내게 한다.
    /// 이래야 창이 커지고 작아지는 동안 글자가 다시 배치되지 않고 서랍처럼 드러난다.
    private func layoutHosting(for size: CGSize) {
        let bounds = container.bounds
        hosting.autoresizingMask = settings.expandSide == .left
            ? [.minXMargin, .minYMargin]
            : [.maxXMargin, .minYMargin]
        hosting.frame = CGRect(
            x: settings.expandSide == .left ? bounds.width - size.width : 0,
            y: bounds.height - size.height,
            width: size.width,
            height: size.height
        )
    }

    private func animate(to frame: NSRect, completion: (() -> Void)?) {
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.22
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            panel.animator().setFrame(frame, display: true)
        }, completionHandler: completion)
    }

    /// HUD가 실제로 놓여 있는 화면.
    var currentScreen: NSScreen? {
        NSScreen.screens.first { $0.frame.intersects(panel.frame) } ?? NSScreen.main
    }

    /// 직전에 숨겨둔 상태였다면 그대로 숨긴 채로 시작한다(메뉴바 아이콘으로 다시 켤 수 있다).
    func show() {
        applyHUDVisible()
    }

    // MARK: - 위치

    private func saveOrigin() {
        let origin = panel.frame.origin
        UserDefaults.standard.set(Double(origin.x), forKey: Self.originXKey)
        UserDefaults.standard.set(Double(origin.y), forKey: Self.originYKey)
    }

    private func restoredOrigin() -> NSPoint {
        let defaults = UserDefaults.standard
        guard defaults.object(forKey: Self.originXKey) != nil,
              defaults.object(forKey: Self.originYKey) != nil
        else { return defaultOrigin() }

        let saved = NSPoint(
            x: defaults.double(forKey: Self.originXKey),
            y: defaults.double(forKey: Self.originYKey)
        )
        return clampedOrigin(saved) ?? defaultOrigin()
    }

    /// 저장된 위치가 화면 밖으로 걸치면 화면 안쪽으로 밀어 넣는다.
    /// HUD 크기가 바뀌거나 모니터 구성이 달라졌을 때 잘려 보이는 걸 막는다.
    private func clampedOrigin(_ origin: NSPoint) -> NSPoint? {
        clampedOrigin(origin, size: panel.frame.size)
    }

    private func clampedOrigin(_ origin: NSPoint, size: NSSize) -> NSPoint? {
        let frame = NSRect(origin: origin, size: size)
        let center = NSPoint(x: frame.midX, y: frame.midY)

        let screen = NSScreen.screens.first { $0.frame.contains(center) }
            ?? NSScreen.screens.max { lhs, rhs in
                overlap(lhs, frame) < overlap(rhs, frame)
            }
        guard let screen, overlap(screen, frame) > 0 else { return nil }

        let area = screen.visibleFrame
        guard area.width >= size.width, area.height >= size.height else { return nil }
        return NSPoint(
            x: min(max(origin.x, area.minX), area.maxX - size.width),
            y: min(max(origin.y, area.minY), area.maxY - size.height)
        )
    }

    private func overlap(_ screen: NSScreen, _ frame: NSRect) -> CGFloat {
        let intersection = screen.frame.intersection(frame)
        return intersection.isNull ? 0 : intersection.width * intersection.height
    }

    /// 모니터가 붙거나 빠지면 위치를 다시 화면 안으로 맞춘다.
    /// 화면 정보가 잡히지 않거나 이미 화면 안이면 아무것도 하지 않는다.
    /// (무조건 저장하면 일시적인 화면 변경에 사용자가 옮겨둔 위치가 날아간다.)
    @objc private func handleScreenChange() {
        guard let corrected = clampedOrigin(panel.frame.origin),
              corrected != panel.frame.origin
        else { return }
        panel.setFrameOrigin(corrected)
        saveOrigin()
    }

    /// 기본 위치: 주 화면 오른쪽 위.
    private func defaultOrigin() -> NSPoint {
        guard let screen = NSScreen.main ?? NSScreen.screens.first else { return .zero }
        let area = screen.visibleFrame
        let size = panel.frame.size
        return NSPoint(
            x: area.maxX - size.width - Self.margin,
            y: area.maxY - size.height - Self.margin
        )
    }

    func resetPosition() {
        panel.setFrameOrigin(defaultOrigin())
        saveOrigin()
    }

    // MARK: - 메뉴 · 툴팁

    private func makeMenu() -> NSMenu {
        let menu = NSMenu()
        populateMenu(menu)
        return menu
    }

    /// HUD 우클릭 메뉴와 메뉴바 아이콘 메뉴가 같은 내용을 쓴다.
    /// NSMenuItem은 메뉴 하나에만 속할 수 있어서, 메뉴를 만들어 넘기는 대신 채워준다.
    func populateMenu(_ menu: NSMenu) {
        let status = NSMenuItem(title: store.summaryText, action: nil, keyEquivalent: "")
        status.isEnabled = false
        menu.addItem(status)
        menu.addItem(.separator())

        let refresh = NSMenuItem(title: "새로고침", action: #selector(handleRefresh), keyEquivalent: "r")
        refresh.target = self
        menu.addItem(refresh)

        let settingsItem = NSMenuItem(
            title: "설정…",
            action: #selector(handleOpenSettings),
            keyEquivalent: ","
        )
        settingsItem.target = self
        menu.addItem(settingsItem)

        let login = NSMenuItem(
            title: "Claude Code 재로그인…",
            action: #selector(handleLogin),
            keyEquivalent: ""
        )
        login.target = self
        // 토큰이 만료된 상태면 이게 해야 할 일이라는 걸 눈에 띄게 한다.
        if store.needsReauth {
            login.attributedTitle = NSAttributedString(
                string: login.title,
                attributes: [.font: NSFont.boldSystemFont(ofSize: NSFont.systemFontSize)]
            )
        }
        menu.addItem(login)

        let collapse = NSMenuItem(
            title: isCollapsed ? "펼치기" : "접기",
            action: #selector(handleToggleCollapse),
            keyEquivalent: ""
        )
        collapse.target = self
        collapse.isEnabled = panel.isVisible
        menu.addItem(collapse)

        let toggle = NSMenuItem(
            title: panel.isVisible ? "HUD 숨기기" : "HUD 보이기",
            action: #selector(handleToggleHUD),
            keyEquivalent: ""
        )
        toggle.target = self
        menu.addItem(toggle)

        let reset = NSMenuItem(title: "위치 초기화", action: #selector(handleResetPosition), keyEquivalent: "")
        reset.target = self
        reset.isEnabled = panel.isVisible
        menu.addItem(reset)

        let iconMenu = NSMenu()
        let styles: [(ClaudeIconStyle, String)] = [
            (.clawd, "Clawd (Claude Code 마스코트)"),
            (.appIcon, "Claude 앱 아이콘"),
            (.mark, "버스트 마크"),
        ]
        for (style, title) in styles {
            let item = NSMenuItem(title: title, action: #selector(handleIconStyle(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = style.rawValue
            item.state = iconStyle == style ? .on : .off
            iconMenu.addItem(item)
        }
        let iconItem = NSMenuItem(title: "가운데 아이콘", action: nil, keyEquivalent: "")
        iconItem.submenu = iconMenu
        menu.addItem(iconItem)

        let themeMenu = NSMenu()
        for value in HUDAppearance.allCases {
            let item = NSMenuItem(title: value.title, action: #selector(handleAppearance(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = value.rawValue
            item.state = appearance == value ? .on : .off
            themeMenu.addItem(item)
        }
        let themeItem = NSMenuItem(title: "테마", action: nil, keyEquivalent: "")
        themeItem.submenu = themeMenu
        menu.addItem(themeItem)

        menu.addItem(.separator())
        let quit = NSMenuItem(title: "dong-mcu 종료", action: #selector(handleQuit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
    }

    @objc private func handleRefresh() { store.refresh(force: true) }

    func startLogin() {
        handleLogin()
    }

    @objc private func handleLogin() {
        guard ClaudeCLI.openLogin() else {
            NSApp.activate(ignoringOtherApps: true)
            let alert = NSAlert()
            alert.messageText = "Claude Code 실행 파일을 찾지 못했습니다"
            alert.informativeText = "터미널에서 직접 claude auth login 을 실행해 주세요."
            alert.runModal()
            return
        }
        // 로그인이 끝나면 새 토큰이 키체인에 쓰인다. 잠시 뒤 한 번 더 시도한다.
        DispatchQueue.main.asyncAfter(deadline: .now() + 30) { [weak self] in
            self?.store.refresh(force: true)
        }
    }
    @objc private func handleResetPosition() { resetPosition() }

    @objc private func handleToggleHUD() {
        settings.isHUDVisible.toggle()
    }

    /// 패널 외형·배경색·팔레트를 현재 설정에 맞춘다.
    private func applyAppearance() {
        panel.appearance = appearance.nsAppearance
        let palette = HUDPalette(isDark: appearance.isDark)
        backdrop.layer?.backgroundColor = palette.backdrop(opacity: settings.backdropOpacity).cgColor
        container.layer?.borderColor = palette.border.cgColor
        rebuildRootView()
    }

    @objc private func handleSystemThemeChange() {
        guard appearance == .system else { return }
        // 알림이 올 때 NSApp.effectiveAppearance가 아직 갱신되지 않은 경우가 있어 한 턴 미룬다.
        DispatchQueue.main.async { [weak self] in self?.applyAppearance() }
    }

    @objc private func handleAppearance(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String,
              let value = HUDAppearance(rawValue: raw) else { return }
        settings.appearance = value
    }

    @objc private func handleToggleCollapse() {
        settings.isCollapsed.toggle()
    }

    @objc private func handleOpenSettings() {
        onOpenSettings?()
    }

    /// 접거나 펼친다. 오른쪽 위 모서리를 붙잡아 두어서 크기가 바뀌어도 자리가 튀지 않는다.
    private func applyCollapsed() {
        let collapsed = settings.isCollapsed
        let newSize = UsageHUDView.size(collapsed: collapsed, showsStats: settings.showsProcessStats)
        let target = targetFrame(for: newSize)

        // 애니메이션 도중에 표본이 갱신되면 화면이 다시 배치되면서 끊겨 보인다.
        // 잠시 멈추고 끝난 뒤에 다시 맞춘다.
        usageMonitor.stop()
        container.layer?.cornerRadius = UsageHUDView.cornerRadius(collapsed: collapsed)
        interactionView.passThroughRect = UsageHUDView.controlsHitRectInPanel(
            collapsed: collapsed,
            side: settings.expandSide
        )

        if collapsed {
            // 접을 때는 펼친 내용을 그대로 둔 채 창만 줄여서, 서랍이 밀려 들어가는 것처럼 보이게 한다.
            animate(to: target) { [weak self] in
                guard let self else { return }
                self.rebuildRootView()
                self.layoutHosting(for: newSize)
                self.saveOrigin()
                self.syncUsageMonitor()
            }
        } else {
            // 펼칠 때는 내용을 먼저 깔아두고 창을 키워서 드러나게 한다.
            rebuildRootView()
            layoutHosting(for: newSize)
            animate(to: target) { [weak self] in
                self?.saveOrigin()
                self?.syncUsageMonitor()
            }
        }
    }

    private func applyHUDVisible() {
        let visible = settings.isHUDVisible
        if visible {
            // 숨겨둔 동안 화면 구성이 바뀌었을 수 있으니 위치를 다시 확인한다.
            panel.setFrameOrigin(clampedOrigin(panel.frame.origin) ?? defaultOrigin())
            panel.orderFrontRegardless()
        } else {
            panel.orderOut(nil)
        }
        syncUsageMonitor(visible: visible)
        rebuildRootView()
    }

    /// 표시 상태가 바뀌면 뷰를 다시 만든다. 숨겨져 있는 동안 카운트다운의 1초 타이머를 끄기 위해서다.
    private func rebuildRootView() {
        hosting.rootView = UsageHUDView(
            store: store,
            iconStyle: iconStyle,
            showsCountdown: panel.isVisible && !isCollapsed,
            isCollapsed: isCollapsed,
            palette: HUDPalette(isDark: appearance.isDark),
            onOpenSettings: { [weak self] in self?.onOpenSettings?() },
            onToggleCollapse: { [weak self] in self?.handleToggleCollapse() },
            expandSide: settings.expandSide,
            // 표본 타이머가 도는지가 아니라 "표시 설정"을 봐야 한다.
            // 접기 애니메이션 동안에는 타이머를 잠시 멈추는데, 그때 뷰를 다시 만들면
            // 줄이 통째로 사라져서 펼친 뒤에도 안 보였다.
            usageMonitor: settings.showsProcessStats && !isCollapsed ? usageMonitor : nil
        )
    }

    @objc private func handleIconStyle(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String,
              let style = ClaudeIconStyle(rawValue: raw) else { return }
        settings.iconStyle = style
    }
    @objc private func handleQuit() { NSApp.terminate(nil) }

    private func observeStore() {
        store.objectWillChange
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.updateTooltip() }
            .store(in: &cancellables)
        updateTooltip()
    }

    private func updateTooltip() {
        interactionView.toolTip = store.summaryText
    }
}
