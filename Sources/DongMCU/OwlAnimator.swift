import Foundation
import SwiftUI

/// 부엉이의 기분. 사용량·연결 상태·드래그 여부에서 정해진다.
///
/// 기분마다 프레임 목록을 들고 있고, 애니메이터는 그걸 차례로 돌린다.
enum OwlMood: String, CaseIterable {
    /// 평소. 가만히 있다가 이따금 눈을 깜빡인다.
    case idle
    /// 세션을 거의 다 썼다. 눈이 반쯤 감긴다.
    case tired
    /// 세션을 다 썼다. 눈을 감고 발 위로 내려앉아 숨만 쉰다.
    case exhausted
    /// 조회가 안 되는 중. 색이 빠지고 멈춘다.
    case offline
    /// 목덜미를 잡혀 끌려가는 중. 다리가 버둥거린다.
    case dragged

    var title: String {
        switch self {
        case .idle: return "평소"
        case .tired: return "지침"
        case .exhausted: return "탈진"
        case .offline: return "끊김"
        case .dragged: return "끌림"
        }
    }

    /// 조회가 안 되는 동안에는 색을 빼서 지금 값이 아님을 몸으로 드러낸다.
    var palette: OwlPalette {
        self == .offline ? .offline : .normal
    }

    /// 이 기분에서 차례로 보여줄 프레임들. 마지막까지 가면 처음으로 돌아간다.
    var frames: [OwlFrame] {
        switch self {
        case .idle:
            // 눈을 뜬 채 한참 있다가 두어 프레임만 깜빡인다.
            // 지터가 없으면 정확히 같은 박자로 깜빡여서 시계처럼 보인다.
            return [
                OwlFrame(OwlPose(), duration: 3.0, jitter: 3.5),
                OwlFrame(OwlPose(eyes: .half), duration: 0.05),
                OwlFrame(OwlPose(eyes: .closed), duration: 0.08),
                OwlFrame(OwlPose(eyes: .half), duration: 0.05),
            ]

        case .tired:
            // 날개를 늘어뜨리고 눈을 반쯤 뜬 게 기본. 이따금 길게 감았다 뜬다.
            // 날개는 탈진까지 늘어진 채로 이어져서, 평소 → 지침 → 탈진이 단계로 읽힌다.
            return [
                OwlFrame(OwlPose(eyes: .half, wings: .droop), duration: 2.4, jitter: 2.2),
                OwlFrame(OwlPose(eyes: .closed, wings: .droop), duration: 0.9),
            ]

        case .exhausted:
            // 발 위로 주저앉아 다리가 몸에 가려진 채, 눈만 이따금 살짝 뜬다.
            // 오르내리며 숨 쉬게 하면 그때마다 다리가 나왔다 들어가서 형태가 흔들린다.
            return [
                OwlFrame(
                    OwlPose(eyes: .closed, wings: .droop, bob: 1),
                    duration: 2.6,
                    jitter: 2.0
                ),
                OwlFrame(OwlPose(eyes: .half, wings: .droop, bob: 1), duration: 0.45),
            ]

        case .offline:
            // 프레임이 하나뿐이라 애니메이터가 타이머를 아예 걸지 않는다.
            return [OwlFrame(OwlPose(eyes: .half), duration: 0)]

        case .dragged:
            // 들려 올라간 것처럼 날개를 퍼덕이고 다리를 번갈아 찬다.
            return [
                OwlFrame(OwlPose(wings: .spread, feet: .stepA, lean: -1), duration: 0.10),
                OwlFrame(OwlPose(), duration: 0.08),
                OwlFrame(OwlPose(wings: .spread, feet: .stepB, lean: 1), duration: 0.10),
                OwlFrame(OwlPose(), duration: 0.08),
            ]
        }
    }
}

/// 애니메이션 한 프레임: 자세와, 다음 프레임으로 넘어가기까지 머무는 시간.
struct OwlFrame {
    var pose: OwlPose
    var duration: TimeInterval
    /// 0이 아니면 이 길이 안에서 무작위로 더 기다린다.
    var jitter: TimeInterval

    init(_ pose: OwlPose, duration: TimeInterval, jitter: TimeInterval = 0) {
        self.pose = pose
        self.duration = duration
        self.jitter = jitter
    }
}

extension OwlMood {
    /// 이 사용률부터 지쳐 보이기 시작한다.
    static let tiredThreshold: Double = 80
    /// 이 사용률부터 주저앉는다.
    static let exhaustedThreshold: Double = 95

    /// 지금 상태에서 어떤 기분이어야 하는지.
    ///
    /// 끌려가는 중에는 무슨 상태든 버둥거리는 게 자연스러우므로 드래그가 가장 세다.
    /// 사용률은 세션(5시간)만 본다 — 주간은 며칠에 걸쳐 천천히 차서, 그걸로 지치면
    /// 한 주 내내 지친 얼굴로 있게 된다.
    @MainActor
    static func resolve(store: UsageStore, isDragging: Bool) -> OwlMood {
        if isDragging { return .dragged }
        if store.needsReauth || store.isStale { return .offline }
        guard let utilization = store.snapshot?.fiveHour?.utilization else { return .idle }
        if utilization >= exhaustedThreshold { return .exhausted }
        if utilization >= tiredThreshold { return .tired }
        return .idle
    }
}

/// 기분에 맞는 프레임을 차례로 넘겨주는 애니메이터.
///
/// 프레임마다 일회용 타이머를 새로 건다. `TimelineView(.animation)`처럼 화면
/// 주사율에 맞춰 도는 방식을 쓰면, 항상 위에 떠 있는 창이라 WindowServer가
/// 쉬지 않고 합성한다. 가만히 있는 부엉이는 몇 초에 한 번만 깨우면 되고,
/// 그 차이가 그대로 전력이 된다.
@MainActor
final class OwlAnimator: ObservableObject {
    @Published private(set) var pose: OwlPose = .idle
    @Published private(set) var mood: OwlMood = .idle

    private var timer: Timer?
    private var frameIndex = 0
    private var isRunning = false

    var palette: OwlPalette { mood.palette }

    /// 보이지 않는 동안에는 부를 이유가 없다. 창을 숨기거나 다른 아이콘을 고르면 멈춘다.
    func start() {
        guard !isRunning else { return }
        isRunning = true
        frameIndex = 0
        advance()
    }

    func stop() {
        isRunning = false
        timer?.invalidate()
        timer = nil
    }

    func setMood(_ newMood: OwlMood) {
        guard newMood != mood else { return }
        mood = newMood
        frameIndex = 0
        if isRunning {
            advance()
        } else {
            // 멈춰 있어도 자세는 새 기분의 첫 프레임으로 맞춰 둔다.
            // 그래야 다시 보일 때 옛 기분의 자세가 한 순간 스치지 않는다.
            timer?.invalidate()
            timer = nil
            pose = newMood.frames[0].pose
        }
    }

    /// 지금 프레임을 화면에 올리고, 그 길이만큼 뒤에 다음 프레임을 예약한다.
    private func advance() {
        let frames = mood.frames
        let frame = frames[frameIndex % frames.count]
        pose = frame.pose

        timer?.invalidate()
        timer = nil
        // 프레임이 하나뿐인 기분은 정지 그림이다. 타이머를 걸지 않는다.
        guard frames.count > 1 else { return }

        let delay = frame.duration + (frame.jitter > 0 ? .random(in: 0...frame.jitter) : 0)
        let timer = Timer(timeInterval: delay, repeats: false) { [weak self] _ in
            // 타이머를 메인 런루프에 걸었으므로 콜백도 메인 스레드에서 온다.
            MainActor.assumeIsolated {
                guard let self else { return }
                self.frameIndex += 1
                self.advance()
            }
        }
        timer.tolerance = delay / 8
        // 드래그하는 동안 런루프는 이벤트 추적 모드로 돌아간다. 기본 모드에만 걸면
        // 목덜미를 잡고 끌고 다니는 내내 다리가 멈춰 있는다.
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }
}

/// 애니메이터를 구독해 자세가 바뀔 때만 다시 그리는 부엉이.
///
/// HUD 전체를 다시 만들면 프레임마다 뷰 트리가 통째로 새로 생긴다.
/// 구독을 이 작은 뷰 안에 가둬서 갱신 범위를 부엉이 한 마리로 좁힌다.
struct AnimatedOwlView: View {
    @ObservedObject var animator: OwlAnimator

    var body: some View {
        OwlMarkView(pose: animator.pose, palette: animator.palette)
    }
}
