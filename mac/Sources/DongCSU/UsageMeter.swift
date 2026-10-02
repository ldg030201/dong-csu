import Combine
import Foundation

/// 시작·중지를 눌러 그 사이에 얼마나 썼는지 재는 저장소.
///
/// **두 가지를 나란히 잰다.** 서로 메우는 구멍이 다르다.
///
/// | | 잡는 범위 | 눈금 |
/// | --- | --- | --- |
/// | 한도 %p | 계정 전부 — Claude Code·클로드 앱·웹 | 1%p (서버가 정수로 준다) |
/// | 토큰 수 | **Claude Code만** | 토큰 단위 |
///
/// 한도 쪽은 어디서 쓰든 같은 창을 깎아서 전부 잡히는 대신 눈금이 굵고, 토큰 쪽은
/// 촘촘한 대신 클로드 앱에서 쓴 것을 못 본다. 그래서 하나만 두면 답이 안 된다.
///
/// **앱을 껐다 켜도 이어진다.** 몇 시간짜리 측정이 재시작 한 번에 날아가면 쓸모가 없다.
@MainActor
final class UsageMeter: ObservableObject {

    /// 한도 하나를 따라가는 기록.
    struct LimitTrack: Codable, Equatable {
        var title: String
        /// 여태 쌓인 소모량(%p). 창이 리셋돼도 계속 더한다.
        var accumulated: Double = 0
        /// 직전에 본 값. 다음 표본과 견줘 증가분을 뽑는다.
        var lastPercent: Double = 0
        var lastResetsAt: Date?
        /// 재는 동안 창이 몇 번 새로 열렸는지.
        var resets: Int = 0
        /// 다음 표본은 더하지 말고 기준만 옮긴다. **일시정지에서 돌아올 때 켠다.**
        ///
        /// 예전에는 측정 전체에 깃발 하나였고, 재개 뒤 **첫 표본에 들어 있던 한도만**
        /// 기준을 옮긴 뒤 그 깃발을 꺼 버렸다. 모델별 주간 한도처럼 표본에 없을 수도
        /// 있는 한도는 옛 기준을 그대로 들고 있다가, 다시 나타나는 순간 세워 둔 동안
        /// 쓴 몫을 통째로 더했다. 한도마다 따로 들면 그 틈이 없어진다.
        /// **파일에도 남는다** — 깃발만 메모리에 두면 재개하고 앱을 끄는 것만으로
        /// 같은 일이 벌어졌다.
        var needsRebaseline = false
    }

    /// 끝난 측정 하나. 목록과 팝업이 이걸 그린다.
    ///
    /// 중지한 **그 순간의 값을 통째로 얼려 둔다.** 나중에 다시 계산하지 않으므로
    /// 그때 무엇을 봤는지가 그대로 남는다.
    struct Record: Codable, Equatable, Identifiable {
        /// 시작 시각이 곧 구분자다. 같은 순간에 두 번 시작할 수 없다.
        var id: Date { startedAt }
        let startedAt: Date
        let stoppedAt: Date
        let tracks: [LimitTrack]
        let tokens: TokenTally
        let tokensByModel: [String: TokenTally]
        let samples: Int

        var duration: TimeInterval { stoppedAt.timeIntervalSince(startedAt) }
    }

    struct State: Codable, Equatable {
        var startedAt: Date?
        var stoppedAt: Date?
        var tracks: [String: LimitTrack] = [:]
        /// 화면에 늘어놓는 차례. 사전은 순서가 없어서 따로 들고 있는다.
        var order: [String] = []
        var tokens = TokenTally()
        var tokensByModel: [String: TokenTally] = [:]
        var offsets: [String: UInt64] = [:]
        var seenIDs = SeenIDs()
        var samples = 0
        var lastSampledAt: Date?
        /// 일시정지한 시각. nil이면 돌고 있다.
        var pausedAt: Date?
        /// 여태 멈춰 있던 시간의 합. 잰 시간에서 뺀다.
        var pausedTotal: TimeInterval = 0
        /// 끝난 측정들. 최신이 앞이다.
        var history: [Record] = []
    }

    /// 남겨 두는 기록 수. 넘치면 오래된 것부터 버린다.
    private static let historyLimit = 50

    @Published private(set) var state = State()
    /// 토큰 세는 중. 파일을 훑는 동안 버튼이 두 번 눌리지 않게 한다.
    @Published private(set) var isScanning = false

    var isRunning: Bool { state.startedAt != nil && state.stoppedAt == nil }
    var isPaused: Bool { isRunning && state.pausedAt != nil }
    /// 실제로 세고 있는 중. 일시정지 동안에는 표본도 토큰도 받지 않는다.
    var isCounting: Bool { isRunning && state.pausedAt == nil }
    var tracksInOrder: [LimitTrack] { state.order.compactMap { state.tracks[$0] } }

    /// 잰 시간. 재는 중이면 지금까지, 멈췄으면 멈춘 시점까지.
    /// **멈춰 있던 시간은 뺀다.** 안 그러면 잠깐 세우고 밥 먹고 온 시간이 측정에 들어간다.
    func elapsed(now: Date = Date()) -> TimeInterval? {
        guard let startedAt = state.startedAt else { return nil }
        let end = state.stoppedAt ?? now
        var paused = state.pausedTotal
        if let pausedAt = state.pausedAt { paused += end.timeIntervalSince(pausedAt) }
        return max(0, end.timeIntervalSince(startedAt) - paused)
    }

    /// 지금 바로 한 번 조회해 달라고 부탁한다.
    ///
    /// **시작을 누른 순간 기준점을 잡아야 한다.** 다음 폴링까지 기다리면 기본 설정에서
    /// 10분 동안 "첫 조회를 기다리는 중"만 뜬다 — 그동안 실제로 쓴 것도 기준이 없어서
    /// 못 센다. 저장소를 직접 알지 않으려고 클로저로 받는다.
    var onNeedsSample: (() -> Void)?

    /// 토큰을 다시 세는 주기.
    ///
    /// 사용량 조회(기본 10분)에 묶어두면 화면 숫자가 너무 오래 멈춰 있는다. 덧붙은
    /// 부분만 읽어서 값이 싸므로 따로 짧게 돈다.
    private static let scanInterval: TimeInterval = 60

    private var scanTimer: Timer?
    private let store: MeterStore

    init(store: MeterStore = MeterStore()) {
        self.store = store
        state = store.load() ?? State()
        guard isRunning else { return }
        startScanTimer()
        // 앱이 꺼져 있는 동안 쌓인 것을 바로 얹는다. 타이머만 걸면 1분 동안 빈다.
        scanTokens()
    }

    /// 렌더 확인용. 파일도 타이머도 건드리지 않는다.
    init(preview state: State) {
        self.store = MeterStore(url: nil)
        self.state = state
    }

    // MARK: - 시작 · 중지

    func start() {
        acceptsFinalSample = false
        finalSampleUntil = nil
        acceptsFinalScan = false
        // 새 `State` 라 한도 기록이 비어 있다 — 재기준 깃발도 같이 사라진다.
        var fresh = State()
        fresh.startedAt = Date()
        // **지난 기록은 그대로 가져간다.** 새로 재기 시작했다고 지난 것을 버리면,
        // 다시 시작 한 번에 여태 쌓은 기록이 통째로 날아간다.
        fresh.history = state.history
        // **지금 파일 끝을 기준으로 잡는다.** 0부터 읽으면 며칠 치 옛 기록을 훑게 된다.
        fresh.offsets = ClaudeCodeUsage.endOffsets()
        state = fresh
        save()

        startScanTimer()
        scanTokens()
        // **기준점은 지금 값이어야 한다.** 마지막 조회는 10분 전 것일 수 있고, 그걸
        // 기준으로 삼으면 시작을 누르기 전에 쓴 몫이 이번 측정에 들어간다.
        requestSample(force: true)
    }

    /// 잠깐 세운다. 세워 둔 동안 쓴 것은 이번 측정에 안 들어간다.
    func pause() {
        guard isCounting else { return }
        // 세우기 직전까지 쓴 토큰은 담는다.
        scanTokens()
        state.pausedAt = Date()
        stopScanTimer()
        save()
    }

    /// 다시 센다. **기준을 지금으로 새로 잡는다** — 세워 둔 동안의 소모는 빼야 한다.
    func resume() {
        guard let pausedAt = state.pausedAt else { return }
        state.pausedTotal += Date().timeIntervalSince(pausedAt)
        state.pausedAt = nil
        state.offsets = ClaudeCodeUsage.endOffsets()
        for id in state.tracks.keys { state.tracks[id]?.needsRebaseline = true }
        save()

        startScanTimer()
        requestSample(force: true)
    }

    /// 조회를 부탁한다. **너무 잦으면 요청 제한(429)에 걸린다.**
    ///
    /// 시작·중지·계속을 연달아 누르면 그때마다 조회가 나가는데, 사용량 API는 창이
    /// 좁아서 금방 막힌다. 최근에 부탁했으면 건너뛴다 — 어차피 폴링이 곧 가져온다.
    /// `force` 는 **사람이 누른 것**에만 쓴다(시작 · 계속 · 중지). 그 셋은 기준점을
    /// 잡는 조회라 건너뛰면 측정 자체가 틀어진다 — 시작 30초 안에 멈추면 중지 조회가
    /// 통째로 안 나가서, 한참 뒤 폴링이 마지막 표본이 되곤 했다.
    /// 요청 제한은 저장소 쪽(10초 바닥 · 429 백오프)이 어차피 다시 막는다.
    private func requestSample(force: Bool = false) {
        if !force, let last = lastSampleRequestAt,
           Date().timeIntervalSince(last) < Self.minSampleInterval {
            return
        }
        lastSampleRequestAt = Date()
        onNeedsSample?()
    }

    private var lastSampleRequestAt: Date?
    private static let minSampleInterval: TimeInterval = 30

    func stop() {
        guard isRunning else { return }

        // **멈출 때도 한 번 더 잰다.** 조회 주기가 10분인데 5분 재고 멈추면 표본이
        // 시작 때 하나뿐이라 소모량이 늘 0%p가 된다. 시작과 중지에서 각각 한 번씩
        // 재면 아무리 짧게 재도 두 점 사이의 차이가 남는다.
        acceptsFinalSample = true
        finalSampleUntil = Date().addingTimeInterval(Self.finalSampleWindow)
        requestSample(force: true)

        if let pausedAt = state.pausedAt {
            state.pausedTotal += Date().timeIntervalSince(pausedAt)
            state.pausedAt = nil
        }
        state.stoppedAt = Date()
        stopScanTimer()
        archiveCurrent()
        save()
        // 멈추기 직전에 쓴 것도 들어가야 한다. 이 한 번만 통과한다.
        acceptsFinalScan = true
        scanTokens()
    }

    // MARK: - 기록

    /// 끝난 측정을 목록 맨 위에 남긴다.
    ///
    /// **중지하는 그 순간 바로 남긴다.** 마지막 표본과 마지막 훑기는 조금 뒤에 도착하는데,
    /// 그때 `syncArchived()` 가 같은 자리를 다시 덮어써서 최종값이 들어간다. 도착을
    /// 기다렸다 남기면, 조회가 실패했을 때 기록이 영영 안 생긴다.
    private func archiveCurrent() {
        guard let record = currentRecord() else { return }
        state.history.removeAll { $0.startedAt == record.startedAt }
        state.history.insert(record, at: 0)
        if state.history.count > Self.historyLimit {
            state.history.removeLast(state.history.count - Self.historyLimit)
        }
    }

    /// 중지 뒤 늦게 도착한 값으로 목록의 그 기록을 갱신한다.
    private func syncArchived() {
        guard !isRunning, let record = currentRecord() else { return }
        guard let index = state.history.firstIndex(where: { $0.startedAt == record.startedAt }) else { return }
        state.history[index] = record
    }

    private func currentRecord() -> Record? {
        guard let startedAt = state.startedAt, let stoppedAt = state.stoppedAt else { return nil }
        return Record(
            startedAt: startedAt,
            stoppedAt: stoppedAt,
            tracks: tracksInOrder,
            tokens: state.tokens,
            tokensByModel: state.tokensByModel,
            samples: state.samples
        )
    }

    /// 목록만 비운다. 재고 있던 것은 건드리지 않는다.
    func clearHistory() {
        state.history = []
        save()
    }

    /// 기록 하나만 지운다. 시작 시각이 구분자다.
    func deleteRecord(_ record: Record) {
        state.history.removeAll { $0.startedAt == record.startedAt }
        save()
    }

    /// 멈춘 뒤 딱 한 번, 마지막 표본을 받아 준다. 조회가 돌아오는 데 시간이 걸려서
    /// 그때는 이미 `stoppedAt` 이 찍혀 있다.
    private var acceptsFinalSample = false

    /// 중지 뒤 **이 시각까지 떠 온 조회만** 마지막 표본으로 받는다.
    ///
    /// "딱 한 번 더 받는다" 로는 모자랐다. 중지가 부탁한 조회는 조용히 버려질 수 있는데
    /// (이미 날아간 요청 · 저장소의 10초 바닥 · 429 백오프 최대 5분), 그러면 깃발만 켜진
    /// 채 남아서 **한참 뒤의 평범한 폴링**이 마지막 표본이 된다 — 20초 재고 멈춘 측정에
    /// 10분치 소모가 붙었다. 시각으로 막으면 늦게 온 것은 그냥 안 받는다.
    private var finalSampleUntil: Date?

    /// 중지 뒤 마지막 표본을 기다려 주는 시간.
    /// 조회 한 번은 보통 1초 안쪽이고, 백오프에 걸렸으면 어차피 기다릴 값어치가 없다.
    private static let finalSampleWindow: TimeInterval = 90

    /// 멈춘 뒤 **딱 한 번** 더 훑는 것을 받아 준다. `stop()` 이 켜고 훑기가 끈다.
    private var acceptsFinalScan = false

    /// 멈춘 뒤 도착한 이 조회를 마지막 표본으로 칠지.
    /// **떠 온 시각으로 가른다** — 도착 시각이 아니라 서버에서 읽어 온 시각이라야,
    /// 느리게 돌아온 조회와 한참 뒤의 새 조회가 구별된다.
    private func takesAsFinalSample(_ snapshot: UsageSnapshot) -> Bool {
        guard acceptsFinalSample, let until = finalSampleUntil else { return false }
        return snapshot.fetchedAt <= until
    }

    // MARK: - 한도

    /// 조회가 성공할 때마다 부른다.
    ///
    /// **창이 리셋돼도 계속 쌓는다.** 5시간 창은 재는 도중에 반드시 한 번은 새로 열리는데,
    /// 그때 값이 0으로 떨어지므로 그냥 빼면 기록이 날아간다. 리셋을 만나면 새 창에서
    /// 쓴 몫을 그대로 더한다.
    func record(_ snapshot: UsageSnapshot) {
        guard isCounting || takesAsFinalSample(snapshot) else { return }
        if !isCounting { acceptsFinalSample = false; finalSampleUntil = nil }

        for limit in Self.limits(of: snapshot) {
            guard let track = state.tracks[limit.id] else {
                // 첫 표본은 기준점일 뿐이다. 여기서 더하면 재기 시작한 순간
                // 여태 쓴 것이 전부 이번 측정치로 들어간다.
                state.tracks[limit.id] = LimitTrack(
                    title: limit.title,
                    lastPercent: limit.percent,
                    lastResetsAt: limit.resetsAt
                )
                state.order.append(limit.id)
                continue
            }

            if track.needsRebaseline {
                // 세워 둔 동안 늘어난 몫은 이번 측정이 쓴 것이 아니다. 기준만 옮긴다.
                var moved = track
                moved.lastPercent = limit.percent
                moved.lastResetsAt = limit.resetsAt
                moved.needsRebaseline = false
                state.tracks[limit.id] = moved
            } else {
                state.tracks[limit.id] = Self.advance(track, with: limit)
            }
        }

        state.samples += 1
        state.lastSampledAt = snapshot.fetchedAt
        syncArchived()
        save()
    }

    /// 표본 하나를 기록에 반영한다.
    ///
    /// 재는 동안 5시간 창은 반드시 한 번은 새로 열린다. **그때 값이 0으로 떨어지므로
    /// 그냥 빼면 기록이 날아간다.** 창이 바뀐 것을 알아채면 새 창에서 쓴 몫을 그대로 더한다.
    ///
    /// 파일도 시계도 타지 않는 순수 계산이라 `--probe-meter selftest`가 이걸 직접 검사한다.
    nonisolated static func advance(_ track: LimitTrack, with limit: UsageLimit) -> LimitTrack {
        var track = track

        if windowMoved(from: track.lastResetsAt, to: limit.resetsAt) {
            track.accumulated += limit.percent
            track.resets += 1
        } else if limit.percent > track.lastPercent {
            track.accumulated += limit.percent - track.lastPercent
        }
        // 창은 그대로인데 값이 내려갔으면 서버 쪽 보정이다. 기준만 옮기고 더하지 않는다.

        track.lastPercent = limit.percent
        track.lastResetsAt = limit.resetsAt
        track.title = limit.title
        return track
    }

    /// 창이 새로 열렸는지.
    ///
    /// **초 단위로 견주지 않는다.** `resets_at`은 마이크로초까지 오고 서버가 매번 조금씩
    /// 다르게 줄 수 있는데, 그걸 리셋으로 세면 표본마다 소모량이 통째로 더해져 값이 터진다.
    nonisolated private static func windowMoved(from old: Date?, to new: Date?) -> Bool {
        guard let old else { return false }
        guard let new else { return false }
        return abs(new.timeIntervalSince(old)) > 60
    }

    /// 옛 서버 응답에는 `limits`가 없다. 그때는 HUD가 쓰는 두 개로 대신한다.
    private static func limits(of snapshot: UsageSnapshot) -> [UsageLimit] {
        if !snapshot.limits.isEmpty { return snapshot.limits }

        var fallback: [UsageLimit] = []
        if let window = snapshot.fiveHour {
            fallback.append(UsageLimit(kind: "session", modelName: nil,
                                       percent: window.utilization, resetsAt: window.resetsAt))
        }
        if let window = snapshot.sevenDay {
            fallback.append(UsageLimit(kind: "weekly_all", modelName: nil,
                                       percent: window.utilization, resetsAt: window.resetsAt))
        }
        return fallback
    }

    // MARK: - 토큰

    func scanTokens() {
        guard let since = state.startedAt, !isScanning else { return }
        // 세워 둔 동안 쓴 것은 세지 않는다. 다만 중지 직후의 마지막 훑기는 통과시킨다.
        guard isCounting || acceptsFinalScan else { return }
        // **딱 한 번만 통과시킨다.** 예전에는 `!isRunning` 이라 중지한 뒤로 **모든**
        // 호출이 통과했다. 설정 창 측정 탭이 5초마다 이걸 부르므로, 멈춘 측정의
        // 토큰이 탭을 열어 둔 내내 계속 올라가고 `syncArchived()` 가 그 값을 기록에
        // 덮어썼다 — 끝난 측정인데 볼 때마다 숫자가 달랐다.
        acceptsFinalScan = false
        guard ClaudeCodeUsage.isAvailable else { return }

        isScanning = true
        let scan = TokenScan(since: since, offsets: state.offsets, seenIDs: state.seenIDs)
        // 훑기 시작한 시점의 측정이 무엇이었는지 적어 둔다. 돌아왔을 때 대조한다.
        let stamp = sessionStamp

        Task { [weak self] in
            // 파일을 훑는 동안 화면을 붙잡지 않는다.
            let result = await Task.detached(priority: .utility) { scan.run() }.value
            guard let self else { return }
            self.apply(result, from: stamp)
        }
    }

    /// 지금 재고 있는 것을 가리키는 표식.
    ///
    /// **다시 시작하면 `startedAt` 이, 계속을 누르면 `pausedTotal` 이 달라진다.** 중지는
    /// 둘 다 그대로 두므로, 중지 직후의 마지막 훑기는 이 대조를 통과한다.
    private struct SessionStamp: Equatable {
        let startedAt: Date?
        let pausedTotal: TimeInterval
    }

    private var sessionStamp: SessionStamp {
        SessionStamp(startedAt: state.startedAt, pausedTotal: state.pausedTotal)
    }

    private func apply(_ result: TokenScan.Result, from stamp: SessionStamp) {
        isScanning = false

        // **훑는 사이에 딴 측정이 됐으면 버린다.** 파일을 읽는 동안 메인 액터가 풀려서,
        // 그 틈에 다시 시작이나 계속을 누르면 옛 결과가 새 측정 위에 떨어진다 —
        // 옛 토큰이 새 측정에 더해지고, 새로 잡아 둔 기준(오프셋)이 옛 자리로 되감긴다.
        // 되감기면 그 뒤로 계속 같은 자리를 다시 읽어서, 세워 둔 동안 쓴 것까지 딸려온다.
        guard stamp == sessionStamp else { return }

        state = Self.applying(result, to: state)
        syncArchived()
        save()
    }

    /// 훑은 결과를 기록에 얹는다.
    ///
    /// 액터 밖에서도 부를 수 있게 떼어 뒀다 — `--probe-meter scan` 이 이걸 그대로 써서,
    /// 확인 통로와 실제 동작이 갈라질 수 없다.
    nonisolated static func applying(_ result: TokenScan.Result, to state: State) -> State {
        var state = state
        state.offsets = result.offsets
        state.seenIDs = result.seenIDs
        state.tokens += result.added
        for (model, tally) in result.addedByModel {
            state.tokensByModel[model, default: TokenTally()] += tally
        }
        return state
    }

    private func startScanTimer() {
        stopScanTimer()
        let timer = Timer(timeInterval: Self.scanInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.scanTokens() }
        }
        timer.tolerance = Self.scanInterval / 4
        RunLoop.main.add(timer, forMode: .common)
        scanTimer = timer
    }

    private func stopScanTimer() {
        scanTimer?.invalidate()
        scanTimer = nil
    }

    private func save() { store.save(state) }
}

// MARK: - 너그러운 복원

/// **없는 키는 기본값으로 둔다.**
///
/// 자동 생성 `Decodable` 은 프로퍼티에 적어 둔 기본값을 **안 쓴다** — 키가 하나라도
/// 없으면 `keyNotFound` 로 통째로 실패한다. 그래서 여기 필드를 하나 더하는 것만으로
/// 옛 `meter.json` 이 못 읽는 파일이 되고, 바로 다음 저장이 그 자리를 덮어써서
/// **측정 기록 50개가 조용히 사라진다.** 키마다 따로 읽어 그 길을 막는다.
///
/// 나중에 필드를 더하면서 여기 한 줄을 빠뜨려도 손해는 "그 값만 기본값으로 돌아간다"
/// 까지다. 기록은 안 날아간다.
extension UsageMeter.LimitTrack {
    init(from decoder: any Decoder) throws {
        let box = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            title: (try? box.decode(String.self, forKey: .title)) ?? "",
            accumulated: (try? box.decode(Double.self, forKey: .accumulated)) ?? 0,
            lastPercent: (try? box.decode(Double.self, forKey: .lastPercent)) ?? 0,
            lastResetsAt: try? box.decode(Date.self, forKey: .lastResetsAt),
            resets: (try? box.decode(Int.self, forKey: .resets)) ?? 0,
            needsRebaseline: (try? box.decode(Bool.self, forKey: .needsRebaseline)) ?? false
        )
    }
}

extension UsageMeter.Record {
    /// **시작·끝 시각만 없으면 포기한다.** 그 둘은 기록의 신원이라 기본값이 없다.
    init(from decoder: any Decoder) throws {
        let box = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            startedAt: try box.decode(Date.self, forKey: .startedAt),
            stoppedAt: try box.decode(Date.self, forKey: .stoppedAt),
            tracks: (try? box.decode([UsageMeter.LimitTrack].self, forKey: .tracks)) ?? [],
            tokens: (try? box.decode(TokenTally.self, forKey: .tokens)) ?? TokenTally(),
            tokensByModel: (try? box.decode([String: TokenTally].self, forKey: .tokensByModel)) ?? [:],
            samples: (try? box.decode(Int.self, forKey: .samples)) ?? 0
        )
    }
}

extension UsageMeter.State {
    init(from decoder: any Decoder) throws {
        let box = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            startedAt: try? box.decode(Date.self, forKey: .startedAt),
            stoppedAt: try? box.decode(Date.self, forKey: .stoppedAt),
            tracks: (try? box.decode([String: UsageMeter.LimitTrack].self, forKey: .tracks)) ?? [:],
            order: (try? box.decode([String].self, forKey: .order)) ?? [],
            tokens: (try? box.decode(TokenTally.self, forKey: .tokens)) ?? TokenTally(),
            tokensByModel: (try? box.decode([String: TokenTally].self, forKey: .tokensByModel)) ?? [:],
            offsets: (try? box.decode([String: UInt64].self, forKey: .offsets)) ?? [:],
            seenIDs: (try? box.decode(SeenIDs.self, forKey: .seenIDs)) ?? SeenIDs(),
            samples: (try? box.decode(Int.self, forKey: .samples)) ?? 0,
            lastSampledAt: try? box.decode(Date.self, forKey: .lastSampledAt),
            pausedAt: try? box.decode(Date.self, forKey: .pausedAt),
            pausedTotal: (try? box.decode(TimeInterval.self, forKey: .pausedTotal)) ?? 0,
            history: Self.records(in: box)
        )
    }

    /// **한 줄이 깨져도 나머지 기록은 지킨다.** 배열째 읽으면 기록 하나가 상한 것과
    /// 파일이 통째로 상한 것이 구별되지 않는다.
    private static func records(
        in box: KeyedDecodingContainer<UsageMeter.State.CodingKeys>
    ) -> [UsageMeter.Record] {
        guard var rows = try? box.nestedUnkeyedContainer(forKey: .history) else { return [] }
        var kept: [UsageMeter.Record] = []
        while !rows.isAtEnd {
            if let row = try? rows.decode(UsageMeter.Record.self) {
                kept.append(row)
            } else if (try? rows.decode(SkippedRow.self)) == nil {
                // 건너뛰지도 못하면 자리가 안 넘어가 제자리를 돈다. 거기서 멈춘다.
                break
            }
        }
        return kept
    }
}

/// 못 읽은 기록 한 줄을 건너뛰려고 둔다. 무엇이든 받아들인다.
private struct SkippedRow: Decodable {}

/// 측정 기록을 파일에 둔다.
///
/// **UserDefaults에 넣지 않는다.** 중복 제거용 id가 몇천 개까지 쌓이는 데이터라
/// 설정과 성격이 다르고, 설정 도메인을 통째로 비우는 "모든 설정 초기화"에 딸려
/// 지워지는 것도 곤란하다 — 초기화는 설정을 되돌리는 것이지 재던 것을 버리는 게 아니다.
struct MeterStore: Sendable {
    private let url: URL?

    init() {
        self.url = AppSupport.folder?.appendingPathComponent("meter.json")
    }

    /// 렌더 확인용. `nil`을 주면 아무것도 읽지도 쓰지도 않는다.
    init(url: URL?) { self.url = url }

    func load() -> UsageMeter.State? {
        guard let url, let data = try? Data(contentsOf: url) else { return nil }
        if let state = try? JSONDecoder().decode(UsageMeter.State.self, from: data) { return state }
        // **못 읽은 파일을 그 자리에 두지 않는다.** 다음 저장이 곧바로 덮어써서,
        // 한 번 못 읽으면 되살릴 방법이 없어진다. 옆으로 치워 두고 빈 상태로 시작한다.
        let spoiled = url.appendingPathExtension("bad")
        try? FileManager.default.removeItem(at: spoiled)
        try? FileManager.default.moveItem(at: url, to: spoiled)
        print("측정 기록을 못 읽었다 — \(spoiled.lastPathComponent) 로 치워 두고 새로 시작한다")
        return nil
    }

    func save(_ state: UsageMeter.State) {
        guard let url, AppSupport.prepared() != nil else { return }
        guard let data = try? JSONEncoder().encode(state) else { return }
        try? data.write(to: url, options: .atomic)
    }
}
