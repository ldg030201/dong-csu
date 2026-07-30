import AppKit
import Combine
import SwiftUI

/// 포커스를 절대 가져가지 않는 항상-위 패널.
final class HUDPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// 링을 감싸고 드래그 이동 / 우클릭 메뉴를 처리하는 투명 오버레이.
/// 링 자체는 조작할 게 없으므로 마우스 이벤트를 전부 여기서 받는다.
final class HUDInteractionView: NSView {
    var onDrag: (@MainActor (CGSize) -> Void)?
    var onDragEnded: (@MainActor () -> Void)?
    var menuBuilder: (@MainActor () -> NSMenu)?

    private var dragOrigin: NSPoint?

    override func mouseDown(with event: NSEvent) {
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

    private let hosting: NSHostingView<UsageHUDView>

    private static let originXKey = "hud.origin.x"
    private static let originYKey = "hud.origin.y"
    private static let iconStyleKey = "hud.iconStyle"
    private static let hiddenKey = "hud.hidden"
    private static let margin: CGFloat = 16

    init(store: UsageStore) {
        self.store = store

        let size = UsageHUDView.size
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

        let container = NSView(frame: NSRect(origin: .zero, size: size))
        container.wantsLayer = true
        container.layer?.cornerRadius = UsageHUDView.cornerRadius
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

        let iconStyle = ClaudeIconStyle(
            rawValue: UserDefaults.standard.string(forKey: Self.iconStyleKey) ?? ""
        ) ?? .default
        hosting = NSHostingView(rootView: UsageHUDView(store: store, iconStyle: iconStyle))
        hosting.frame = container.bounds
        hosting.autoresizingMask = [.width, .height]
        container.addSubview(hosting)

        interactionView.frame = container.bounds
        interactionView.autoresizingMask = [.width, .height]
        container.addSubview(interactionView)

        panel.contentView = container

        interactionView.onDrag = { [weak self] delta in self?.move(by: delta) }
        interactionView.onDragEnded = { [weak self] in self?.saveOrigin() }
        interactionView.menuBuilder = { [weak self] in self?.makeMenu() ?? NSMenu() }

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
        panel.orderFrontRegardless()
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
            item.state = hosting.rootView.iconStyle == style ? .on : .off
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
    @objc private func handleResetPosition() { resetPosition() }

    @objc private func handleToggleHUD() {
        setHUDVisible(!panel.isVisible)
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
    }

    @objc private func handleIconStyle(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String,
              let style = ClaudeIconStyle(rawValue: raw) else { return }
        UserDefaults.standard.set(raw, forKey: Self.iconStyleKey)
        hosting.rootView = UsageHUDView(store: store, iconStyle: style)
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
