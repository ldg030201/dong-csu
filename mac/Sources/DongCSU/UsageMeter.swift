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
        var seenIDs: Set<String> = []
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
        needsRebaseline = false
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
        requestSample()
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
        needsRebaseline = true
        save()

        startScanTimer()
        requestSample()
    }

    /// 다음 표본은 더하지 말고 기준만 옮긴다. 일시정지에서 돌아올 때 쓴다.
    private var needsRebaseline = false

    /// 조회를 부탁한다. **너무 잦으면 요청 제한(429)에 걸린다.**
    ///
    /// 시작·중지·계속을 연달아 누르면 그때마다 조회가 나가는데, 사용량 API는 창이
    /// 좁아서 금방 막힌다. 최근에 부탁했으면 건너뛴다 — 어차피 폴링이 곧 가져온다.
    private func requestSample() {
        if let last = lastSampleRequestAt, Date().timeIntervalSince(last) < Self.minSampleInterval {
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
        requestSample()

        if let pausedAt = state.pausedAt {
            state.pausedTotal += Date().timeIntervalSince(pausedAt)
            state.pausedAt = nil
        }
        state.stoppedAt = Date()
        stopScanTimer()
        archiveCurrent()
        save()
        // 멈추기 직전에 쓴 것도 들어가야 한다.
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

    // MARK: - 한도

    /// 조회가 성공할 때마다 부른다.
    ///
    /// **창이 리셋돼도 계속 쌓는다.** 5시간 창은 재는 도중에 반드시 한 번은 새로 열리는데,
    /// 그때 값이 0으로 떨어지므로 그냥 빼면 기록이 날아간다. 리셋을 만나면 새 창에서
    /// 쓴 몫을 그대로 더한다.
    func record(_ snapshot: UsageSnapshot) {
        guard isCounting || acceptsFinalSample else { return }
        if !isCounting { acceptsFinalSample = false }

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

            if needsRebaseline {
                // 세워 둔 동안 늘어난 몫은 이번 측정이 쓴 것이 아니다. 기준만 옮긴다.
                var moved = track
                moved.lastPercent = limit.percent
                moved.lastResetsAt = limit.resetsAt
                state.tracks[limit.id] = moved
            } else {
                state.tracks[limit.id] = Self.advance(track, with: limit)
            }
        }
        needsRebaseline = false

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
        guard isCounting || !isRunning else { return }
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
        return try? JSONDecoder().decode(UsageMeter.State.self, from: data)
    }

    func save(_ state: UsageMeter.State) {
        guard let url, AppSupport.prepared() != nil else { return }
        guard let data = try? JSONEncoder().encode(state) else { return }
        try? data.write(to: url, options: .atomic)
    }
}
