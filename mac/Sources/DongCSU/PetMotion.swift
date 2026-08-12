import AppKit

/// 펫이 **스스로** 움직이는 것들을 한곳에서 정한다.
///
/// 배회(혼자 걸어다니기)와 회피(비켜주기)를 한 객체가 들고 있는 이유는 둘 다 같은
/// 창을 옮기기 때문이다. 따로 두면 배회가 잡은 목표와 회피가 잡은 목표가 매 틱
/// 서로를 덮어써서 제자리에서 떨린다. **회피가 항상 배회를 끊는다.**
///
/// 위치를 매 틱 저장하지도 않는다. 걷는 동안 UserDefaults를 초당 열 번 쓰는 셈이라
/// 자리를 잡은 뒤에 한 번만 저장한다(`didSettle`).
@MainActor
final class PetMotionController {
    // MARK: - 바깥과 잇는 자리

    /// 지금 창 프레임.
    var frame: () -> NSRect = { .zero }
    /// 창 안에서 **그림이 실제로 가리는** 자리(화면 좌표). 펫은 창이 링만큼 큰데
    /// 마스코트는 그보다 작아서, 캐럿과 겹치는지는 이걸로 따져야 한다.
    var visualFrame: () -> NSRect = { .zero }
    /// 창을 이 자리로 옮긴다.
    var move: (NSPoint) -> Void = { _ in }
    /// 움직임이 끝났다. 위치를 저장할 자리.
    var didSettle: () -> Void = {}
    /// 걷는·뛰는 모습을 켜고 끈다. nil이면 서 있다.
    /// 걸음걸이와 **바라보는 쪽**. 방향은 그림 마스코트가 좌우를 뒤집는 데 쓴다
    /// (격자 부엉이는 정면 대칭이라 아무 일도 안 한다).
    var setGait: (OwlGait?, Bool?) -> Void = { _, _ in }
    /// 그 자리에 붙었을 때 창이 놓일 원점. **붙을 수 없으면 nil** — 그림이 화면 밖으로
    /// 나가는 자리가 여기서 걸린다. 자리 계산은 뷰 치수를 아는 쪽(`HUDController`)이 한다.
    var perchOrigin: (PerchSpot) -> NSPoint? = { _ in nil }
    /// 어느 테두리에 붙었는지 그림 쪽에 알린다. nil이면 떨어졌다.
    var setPerch: (MascotPerch?) -> Void = { _ in }
    /// 붙은 창이 지금 맨 앞인지. 창 층에서 펫을 어디에 끼울지 정하는 데 쓴다.
    var setPerchFront: (Bool) -> Void = { _ in }

    // MARK: - 설정

    /// 가만히 두면 혼자 걸어다닌다.
    var wanders = false
    /// 커서를 위에 올려두고 잡지 않으면 비킨다.
    var dodgesCursor = false
    /// 끌어다 놓으면 다른 앱 창 테두리에 붙는다.
    var perches = false
    /// 지금 **붙어 있어도 되는** 상황인지.
    ///
    /// `update(active:)` 와 따로 두는 이유: 움직여도 되는지와 붙어 있어도 되는지는
    /// 조건이 다르다. **눌림·메뉴는 잠깐 멈추는 이유일 뿐 떨어질 이유가 아니다** —
    /// 하나로 묶어 놨더니 붙여 놓은 것을 한 번 클릭하거나 우클릭 메뉴를 열면 곧바로
    /// 떨어져서 걸어나갔다.
    var canStayPerched = false
    /// 탈진했는지.
    ///
    /// **배회만 끊는다.** 지쳐서 제 발로 산책 나갈 기운은 없어도, 커서가 밀고 들어오면
    /// 비켜야 한다 — 안 비키면 지친 게 아니라 멎은 것으로 보이고 화면도 가린다.
    var isDrained = false

    // MARK: - 치수

    /// 한 틱. 걸음 애니메이션 한 칸(0.22초)보다 짧아야 움직임이 뚝뚝 끊기지 않는다.
    private static let tick: TimeInterval = 0.1
    /// 배회 속도(pt/s). 걸음 한 바퀴(0.56초)에 한 칸 남짓 나아가는 정도라
    /// 발을 갈아 딛는 것과 실제 이동이 크게 어긋나지 않는다.
    private static let walkSpeed: CGFloat = 26
    /// 커서를 피할 때의 속도(pt/s).
    ///
    /// 처음엔 120으로 뒀는데 **비키는 데 1.2초가 걸려서 굼떠 보였다.**
    /// 커서는 이미 위에 올라와 있는 상태라, 비키기로 정한 뒤에는 지체할 이유가 없다.
    private static let dodgeSpeed: CGFloat = 210
    /// 쫓길 때의 속도(pt/s). 걸어서 비키는 것과 확실히 구별돼야 한다.
    private static let dashSpeed: CGFloat = 300
    /// 화면 가장자리에서 이만큼은 띄운다.
    private static let edgeMargin: CGFloat = 8
    /// 한 번 걷고 나서 쉬는 시간.
    private static let restRange: ClosedRange<TimeInterval> = 3...11
    /// 이보다 짧게 갈 거면 아예 가지 않는다. 찔끔거리는 쪽이 더 성가시다.
    private static let minimumMove: CGFloat = 24
    /// 커서에서 물러나는 거리. 클릭 영역(창 한 변)보다 커야 한 번에 벗어난다.
    private static let cursorRetreat: CGFloat = 1.15
    /// 비킨 지 이 안에 또 비켜야 하면 쫓기는 것으로 본다.
    private static let chaseWindow: TimeInterval = 4
    /// 붙어 있는 동안 붙은 창을 얼마나 자주 다시 보나.
    ///
    /// **창을 끌지 않을 때도 반드시 본다.** 단축키로 창을 옮기는 도구(Rectangle 류)나
    /// 미션 컨트롤·전체화면 전환은 마우스 이벤트를 하나도 안 내서, 끄는 중인지만
    /// 보고 폴링을 끄면 그때 통째로 놓친다.
    private static let perchTick: TimeInterval = 0.25
    /// 창을 끄는 중일 때. 창 목록 한 번이 0.3ms 도 안 돼서 이 주기가 부담이 아니다.
    private static let perchChaseTick: TimeInterval = 0.05
    /// 마지막 입력 뒤 이만큼은 얌전히 있는다.
    ///
    /// **2초로 뒀더니 짧았다.** 글을 쓰다 잠깐 생각하는 사이에 배회가 걸어나가서,
    /// 쓰던 사람 눈에는 "타이핑 중에 왼쪽으로 간다"로 보였다. 문장 사이에 쉬는 시간을
    /// 덮을 만큼은 잡아야 한다.
    private static let typingQuiet: TimeInterval = 5

    // MARK: - 상태

    private enum Motion {
        /// 아무것도 하지 않는다.
        case still
        /// 다음 산책까지 서서 쉰다.
        case resting(until: Date)
        /// 배회 목표로 걸어간다.
        case walking(to: NSPoint)
        /// 비키는 중. 배회보다 세다. `hurried`면 글자에 쫓기는 중이라 뛴다.
        case dodging(to: NSPoint, hurried: Bool)
        /// 다른 앱 창 테두리에 붙어 있다. **사용자가 끌어다 놓아야만 들어온다** —
        /// 배회하다 저절로 붙지 않는다.
        case perched(PerchSpot)
    }

    private var motion: Motion = .still
    /// 마지막으로 커서를 피한 시각. 연달아 피하면 쫓기는 중이다.
    private var lastCursorDodgeAt = Date.distantPast
    private var isActive = false
    private var timer: Timer?

    private var isMoving: Bool {
        switch motion {
        case .walking, .dodging: return true
        case .still, .resting, .perched: return false
        }
    }

    private var isDodging: Bool {
        if case .dodging = motion { return true }
        return false
    }

    /// 마지막 키 입력 뒤 얼마나 지났는지(초).
    ///
    /// **아무 권한도 필요 없다.** 무슨 키를 눌렀는지가 아니라 "마지막 입력이 언제였나"만
    /// 돌려주는 값이라, 손쉬운 사용 권한 없이도 읽힌다. 캐럿 자리(입력 피하기)와 달리
    /// 이건 늘 알 수 있어서, **혼자 돌아다니기만 켜 둔 사람도 글을 쓰는 동안 보호된다.**
    private static var secondsSinceKey: CFTimeInterval {
        CGEventSource.secondsSinceLastEventType(.hidSystemState, eventType: .keyDown)
    }

    /// 지금 마우스로 무언가를 끌고 있는지. 붙어 있는 동안 창을 얼마나 촘촘히 따라갈지에 쓴다.
    ///
    /// **창이 움직이는 것을 알림으로 받는 길이 없다.** 손쉬운 사용 권한이 필요한 AX
    /// 알림뿐이고, `NSWorkspace` 알림은 앱 단위라 창 이동과 무관하다. 그래서 폴링인데,
    /// 끄는 동안만 촘촘하게 하려고 이걸 본다 — 위의 키 입력 판정과 같은 API 라
    /// 권한도 안 들고 재 보면 1µs 도 안 걸린다.
    private static var isDraggingSomething: Bool {
        CGEventSource.secondsSinceLastEventType(
            .hidSystemState, eventType: .leftMouseDragged
        ) < 0.3
    }

    /// 글을 쓰는 중이라 얌전히 있어야 하는지.
    ///
    /// **글을 쓰는 동안에는 아무것도 하지 않는다.** 배회는 방향을 가리지 않아서
    /// 방금 쓴 글 위를 왼쪽으로 가로지르고, 커서 피하기도 커서가 오른쪽에 있으면
    /// 왼쪽으로 물러난다. 둘 다 **이미 쓴 글을 덮는** 움직임이다.
    var isTypingQuiet: Bool { Self.secondsSinceKey < Self.typingQuiet }

    // MARK: - 켜고 끄기

    /// 스스로 움직여도 되는 상황인지 알려준다.
    /// 끌고 있는 중·숨겨진 중·펫이 아닌 보기에서는 `false`가 들어온다.
    func update(active: Bool) {
        // 붙어 있을 수 없게 됐으면(펫 모드에서 나감·화면 잠김·집어 듦) 먼저 뗀다.
        if !canStayPerched, case .perched = motion { unperch() }
        if active != isActive {
            isActive = active
            if case .perched = motion {
                // **붙어 있으면 자리를 지킨다.** 여기서 `.resting` 으로 덮으면 자세만
                // 매달린 채로 배회가 시작된다 — 붙여 놓은 것을 누르기만 해도 걸어나갔다.
            } else if active {
                // 켜자마자 걸어나가면 방금 놓은 자리에서 도망치는 것처럼 보인다.
                motion = .resting(until: Date().addingTimeInterval(.random(in: 1...3)))
            } else {
                halt()
            }
        }
        // 배회를 끄면(탈진 포함) 걷던 것도 그 자리에 멈춘다.
        if !canWander, case .walking = motion { halt() }
        // 설정에서 붙기를 껐으면 붙어 있던 것도 놓는다.
        if !perches, case .perched = motion { unperch() }
        syncTimer()
    }

    /// 지금 움직이던 걸 멈추고 자리를 굳힌다.
    private func halt() {
        let wasMoving = isMoving
        // **여기까지 왔으면 뗀다.** 붙은 것을 지키는 판단은 `update(active:)` 가 이미
        // 했고(`canStayPerched`), 그걸 통과해 여기 온 것은 정말 멈춰야 하는 것이다.
        let wasPerched = releasePerch()
        motion = isActive ? .resting(until: Date().addingTimeInterval(.random(in: Self.restRange))) : .still
        setGait(nil, nil)
        if wasMoving || wasPerched { didSettle() }
        syncTimer()
    }

    /// 다음에 깨어날 때까지. nil이면 깨울 이유가 없다.
    ///
    /// **쉬는 동안에는 일어날 시각에 한 번만 깨운다.** 걸을 때와 같은 주기로 깨워서
    /// 시계만 확인하면, 가만히 서 있는 펫이 걷는 펫만큼 전기를 쓴다.
    private var nextWakeup: TimeInterval? {
        guard isActive else { return nil }
        switch motion {
        case .still:
            return nil
        case .resting(let until):
            guard canWander else { return nil }
            // 글을 쓰는 중이면 그게 멎을 때까지도 기다린다. 쓰는 내내 깨어나서
            // 시계만 확인할 이유가 없다.
            let quiet = Self.typingQuiet - Self.secondsSinceKey
            return max(Self.tick, max(until.timeIntervalSinceNow, quiet))
        case .walking, .dodging:
            return Self.tick
        case .perched:
            // 창을 끄는 동안에는 촘촘히 따라간다. 20Hz 면 50ms 뒤처지는데,
            // 창을 빠르게 끌어도 15pt 라 붙어 있는 것으로 읽힌다.
            return Self.isDraggingSomething ? Self.perchChaseTick : Self.perchTick
        }
    }

    /// 할 일이 있을 때만, 그것도 필요한 시각에만 깨운다.
    /// 애니메이터와 같이 **일회용 타이머를 매번 새로 건다.**
    private func syncTimer() {
        timer?.invalidate()
        timer = nil
        guard let delay = nextWakeup else { return }

        let timer = Timer(timeInterval: delay, repeats: false) { _ in
            MainActor.assumeIsolated { [weak self] in
                guard let self else { return }
                self.timer = nil
                self.tick()
                self.syncTimer()
            }
        }
        timer.tolerance = delay / 8
        // 메뉴가 떠 있거나 창을 끄는 동안 런루프는 기본 모드가 아니다.
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    // MARK: - 한 틱

    private func tick() {
        guard isActive else { return halt() }

        switch motion {
        case .still:
            syncTimer()

        case .resting(let until):
            guard canWander else { return syncTimer() }
            // 글을 쓰는 동안에는 새로 걸어나가지 않는다.
            guard !isTypingQuiet, Date() >= until else { return }
            guard let target = wanderTarget() else { return rest() }
            motion = .walking(to: target)

        case .walking(let target):
            // 걷는 도중에 글을 쓰기 시작했으면 그 자리에 선다.
            guard !isTypingQuiet else { return halt() }
            step(toward: target, speed: Self.walkSpeed, gait: .walk) { [weak self] in
                self?.rest()
            }

        case .dodging(let target, let hurried):
            step(
                toward: target,
                speed: hurried ? Self.dashSpeed : Self.dodgeSpeed,
                gait: hurried ? .run : .walk
            ) { [weak self] in
                self?.rest()
            }

        case .perched(let spot):
            follow(spot)
        }
    }

    // MARK: - 창에 붙기

    /// 끌어다 놓은 자리에서 창 테두리에 붙인다. 붙었으면 true.
    ///
    /// **`tick` 밖에서 들어오는 전이라 타이머를 직접 다시 건다**(`dodgeCursor` 와 같다).
    func perch(at spot: PerchSpot) -> Bool {
        guard isActive, perches else { return false }
        guard let origin = perchOrigin(spot) else { return false }
        motion = .perched(spot)
        move(origin)
        setGait(nil, nil)
        setPerch(spot.edge)
        // **`isMoving` 이 false 라 `rest()`·`halt()` 의 저장을 못 탄다.** 여기서 직접
        // 부르지 않으면 붙은 자리가 저장되지 않아서, 껐다 켜면 옛 자리로 돌아간다.
        didSettle()
        syncTimer()
        return true
    }

    /// 붙어 있는 동안의 한 틱. 창이 움직였으면 따라가고, 없어졌으면 떨어진다.
    private func follow(_ spot: PerchSpot) {
        // 창이 닫혔거나 최소화됐거나 다른 스페이스로 갔다.
        guard let found = WindowSurvey.locate(spot.window) else { return unperch() }
        // 창을 좁혀서 붙어 있을 자리가 없어졌다.
        guard let moved = spot.clamped(to: found.frame, mascot: visualFrame().size) else {
            return unperch()
        }
        // 붙은 자리가 화면 밖으로 나갔다.
        guard let origin = perchOrigin(moved) else { return unperch() }
        motion = .perched(moved)
        setPerchFront(found.isFront)
        guard origin != frame().origin else { return }
        move(origin)
    }

    /// 붙어 있던 것을 놓고 그 자리에 선다.
    ///
    /// **떨어지는 연출은 없다.** 떨어지는 칸이 시트에 없어서 그리는 사람이 새로 그려야
    /// 하는데, `MascotSheet.layout` 은 순서를 바꾸면 이미 그려진 사용자 시트가 전부
    /// 깨진다. 연출 하나에 치를 값이 아니다.
    private func unperch() {
        guard releasePerch() else { return }
        // 창을 따라가다 화면 밖까지 나갔을 수 있다.
        if let inside = clamped(frame().origin), inside != frame().origin { move(inside) }
        motion = isActive
            ? .resting(until: Date().addingTimeInterval(.random(in: Self.restRange)))
            : .still
        didSettle()
        syncTimer()
    }

    /// 붙은 자세만 푼다. 붙어 있었으면 true.
    @discardableResult
    private func releasePerch() -> Bool {
        guard case .perched = motion else { return false }
        setPerch(nil)
        return true
    }

    /// 목표 쪽으로 한 발짝. 다 왔으면 `onArrive`를 부른다.
    ///
    /// 한 걸음마다 화면 안으로 다시 가둔다. 목표는 잡을 때 가뒀지만, 걷는 도중에
    /// 모니터가 빠지면 그 목표가 화면 밖이 된다. 그러면 벽에 붙어서 영영 도착하지
    /// 못하므로, **제자리걸음이면 도착으로 친다.**
    private func step(
        toward target: NSPoint,
        speed: CGFloat,
        gait: OwlGait,
        onArrive: () -> Void
    ) {
        let origin = frame().origin
        let dx = target.x - origin.x
        let dy = target.y - origin.y
        let distance = (dx * dx + dy * dy).squareRoot()
        let stride = speed * CGFloat(Self.tick)

        guard distance > stride else {
            move(clamped(target) ?? target)
            onArrive()
            return
        }

        let next = NSPoint(x: origin.x + dx / distance * stride, y: origin.y + dy / distance * stride)
        let bounded = clamped(next) ?? next
        guard Self.distance(origin, bounded) > 0.5 else { return onArrive() }

        move(bounded)
        // 목표가 오른쪽이면 오른쪽을 본다. **가로로 안 움직이면 보던 쪽 그대로다** —
        // `dx >= 0` 으로 두면 세로로만 걷는 동안 내내 오른쪽으로 덮여서, 옆모습
        // 캐릭터가 왼쪽을 보고 있다가 반대로 돌아버린다. 화면 가장자리에 붙어 있으면
        // 목표가 잘려 dx == 0 이 되므로 드물지도 않다.
        setGait(gait, dx == 0 ? nil : dx > 0)
    }

    /// 다 왔다. 자리를 저장하고 다음 산책까지 쉰다.
    private func rest() {
        let wasMoving = isMoving
        releasePerch()
        motion = .resting(until: Date().addingTimeInterval(.random(in: Self.restRange)))
        setGait(nil, nil)
        if wasMoving { didSettle() }
        syncTimer()
    }

    private func begin(dodge target: NSPoint, hurried: Bool) {
        motion = .dodging(to: target, hurried: hurried)
        syncTimer()
    }

    // MARK: - 커서 피하기

    /// 커서가 위에 머문 채 잡히지 않는다. 커서 **반대쪽**으로 한 발짝 물러난다.
    func dodgeCursor() {
        guard isActive, dodgesCursor, !isDodging else { return }
        // **붙어 있으면 안 비킨다.** 붙는 자리는 창 바깥이라 글을 가리지 않는데,
        // 제목 표시줄에 손이 갈 때마다 떨어지면 붙여 둔 뜻이 없어진다.
        if case .perched = motion { return }
        // 글을 쓰는 동안에는 비키지 않는다. 커서가 오른쪽에 놓여 있으면 왼쪽으로
        // 물러나는데, 거기는 방금 쓴 글이 있는 자리다. 손은 어차피 키보드에 있다.
        guard !isTypingQuiet else { return }

        let panel = frame()
        let mouse = NSEvent.mouseLocation
        var away = CGVector(dx: panel.midX - mouse.x, dy: panel.midY - mouse.y)
        let length = (away.dx * away.dx + away.dy * away.dy).squareRoot()
        if length < 1 {
            // 정확히 가운데면 물러날 방향이 없다. 오른쪽 아래로 뺀다.
            away = CGVector(dx: 0.7071, dy: -0.7071)
        } else {
            away = CGVector(dx: away.dx / length, dy: away.dy / length)
        }

        let retreat = max(panel.width, panel.height) * Self.cursorRetreat
        // 곧장 물러날 자리가 막혔으면 옆으로 돌려 본다. 네 방향 다 막힌 구석에서는
        // 그냥 있는다 — 벽에 대고 찔끔거리면 비켜준 게 아니라 고장난 것처럼 보인다.
        for turn in [CGFloat.zero, .pi / 2, -.pi / 2, .pi] {
            let direction = CGVector(
                dx: away.dx * cos(turn) - away.dy * sin(turn),
                dy: away.dx * sin(turn) + away.dy * cos(turn)
            )
            guard let target = clamped(NSPoint(
                x: panel.minX + direction.dx * retreat,
                y: panel.minY + direction.dy * retreat
            )) else { return }
            guard Self.distance(panel.origin, target) >= Self.minimumMove else { continue }
            // 한 번 비켰는데 또 올라왔으면 장난치는 것이다. 그때도 느긋하게 걸으면
            // 잡히려고 서 있는 것처럼 보인다. **쫓아오면 뛴다.**
            let chased = Date().timeIntervalSince(lastCursorDodgeAt) < Self.chaseWindow
            lastCursorDodgeAt = Date()
            begin(dodge: target, hurried: chased)
            return
        }
    }

    // MARK: - 배회

    /// 지금 혼자 걸어다녀도 되는지.
    private var canWander: Bool { wanders && !isDrained }

    private func wanderTarget() -> NSPoint? {
        guard walkArea() != nil else { return nil }
        let origin = frame().origin

        // 가로로 크게, 세로로 조금. 부엉이는 걸어 다니지 날아다니지 않는다.
        // 한쪽이 벽에 막혀 있으면 반대쪽으로 돌려서 한 번 더 본다.
        let first: CGFloat = Bool.random() ? 1 : -1
        for direction in [first, -first] {
            let candidate = NSPoint(
                x: origin.x + direction * .random(in: 90...360),
                y: origin.y + .random(in: -70...70)
            )
            guard let target = clamped(candidate) else { return nil }
            if Self.distance(origin, target) >= Self.minimumMove { return target }
        }
        return nil
    }

    // MARK: - 범위

    /// 창 **원점**이 놓일 수 있는 범위. 화면 밖으로 걸어나가지 않게 여기로 가둔다.
    private func walkArea() -> CGRect? {
        let panel = frame()
        let screen = NSScreen.screens.first { $0.frame.intersects(panel) } ?? NSScreen.main
        guard let screen else { return nil }

        let area = screen.visibleFrame.insetBy(dx: Self.edgeMargin, dy: Self.edgeMargin)
        let width = area.width - panel.width
        let height = area.height - panel.height
        guard width >= 0, height >= 0 else { return nil }
        return CGRect(x: area.minX, y: area.minY, width: width, height: height)
    }

    private func clamped(_ origin: NSPoint) -> NSPoint? {
        guard let area = walkArea() else { return nil }
        return NSPoint(
            x: min(max(origin.x, area.minX), area.maxX),
            y: min(max(origin.y, area.minY), area.maxY)
        )
    }

    private static func distance(_ lhs: NSPoint, _ rhs: NSPoint) -> CGFloat {
        let dx = rhs.x - lhs.x
        let dy = rhs.y - lhs.y
        return (dx * dx + dy * dy).squareRoot()
    }
}
