import AppKit
import ApplicationServices

/// 뒤에 있는 창에서 **글을 쓰고 있는 자리**(캐럿)를 좇는다.
///
/// 펫이 입력을 가리지 않고 비키려면 글자가 어디까지 왔는지 알아야 하는데, 그건
/// macOS의 손쉬운 사용(Accessibility) API 말고는 알 방법이 없다. 그래서 이 기능만
/// 권한을 요구하고, **설정에서 켤 때만** 요청한다. 꺼 두면 아무것도 묻지 않는다.
///
/// 키를 눌렀다는 **사실만** 신호로 쓴다. 무슨 키인지는 읽지 않고, 캐럿 자리도
/// 저장하지 않는다 — 그때 비킬 자리를 정하는 데만 쓰고 버린다.
@MainActor
final class CaretWatcher {
    /// 지금 글을 쓰고 있는 자리.
    var onTyping: ((TypingArea) -> Void)?

    private var keyMonitor: Any?
    private var pollTimer: Timer?
    private var trustTimer: Timer?
    private var typingUntil = Date.distantPast

    /// 마지막 키 입력 뒤 이만큼은 캐럿을 좇는다. 타이핑이 멈추면 저절로 멈춘다.
    private static let followWindow: TimeInterval = 1.5
    /// 좇는 동안의 조회 주기. 이보다 조이면 남의 앱에 거는 IPC가 눈에 띄게 늘어난다.
    private static let pollInterval: TimeInterval = 0.18
    /// 권한이 아직 없을 때 다시 확인하는 주기. 권한은 시스템 설정에서 켜므로
    /// 앱이 알림을 받지 못한다.
    private static let trustRetryInterval: TimeInterval = 5
    /// 상대 앱이 굳어 있어도 여기서 끊는다. 메인 스레드가 오래 잡히면 부엉이가 멈춘다.
    private static let messagingTimeout: Float = 0.12

    /// 이 셋은 인스턴스 상태를 건드리지 않는 C 호출뿐이라 메인 액터에 묶지 않는다.
    /// 진단 통로(`--probe-accessibility`)처럼 액터 밖에서도 물어볼 자리가 있다.
    nonisolated static var isTrusted: Bool { AXIsProcessTrusted() }

    /// 권한 창을 띄운다. 켜는 순간에만 부른다.
    nonisolated static func requestTrust() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue(): true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    /// 시스템 설정의 손쉬운 사용 화면을 연다. 한 번 거절하면 권한 창이 다시 뜨지 않아서,
    /// 직접 갈 수 있는 길을 남겨 둬야 한다.
    nonisolated static func openAccessibilitySettings() {
        guard let url = URL(
            string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"
        ) else { return }
        NSWorkspace.shared.open(url)
    }

    /// 권한이 없으면 생길 때까지 조용히 기다렸다가 저절로 켜진다.
    ///
    /// **여기서 권한 창을 띄우지 않는다.** 켜고 끄는 자리마다 불리는 함수라
    /// 여기 두면 뜰 때마다 뜬다. 물어보는 건 한 번뿐이고 그 판단은 바깥이 한다.
    func start() {
        guard keyMonitor == nil else { return }
        guard Self.isTrusted else { return waitForTrust() }

        stopWaitingForTrust()
        // 전역 모니터는 이 앱이 앞에 없을 때만 온다. HUD는 절대 활성화되지 않는 창이라
        // 사용자가 글을 쓰는 상황은 전부 여기로 들어온다.
        keyMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.keyDown]) { _ in
            MainActor.assumeIsolated { [weak self] in self?.noteTyping() }
        }
    }

    func stop() {
        if let keyMonitor {
            NSEvent.removeMonitor(keyMonitor)
            self.keyMonitor = nil
        }
        stopPolling()
        stopWaitingForTrust()
    }

    // MARK: - 권한 기다리기

    private func waitForTrust() {
        guard trustTimer == nil else { return }
        let timer = Timer(timeInterval: Self.trustRetryInterval, repeats: true) { _ in
            MainActor.assumeIsolated { [weak self] in
                guard let self, Self.isTrusted else { return }
                self.start()
            }
        }
        timer.tolerance = Self.trustRetryInterval / 2
        RunLoop.main.add(timer, forMode: .common)
        trustTimer = timer
    }

    private func stopWaitingForTrust() {
        trustTimer?.invalidate()
        trustTimer = nil
    }

    // MARK: - 좇기

    /// 키가 눌렸다. 잠시 동안 캐럿을 따라간다.
    private func noteTyping() {
        typingUntil = Date().addingTimeInterval(Self.followWindow)
        guard pollTimer == nil else { return }

        let timer = Timer(timeInterval: Self.pollInterval, repeats: true) { _ in
            MainActor.assumeIsolated { [weak self] in self?.poll() }
        }
        timer.tolerance = Self.pollInterval / 4
        RunLoop.main.add(timer, forMode: .common)
        pollTimer = timer
        // 첫 글자에서 바로 반응해야 한다. 주기를 기다리면 이미 가려진 뒤다.
        poll()
    }

    private func poll() {
        guard Date() < typingUntil else { return stopPolling() }
        guard let area = typingArea() else { return }
        onTyping?(area)
    }

    private func stopPolling() {
        pollTimer?.invalidate()
        pollTimer = nil
    }

    // MARK: - 캐럿 자리 읽기

    /// 지금 글을 쓰고 있는 자리. 아무것도 못 알아내면 nil이다.
    ///
    /// **세 가지를 한꺼번에 모은다.** 앱마다 내주는 게 달라서다.
    /// 1. 캐럿(`AXBoundsForRange`) — 있으면 이게 제일 정확하다
    /// 2. 입력창(`AXFocusedUIElement`의 자리)
    /// 3. 그 창 전체(`AXFocusedWindow`)
    ///
    /// 실제로 재 보니 Electron으로 만든 앱은 1을 안 주고(cmux), 어떤 앱은 2까지도
    /// 안 준다(Claude). **정작 사람들이 글을 제일 많이 쓰는 앱들이 그쪽이다.**
    /// 그래서 얻을 수 있는 것 중 가장 정확한 것으로 떨어지게 했다.
    ///
    /// 값이 늘 멀쩡하지도 않다. 게임처럼 손쉬운 사용을 제대로 구현하지 않은 앱은
    /// `5,0 0x15` 같은 엉뚱한 자리를 준다. **창 밖에 있는 값은 버린다.**
    private func typingArea() -> TypingArea? {
        let system = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(system, Self.messagingTimeout)

        let window = frontmostWindow()
        let focused: AXUIElement? = Self.attribute(system, kAXFocusedUIElementAttribute)

        var caret: CGRect?
        var field: CGRect?
        if let focused {
            field = Self.frame(of: focused)
            if let range: AXValue = Self.attribute(focused, kAXSelectedTextRangeAttribute),
               let bounds: AXValue = Self.parameterized(
                   focused,
                   kAXBoundsForRangeParameterizedAttribute,
                   range
               ) {
                var rect = CGRect.zero
                if AXValueGetValue(bounds, .cgRect, &rect), rect.height > 0 {
                    caret = rect.flippedFromQuartz
                }
            }
        }

        // 창을 알면 그 안에 있는 값만 믿는다.
        if let window {
            if let c = caret, !window.intersects(c) { caret = nil }
            if let f = field, !window.intersects(f) { field = nil }
        }

        guard caret != nil || field != nil || window != nil else { return nil }
        return TypingArea(caret: caret, field: field, window: window)
    }

    /// 지금 앞에 나와 있는 앱의 포커스된 창.
    ///
    /// 손쉬운 사용으로 먼저 물어보고(그쪽이 "지금 쓰고 있는 창"을 정확히 안다),
    /// 안 주면 창 목록으로 떨어진다.
    private func frontmostWindow() -> CGRect? {
        guard let pid = NSWorkspace.shared.frontmostApplication?.processIdentifier
        else { return nil }

        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, Self.messagingTimeout)
        if let window: AXUIElement = Self.attribute(app, kAXFocusedWindowAttribute),
           let rect = Self.frame(of: window) {
            return rect
        }

        guard let list = CGWindowListCopyWindowInfo(
            [.optionOnScreenOnly, .excludeDesktopElements],
            kCGNullWindowID
        ) as? [[String: Any]] else { return nil }

        for entry in list {
            guard entry[kCGWindowOwnerPID as String] as? pid_t == pid,
                  entry[kCGWindowLayer as String] as? Int == 0,
                  let raw = entry[kCGWindowBounds as String] as? [String: Any],
                  let bounds = CGRect(dictionaryRepresentation: raw as CFDictionary),
                  bounds.width > 160, bounds.height > 100
            else { continue }
            return bounds.flippedFromQuartz
        }
        return nil
    }

    /// 손쉬운 사용 요소의 화면 위 자리.
    private static func frame(of element: AXUIElement) -> CGRect? {
        guard let position: AXValue = attribute(element, kAXPositionAttribute),
              let size: AXValue = attribute(element, kAXSizeAttribute)
        else { return nil }

        var origin = CGPoint.zero
        var extent = CGSize.zero
        guard AXValueGetValue(position, .cgPoint, &origin),
              AXValueGetValue(size, .cgSize, &extent),
              extent.width > 1, extent.height > 1
        else { return nil }
        return CGRect(origin: origin, size: extent).flippedFromQuartz
    }

    private static func attribute<T>(_ element: AXUIElement, _ name: String) -> T? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success,
              let value
        else { return nil }
        return cast(value)
    }

    private static func parameterized<T>(
        _ element: AXUIElement,
        _ name: String,
        _ parameter: CFTypeRef
    ) -> T? {
        var value: CFTypeRef?
        guard AXUIElementCopyParameterizedAttributeValue(
            element,
            name as CFString,
            parameter,
            &value
        ) == .success, let value else { return nil }
        return cast(value)
    }

    /// CFTypeRef를 원하는 CF 타입으로 내린다. AXUIElement·AXValue는 Swift 타입이 아니라
    /// `as?`가 늘 통하지는 않아서, 타입 ID를 직접 확인하고 내린다.
    private static func cast<T>(_ value: CFTypeRef) -> T? {
        if T.self == AXUIElement.self {
            guard CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        } else if T.self == AXValue.self {
            guard CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
        }
        return value as? T
    }

}

/// 지금 글을 쓰고 있는 자리. 앱이 내주는 만큼만 채워진다.
///
/// 셋은 피하는 방법이 다르다. **캐럿은 그 앞을 살짝 비켜서면 되지만, 상자는 밖으로
/// 나가야 안 가린다.** 상자가 둘인 이유는 창 밖으로 못 나갈 때(거의 전체화면인 창)
/// 입력창만이라도 벗어나면 가리지는 않기 때문이다.
struct TypingArea {
    /// 글자가 찍히는 그 지점. 주는 앱이 많지 않다.
    let caret: CGRect?
    /// 글을 쓰고 있는 입력 상자.
    let field: CGRect?
    /// 그 창 전체.
    let window: CGRect?
}

extension CGRect {
    /// Quartz 좌표를 AppKit 좌표로 뒤집는다.
    ///
    /// 창 목록(`CGWindowListCopyWindowInfo`)과 손쉬운 사용이 주는 좌표는 원점이
    /// **주 화면 왼쪽 위**고, 창 프레임(`NSWindow.frame`)은 왼쪽 아래다.
    /// 뒤집지 않고 섞어 쓰면 세로가 통째로 반대인 자리가 나온다.
    var flippedFromQuartz: CGRect {
        guard let primary = NSScreen.screens.first else { return self }
        return CGRect(x: minX, y: primary.frame.maxY - maxY, width: width, height: height)
    }
}
