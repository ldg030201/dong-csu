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

    /// 지금 글을 쓰고 있는 자리. 알아내지 못하면 nil이다.
    ///
    /// **두 단계로 떨어진다.**
    /// 1. 캐럿의 정확한 자리(`AXBoundsForRange`) — 있으면 이게 제일 좋다
    /// 2. 없으면 **입력창 전체**(`AXPosition`+`AXSize`)
    ///
    /// 2번이 필요한 이유는 Electron으로 만든 앱들이 캐럿 자리를 안 주기 때문이다.
    /// 실제로 재 보니 `AXFocusedUIElement`와 `AXSelectedTextRange`까지는 주는데
    /// `AXBoundsForRange`에서 막힌다. 거기서 포기하면 **그런 앱에서는 이 기능이
    /// 통째로 죽는다** — 정작 사람들이 글을 제일 많이 쓰는 앱들이 그쪽이다.
    ///
    /// 입력창 전체도 짐작이 아니라 **권한으로 읽은 사실**이다. 창 목록으로 넘겨짚던
    /// 것과는 다르다 — 지금 글을 쓰고 있는 바로 그 상자다.
    private func typingArea() -> TypingArea? {
        let system = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(system, Self.messagingTimeout)

        guard let focused: AXUIElement = Self.attribute(system, kAXFocusedUIElementAttribute)
        else { return nil }

        if let range: AXValue = Self.attribute(focused, kAXSelectedTextRangeAttribute),
           let bounds: AXValue = Self.parameterized(
               focused,
               kAXBoundsForRangeParameterizedAttribute,
               range
           ) {
            var rect = CGRect.zero
            if AXValueGetValue(bounds, .cgRect, &rect),
               rect.width.isFinite, rect.height.isFinite, rect.height > 0 {
                return TypingArea(rect: rect.flippedFromQuartz, isCaret: true)
            }
        }

        guard let position: AXValue = Self.attribute(focused, kAXPositionAttribute),
              let size: AXValue = Self.attribute(focused, kAXSizeAttribute)
        else { return nil }

        var origin = CGPoint.zero
        var extent = CGSize.zero
        guard AXValueGetValue(position, .cgPoint, &origin),
              AXValueGetValue(size, .cgSize, &extent),
              extent.width > 1, extent.height > 1
        else { return nil }
        return TypingArea(
            rect: CGRect(origin: origin, size: extent).flippedFromQuartz,
            isCaret: false
        )
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

/// 지금 글을 쓰고 있는 자리.
///
/// `isCaret`이면 글자가 찍히는 그 지점이고, 아니면 **입력창 전체**다.
/// 둘은 피하는 방법이 다르다 — 캐럿은 그 앞을 살짝 비켜서면 되지만,
/// 입력창은 상자 밖으로 나가야 안 가린다.
struct TypingArea {
    let rect: CGRect
    let isCaret: Bool
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
