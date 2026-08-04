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
            // **실제로는 이 목록을 쓰지 않는다.** 끌림만은 시간표가 아니라 마우스가
            // 자세를 정하기 때문에 `OwlAnimator`가 따로 돌린다. 여기 목록은 문서와
            // 렌더 통로가 쓰는 대표 한 바퀴 — 오른쪽으로 끌었다 왼쪽으로 끌 때의 모습이다.
            //
            // 날개는 늘어뜨리고 다리는 모아 내린다. 퍼덕이며 다리를 벌리면 도망치려는
            // 몸부림이 되는데, 힘을 뺀 채 들려 있는 쪽이 부엉이답고 덜 사납다.
            return [
                OwlFrame(.carried(lean: -1, face: 0, feet: 0), duration: 0.18),
                OwlFrame(.carried(lean: -1, face: -1, feet: 0), duration: 0.18),
                OwlFrame(.carried(lean: 0, face: -1, feet: -1), duration: 0.18),
                OwlFrame(.carried(lean: 1, face: 0, feet: -1), duration: 0.18),
                OwlFrame(.carried(lean: 1, face: 1, feet: 0), duration: 0.18),
                OwlFrame(.carried(lean: 0, face: 1, feet: 1), duration: 0.18),
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

    // MARK: - 끌림 상태
    //
    // 끌림만은 시간표가 아니라 마우스가 자세를 정한다. 어느 쪽으로 얼마나 빨리
    // 움직이는지에 따라 몸이 반대로 처지고, 얼굴과 다리가 차례로 뒤따라온다.

    private var dragVelocity: CGFloat = 0
    private var dragVelocityAt: Date = .distantPast
    /// 한 틱 전과 두 틱 전의 몸 기울기. 얼굴과 다리가 각각 여기에 남는다.
    private var previousLean = 0
    private var olderLean = 0
    private var blinkCountdown = OwlAnimator.blinkInterval

    /// 끌리는 동안 자세를 다시 잡는 주기.
    private static let dragTick: TimeInterval = 0.09
    /// 마우스가 멈추면 이벤트가 끊긴다. 이만큼 지난 속도는 0으로 본다.
    private static let dragIdle: TimeInterval = 0.13
    /// 이 속도(pt/s)를 넘어야 몸이 처진다. 느리게 움직이면 그냥 매달려 있다.
    private static let dragLeanSpeed: CGFloat = 140
    /// 몇 틱마다 깜빡일지. 눈을 붙박아 두기만 하면 노려보는 것처럼 보인다.
    private static let blinkInterval = 22

    var palette: OwlPalette { mood.palette }

    /// 끌려가는 동안 마우스의 가로 속도(pt/s). 부호는 **마우스가 가는 쪽**이고,
    /// 부엉이는 그 반대로 처진다 — 들고 오른쪽으로 가면 몸이 왼쪽으로 뒤처진다.
    func setDragVelocity(_ velocity: CGFloat) {
        dragVelocity = velocity
        dragVelocityAt = Date()
    }

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
        // 지난번에 끌던 기울기가 남아 있으면 집어 든 순간 몸이 한쪽으로 튄다.
        previousLean = 0
        olderLean = 0
        dragVelocityAt = .distantPast
        blinkCountdown = Self.blinkInterval
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
        guard mood != .dragged else { return advanceDrag() }

        let frames = mood.frames
        let frame = frames[frameIndex % frames.count]
        pose = frame.pose

        timer?.invalidate()
        timer = nil
        // 프레임이 하나뿐인 기분은 정지 그림이다. 타이머를 걸지 않는다.
        guard frames.count > 1 else { return }

        schedule(after: frame.duration + (frame.jitter > 0 ? .random(in: 0...frame.jitter) : 0))
    }

    /// 끌려가는 동안의 한 틱. 마우스가 어디로 얼마나 빨리 가는지에서 자세를 만든다.
    ///
    /// 몸은 지금 기울기에, 얼굴은 한 틱 전 자리에, 다리는 두 틱 전 자리에 남는다.
    /// 이 시차가 매달린 것을 들고 움직일 때의 뒤따라옴이 된다.
    private func advanceDrag() {
        let moving = Date().timeIntervalSince(dragVelocityAt) < Self.dragIdle
        let lean = moving ? Self.lean(for: dragVelocity) : 0

        blinkCountdown -= 1
        let eyes: OwlPose.Eyes
        switch blinkCountdown {
        case 1: eyes = .closed
        case 0, 2: eyes = .half
        default: eyes = .open
        }
        if blinkCountdown <= 0 { blinkCountdown = Self.blinkInterval }

        pose = .carried(lean: lean, face: previousLean, feet: olderLean, eyes: eyes)
        olderLean = previousLean
        previousLean = lean

        schedule(after: Self.dragTick)
    }

    /// 마우스가 가는 반대쪽으로 처진다. 매달린 것은 손보다 늦게 따라오기 때문이다.
    private static func lean(for velocity: CGFloat) -> Int {
        guard abs(velocity) > dragLeanSpeed else { return 0 }
        return velocity > 0 ? -1 : 1
    }

    private func schedule(after delay: TimeInterval) {
        timer?.invalidate()
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
