import AppKit
import Combine
import Foundation

/// 사용량을 주기적으로 가져와서 UI에 물려주는 상태 저장소.
@MainActor
final class UsageStore: ObservableObject {
    /// 기본 폴링 주기. 사용량 API는 5분 단위 레이트리밋 창을 쓰므로 너무 조이면 429가 난다.
    static let pollInterval: TimeInterval = 600

    @Published private(set) var snapshot: UsageSnapshot?
    @Published private(set) var errorText: String?
    @Published private(set) var isRefreshing = false

    private var timer: Timer?
    private var backoffUntil: Date?
    private var consecutiveRateLimits = 0
    private var inFlight = false

    init() {}

    /// 렌더 확인용. 네트워크 없이 고정값으로 채운 저장소.
    init(preview snapshot: UsageSnapshot, nextPoll: Date? = nil) {
        self.snapshot = snapshot
        self.previewNextPoll = nextPoll
    }

    /// 렌더 확인용 고정값. 실제 실행에서는 항상 nil이다.
    private var previewNextPoll: Date?

    func start() {
        startTimer()

        let center = NSWorkspace.shared.notificationCenter
        center.addObserver(self, selector: #selector(handleWake),
                           name: NSWorkspace.didWakeNotification, object: nil)
        // 화면이 꺼져 있으면 아무도 HUD를 보지 않는다. 그동안은 네트워크 폴링을 멈춘다.
        center.addObserver(self, selector: #selector(handleScreensSleep),
                           name: NSWorkspace.screensDidSleepNotification, object: nil)
        center.addObserver(self, selector: #selector(handleScreensWake),
                           name: NSWorkspace.screensDidWakeNotification, object: nil)

        refresh(force: true)
    }

    private func startTimer() {
        timer?.invalidate()
        let timer = Timer(timeInterval: Self.pollInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refresh() }
        }
        // 타이머를 다른 시스템 깨우기와 묶어서 처리하게 여유를 크게 준다(전력 절약).
        timer.tolerance = Self.pollInterval / 4
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    @objc private func handleWake() {
        refresh(force: true)
    }

    @objc private func handleScreensSleep() {
        timer?.invalidate()
        timer = nil
    }

    @objc private func handleScreensWake() {
        guard timer == nil else { return }
        startTimer()
        refresh(force: true)
    }

    func refresh(force: Bool = false) {
        if inFlight { return }
        if !force, let backoffUntil, backoffUntil > Date() { return }
        if force { backoffUntil = nil }

        inFlight = true
        isRefreshing = true

        // 조회가 실제로 나가는 시점부터 다음 주기를 다시 센다.
        // 수동 새로고침이나 절전 복귀처럼 타이머 밖에서 조회된 경우에도
        // 카운트다운이 맞고, 방금 조회했는데 곧바로 또 쏘는 일도 없어진다.
        // 화면이 꺼져 폴링을 멈춘 상태(timer == nil)라면 여기서 다시 켜지 않는다.
        if timer != nil { startTimer() }

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

    /// 다음 조회 예정 시각. 화면이 꺼져 폴링이 멈춰 있으면 nil.
    /// 429 백오프 중이면 타이머가 울려도 건너뛰므로, 실제로 조회하는 시점은 백오프가 풀린 뒤다.
    var nextPollDate: Date? {
        if let previewNextPoll { return previewNextPoll }
        guard let timer, timer.isValid else { return nil }
        if let backoffUntil, backoffUntil > timer.fireDate { return backoffUntil }
        return timer.fireDate
    }

    /// 툴팁·메뉴 맨 위에 쓰는 한 줄 요약.
    var summaryText: String {
        guard let snapshot else { return errorText ?? "사용량 불러오는 중…" }

        var parts: [String] = []
        if let plan = snapshot.planName { parts.append(plan) }
        if let fiveHour = snapshot.fiveHour {
            parts.append("세션 \(Int(fiveHour.utilization.rounded()))%\(Self.resetSuffix(fiveHour.resetsAt))")
        }
        if let sevenDay = snapshot.sevenDay {
            parts.append("주간 \(Int(sevenDay.utilization.rounded()))%\(Self.resetSuffix(sevenDay.resetsAt))")
        }
        if let errorText { parts.append("(갱신 실패: \(errorText))") }
        return parts.isEmpty ? "사용량 정보 없음" : parts.joined(separator: " · ")
    }

    private static func resetSuffix(_ resetsAt: Date?) -> String {
        guard let resetsAt, resetsAt.timeIntervalSinceNow > 0 else { return "" }
        return " (\(RemainingTime.text(until: resetsAt, now: Date())))"
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
