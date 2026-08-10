import AppKit
import Combine
import Foundation

/// 사용량을 주기적으로 가져와서 UI에 물려주는 상태 저장소.
@MainActor
final class UsageStore: ObservableObject {
    /// 조회 주기. 사용량 API는 5분 단위 레이트리밋 창을 쓰므로 너무 조이면 429가 난다.
    private(set) var pollInterval: TimeInterval = PollInterval.default.seconds

    /// 설정에서 주기를 바꾸면 돌고 있던 타이머를 새 주기로 다시 건다.
    func setPollInterval(_ seconds: TimeInterval) {
        guard seconds != pollInterval else { return }
        pollInterval = seconds
        if timer != nil { startTimer() }
    }

    @Published private(set) var snapshot: UsageSnapshot?
    @Published private(set) var errorText: String?
    @Published private(set) var isRefreshing = false
    /// 자격증명이 없거나 만료됐다. 재시도해도 소용없고 Claude Code 재로그인이 필요하다.
    @Published private(set) var needsReauth = false

    /// 화면에 떠 있는 숫자가 마지막 성공값(= 지금 값이 아닐 수 있음)인지.
    var isStale: Bool { snapshot != nil && errorText != nil }

    /// 화면 숫자가 지금 값이 아닌 상태. 재로그인이 필요하거나 갱신이 끊겼다.
    ///
    /// 마스코트가 회색이 되는 조건이자 스스로 움직이기를 멈추는 조건이다.
    /// 여러 곳에서 같은 판단을 하므로 한 곳에 둔다 — 어긋나면 캐릭터만 회색이고
    /// 나머지는 멀쩡해 보인다.
    var isDisconnected: Bool { needsReauth || isStale }

    /// 주간 한도를 다 썼다.
    ///
    /// **이러면 세션이 얼마 남았든 쓸 수 없다.** 세션 링만 초록으로 남아 있으면
    /// 아직 여유가 있는 것처럼 보이므로, 화면에서도 마스코트에서도 같이 죽은 것으로
    /// 다룬다. 여러 곳에서 따로 판단하면 어긋나므로 여기 한 곳에 둔다.
    var isWeeklySpent: Bool {
        guard let weekly = snapshot?.sevenDay?.utilization else { return false }
        return weekly >= 100
    }

    /// 조회가 성공할 때마다 부른다. 측정 기록(`UsageMeter`)이 여기에 붙는다.
    ///
    /// 저장소가 측정 객체를 직접 알면 조회와 기록이 한 덩어리가 되어, 조회만 쓰는
    /// 미리보기에서도 측정이 딸려 온다. 클로저로 끊어 둔다.
    var onSnapshot: ((UsageSnapshot) -> Void)?

    private var timer: Timer?
    private var backoffUntil: Date?
    private var consecutiveRateLimits = 0
    private var inFlight = false

    init() {}

    /// 렌더 확인용. 네트워크 없이 고정값으로 채운 저장소.
    init(
        preview snapshot: UsageSnapshot,
        nextPoll: Date? = nil,
        error: String? = nil,
        needsReauth: Bool = false
    ) {
        self.snapshot = snapshot
        self.previewNextPoll = nextPoll
        self.errorText = error
        self.needsReauth = needsReauth
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
        let timer = Timer(timeInterval: pollInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        // 타이머를 다른 시스템 깨우기와 묶어서 처리하게 여유를 크게 준다(전력 절약).
        timer.tolerance = pollInterval / 4
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

    /// 조회 사이 최소 간격.
    ///
    /// **`force` 로도 못 뚫는 바닥이다.** 새로고침 버튼·절전 복귀·화면 켜짐·측정 시작이
    /// 겹치면 몇 초 안에 여러 번 나가는데, 사용량 API는 창이 좁아서 그것만으로 429가 된다.
    ///
    /// 429를 맞은 뒤 쉬는 백오프와 **다른 물건이다** — 저쪽은 맞고 나서 물러서는 것이고
    /// 이건 맞기 전에 막는 것이다. force 가 백오프를 무시하도록 둔 이유(재로그인 직후처럼
    /// 사람이 상황을 바꾼 뒤엔 바로 봐야 한다)는 그대로 살아 있다.
    static let minFetchInterval: TimeInterval = 10

    /// 마지막으로 조회를 **내보낸** 시각. 성공·실패를 가리지 않는다 — 실패한 요청도
    /// 서버 쪽 계산에는 똑같이 들어간다.
    private var lastFetchAt: Date?

    /// 다음 조회까지 남은 초. 0이면 지금 할 수 있다. 버튼에 숫자로 보여준다.
    ///
    /// **눌렀는데 아무 일도 안 일어나면 고장으로 보인다.** 몇 초 뒤면 되는지 알려 준다.
    func fetchCooldown(now: Date = Date()) -> TimeInterval {
        guard let lastFetchAt else { return 0 }
        return max(0, Self.minFetchInterval - now.timeIntervalSince(lastFetchAt))
    }

    /// 지금 조회를 내보낼 수 있는지. 버튼을 잠그는 데도 쓴다.
    var canFetchNow: Bool { fetchCooldown() <= 0 }

    func refresh(force: Bool = false) {
        if inFlight { return }
        guard canFetchNow else { return }
        if !force, let backoffUntil, backoffUntil > Date() { return }
        if force { backoffUntil = nil }
        lastFetchAt = Date()

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
                self.needsReauth = false
                self.consecutiveRateLimits = 0
                self.backoffUntil = nil
                self.onSnapshot?(result)
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

    /// 재시도해도 소용없는 상태에서 얼마나 쉬었다 다시 볼지.
    /// 재로그인 전에는 조회가 반드시 실패하는데, 그때마다 키체인을 읽으려고
    /// `security` 프로세스를 새로 띄운다(1회 80ms). 그냥 두면 재로그인할 때까지
    /// 성공할 수 없는 조회를 폴링 주기마다 영원히 반복한다.
    private static let terminalRetryInterval: TimeInterval = 30 * 60

    /// 실패해도 직전 성공값(snapshot)은 유지해서 링이 비어 보이지 않게 한다.
    private func apply(error: Error) {
        let usageError = error as? UsageError
        errorText = usageError?.description ?? error.localizedDescription
        needsReauth = usageError?.isTerminal ?? false

        // 재로그인은 사람이 해야 끝난다. 깨어남·수동 새로고침·로그인 직후에는
        // force로 들어오므로 여기서 길게 쉬어도 반응이 늦어지지 않는다.
        if needsReauth {
            consecutiveRateLimits = 0
            backoffUntil = Date().addingTimeInterval(Self.terminalRetryInterval)
            return
        }

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
