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

        let blur = NSVisualEffectView(frame: container.bounds)
        blur.material = .hudWindow
        blur.blendingMode = .behindWindow
        blur.state = .active
        blur.autoresizingMask = [.width, .height]
        container.addSubview(blur)

        // 밝은 배경 위에서도 흰 글자가 뜨도록 어두운 막을 한 겹 깐다.
        let scrim = NSView(frame: container.bounds)
        scrim.wantsLayer = true
        scrim.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.34).cgColor
        scrim.autoresizingMask = [.width, .height]
        container.addSubview(scrim)

        let iconStyle = ClaudeIconStyle(
            rawValue: UserDefaults.standard.string(forKey: Self.iconStyleKey) ?? ""
        ) ?? .appIcon
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

    func show() {
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
    @objc private func handleScreenChange() {
        let corrected = clampedOrigin(panel.frame.origin) ?? defaultOrigin()
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

        let status = NSMenuItem(title: statusLine(), action: nil, keyEquivalent: "")
        status.isEnabled = false
        menu.addItem(status)
        menu.addItem(.separator())

        let refresh = NSMenuItem(title: "새로고침", action: #selector(handleRefresh), keyEquivalent: "r")
        refresh.target = self
        menu.addItem(refresh)

        let reset = NSMenuItem(title: "위치 초기화", action: #selector(handleResetPosition), keyEquivalent: "")
        reset.target = self
        menu.addItem(reset)

        let iconMenu = NSMenu()
        for (style, title) in [(ClaudeIconStyle.appIcon, "Claude 앱 아이콘"), (.mark, "직접 그린 마크")] {
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
        let quit = NSMenuItem(title: "종료", action: #selector(handleQuit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        return menu
    }

    @objc private func handleRefresh() { store.refresh(force: true) }
    @objc private func handleResetPosition() { resetPosition() }

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
        interactionView.toolTip = statusLine()
    }

    private func statusLine() -> String {
        guard let snapshot = store.snapshot else {
            return store.errorText ?? "사용량 불러오는 중…"
        }

        var parts: [String] = []
        if let plan = snapshot.planName { parts.append(plan) }
        if let fiveHour = snapshot.fiveHour {
            parts.append("5시간 \(Int(fiveHour.utilization.rounded()))%\(resetSuffix(fiveHour.resetsAt))")
        }
        if let sevenDay = snapshot.sevenDay {
            parts.append("7일 \(Int(sevenDay.utilization.rounded()))%")
        }
        if let error = store.errorText {
            parts.append("(갱신 실패: \(error))")
        }
        return parts.isEmpty ? "사용량 정보 없음" : parts.joined(separator: " · ")
    }

    private func resetSuffix(_ resetsAt: Date?) -> String {
        guard let resetsAt else { return "" }
        let remaining = resetsAt.timeIntervalSinceNow
        guard remaining > 0 else { return "" }
        let hours = Int(remaining) / 3600
        let minutes = (Int(remaining) % 3600) / 60
        return hours > 0 ? " (\(hours)시간 \(minutes)분 후 초기화)" : " (\(minutes)분 후 초기화)"
    }
}
