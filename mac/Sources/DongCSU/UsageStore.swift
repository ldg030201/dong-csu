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

    /// 계정 하나의 조회 상태.
    ///
    /// **계정마다 따로 든다.** 하나로 두면 계정을 바꿀 때마다 비워야 해서, 빨리 오가면 빈 화면이
    /// 남았다 — 10초 바닥에 걸린 조회가 그냥 버려졌기 때문이다. 429 대기도 같이 지워져서 오갈수록
    /// 요청이 늘었다. 따로 들면 돌아왔을 때 마지막 값이 바로 뜨고, 대기는 계정마다 그대로 남는다.
    struct AccountState {
        var snapshot: UsageSnapshot?
        var errorText: String?
        /// 자격증명이 없거나 만료됐다. 재시도해도 소용없고 Claude Code 재로그인이 필요하다.
        var needsReauth = false
        var backoffUntil: Date?
        var consecutiveRateLimits = 0
        /// 마지막으로 조회를 **내보낸** 시각. 성공·실패를 가리지 않는다 — 실패한 요청도
        /// 서버 쪽 계산에는 똑같이 들어간다. 요청 제한은 토큰에 걸리므로 계정마다 센다.
        var lastFetchAt: Date?
    }

    /// 지금 보는 계정의 상태. 화면은 전부 이걸 본다.
    @Published private(set) var current = AccountState()
    @Published private(set) var isRefreshing = false

    /// 안 보는 계정들의 상태. 돌아오면 그대로 꺼내 쓴다.
    private var parked: [String: AccountState] = [:]

    var snapshot: UsageSnapshot? { current.snapshot }
    var errorText: String? { current.errorText }
    var needsReauth: Bool { current.needsReauth }

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

    /// 5시간 한도가 있는 계정인지. 없으면 세션 링·줄을 통째로 뺀다.
    ///
    /// **아직 한 번도 못 읽었으면 있다고 본다.** 거의 모든 계정에 있어서, 첫 조회 전에
    /// 빼 두면 대부분의 사람에게 링 하나가 뒤늦게 생겨나는 것으로 보인다.
    var hasSessionLimit: Bool { snapshot?.hasSessionLimit ?? true }

    /// 세션(5시간) 한도를 다 썼다.
    var isSessionSpent: Bool {
        guard let session = snapshot?.fiveHour?.utilization else { return false }
        return session >= 100
    }

    /// 화면에 그릴 모델별 주간 한도(`weekly_scoped`) 하나. 제일 많이 쓴 것.
    ///
    /// **세션·주간과 달리 없을 수 있다.** 요금제와 쓴 모델에 따라 달라서, 화면에서도
    /// 있을 때만 그린다. 여럿이면 많이 쓴 것을 준다 — 링은 하나만 그릴 수 있다.
    var scopedLimit: UsageLimit? {
        snapshot?.limits.filter { $0.modelName != nil }.max { $0.percent < $1.percent }
    }

    /// 지금 이 순간 쓸 수 없다. **마스코트가 죽는 조건이다.**
    ///
    /// **둘 중 하나만 차도 못 쓴다.** 예전에는 주간만 봤는데, 세션을 다 쓴 사람은
    /// 다음 창이 열릴 때까지 한 글자도 못 보내면서 마스코트만 멀쩡히 걸어다녔다.
    /// 다시 쓸 수 있게 되기까지가 얼마나 남았는지는 링이 말하므로, 여기서는
    /// **지금 되느냐** 만 본다.
    var isSpent: Bool { isWeeklySpent || isSessionSpent }

    /// 조회가 성공할 때마다 부른다. 측정 기록(`UsageMeter`)이 여기에 붙는다.
    ///
    /// 저장소가 측정 객체를 직접 알면 조회와 기록이 한 덩어리가 되어, 조회만 쓰는
    /// 미리보기에서도 측정이 딸려 온다. 클로저로 끊어 둔다.
    var onSnapshot: ((UsageSnapshot) -> Void)?

    private var timer: Timer?
    private var inFlight = false

    /// 사용량을 볼 계정의 키체인 자리. nil 이면 Claude Code 기본 로그인.
    private(set) var account: String?

    private var accountKey: String { Self.key(account) }
    private static func key(_ service: String?) -> String { service ?? "" }

    /// 볼 계정을 바꾼다. **그 계정의 마지막 값을 바로 띄우고, 조회는 그 뒤에 덮어쓴다.**
    ///
    /// `force` 로 묻지 않는다 — 그 계정이 429 대기 중이면 대기를 지킨다. 바닥(10초)에 걸리면
    /// 조회는 버려지지 않고 풀리는 때로 미뤄진다(`scheduleRetry`). 그래서 마구 오가도 계정마다
    /// 10초에 한 번을 안 넘고, 화면은 늘 무언가를 띄우고 있다.
    func setAccount(_ service: String?) {
        guard service != account else { return }
        parked[accountKey] = current
        account = service
        current = parked.removeValue(forKey: accountKey) ?? AccountState()
        // 미뤄 둔 조회는 옛 계정의 바닥에 맞춘 것이다. 새 계정은 제 바닥으로 다시 잰다.
        cancelRetry()
        refresh()
    }

    init() {}

    /// 렌더 확인용. 네트워크 없이 고정값으로 채운 저장소.
    init(
        preview snapshot: UsageSnapshot,
        nextPoll: Date? = nil,
        error: String? = nil,
        needsReauth: Bool = false
    ) {
        self.current = AccountState(snapshot: snapshot, errorText: error, needsReauth: needsReauth)
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

    /// 다음 조회까지 남은 초. 0이면 지금 할 수 있다. 버튼에 숫자로 보여준다.
    ///
    /// **눌렀는데 아무 일도 안 일어나면 고장으로 보인다.** 몇 초 뒤면 되는지 알려 준다.
    func fetchCooldown(now: Date = Date()) -> TimeInterval {
        guard let last = current.lastFetchAt else { return 0 }
        return max(0, Self.minFetchInterval - now.timeIntervalSince(last))
    }

    /// 지금 조회를 내보낼 수 있는지. 버튼을 잠그는 데도 쓴다.
    var canFetchNow: Bool { fetchCooldown() <= 0 }

    /// 바닥에 걸린 조회를 바닥이 풀리는 때로 미뤄 둔 것. **하나만 걸어 둔다.**
    ///
    /// 예전에는 바닥에 걸리면 그냥 버렸다. 계정을 바꿔 화면을 새로 채워야 할 때 그게
    /// 버려지면 다음 폴링(기본 10분)까지 아무것도 안 뜬다.
    private var pendingRetry: DispatchWorkItem?
    /// 미뤄 둔 것들 중 하나라도 force 였는지. 미루는 사이 force 가 뭉개지면 안 된다.
    private var pendingRetryForce = false

    private func scheduleRetry(force: Bool) {
        pendingRetryForce = pendingRetryForce || force
        guard pendingRetry == nil else { return }
        let item = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                let force = self.pendingRetryForce
                self.cancelRetry()
                self.refresh(force: force)
            }
        }
        pendingRetry = item
        // 딱 맞춰 깨면 몇 ms 모자라 다시 미뤄질 수 있다. 조금 늦게 깬다.
        DispatchQueue.main.asyncAfter(deadline: .now() + fetchCooldown() + 0.1, execute: item)
    }

    private func cancelRetry() {
        pendingRetry?.cancel()
        pendingRetry = nil
        pendingRetryForce = false
    }

    func refresh(force: Bool = false) {
        if inFlight { return }
        guard canFetchNow else {
            scheduleRetry(force: force)
            return
        }
        if !force, let backoffUntil = current.backoffUntil, backoffUntil > Date() { return }
        if force { current.backoffUntil = nil }
        current.lastFetchAt = Date()

        inFlight = true
        isRefreshing = true

        // 조회가 실제로 나가는 시점부터 다음 주기를 다시 센다.
        // 수동 새로고침이나 절전 복귀처럼 타이머 밖에서 조회된 경우에도
        // 카운트다운이 맞고, 방금 조회했는데 곧바로 또 쏘는 일도 없어진다.
        // 화면이 꺼져 폴링을 멈춘 상태(timer == nil)라면 여기서 다시 켜지 않는다.
        if timer != nil { startTimer() }

        let requested = account
        // Task는 감싼 @MainActor 컨텍스트를 물려받으므로 본문은 메인 액터에서 돈다.
        Task { [weak self] in
            let outcome: Result<UsageSnapshot, Error>
            do {
                outcome = .success(try await UsageAPI.fetch(service: requested))
            } catch {
                outcome = .failure(error)
            }
            guard let self else { return }
            self.finish()

            if self.account == requested {
                Self.apply(outcome, to: &self.current)
            } else {
                // **도는 사이 계정을 바꿨다.** 이 결과는 다른 계정 것이라 지금 화면에 얹으면 안
                // 되지만, 버리지도 않는다 — 그 계정 자리에 넣어 두면 돌아왔을 때 바로 뜬다.
                // 지금 계정은 이게 도는 동안 묻지 못했으니 이제 묻는다.
                Self.apply(outcome, to: &self.parked[Self.key(requested), default: AccountState()])
                self.refresh()
            }
            // 측정은 계정을 보고 제 것만 고른다(`UsageMeter.record`). 다른 계정 값도 그대로 넘긴다.
            if case .success(let result) = outcome { self.onSnapshot?(result) }
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
        if let backoffUntil = current.backoffUntil, backoffUntil > timer.fireDate { return backoffUntil }
        return timer.fireDate
    }

    /// 툴팁·메뉴 맨 위에 쓰는 한 줄 요약.
    var summaryText: String {
        guard let snapshot else { return errorText ?? "사용량 불러오는 중…" }

        var parts: [String] = []
        if let plan = snapshot.planTitle { parts.append(plan) }
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

    /// 조회 결과를 그 계정의 상태에 얹는다.
    ///
    /// 실패해도 직전 성공값(snapshot)은 유지해서 링이 비어 보이지 않게 한다.
    private static func apply(_ outcome: Result<UsageSnapshot, Error>, to state: inout AccountState) {
        let error: Error
        switch outcome {
        case .success(let result):
            state.snapshot = result
            state.errorText = nil
            state.needsReauth = false
            state.consecutiveRateLimits = 0
            state.backoffUntil = nil
            return
        case .failure(let failure):
            error = failure
        }

        let usageError = error as? UsageError
        state.errorText = usageError?.description ?? error.localizedDescription
        state.needsReauth = usageError?.isTerminal ?? false

        // 재로그인은 사람이 해야 끝난다. 깨어남·수동 새로고침·로그인 직후에는
        // force로 들어오므로 여기서 길게 쉬어도 반응이 늦어지지 않는다.
        if state.needsReauth {
            state.consecutiveRateLimits = 0
            state.backoffUntil = Date().addingTimeInterval(terminalRetryInterval)
            return
        }

        guard let usageError, case .rateLimited(let retryAfter) = usageError else {
            state.consecutiveRateLimits = 0
            return
        }
        state.consecutiveRateLimits += 1
        // 60s, 120s, 240s … 최대 5분. Retry-After 헤더가 더 길면 그쪽을 따른다.
        let backoff = min(60 * pow(2, Double(state.consecutiveRateLimits - 1)), 300)
        state.backoffUntil = Date().addingTimeInterval(max(backoff, retryAfter ?? 0))
    }
}
