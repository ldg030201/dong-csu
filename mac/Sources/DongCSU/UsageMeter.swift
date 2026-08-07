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
    }

    @Published private(set) var state = State()
    /// 토큰 세는 중. 파일을 훑는 동안 버튼이 두 번 눌리지 않게 한다.
    @Published private(set) var isScanning = false

    var isRunning: Bool { state.startedAt != nil && state.stoppedAt == nil }
    var hasRecord: Bool { state.startedAt != nil }
    var tracksInOrder: [LimitTrack] { state.order.compactMap { state.tracks[$0] } }

    /// 잰 시간. 재는 중이면 지금까지, 멈췄으면 멈춘 시점까지.
    func elapsed(now: Date = Date()) -> TimeInterval? {
        guard let startedAt = state.startedAt else { return nil }
        return (state.stoppedAt ?? now).timeIntervalSince(startedAt)
    }

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
        if isRunning { startScanTimer() }
    }

    /// 렌더 확인용. 파일도 타이머도 건드리지 않는다.
    init(preview state: State) {
        self.store = MeterStore(url: nil)
        self.state = state
    }

    // MARK: - 시작 · 중지

    func start() {
        var fresh = State()
        fresh.startedAt = Date()
        // **지금 파일 끝을 기준으로 잡는다.** 0부터 읽으면 며칠 치 옛 기록을 훑게 된다.
        fresh.offsets = ClaudeCodeUsage.endOffsets()
        state = fresh
        save()

        startScanTimer()
        scanTokens()
    }

    func stop() {
        guard isRunning else { return }
        state.stoppedAt = Date()
        stopScanTimer()
        save()
        // 멈추기 직전에 쓴 것도 들어가야 한다.
        scanTokens()
    }

    func reset() {
        stopScanTimer()
        state = State()
        save()
    }

    // MARK: - 한도

    /// 조회가 성공할 때마다 부른다.
    ///
    /// **창이 리셋돼도 계속 쌓는다.** 5시간 창은 재는 도중에 반드시 한 번은 새로 열리는데,
    /// 그때 값이 0으로 떨어지므로 그냥 빼면 기록이 날아간다. 리셋을 만나면 새 창에서
    /// 쓴 몫을 그대로 더한다.
    func record(_ snapshot: UsageSnapshot) {
        guard isRunning else { return }

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

            state.tracks[limit.id] = Self.advance(track, with: limit)
        }

        state.samples += 1
        state.lastSampledAt = snapshot.fetchedAt
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
        guard ClaudeCodeUsage.isAvailable else { return }

        isScanning = true
        let scan = TokenScan(since: since, offsets: state.offsets, seenIDs: state.seenIDs)

        Task { [weak self] in
            // 파일을 훑는 동안 화면을 붙잡지 않는다.
            let result = await Task.detached(priority: .utility) { scan.run() }.value
            guard let self else { return }
            self.apply(result)
        }
    }

    private func apply(_ result: TokenScan.Result) {
        isScanning = false
        state = Self.applying(result, to: state)
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
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
        // 번들 ID로 갈라서 테스트판과 정식판이 서로의 기록을 건드리지 않게 한다.
        let folder = base?.appendingPathComponent(
            Bundle.main.bundleIdentifier ?? "com.ldg.dong-csu", isDirectory: true
        )
        self.url = folder?.appendingPathComponent("meter.json")
    }

    /// 렌더 확인용. `nil`을 주면 아무것도 읽지도 쓰지도 않는다.
    init(url: URL?) { self.url = url }

    func load() -> UsageMeter.State? {
        guard let url, let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(UsageMeter.State.self, from: data)
    }

    func save(_ state: UsageMeter.State) {
        guard let url else { return }
        let folder = url.deletingLastPathComponent()
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        guard let data = try? JSONEncoder().encode(state) else { return }
        try? data.write(to: url, options: .atomic)
    }
}
