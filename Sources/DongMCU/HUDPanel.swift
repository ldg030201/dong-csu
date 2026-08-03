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
    var onDrag: (@MainActor (CGSize) -> Void)?
    var onDragEnded: (@MainActor () -> Void)?
    var menuBuilder: (@MainActor () -> NSMenu)?
    var onDoubleClick: (@MainActor () -> Void)?

    /// 이 영역의 마우스 이벤트는 아래(SwiftUI)로 흘려보낸다. 새로고침 버튼용.
    var passThroughRect: CGRect = .zero

    private var dragOrigin: NSPoint?

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        // point는 superview 좌표계로 들어온다.
        if passThroughRect.contains(convert(point, from: superview)) { return nil }
        return super.hitTest(point)
    }

    override func mouseDown(with event: NSEvent) {
        // 더블클릭으로 접었다 폈다 한다. 이때는 드래그를 시작하지 않는다.
        if event.clickCount == 2 {
            dragOrigin = nil
            onDoubleClick?()
            return
        }
        dragOrigin = NSEvent.mouseLocation
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = dragOrigin else { return }
        let current = NSEvent.mouseLocation
        onDrag?(CGSize(width: current.x - start.x, height: current.y - start.y))
        dragOrigin = current
    }

    override func mouseUp(with event: NSEvent) {
        dragOrigin = nil
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
    private var iconStyle: ClaudeIconStyle
    private var isCollapsed: Bool

    private static let originXKey = "hud.origin.x"
    private static let originYKey = "hud.origin.y"
    private static let iconStyleKey = "hud.iconStyle"
    private static let hiddenKey = "hud.hidden"
    private static let collapsedKey = "hud.collapsed"
    private static let margin: CGFloat = 16

    init(store: UsageStore) {
        self.store = store

        isCollapsed = UserDefaults.standard.bool(forKey: Self.collapsedKey)
        let size = UsageHUDView.size(collapsed: isCollapsed)
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
        // 시스템이 라이트 모드여도 배경을 어둡게 유지한다. 흰 글자 가독성이 여기서 나온다.
        panel.appearance = NSAppearance(named: .darkAqua)

        container = NSView(frame: NSRect(origin: .zero, size: size))
        container.wantsLayer = true
        container.layer?.cornerRadius = UsageHUDView.cornerRadius(collapsed: isCollapsed)
        container.layer?.masksToBounds = true
        container.layer?.borderWidth = 1
        container.layer?.borderColor = NSColor.white.withAlphaComponent(0.10).cgColor

        // 배경은 단색 반투명. NSVisualEffectView의 behindWindow 블러를 쓰면
        // 항상 위에 떠 있는 창이라 뒤 내용이 바뀔 때마다 WindowServer가 계속
        // 블러를 다시 합성한다. 어두운 카드에서는 눈에 차이가 없고 비용만 든다.
        let backdrop = NSView(frame: container.bounds)
        backdrop.wantsLayer = true
        backdrop.layer?.backgroundColor = NSColor(calibratedWhite: 0.09, alpha: 0.92).cgColor
        backdrop.autoresizingMask = [.width, .height]
        container.addSubview(backdrop)

        iconStyle = ClaudeIconStyle(
            rawValue: UserDefaults.standard.string(forKey: Self.iconStyleKey) ?? ""
        ) ?? .default
        hosting = FirstMouseHostingView(
            rootView: UsageHUDView(
                store: store,
                iconStyle: iconStyle,
                showsCountdown: false,
                isCollapsed: isCollapsed
            )
        )
        hosting.frame = container.bounds
        hosting.autoresizingMask = [.width, .height]
        container.addSubview(hosting)

        interactionView.frame = container.bounds
        interactionView.autoresizingMask = [.width, .height]
        interactionView.passThroughRect = UsageHUDView.refreshHitRectInPanel(collapsed: isCollapsed)
        container.addSubview(interactionView)

        panel.contentView = container

        interactionView.onDrag = { [weak self] delta in self?.move(by: delta) }
        interactionView.onDragEnded = { [weak self] in self?.saveOrigin() }
        interactionView.menuBuilder = { [weak self] in self?.makeMenu() ?? NSMenu() }
        interactionView.onDoubleClick = { [weak self] in self?.handleToggleCollapse() }

        panel.setFrameOrigin(restoredOrigin())
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(handleScreenChange),
            name: NSApplication.didChangeScreenParametersNotification,
            object: nil
        )
        observeStore()
    }

    /// 직전에 숨겨둔 상태였다면 그대로 숨긴 채로 시작한다(메뉴바 아이콘으로 다시 켤 수 있다).
    func show() {
        guard !UserDefaults.standard.bool(forKey: Self.hiddenKey) else { return }
        setHUDVisible(true)
    }

    // MARK: - 위치

    private func move(by delta: CGSize) {
        var origin = panel.frame.origin
        origin.x += delta.width
        origin.y += delta.height
        panel.setFrameOrigin(origin)
    }

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
        let size = panel.frame.size
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

    private func resetPosition() {
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

        menu.addItem(.separator())
        let quit = NSMenuItem(title: "dong-mcu 종료", action: #selector(handleQuit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
    }

    @objc private func handleRefresh() { store.refresh(force: true) }

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
        setHUDVisible(!panel.isVisible)
    }

    @objc private func handleToggleCollapse() {
        setCollapsed(!isCollapsed)
    }

    /// 접거나 펼친다. 오른쪽 위 모서리를 붙잡아 두어서 크기가 바뀌어도 자리가 튀지 않는다.
    private func setCollapsed(_ collapsed: Bool) {
        isCollapsed = collapsed
        UserDefaults.standard.set(collapsed, forKey: Self.collapsedKey)

        let newSize = UsageHUDView.size(collapsed: collapsed)
        let old = panel.frame
        let origin = NSPoint(x: old.maxX - newSize.width, y: old.maxY - newSize.height)
        let target = NSRect(origin: origin, size: newSize)

        panel.setFrame(target, display: true)
        panel.setFrameOrigin(clampedOrigin(panel.frame.origin) ?? defaultOrigin())

        container.layer?.cornerRadius = UsageHUDView.cornerRadius(collapsed: collapsed)
        interactionView.passThroughRect = UsageHUDView.refreshHitRectInPanel(collapsed: collapsed)
        rebuildRootView()
        saveOrigin()
    }

    private func setHUDVisible(_ visible: Bool) {
        if visible {
            // 숨겨둔 동안 화면 구성이 바뀌었을 수 있으니 위치를 다시 확인한다.
            panel.setFrameOrigin(clampedOrigin(panel.frame.origin) ?? defaultOrigin())
            panel.orderFrontRegardless()
        } else {
            panel.orderOut(nil)
        }
        UserDefaults.standard.set(!visible, forKey: Self.hiddenKey)
        rebuildRootView()
    }

    /// 표시 상태가 바뀌면 뷰를 다시 만든다. 숨겨져 있는 동안 카운트다운의 1초 타이머를 끄기 위해서다.
    private func rebuildRootView() {
        hosting.rootView = UsageHUDView(
            store: store,
            iconStyle: iconStyle,
            showsCountdown: panel.isVisible && !isCollapsed,
            isCollapsed: isCollapsed
        )
    }

    @objc private func handleIconStyle(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String,
              let style = ClaudeIconStyle(rawValue: raw) else { return }
        UserDefaults.standard.set(raw, forKey: Self.iconStyleKey)
        iconStyle = style
        rebuildRootView()
    }
    @objc private func handleQuit() { NSApp.terminate(nil) }

    private func observeStore() {
        store.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.updateTooltip() }
            .store(in: &cancellables)
        updateTooltip()
    }

    private func updateTooltip() {
        interactionView.toolTip = store.summaryText
    }
}
