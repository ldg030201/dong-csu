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
    /// 마구 흔들린 직후. 눈이 풀리고 비틀거린다.
    case dizzy

    var title: String {
        switch self {
        case .idle: return "평소"
        case .tired: return "지침"
        case .exhausted: return "탈진"
        case .offline: return "끊김"
        case .dragged: return "끌림"
        case .dizzy: return "어지러움"
        }
    }

    /// 조회가 안 되는 동안에는 색을 빼서 지금 값이 아님을 몸으로 드러낸다.
    /// 그 밖에는 빌드에 맞는 색을 쓴다 — 테스트판은 몸이 보라색이다.
    var palette: OwlPalette {
        self == .offline ? .offline : AppInfo.owlPalette
    }

    /// 이 기분에서 차례로 보여줄 프레임들. 마지막까지 가면 처음으로 돌아간다.
    var frames: [OwlFrame] {
        switch self {
        case .idle:
            // 눈을 뜬 채 한참 있다가 두어 프레임만 깜빡인다.
            // 지터가 없으면 정확히 같은 박자로 깜빡여서 시계처럼 보인다.
            return [
                OwlFrame(OwlPose(), duration: 2.0, jitter: 1.6),
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
            // 뒤쪽 두 프레임은 위아래로 움직일 때 날개가 올라가는 모습이다.
            return [
                OwlFrame(.carried(lean: -1, face: 0, feet: 0), duration: 0.18),
                OwlFrame(.carried(lean: -1, face: -1, feet: 0), duration: 0.18),
                OwlFrame(.carried(lean: 0, face: -1, feet: -1), duration: 0.18),
                OwlFrame(.carried(lean: 1, face: 0, feet: -1), duration: 0.18),
                OwlFrame(.carried(lean: 1, face: 1, feet: 0), duration: 0.18),
                OwlFrame(.carried(lean: 0, face: 1, feet: 1), duration: 0.18),
                OwlFrame(.carried(lean: 0, face: 0, feet: 0, wings: .lift), duration: 0.30),
                OwlFrame(.carried(lean: 0, face: 0, feet: 0, wings: .spread), duration: 0.30),
            ]

        case .dizzy:
            // 내려놓고도 한동안 비틀거린다. 몸과 얼굴이 번갈아 쏠려서 중심을 못 잡는다.
            // 오르내림(`bob`)으로 흔들면 다리가 몸에 들어갔다 나왔다 해서 쓰지 않는다.
            return [
                OwlFrame(OwlPose(eyes: .dizzy, lean: -1), duration: 0.13),
                OwlFrame(OwlPose(eyes: .dizzy, faceLean: -1), duration: 0.13),
                OwlFrame(OwlPose(eyes: .dizzy, lean: 1), duration: 0.13),
                OwlFrame(OwlPose(eyes: .dizzy, faceLean: 1), duration: 0.13),
            ]
        }
    }
}

/// 걸음걸이.
///
/// 배회하거나 커서를 피할 때는 **걷는다** — 산책이고, 급할 게 없다.
/// 글자에 쫓길 때만 **뛴다.** 타이핑은 계속 밀고 들어오므로 느긋하게 비키면
/// 비키는 도중에 이미 덮인다.
enum OwlGait: String, CaseIterable {
    case walk
    case run

    var title: String {
        switch self {
        case .walk: return "걷기"
        case .run: return "달리기"
        }
    }

    /// 한 칸에 머무는 시간. 걷기는 이보다 빠르면 종종거리고, 느리면 미끄러져 보인다.
    var tick: TimeInterval {
        switch self {
        case .walk: return 0.14
        case .run: return 0.08
        }
    }

    /// 문서와 미리보기가 늘어놓는 한 바퀴. 두 바퀴를 이어 붙인 것이다.
    ///
    /// 실제로 깜빡이는 간격은 몇 초에 한 번이고 지터도 붙는다. 한 바퀴가 그보다
    /// 훨씬 짧아서 뒤쪽에 깜빡임을 한 번 끼워 뒀다 — **간격만은 실제와 다르다.**
    @MainActor
    var cycle: [OwlFrame] {
        (0..<8).map { phase in
            var pose = OwlAnimator.gaitPose(base: OwlPose(), phase: phase, gait: self)
            switch phase {
            case 5, 7: pose.eyes = .half
            case 6: pose.eyes = .closed
            default: break
            }
            return OwlFrame(pose, duration: tick)
        }
    }
}

/// 렌더 통로가 늘어놓는 애니메이션 한 줄.
///
/// **걸음걸이는 기분이 아니라 기분 위에 얹히는 것**이라 `OwlMood.allCases`에 없다.
/// 기분만 돌면 문서와 미리보기에서 걷기·달리기가 통째로 빠지므로, 보여줄 것들을 여기 모은다.
struct OwlAnimation {
    /// 파일 이름에 쓰는 이름.
    let name: String
    let title: String
    let frames: [OwlFrame]
    let palette: OwlPalette

    /// 걸음걸이 한 바퀴가 `OwlAnimator`(메인 액터)에 있어서 여기도 메인 액터다.
    /// 부르는 쪽은 전부 렌더 통로라 문제되지 않는다.
    @MainActor
    static var all: [OwlAnimation] {
        OwlMood.allCases.map {
            OwlAnimation(name: $0.rawValue, title: $0.title, frames: $0.frames, palette: $0.palette)
        } + OwlGait.allCases.map {
            OwlAnimation(
                name: $0.rawValue,
                title: $0.title,
                frames: $0.cycle,
                palette: OwlMood.idle.palette
            )
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
    /// **끊김이 가장 세다.** 색이 빠진 채 멈춰 있는 건 "지금 값이 아니다"라는 표시라,
    /// 집어 들었다고 색이 돌아오면 조회가 되살아난 것처럼 보인다. 끌든 걷든
    /// 끊긴 동안에는 회색으로 굳어 있어야 한다.
    ///
    /// 사용률은 세션(5시간)만 본다 — 주간은 며칠에 걸쳐 천천히 차서, 그걸로 지치면
    /// 한 주 내내 지친 얼굴로 있게 된다.
    @MainActor
    static func resolve(store: UsageStore, isDragging: Bool) -> OwlMood {
        if store.isDisconnected { return .offline }
        if isDragging { return .dragged }
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

    private var dragVelocity: CGVector = .zero
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
    /// 위아래로 이만큼 빠르면 날개가 한 단계 움직인다(pt/s).
    private static let wingLiftSpeed: CGFloat = 200
    private static let wingSpreadSpeed: CGFloat = 620
    /// 몇 틱마다 깜빡일지. 눈을 붙박아 두기만 하면 노려보는 것처럼 보인다.
    private static let blinkInterval = 22
    /// 그 간격에 얹는 지터(틱). 없으면 시계처럼 정확한 박자로 깜빡인다.
    private static let blinkJitter = 12

    // MARK: - 어지러움
    //
    // 흔들린 정도를 점수로 쌓는다. 방향이 홱 바뀔 때 크게 오르고, 아주 빠르게 끌면
    // 조금씩 오른다. 가만히 두면 내려간다. 문턱을 넘으면 한동안 어지러워한다.

    private var dizziness = 0.0
    private var lastVelocitySign = 0
    private var lastVerticalSign = 0
    private var dizzyUntil = Date.distantPast

    /// 방향이 뒤집힐 때 한 번에 오르는 값. 셋을 채우면 문턱을 넘는다.
    private static let reversalGain = 1.1
    /// 방향 뒤집힘으로 치려면 이만큼은 빨라야 한다(pt/s). 손 떨림은 세지 않는다.
    private static let reversalSpeed: CGFloat = 320
    /// 뒤집지 않아도 이보다 빠르면 조금씩 쌓인다(pt/s).
    private static let spinSpeed: CGFloat = 950
    private static let spinGain = 0.07
    /// 한 틱마다 빠지는 값. 천천히 옮기는 동안 저절로 쌓이지 않게 한다.
    private static let dizzinessDecay = 0.06
    private static let dizzinessThreshold = 3.0
    /// 문턱을 넘고 나서 어지러워하는 시간.
    private static let dizzyDuration: TimeInterval = 2.4

    private var isDizzy: Bool { dizzyUntil > Date() }

    // MARK: - 걷기
    //
    // **걷기는 기분이 아니다.** 지친 부엉이도 걸어다녀야 하고, 걷는다고 눈이 다시
    // 떠지면 사용량이 줄어든 것처럼 읽힌다. 그래서 기분이 정한 눈·날개는 그대로 두고
    // 발과 몸 흔들림만 덮어쓴다.

    /// 지금 걸음걸이. nil이면 서 있다.
    private var gait: OwlGait?

    /// 걷는·뛰는 모습을 켜고 끈다.
    func setGait(_ next: OwlGait?) {
        guard next != gait else { return }
        let wasMoving = gait != nil
        gait = next
        guard isRunning else { return }
        // 걷다 뛰기로 바뀌면 박자만 빨라지면 된다. 다음 칸에서 알아서 따라간다.
        guard wasMoving != (next != nil) else { return }
        frameIndex = 0
        advance()
    }

    /// 한 칸의 자세. 렌더 통로도 같은 계산을 써야 문서가 실제와 어긋나지 않는다.
    ///
    /// 네 칸 한 바퀴다. 한 발 딛고 → 모으고 → 다른 발 딛고 → 모은다. 두 칸만 쓰면
    /// 발이 좌우로 튀기만 해서 걷는 게 아니라 미끄러지는 것으로 보인다.
    /// 발은 `lean`을 받지 않으므로 몸만 흔들리고 발은 제자리에서 갈아 딛는다.
    ///
    /// **얼굴은 몸을 그대로 따라간다(`faceLean` 0).** 매달렸을 때처럼 얼굴을 뒤에
    /// 남기면 몸만 흔들리고 눈·부리는 공간에 못 박힌 것처럼 보여서 징그럽다. 걷는
    /// 부엉이는 매달린 게 아니라 통짜로 뒤뚱거린다.
    ///
    /// **뛸 때는 발을 모으는 칸마다 날개를 펼친다.** 부엉이는 다리가 짧아서 발만 빨리
    /// 놀리면 종종거리는 것으로 보인다. 날개를 써야 급한 것으로 읽힌다.
    ///
    /// 펼친 날개는 좌우 여백 두 칸을 끝까지 쓴다. 그래서 **몸이 기운 칸에서 펴면
    /// 바깥쪽 한 칸이 캔버스 밖으로 잘려** 한쪽 날개만 짧아진다. 기울기가 0인
    /// 칸에서만 펴면 그 일이 없고, 딛고 → 펴고 → 딛고 순서라 도약으로도 읽힌다.
    static func gaitPose(base: OwlPose, phase: Int, gait: OwlGait) -> OwlPose {
        var pose = base
        let planted: Bool
        switch phase % 4 {
        case 0:
            pose.feet = .stepA
            pose.lean = -1
            planted = true
        case 2:
            pose.feet = .stepB
            pose.lean = 1
            planted = true
        default:
            pose.feet = .stand
            pose.lean = 0
            planted = false
        }
        if gait == .run {
            pose.wings = planted ? .folded : .spread
        }
        pose.faceLean = 0
        // 주저앉은 채로 걸으면 다리가 몸에 가려져서 미끄러지는 것으로 보인다.
        pose.bob = 0
        return pose
    }

    /// 이번 틱의 흔들림을 점수에 반영한다.
    /// 방향 뒤집힘은 가로만 보고, 빠르기는 위아래를 합친 값으로 본다 —
    /// 위아래로만 마구 흔들어도 어지러워져야 한다.
    private func accumulateDizziness(velocity: CGVector) {
        dizziness = max(0, dizziness - Self.dizzinessDecay)

        let speed = (velocity.dx * velocity.dx + velocity.dy * velocity.dy).squareRoot()
        let sign = abs(velocity.dx) > Self.reversalSpeed ? (velocity.dx > 0 ? 1 : -1) : 0
        if sign != 0 {
            if lastVelocitySign != 0 && sign != lastVelocitySign {
                dizziness += Self.reversalGain
            }
            lastVelocitySign = sign
        }
        let verticalSign = abs(velocity.dy) > Self.reversalSpeed ? (velocity.dy > 0 ? 1 : -1) : 0
        if verticalSign != 0 {
            if lastVerticalSign != 0 && verticalSign != lastVerticalSign {
                dizziness += Self.reversalGain
            }
            lastVerticalSign = verticalSign
        }
        if speed > Self.spinSpeed { dizziness += Self.spinGain }

        guard dizziness >= Self.dizzinessThreshold else { return }
        dizziness = 0
        lastVelocitySign = 0
        lastVerticalSign = 0
        dizzyUntil = Date().addingTimeInterval(Self.dizzyDuration)
    }

    /// 팔레트를 바깥에서 덮어쓴다. **렌더 통로 전용** — 테스트판 모습(보라색 몸)을
    /// 실제 테스트 번들 없이 그려 보려고 둔 자리다. `@Published`가 아니라서 뷰가 생긴
    /// 뒤에 바꿔도 다시 그려지지 않는다. 그리기 전에 한 번만 꽂는다.
    var paletteOverride: OwlPalette?

    var palette: OwlPalette {
        // 끊김의 회색은 색 자체가 정보라 덮어쓰지 않는다.
        guard mood != .offline, let paletteOverride else { return mood.palette }
        return paletteOverride
    }

    /// 끌려가는 동안 마우스의 속도(pt/s). 부호는 **마우스가 가는 쪽**이다.
    ///
    /// 가로는 몸이 처지는 방향을 정한다 — 오른쪽으로 가면 몸이 왼쪽으로 뒤처진다.
    /// 세로는 날개 높이를 정한다 — 들어 올리면 날개를 들고, 세게 내리면 활짝 편다.
    func setDragVelocity(_ velocity: CGVector) {
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

    /// 사용량·연결 상태·드래그에서 정해지는 기분. 어지러움은 여기 안 들어온다.
    private var requestedMood: OwlMood = .idle

    func setMood(_ newMood: OwlMood) {
        guard newMood != requestedMood else { return }
        requestedMood = newMood
        applyMood()
    }

    /// 어지러움은 사용량이나 연결 상태가 아니라 **이 앱이 어떻게 다뤄졌는지**에서 나온다.
    /// 그래서 바깥이 정하지 않고 여기서 덮어쓴다.
    ///
    /// 끌리는 동안에는 끌림이 이긴다 — 손에 들려 있는데 바닥에서 비틀거리면 앞뒤가
    /// 안 맞는다. 대신 그때는 눈만 풀린 채로 끌려간다.
    private var effectiveMood: OwlMood {
        guard requestedMood != .dragged, isDizzy else { return requestedMood }
        return .dizzy
    }

    private func applyMood() {
        let next = effectiveMood
        guard next != mood else { return }
        mood = next
        frameIndex = 0
        // 지난번에 끌던 기울기가 남아 있으면 집어 든 순간 몸이 한쪽으로 튄다.
        previousLean = 0
        olderLean = 0
        dragVelocityAt = .distantPast
        blinkCountdown = Self.blinkInterval
        // 흔들림 점수는 끌 때마다 새로 센다. 사이를 두고 조금씩 흔든 게 쌓여서
        // 갑자기 어지러워지면 무엇 때문인지 알 수 없다.
        dizziness = 0
        lastVelocitySign = 0
        lastVerticalSign = 0

        if isRunning {
            advance()
        } else {
            // 멈춰 있어도 자세는 새 기분의 첫 프레임으로 맞춰 둔다.
            // 그래야 다시 보일 때 옛 기분의 자세가 한 순간 스치지 않는다.
            timer?.invalidate()
            timer = nil
            pose = next.frames[0].pose
        }
    }

    /// 지금 프레임을 화면에 올리고, 그 길이만큼 뒤에 다음 프레임을 예약한다.
    private func advance() {
        // 어지러움이 풀렸으면 원래 기분으로 돌아간다. 시간이 정하는 상태라
        // 바깥에서 알려줄 사람이 없어서 틱마다 스스로 확인한다.
        if mood == .dizzy, !isDizzy { return applyMood() }
        guard mood != .dragged else { return advanceDrag() }
        // 들려 있는 동안에는 걷지 않는다. 허공에서 발을 갈아 딛으면 우스워진다.
        // 끊긴 동안에도 걷지 않는다 — 멈춘 그림이라야 지금 값이 아님이 드러난다.
        if let gait, mood != .offline { return advanceGait(gait) }

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
        let velocity = moving ? dragVelocity : .zero
        accumulateDizziness(velocity: velocity)

        let lean = Self.lean(for: velocity.dx)
        pose = .carried(
            lean: lean,
            face: previousLean,
            feet: olderLean,
            eyes: blinkingEyes(base: .open),
            wings: Self.wings(for: velocity.dy)
        )
        olderLean = previousLean
        previousLean = lean

        schedule(after: Self.dragTick)
    }

    /// 움직이는 동안의 한 칸. 기분이 정한 눈·날개 위에 발과 몸 흔들림만 얹는다.
    ///
    /// 눈은 기분이 준 첫 프레임에 붙박아 두지 않는다. 걷는 내내 뜨고만 있으면
    /// 살아 있는 게 아니라 굳은 것처럼 보인다 — 끌릴 때와 같은 이유다.
    private func advanceGait(_ gait: OwlGait) {
        var next = Self.gaitPose(base: mood.frames[0].pose, phase: frameIndex, gait: gait)
        next.eyes = blinkingEyes(base: next.eyes)
        pose = next
        schedule(after: gait.tick)
    }

    /// 스스로 도는 상태(끌림·걷기)의 눈. 흔들려 놨으면 풀린 채로, 아니면 이따금 깜빡인다.
    ///
    /// **지친 눈을 억지로 뜨게 하지 않는다.** 걷는다고 눈이 다시 떠지면 사용량이 줄어든
    /// 것처럼 읽힌다. 이미 감고 있는 부엉이(탈진)는 반대로 이따금 실눈을 뜨는 것으로 대신한다.
    private func blinkingEyes(base: OwlPose.Eyes) -> OwlPose.Eyes {
        if isDizzy { return .dizzy }
        blinkCountdown -= 1
        // 지터가 없으면 정확히 같은 박자로 깜빡여서 시계처럼 보인다.
        defer {
            if blinkCountdown <= 0 {
                blinkCountdown = Self.blinkInterval + .random(in: 0...Self.blinkJitter)
            }
        }
        switch blinkCountdown {
        case 1: return base == .closed ? .half : .closed
        case 0, 2: return .half
        default: return base
        }
    }

    /// 마우스가 가는 반대쪽으로 처진다. 매달린 것은 손보다 늦게 따라오기 때문이다.
    private static func lean(for velocity: CGFloat) -> Int {
        guard abs(velocity) > dragLeanSpeed else { return 0 }
        return velocity > 0 ? -1 : 1
    }

    /// 위아래로 얼마나 빨리 움직이는지에서 날개 높이를 정한다.
    ///
    /// **들어 올리면 날개를 든다.** 매달린 게 위로 딸려 올라가면 날개가 버티듯 올라오고,
    /// 세게 내리면 떨어지지 않으려고 활짝 편다. 화면 좌표는 위가 양수다.
    private static func wings(for velocity: CGFloat) -> OwlPose.Wings {
        if velocity > wingLiftSpeed { return .lift }
        if velocity < -wingSpreadSpeed { return .spread }
        if velocity < -wingLiftSpeed { return .lift }
        return .droop
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
