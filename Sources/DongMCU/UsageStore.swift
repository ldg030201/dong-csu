import AppKit
import Combine
import Foundation

/// 사용량을 주기적으로 가져와서 UI에 물려주는 상태 저장소.
@MainActor
final class UsageStore: ObservableObject {
    /// 기본 폴링 주기. 사용량 API는 5분 단위 레이트리밋 창을 쓰므로 너무 조이면 429가 난다.
    static let pollInterval: TimeInterval = 120

    @Published private(set) var snapshot: UsageSnapshot?
    @Published private(set) var errorText: String?
    @Published private(set) var isRefreshing = false

    private var timer: Timer?
    private var backoffUntil: Date?
    private var consecutiveRateLimits = 0
    private var inFlight = false

    func start() {
        timer?.invalidate()
        let timer = Timer(timeInterval: Self.pollInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refresh() }
        }
        timer.tolerance = 10
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer

        NSWorkspace.shared.notificationCenter.addObserver(
            self,
            selector: #selector(handleWake),
            name: NSWorkspace.didWakeNotification,
            object: nil
        )

        refresh(force: true)
    }

    @objc private func handleWake() {
        refresh(force: true)
    }

    func refresh(force: Bool = false) {
        if inFlight { return }
        if !force, let backoffUntil, backoffUntil > Date() { return }
        if force { backoffUntil = nil }

        inFlight = true
        isRefreshing = true

        // Task는 감싼 @MainActor 컨텍스트를 물려받으므로 본문은 메인 액터에서 돈다.
        Task { [weak self] in
            do {
                let result = try await UsageAPI.fetch()
                guard let self else { return }
                self.snapshot = result
                self.errorText = nil
                self.consecutiveRateLimits = 0
                self.backoffUntil = nil
            } catch {
                self?.apply(error: error)
            }
            self?.finish()
        }
    }

    private func finish() {
        inFlight = false
        isRefreshing = false
    }

    /// 실패해도 직전 성공값(snapshot)은 유지해서 링이 비어 보이지 않게 한다.
    private func apply(error: Error) {
        let usageError = error as? UsageError
        errorText = usageError?.description ?? error.localizedDescription

        guard let usageError, case .rateLimited(let retryAfter) = usageError else {
            consecutiveRateLimits = 0
            return
        }
        consecutiveRateLimits += 1
        // 60s, 120s, 240s … 최대 5분. Retry-After 헤더가 더 길면 그쪽을 따른다.
        let backoff = min(60 * pow(2, Double(consecutiveRateLimits - 1)), 300)
        backoffUntil = Date().addingTimeInterval(max(backoff, retryAfter ?? 0))
    }
}
