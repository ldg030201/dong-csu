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
    /// 캐럿이 있는 자리(Cocoa 좌표, 원점 왼쪽 아래).
    var onCaret: ((CGRect) -> Void)?

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
        guard let rect = caretRect() else { return }
        onCaret?(rect)
    }

    private func stopPolling() {
        pollTimer?.invalidate()
        pollTimer = nil
    }

    // MARK: - 캐럿 자리 읽기

    /// 지금 글을 쓰고 있는 자리. 알아내지 못하면 nil이다.
    ///
    /// 앱마다 손쉬운 사용 지원 정도가 달라서 캐럿 자리를 못 주는 곳이 있다.
    /// 그럴 때 입력창 전체를 대신 쓰면 커서가 멀리 있는데도 비켜서 더 성가시므로,
    /// **모르면 그냥 안 비킨다.**
    private func caretRect() -> CGRect? {
        let system = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(system, Self.messagingTimeout)

        guard let focused: AXUIElement = Self.attribute(system, kAXFocusedUIElementAttribute),
              let range: AXValue = Self.attribute(focused, kAXSelectedTextRangeAttribute),
              let bounds: AXValue = Self.parameterized(
                  focused,
                  kAXBoundsForRangeParameterizedAttribute,
                  range
              )
        else { return nil }

        var rect = CGRect.zero
        guard AXValueGetValue(bounds, .cgRect, &rect),
              rect.width.isFinite, rect.height.isFinite,
              rect.height > 0
        else { return nil }
        return rect.flippedFromQuartz
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
