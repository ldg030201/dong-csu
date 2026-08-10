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
    /// 드래그하는 동안 마우스의 속도(pt/s). 부엉이가 처지는 방향과 날개 높이를 정한다.
    var onDragVelocity: (@MainActor (CGVector) -> Void)?
    var onDragEnded: (@MainActor () -> Void)?
    var menuBuilder: (@MainActor () -> NSMenu)?
    /// 더블클릭한 자리(뷰 좌표). 마스코트를 눌렀는지에 따라 하는 일이 달라진다.
    var onDoubleClick: (@MainActor (NSPoint) -> Void)?

    /// 이 영역들의 마우스 이벤트는 아래(SwiftUI)로 흘려보낸다.
    /// 버튼 묶음과 업데이트 배지처럼 눌려야 하는 자리들이 들어온다.
    var passThroughRects: [CGRect] = []

    /// 이 사각형들 안에서만 마우스를 받는다. 비어 있으면 뷰 전체가 대상이다.
    /// 펫 모드는 창 대부분이 투명해서, 전부 받으면 빈 자리를 눌러도 클릭이 먹는다.
    /// 링과 그 아래 버튼 줄, 둘로 갈려 있다.
    var liveRects: [CGRect] = []

    /// 마스코트 위에 마우스가 올라오고 나갈 때. 추적 영역을 걸어야 불린다.
    var onHoverChanged: (@MainActor (Bool) -> Void)?

    /// 링 아래 버튼 줄 위에 마우스가 올라오고 나갈 때.
    var onButtonsHoverChanged: (@MainActor (Bool) -> Void)?

    /// 마우스를 누르고 있는 동안. 스스로 움직이던 걸 그동안 멈춘다.
    /// 손에 잡힌 채로 걸어나가면 잡은 자리에서 미끄러진다.
    var onPressChanged: (@MainActor (Bool) -> Void)?

    /// 마우스와 창 원점 사이의 간격. 드래그 내내 이 값을 유지한다.
    private var dragOffset: CGSize?

    /// 직전 드래그 이벤트의 위치와 시각. 속도를 내는 데 쓴다.
    private var lastDragPoint: NSPoint?
    private var lastDragAt: TimeInterval?

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        // point는 superview 좌표계로 들어온다.
        let local = convert(point, from: superview)
        if !liveRects.isEmpty, !liveRects.contains(where: { $0.contains(local) }) { return nil }
        if passThroughRects.contains(where: { $0.contains(local) }) { return nil }
        return super.hitTest(point)
    }

    /// 어느 추적 영역에서 온 것인지 갈라서 알린다.
    /// `userInfo["buttons"]`가 있으면 버튼 줄, 없으면 마스코트 쪽이다.
    private func isButtons(_ event: NSEvent) -> Bool {
        (event.trackingArea?.userInfo?["buttons"] as? Bool) == true
    }

    override func mouseEntered(with event: NSEvent) {
        if isButtons(event) { onButtonsHoverChanged?(true) } else { onHoverChanged?(true) }
    }

    override func mouseExited(with event: NSEvent) {
        if isButtons(event) { onButtonsHoverChanged?(false) } else { onHoverChanged?(false) }
    }

    override func mouseDown(with event: NSEvent) {
        onPressChanged?(true)
        // 더블클릭으로 접었다 폈다 한다. 이때는 드래그를 시작하지 않는다.
        if event.clickCount == 2 {
            dragOffset = nil
            onDoubleClick?(convert(event.locationInWindow, from: nil))
            return
        }
        guard let origin = window?.frame.origin else { return }
        let mouse = NSEvent.mouseLocation
        dragOffset = CGSize(width: mouse.x - origin.x, height: mouse.y - origin.y)
        lastDragPoint = nil
        lastDragAt = nil
    }

    /// 창을 절대 좌표로 옮긴다.
    /// 이동량(델타)을 더해 나가면 이벤트가 하나만 누락돼도 그만큼 어긋난 채로 남고,
    /// 그 오차가 계속 쌓여서 창이 커서에서 점점 멀어진다.
    override func mouseDragged(with event: NSEvent) {
        guard let offset = dragOffset else { return }
        let mouse = NSEvent.mouseLocation
        onDragTo?(NSPoint(x: mouse.x - offset.width, y: mouse.y - offset.height))

        // 이벤트가 실린 시각을 쓴다. 지금 시각으로 재면 이벤트가 밀려 들어올 때
        // 간격이 0에 가까워져서 속도가 터무니없이 커진다.
        if let lastDragPoint, let lastDragAt, event.timestamp > lastDragAt {
            let elapsed = CGFloat(event.timestamp - lastDragAt)
            onDragVelocity?(CGVector(
                dx: (mouse.x - lastDragPoint.x) / elapsed,
                dy: (mouse.y - lastDragPoint.y) / elapsed
            ))
        }
        lastDragPoint = mouse
        lastDragAt = event.timestamp
    }

    override func mouseUp(with event: NSEvent) {
        dragOffset = nil
        lastDragPoint = nil
        lastDragAt = nil
        onDragEnded?()
        onPressChanged?(false)
    }

    /// 메뉴가 떠 있는 동안에도 멈춰 있어야 한다. `popUpContextMenu`는 메뉴가 닫힐 때까지
    /// 돌아오지 않지만, 타이머는 `.common` 모드라 그동안에도 울린다. 눌러 둔 것으로 쳐서
    /// 메뉴 뒤에서 펫이 걸어나가지 않게 한다.
    override func rightMouseDown(with event: NSEvent) {
        guard let menu = menuBuilder?() else { return }
        onPressChanged?(true)
        NSMenu.popUpContextMenu(menu, with: event, for: self)
        onPressChanged?(false)
    }
}

/// 이름을 가진 설정 값. 설정 창이 목록을 그릴 때 쓴다.
protocol TitledOption {
    var title: String { get }
}

/// 패널 생성 · 위치 기억 · 컨텍스트 메뉴를 담당한다.
@MainActor
final class HUDController {
    private let store: UsageStore
    private let panel: HUDPanel
    private let interactionView = HUDInteractionView()
    private var cancellables: Set<AnyCancellable> = []
    /// 직전에 본 측정 상태. 바뀔 때만 뷰를 다시 만든다.
    private var wasMeasuring = false

    private let hosting: FirstMouseHostingView<UsageHUDView>
    private let container: NSView
    let settings: HUDSettings
    private let updates: UpdateChecker
    private let meter: UsageMeter
    /// 설정 창을 여는 동작. AppDelegate가 꽂아준다.
    var onOpenSettings: (@MainActor () -> Void)?
    private let backdrop = NSView()
    private let usageMonitor = ProcessUsageMonitor()
    private let owlAnimator = OwlAnimator()
    /// 펫이 혼자 걸어다니고 비켜주는 것들. 창을 옮기는 주인은 여기 하나뿐이다.
    private let motion = PetMotionController()
    /// 지금 창을 끌고 있는지. 부엉이가 버둥거릴지를 정한다.
    private var isDraggingPanel = false
    /// 마우스 버튼이 눌려 있는지. 눌린 동안에는 스스로 움직이지 않는다.
    private var isPressed = false
    /// 커서가 펫 위에 머문 채 잡히지 않으면 비킨다. 그때까지 기다리는 타이머.
    private var hoverDodgeTimer: Timer?
    /// 화면이 꺼져 있는지. 꺼진 동안에는 부엉이를 움직일 이유가 없다.
    private var areScreensAsleep = false

    private static let originXKey = "hud.origin.x"
    private static let originYKey = "hud.origin.y"
    private static let margin: CGFloat = 16
    /// 커서가 올라온 채 이만큼 지나도 잡지 않으면 비켜준다.
    ///
    /// 지나가는 커서에까지 도망가지 않을 만큼은 기다려야 하지만, 0.9초는 길었다 —
    /// 비키는 시간까지 더하면 손을 올리고 2초를 기다리는 셈이라 굼떠 보였다.
    private static let hoverDodgeDelay: TimeInterval = 0.5

    private var mode: HUDMode { settings.mode }
    private var iconStyle: ClaudeIconStyle { settings.iconStyle }
    private var appearance: HUDAppearance { settings.appearance }
    private var scale: CGFloat { settings.scale.factor }

    init(store: UsageStore, settings: HUDSettings, updates: UpdateChecker, meter: UsageMeter) {
        self.store = store
        self.settings = settings
        self.updates = updates
        self.meter = meter

        let size = UsageHUDView.size(
            mode: settings.mode,
            showsStats: settings.showsProcessStats,
            scale: settings.scale.factor
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
        container.layer?.cornerRadius = UsageHUDView.cornerRadius(
            mode: settings.mode,
            scale: settings.scale.factor
        )
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
                mode: settings.mode,
                palette: HUDPalette(isDark: true),
                scale: settings.scale.factor,
                owlAnimator: owlAnimator
            )
        )
        container.addSubview(hosting)

        interactionView.frame = container.bounds
        interactionView.autoresizingMask = [.width, .height]
        // 시작 시점에는 아직 업데이트를 확인하기 전이라 버튼 영역만 넣는다.
        interactionView.passThroughRects = [
            UsageHUDView.controlsHitRectInPanel(
                mode: settings.mode,
                side: settings.expandSide,
                showsStats: settings.showsProcessStats,
                scale: settings.scale.factor
            )
        ]
        container.addSubview(interactionView)

        panel.contentView = container

        // 창이 실제로 움직이기 시작한 뒤에야 끌린 것으로 본다. 누르기만 하고
        // 만 클릭까지 버둥거리면 새로고침 한 번에 부엉이가 요동친다.
        interactionView.onDragTo = { [weak self] origin in
            guard let self else { return }
            self.panel.setFrameOrigin(origin)
            guard !self.isDraggingPanel else { return }
            self.isDraggingPanel = true
            self.refreshMood()
            self.syncMotion()
        }
        interactionView.onDragVelocity = { [weak self] velocity in
            self?.owlAnimator.setDragVelocity(velocity)
        }
        interactionView.onHoverChanged = { [weak self] hovering in
            self?.setPetHover(hovering)
        }
        interactionView.onButtonsHoverChanged = { [weak self] hovering in
            self?.setPetButtonsHover(hovering)
        }
        interactionView.onPressChanged = { [weak self] pressed in
            guard let self, self.isPressed != pressed else { return }
            self.isPressed = pressed
            self.syncMotion()
        }
        interactionView.onDragEnded = { [weak self] in
            guard let self else { return }
            self.saveOrigin()
            guard self.isDraggingPanel else { return }
            self.isDraggingPanel = false
            self.refreshMood()
            self.syncMotion()
        }
        interactionView.menuBuilder = { [weak self] in self?.makeMenu() ?? NSMenu() }
        interactionView.onDoubleClick = { [weak self] point in self?.handleDoubleClick(at: point) }

        motion.frame = { [weak self] in self?.panel.frame ?? .zero }
        motion.visualFrame = { [weak self] in self?.mascotScreenRect() ?? .zero }
        motion.move = { [weak self] origin in self?.panel.setFrameOrigin(origin) }
        motion.setGait = { [weak self] gait in self?.owlAnimator.setGait(gait) }
        motion.didSettle = { [weak self] in
            guard let self else { return }
            self.saveOrigin()
            // 스스로 움직이는 동안에는 추적 영역이 커서를 놓친다(mouseEntered는 커서가
            // 움직여야 온다). 자리를 잡은 뒤에 지금 상태를 다시 맞춘다.
            self.setPetHover(self.isMouseInside(UsageHUDView.petHitRect(scale: self.scale)))
        }

        applyAppearance()
        layoutHosting(for: size)
        refreshPassThroughRects()
        refreshTrackingArea()
        syncUsageMonitor(visible: true)
        syncOwlAnimator(visible: true)
        syncMotion(visible: true)
        refreshMood()

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
        // 화면이 꺼져 있으면 아무도 부엉이를 보지 않는다. UsageStore가 같은 이유로
        // 폴링을 멈추는데, 애니메이션은 그보다 훨씬 자주 깨어나서 더 아깝다.
        let workspaceCenter = NSWorkspace.shared.notificationCenter
        workspaceCenter.addObserver(
            self,
            selector: #selector(handleScreensSleep),
            name: NSWorkspace.screensDidSleepNotification,
            object: nil
        )
        workspaceCenter.addObserver(
            self,
            selector: #selector(handleScreensWake),
            name: NSWorkspace.screensDidWakeNotification,
            object: nil
        )
        observeStore()
        observeMeter()
        observeSettings()
    }

    /// @Published는 값이 바뀌기 "직전"에 알림을 보낸다. 그래서 한 턴 미뤄서 읽어야
    /// 새 값이 들어와 있다. 이때 RunLoop.main을 쓰면 안 된다 — 기본 모드에서만 돌기 때문에
    /// 마우스를 누르고 있는 동안(이벤트 추적 모드)에는 실행이 미뤄져서, 버튼을 눌러도
    /// 손을 뗄 때까지 반응이 없다. DispatchQueue.main은 모드와 무관하게 처리된다.
    private func observeSettings() {
        observe(settings.$appearance) { $0.applyAppearance() }
        observe(settings.$backdropOpacity) { $0.applyAppearance() }
        observe(settings.$iconStyle) {
            $0.syncOwlAnimator()
            $0.rebuildRootView()
        }
        observe(settings.$mode) { $0.applyMode() }
        observe(settings.$petRingDisplay) {
            $0.refreshTrackingArea()
            $0.rebuildRootView()
        }
        observe(settings.$showsVersionBadge) { $0.rebuildRootView() }
        observe(settings.$animatesIcon) { $0.syncOwlAnimator() }
        observe(settings.$petWanders) { $0.syncMotion() }
        // 커서를 피하려면 마스코트 위에 커서가 있는지를 알아야 한다.
        // 링을 항상 보이게 해 뒀어도 그때는 추적 영역이 필요하다.
        observe(settings.$petDodgesCursor) {
            $0.syncMotion()
            $0.refreshTrackingArea()
        }
        observe(settings.$isHUDVisible) { $0.applyHUDVisible() }
        observe(settings.$expandSide) { $0.applyExpandSide() }
        observe(settings.$showsProcessStats) { $0.applyProcessStats() }
        // 배율은 창 크기·모서리·클릭 영역까지 바꾼다. 접기와 같은 경로를 탄다.
        observe(settings.$scale) { $0.applyMode() }
        // 새 버전이 잡히면 표시를 띄우고 그 자리를 클릭 통과 영역에 더한다.
        // 펫에서는 배지 위에서 도망을 막는 추적 영역도 그때 걸린다.
        observe(updates.$remoteEntries) {
            $0.refreshPassThroughRects()
            $0.refreshTrackingArea()
            $0.rebuildRootView()
        }
    }

    /// 설정 하나가 바뀌면 무엇을 다시 맞출지 잇는다.
    ///
    /// `dropFirst`(초기값 무시)와 `DispatchQueue.main`(위 주석의 이벤트 추적 모드 문제)은
    /// 빠뜨려도 눈에 띄지 않는 실수라 여기 한 곳에 가둔다.
    private func observe<T>(
        _ publisher: Published<T>.Publisher,
        _ action: @escaping (HUDController) -> Void
    ) {
        publisher
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self.map(action) }
            .store(in: &cancellables)
    }

    /// 자원 사용량 표시를 켜고 끈다. 크기까지 바뀌므로 레이아웃도 다시 잡는다.
    private func applyProcessStats() {
        syncUsageMonitor()
        applyMode()
    }

    /// 보이지도 않는데 표본을 뜰 이유가 없다. 조건이 바뀌는 자리마다 이걸 부른다.
    private func syncUsageMonitor(visible: Bool? = nil) {
        let isVisible = visible ?? panel.isVisible

        if settings.showsProcessStats, isVisible, settings.mode == .expanded {
            usageMonitor.start()
        } else {
            usageMonitor.stop()
        }
    }

    /// 움직이는 캐릭터가 눈에 보일 때만 자세를 넘긴다.
    /// 접혀 있어도 링은 남아 있어서 캐릭터는 계속 보인다 — 접힘은 조건이 아니다.
    private func syncOwlAnimator(visible: Bool? = nil) {
        let isVisible = visible ?? panel.isVisible

        if isVisible, !areScreensAsleep, settings.iconStyle.isAnimated, settings.animatesIcon {
            owlAnimator.start()
        } else {
            owlAnimator.stop()
        }
    }

    /// 펫이 스스로 움직여도 되는 상황인지 다시 판단한다.
    ///
    /// 펫 모드에서만 돈다. 숫자가 붙은 카드가 혼자 걸어다니면 읽으려던 값이 도망가고,
    /// 접힌 링은 서랍 손잡이라 자리가 고정돼 있어야 한다.
    private func syncMotion(visible: Bool? = nil) {
        let isVisible = visible ?? panel.isVisible
        // 조회가 끊긴 동안에는 멈춰 있는다. 회색으로 굳은 채 걸어다니면
        // "멈췄다"는 표시가 무색해진다.
        //
        // **주간을 다 썼을 때도 같다.** 그때는 아예 죽은 것으로 다루므로 스스로 걷지도,
        // 커서를 피하지도 않는다. 색만 빼고 계속 돌아다니면 살아 있는 것으로 보인다.
        let canMove = isVisible
            && !areScreensAsleep
            && settings.mode == .pet
            && !isDraggingPanel
            && !isPressed
            && !store.isDisconnected
            && !store.isWeeklySpent

        motion.wanders = settings.petWanders
        motion.dodgesCursor = settings.petDodgesCursor
        motion.update(active: canMove)
    }

    /// 마스코트가 실제로 화면을 가리는 자리(화면 좌표).
    ///
    /// 펫의 창은 링이 들어갈 만큼 크지만 그림은 그보다 작다. 창으로 따지면 아직
    /// 글자를 가리지도 않았는데 비켜서, 왜 움직였는지 알 수 없어진다.
    ///
    /// **자리 계산은 여기서 하지 않는다.** 같은 셈이 두 곳에 있으면 한쪽만 고쳐진다 —
    /// 실제로 그랬다. 여기서는 뷰 좌표를 화면 좌표로 옮기기만 한다.
    private func mascotScreenRect() -> NSRect {
        let panelFrame = panel.frame
        guard settings.mode == .pet else { return panelFrame }

        let local = UsageHUDView.petMascotRect(scale: scale)
        return NSRect(
            x: panelFrame.minX + local.minX,
            y: panelFrame.minY + local.minY,
            width: local.width,
            height: local.height
        )
    }

    @objc private func handleScreensSleep() {
        areScreensAsleep = true
        syncOwlAnimator()
        syncMotion()
    }

    @objc private func handleScreensWake() {
        areScreensAsleep = false
        syncOwlAnimator()
        syncMotion()
    }

    /// 사용량·연결 상태·드래그 여부에서 지금 기분을 다시 정한다.
    private func refreshMood() {
        // 다 쓴 것을 먼저 알려 준다. 순서가 뒤면 한 틱 동안 살아 있는 자세가 스친다.
        owlAnimator.setUnusable(store.isWeeklySpent)
        owlAnimator.setMood(OwlMood.resolve(store: store, isDragging: isDraggingPanel))
    }

    /// 펼침 방향이 바뀌면 손잡이(링·버튼)가 반대쪽으로 옮겨간다.
    private func applyExpandSide() {
        refreshPassThroughRects()
        rebuildRootView()
        layoutHosting(for: UsageHUDView.size(
            mode: settings.mode,
            showsStats: settings.showsProcessStats,
            scale: scale
        ))
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

    /// 완료 콜백은 메인 액터에서 도는데 `completionHandler`는 그걸 모른다.
    /// 그대로 넘기면 non-Sendable 경고가 난다. 애니메이션 완료는 항상 메인
    /// 스레드에서 오므로 그 자리에서 격리를 되찾아 부른다.
    private func animate(to frame: NSRect, completion: (@MainActor () -> Void)?) {
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.22
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            panel.animator().setFrame(frame, display: true)
        }, completionHandler: completion.map { body in
            { @Sendable in MainActor.assumeIsolated(body) }
        })
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
    /// 처음 자리이자 "위치 초기화"가 보내는 자리 — **주 모니터의 오른쪽 위**.
    ///
    /// `NSScreen.main` 을 쓰면 안 된다. 그건 주 모니터가 아니라 **키보드 포커스가 있는
    /// 화면**이라, 초기화할 때마다 그때 쓰던 모니터로 간다. 모니터를 여러 대 쓰면
    /// 매번 다른 데로 가서 "초기화"가 아니게 된다.
    /// 주 모니터(메뉴 막대가 있는 화면)는 `NSScreen.screens` 의 첫 번째다.
    private func defaultOrigin() -> NSPoint {
        guard let screen = NSScreen.screens.first ?? NSScreen.main else { return .zero }
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
    ///
    /// **여기에 설정 항목을 늘리지 않는다.** 모드·크기·테마·아이콘은 전부 설정 창에
    /// 있고, 메뉴에 같은 걸 한 벌 더 두면 두 곳을 함께 고쳐야 하는 데다 자주 누르는
    /// 항목이 목록에 파묻힌다. 메뉴에는 **바로 누르는 것**만 남긴다.
    func populateMenu(_ menu: NSMenu) {
        let status = NSMenuItem(title: store.summaryText, action: nil, keyEquivalent: "")
        status.isEnabled = false
        menu.addItem(status)
        menu.addItem(.separator())

        // 토큰이 만료됐을 때만 나온다. 그때는 이게 해야 할 유일한 일이라 맨 위에 굵게 둔다
        // — 새로고침해 봐야 다시 실패한다.
        if store.needsReauth {
            let login = NSMenuItem(
                title: "Claude Code 재로그인…",
                action: #selector(handleLogin),
                keyEquivalent: ""
            )
            login.target = self
            login.attributedTitle = NSAttributedString(
                string: login.title,
                attributes: [.font: NSFont.boldSystemFont(ofSize: NSFont.systemFontSize)]
            )
            menu.addItem(login)
        }

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

        menu.addItem(.separator())
        let quit = NSMenuItem(title: "\(AppInfo.name) 종료", action: #selector(handleQuit), keyEquivalent: "q")
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
    /// 패널 외형·배경색·팔레트를 현재 설정에 맞춘다.
    private func applyAppearance() {
        panel.appearance = appearance.nsAppearance
        let palette = HUDPalette(isDark: appearance.isDark)

        // 펫은 마스코트만 떠 있어야 해서 카드 배경·테두리·창 그림자를 모두 지운다.
        // 창 그림자는 내용이 바뀔 때마다 invalidateShadow()를 불러야 맞는데, 프레임마다
        // 움직이는 그림에 그걸 걸면 비싸다. 마스코트가 자기 그림자를 갖고 있어서 없어도 된다.
        let showsBackdrop = mode.showsBackdrop
        backdrop.layer?.backgroundColor = showsBackdrop
            ? palette.backdrop(opacity: settings.backdropOpacity).cgColor
            : NSColor.clear.cgColor
        container.layer?.borderColor = showsBackdrop ? palette.border.cgColor : NSColor.clear.cgColor
        panel.hasShadow = showsBackdrop

        rebuildRootView()
    }

    @objc private func handleSystemThemeChange() {
        guard appearance == .system else { return }
        // 알림이 올 때 NSApp.effectiveAppearance가 아직 갱신되지 않은 경우가 있어 한 턴 미룬다.
        DispatchQueue.main.async { [weak self] in self?.applyAppearance() }
    }

    /// 더블클릭한 자리에 따라 갈린다.
    ///
    /// - 마스코트 위 → 펫 모드로 들어가고, 펫에서 다시 누르면 원래 보기로 돌아간다
    /// - 그 밖 → 예전처럼 접었다 폈다 한다
    ///
    /// 셋을 한 줄로 돌리면 접으려다 펫으로 넘어가서, 원래 있던 접기 동작이 무엇을
    /// 할지 예측할 수 없어진다.
    private func handleDoubleClick(at point: NSPoint) {
        let character = UsageHUDView.characterRectInPanel(
            mode: mode,
            side: settings.expandSide,
            showsStats: settings.showsProcessStats,
            scale: scale
        )
        guard character.contains(point) else {
            handleToggleCollapse()
            return
        }
        handleTogglePet()
    }

    @objc private func handleTogglePet() {
        if mode == .pet {
            settings.mode = settings.modeBeforePet
        } else {
            settings.modeBeforePet = mode
            settings.mode = .pet
        }
    }

    @objc private func handleToggleCollapse() {
        // 펫에서 마스코트 밖을 눌렀다면 일단 펫에서 나온다.
        settings.mode = mode == .pet ? settings.modeBeforePet : mode.toggled
    }

    @objc private func handleOpenSettings() {
        onOpenSettings?()
    }

    /// 보기를 바꾼다. 오른쪽 위 모서리를 붙잡아 두어서 크기가 바뀌어도 자리가 튀지 않는다.
    private func applyMode() {
        let mode = settings.mode
        let newSize = UsageHUDView.size(
            mode: mode,
            showsStats: settings.showsProcessStats,
            scale: scale
        )
        let target = targetFrame(for: newSize)

        // 애니메이션 도중에 표본이 갱신되면 화면이 다시 배치되면서 끊겨 보인다.
        // 잠시 멈추고 끝난 뒤에 다시 맞춘다.
        usageMonitor.stop()
        // 보기가 바뀌는 동안 혼자 걸어가면 창이 두 곳에서 동시에 움직인다.
        syncMotion()
        container.layer?.cornerRadius = UsageHUDView.cornerRadius(mode: mode, scale: scale)
        setPetHover(false)
        applyAppearance()
        refreshPassThroughRects()

        // 작아질 때는 옛 내용을 그대로 둔 채 창만 줄여서 서랍이 밀려 들어가는 것처럼 보이게 하고,
        // 커질 때는 새 내용을 먼저 깔아두고 창을 키워서 드러나게 한다.
        let shrinking = newSize.width < panel.frame.width
        if shrinking {
            animate(to: target) { [weak self] in
                guard let self else { return }
                self.rebuildRootView()
                self.layoutHosting(for: newSize)
                self.saveOrigin()
                self.syncUsageMonitor()
                self.refreshTrackingArea()
                self.syncMotion()
            }
        } else {
            rebuildRootView()
            layoutHosting(for: newSize)
            animate(to: target) { [weak self] in
                self?.saveOrigin()
                self?.syncUsageMonitor()
                self?.refreshTrackingArea()
                self?.syncMotion()
            }
        }
    }

    // MARK: - 펫 호버

    /// 펫 모드에서 마스코트 위에 마우스가 있는지. 그동안만 뒤에 링이 드러난다.
    private var isHoveringPet = false

    /// 링 아래 버튼 줄에 마우스가 올라와 있는지.
    ///
    /// **여기 있는 동안에는 절대 비키지 않는다.** 버튼을 누르러 다가갔는데 달아나면
    /// 영영 못 누른다. 자리를 갈라 뒀지만(`petButtonsRect`), 링을 스쳐 내려오면서
    /// 이미 예약된 도망이 남아 있을 수 있어 여기서 한 번 더 막는다.
    private var isHoveringPetButtons = false
    private var trackingArea: NSTrackingArea?
    private var buttonTrackingArea: NSTrackingArea?
    private var updateTrackingArea: NSTrackingArea?

    /// 호버를 감시할 영역을 지금 모드에 맞춘다. 펫이 아니면 아예 걸지 않는다.
    private func refreshTrackingArea() {
        if let trackingArea {
            interactionView.removeTrackingArea(trackingArea)
            self.trackingArea = nil
        }
        if let buttonTrackingArea {
            interactionView.removeTrackingArea(buttonTrackingArea)
            self.buttonTrackingArea = nil
        }
        if let updateTrackingArea {
            interactionView.removeTrackingArea(updateTrackingArea)
            self.updateTrackingArea = nil
        }
        // 링을 항상 보이거나 아예 안 보이게 해 뒀으면 마우스를 좇을 이유가 없다 —
        // 커서를 피하게 해 뒀다면 그때는 링과 무관하게 커서 자리를 알아야 한다.
        guard settings.mode == .pet else { return }
        guard settings.petRingDisplay == .hover || settings.petDodgesCursor else { return }

        let area = NSTrackingArea(
            rect: UsageHUDView.petHitRect(scale: scale),
            // activeAlways가 아니면 이 앱이 앞에 없을 때 호버가 잡히지 않는다.
            // HUD는 절대 활성화되지 않는 창이라 그 경우가 사실상 전부다.
            options: [.mouseEnteredAndExited, .activeAlways],
            owner: interactionView,
            userInfo: nil
        )
        interactionView.addTrackingArea(area)
        trackingArea = area

        // 추적 영역은 **이미 안에 들어와 있는 커서에는 mouseEntered를 보내지 않는다.**
        // 마스코트를 더블클릭해서 펫으로 들어오면 커서가 바로 그 위에 있으므로,
        // 이걸 빠뜨리면 한 번 밖으로 나갔다 들어올 때까지 링이 뜨지 않는다.
        //
        // **`defer` 로 거는 이유:** 아래에 `guard` 가 있어서, 그냥 마지막 줄에 두면
        // 새 버전이 없을 때(=평소) 여기까지 오지 못한다. 실제로 그랬다.
        defer { setPetHover(isMouseInside(area.rect)) }

        // 버튼 줄은 **따로** 좇는다. 여기 들어온 것은 도망의 이유가 아니라
        // 버튼을 보여줄 이유다. 같은 영역으로 묶으면 둘을 구분할 수 없다.
        let buttons = NSTrackingArea(
            rect: UsageHUDView.petButtonsRect(scale: scale),
            options: [.mouseEnteredAndExited, .activeAlways],
            owner: interactionView,
            userInfo: ["buttons": true]
        )
        interactionView.addTrackingArea(buttons)
        buttonTrackingArea = buttons

        // 새 버전 배지는 링 사각형 **안**에 있어서 자리로 가를 수 없다.
        // 겹쳐 걸고, 여기 들어오면 도망을 막는 것으로 푼다.
        guard updates.hasUpdate else { return }
        let badge = NSTrackingArea(
            rect: UsageHUDView.petUpdateRect(scale: scale),
            options: [.mouseEnteredAndExited, .activeAlways],
            owner: interactionView,
            userInfo: ["buttons": true]
        )
        interactionView.addTrackingArea(badge)
        updateTrackingArea = badge
    }

    /// 뷰 좌표의 사각형 안에 지금 마우스가 있는지.
    private func isMouseInside(_ rect: CGRect) -> Bool {
        guard panel.isVisible else { return false }
        let inWindow = panel.convertPoint(fromScreen: NSEvent.mouseLocation)
        return rect.contains(interactionView.convert(inWindow, from: nil))
    }

    private func setPetHover(_ hovering: Bool) {
        // 대기 타이머는 상태가 그대로여도 다시 잡는다. 비키고 나서 커서가 여전히
        // 위에 있으면 mouseEntered가 다시 오지 않아서, 여기서 이어 걸어야 계속 비킨다.
        scheduleHoverDodge(hovering)
        guard isHoveringPet != hovering else { return }
        isHoveringPet = hovering
        rebuildRootView()
    }

    /// 커서가 올라와 있으면 잠시 뒤에 비키도록 예약한다.
    /// 올라오자마자 도망가면 잡을 수가 없어서, 잡을 틈을 주고 나서 움직인다.
    private func scheduleHoverDodge(_ hovering: Bool) {
        hoverDodgeTimer?.invalidate()
        hoverDodgeTimer = nil
        guard hovering, settings.petDodgesCursor, settings.mode == .pet, panel.isVisible else { return }

        let timer = Timer(timeInterval: Self.hoverDodgeDelay, repeats: false) { _ in
            MainActor.assumeIsolated { [weak self] in self?.dodgeCursorIfIdle() }
        }
        RunLoop.main.add(timer, forMode: .common)
        hoverDodgeTimer = timer
    }

    /// 버튼 줄 위에 마우스가 들어오고 나갈 때.
    ///
    /// 들어오면 **예약된 도망을 취소한다.** 링을 스쳐 내려오면 이미 타이머가 걸려 있고,
    /// 그대로 두면 버튼을 누르려는 순간 달아난다.
    private func setPetButtonsHover(_ hovering: Bool) {
        guard isHoveringPetButtons != hovering else { return }
        isHoveringPetButtons = hovering

        if hovering {
            hoverDodgeTimer?.invalidate()
            hoverDodgeTimer = nil
        }
        // 버튼은 링과 같은 조건으로 보인다. 여기 있는 동안에도 보이게 유지한다.
        rebuildRootView()
    }

    private func dodgeCursorIfIdle() {
        hoverDodgeTimer = nil
        // 버튼 줄 위라면 비키지 않는다. 누르러 온 손에서 달아나면 안 된다.
        guard !isHoveringPetButtons else { return }
        // 잡고 있는 중이면 비키지 않는다. 손에 들린 게 도망가면 놀란다.
        guard !isPressed, !isDraggingPanel, NSEvent.pressedMouseButtons == 0 else { return }
        // 글을 쓰는 동안에는 커서를 피하지 않는다(왼쪽으로 물러나면 쓴 글을 덮는다).
        // 다 쓰고 나서도 커서가 그대로 올라와 있으면 그때 비킨다.
        guard !motion.isTypingQuiet else { return scheduleHoverDodge(isHoveringPet) }
        motion.dodgeCursor()
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
        syncOwlAnimator(visible: visible)
        syncMotion(visible: visible)
        // 숨겨져 있는 동안 커서가 움직였을 수 있다. 다시 보일 때 호버 상태를 맞춘다.
        refreshTrackingArea()
        rebuildRootView()
    }

    /// 드래그 오버레이가 클릭을 삼키지 않을 자리들을 다시 계산한다.
    /// 버튼 묶음은 항상, 업데이트 표시는 새 버전이 있을 때만 넣는다.
    private func refreshPassThroughRects() {
        let mode = settings.mode
        var rects = [
            UsageHUDView.controlsHitRectInPanel(
                mode: mode,
                side: settings.expandSide,
                showsStats: settings.showsProcessStats,
                scale: scale
            )
        ]
        if updates.hasUpdate {
            rects.append(
                UsageHUDView.updateBadgeRectInPanel(
                    mode: mode,
                    side: settings.expandSide,
                    showsStats: settings.showsProcessStats,
                    scale: scale
                )
            )
        }
        // 펫의 버튼 줄과 새 버전 배지는 SwiftUI 버튼이라 클릭을 아래로 흘려보내야 눌린다.
        if mode == .pet {
            rects.append(UsageHUDView.petButtonsRect(scale: scale))
            if updates.hasUpdate {
                rects.append(UsageHUDView.petUpdateRect(scale: scale))
            }
        }
        interactionView.passThroughRects = rects

        // 펫은 창 대부분이 투명하다. 마스코트가 있는 자리와 버튼 줄만 마우스를 받게 좁힌다.
        interactionView.liveRects = mode == .pet
            ? [
                UsageHUDView.petHitRect(scale: scale),
                UsageHUDView.petButtonsRect(scale: scale),
                UsageHUDView.petUpdateRect(scale: scale),
            ]
            : []
    }

    /// 표시 상태가 바뀌면 뷰를 다시 만든다. 숨겨져 있는 동안 카운트다운의 1초 타이머를 끄기 위해서다.
    private func rebuildRootView() {
        hosting.rootView = UsageHUDView(
            store: store,
            iconStyle: iconStyle,
            showsCountdown: panel.isVisible && mode == .expanded,
            mode: mode,
            isHovered: isHoveringPet || isHoveringPetButtons,
            petRingDisplay: settings.petRingDisplay,
            palette: HUDPalette(isDark: appearance.isDark),
            onOpenSettings: { [weak self] in self?.onOpenSettings?() },
            onOpenMeasure: { [weak self] in self?.handleOpenMeasure() },
            isMeasuring: meter.isRunning,
            onToggleCollapse: { [weak self] in self?.handleToggleCollapse() },
            expandSide: settings.expandSide,
            // 표본 타이머가 도는지가 아니라 "표시 설정"을 봐야 한다.
            // 접기 애니메이션 동안에는 타이머를 잠시 멈추는데, 그때 뷰를 다시 만들면
            // 줄이 통째로 사라져서 펼친 뒤에도 안 보였다.
            usageMonitor: settings.showsProcessStats && mode == .expanded ? usageMonitor : nil,
            scale: scale,
            showsUpdateBadge: updates.hasUpdate,
            versionBadge: settings.showsVersionBadge ? AppInfo.badgeVersion : nil,
            versionBadgeIsTest: AppInfo.isTestBuild,
            onOpenUpdates: { [weak self] in self?.openUpdates() },
            owlAnimator: owlAnimator
        )
    }

    /// 업데이트 표시나 메뉴 항목을 눌렀을 때. 설정 창의 버전 화면을 연다.
    @objc private func openUpdates() {
        settings.settingsTab = .version
        onOpenSettings?()
    }

    @objc private func handleQuit() { NSApp.terminate(nil) }

    /// HUD·펫의 측정 버튼. **측정 화면을 열기만 한다.**
    ///
    /// 여기서 바로 재기 시작하면 손이 스칠 때마다 재던 것이 끊기고, 무엇이 시작됐는지도
    /// 화면에 안 보인다. 시작·일시정지·중지는 보이는 자리에서 누르게 한다.
    private func handleOpenMeasure() {
        settings.settingsTab = .measure
        onOpenSettings?()
    }

    /// 재는 중인지가 바뀌면 버튼 모양이 달라진다. **그때만** 다시 그린다 —
    /// 측정은 토큰을 셀 때마다 알림을 보내는데, 그때마다 뷰를 새로 만들면 낭비다.
    private func observeMeter() {
        meter.objectWillChange
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in
                guard let self, self.wasMeasuring != self.meter.isRunning else { return }
                self.wasMeasuring = self.meter.isRunning
                self.rebuildRootView()
            }
            .store(in: &cancellables)
    }

    private func observeStore() {
        store.objectWillChange
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in
                self?.updateTooltip()
                self?.refreshMood()
                // 끊겼다 돌아오면 다시 걸어다녀야 하고, 끊기면 멈춰야 한다.
                self?.syncMotion()
            }
            .store(in: &cancellables)
        updateTooltip()
    }

    private func updateTooltip() {
        interactionView.toolTip = store.summaryText
    }
}
