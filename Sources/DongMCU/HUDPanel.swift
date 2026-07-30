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

    private static let originXKey = "hud.origin.x"
    private static let originYKey = "hud.origin.y"
    private static let margin: CGFloat = 16

    init(store: UsageStore) {
        self.store = store

        let size = UsageRingView.diameter
        panel = HUDPanel(
            contentRect: NSRect(x: 0, y: 0, width: size, height: size),
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

        let container = NSView(frame: NSRect(x: 0, y: 0, width: size, height: size))
        container.wantsLayer = true
        container.layer?.cornerRadius = size / 2
        container.layer?.masksToBounds = true

        let blur = NSVisualEffectView(frame: container.bounds)
        blur.material = .hudWindow
        blur.blendingMode = .behindWindow
        blur.state = .active
        blur.autoresizingMask = [.width, .height]
        container.addSubview(blur)

        let hosting = NSHostingView(rootView: UsageRingView(store: store))
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
        // 모니터 구성이 바뀌어 화면 밖으로 나간 위치면 기본값으로 되돌린다.
        let frame = NSRect(origin: saved, size: panel.frame.size)
        let visible = NSScreen.screens.contains { $0.visibleFrame.intersects(frame) }
        return visible ? saved : defaultOrigin()
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

        menu.addItem(.separator())
        let quit = NSMenuItem(title: "종료", action: #selector(handleQuit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        return menu
    }

    @objc private func handleRefresh() { store.refresh(force: true) }
    @objc private func handleResetPosition() { resetPosition() }
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
