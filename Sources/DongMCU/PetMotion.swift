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
    var setGait: (OwlGait?) -> Void = { _ in }

    // MARK: - 설정

    /// 가만히 두면 혼자 걸어다닌다.
    var wanders = false
    /// 커서를 위에 올려두고 잡지 않으면 비킨다.
    var dodgesCursor = false
    /// 글을 쓰는 자리를 비켜준다.
    var dodgesTyping = false
    /// 캐럿 자리를 읽을 수 있는지(손쉬운 사용 권한).
    ///
    /// 읽을 수 있으면 **거친 회피는 쓰지 않는다** — 캐럿이 창 저쪽 끝에 있는데도
    /// 창 밖으로 튀어나가면 왜 움직였는지 알 수 없다. 정밀한 쪽이 늘 이긴다.
    var canReadCaret: () -> Bool = { false }

    // MARK: - 치수

    /// 한 틱. 걸음 애니메이션 한 칸(0.22초)보다 짧아야 움직임이 뚝뚝 끊기지 않는다.
    private static let tick: TimeInterval = 0.1
    /// 배회 속도(pt/s). 걸음 한 바퀴(0.56초)에 한 칸 남짓 나아가는 정도라
    /// 발을 갈아 딛는 것과 실제 이동이 크게 어긋나지 않는다.
    private static let walkSpeed: CGFloat = 26
    /// 커서를 피할 때의 속도(pt/s). 배회보다 빠르되 순간이동처럼 보이지는 않게.
    /// 커서는 가만히 있어 주므로 느긋해도 된다.
    private static let dodgeSpeed: CGFloat = 120
    /// 글자를 피할 때의 속도(pt/s). **타이핑은 기다려 주지 않는다** —
    /// 느긋하게 비키면 비키는 도중에 이미 덮인다. 이때만 뛴다.
    private static let dashSpeed: CGFloat = 300
    /// 화면 가장자리에서 이만큼은 띄운다.
    private static let edgeMargin: CGFloat = 8
    /// 한 번 걷고 나서 쉬는 시간.
    private static let restRange: ClosedRange<TimeInterval> = 3...11
    /// 캐럿과 이만큼은 떨어져 있어야 안심한다.
    private static let caretMargin: CGFloat = 16
    /// 이보다 짧게 갈 거면 아예 가지 않는다. 찔끔거리는 쪽이 더 성가시다.
    private static let minimumMove: CGFloat = 24
    /// 커서에서 물러나는 거리. 클릭 영역(창 한 변)보다 커야 한 번에 벗어난다.
    private static let cursorRetreat: CGFloat = 1.15
    /// 글을 쓰기 **시작한 순간**을 잡으려면 이 주기로 깨어나야 한다.
    /// 깨어나서 하는 일은 마지막 입력 시각 하나를 읽는 것뿐이라 싸다 —
    /// 비싼 창 목록 조회는 시작을 잡은 그 순간에만 한다.
    private static let typingPoll: TimeInterval = 0.3
    /// 창 밖으로 물러날 때 창과 띄우는 간격.
    private static let windowGap: CGFloat = 10
    /// 마지막 입력 뒤 이만큼은 얌전히 있는다. `CaretWatcher`가 캐럿을 좇는 시간보다
    /// 길게 잡아서, 좇기가 끝나기도 전에 다시 걸어나가지 않게 한다.
    /// 이 값이 짧아도 걷다 멈춘 뒤에는 `restRange`만큼 더 쉬므로 바로 걸어나가지는 않는다.
    private static let typingQuiet: TimeInterval = 2

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
    }

    private var motion: Motion = .still
    private var isActive = false
    private var timer: Timer?

    private var isMoving: Bool {
        switch motion {
        case .walking, .dodging: return true
        case .still, .resting: return false
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

    /// 글을 쓰는 중이라 얌전히 있어야 하는지.
    ///
    /// **글을 쓰는 동안에는 비켜주는 것 말고는 아무것도 하지 않는다.** 배회는 방향을
    /// 가리지 않아서 방금 쓴 글 위를 왼쪽으로 가로지르고, 커서 피하기도 커서가
    /// 오른쪽에 있으면 왼쪽으로 물러난다. 둘 다 **이미 쓴 글을 덮는** 움직임이다.
    /// 그동안 움직여도 되는 건 오른쪽·아래로만 가는 입력 회피뿐이다.
    var isTypingQuiet: Bool { Self.secondsSinceKey < Self.typingQuiet }

    /// 직전 틱에도 쓰고 있었는지. 안 쓰다가 **쓰기 시작한 순간**을 잡는 데 쓴다.
    private var wasTyping = false

    // MARK: - 켜고 끄기

    /// 스스로 움직여도 되는 상황인지 알려준다.
    /// 끌고 있는 중·숨겨진 중·펫이 아닌 보기에서는 `false`가 들어온다.
    func update(active: Bool) {
        if active != isActive {
            isActive = active
            if active {
                // 켜자마자 걸어나가면 방금 놓은 자리에서 도망치는 것처럼 보인다.
                motion = .resting(until: Date().addingTimeInterval(.random(in: 1...3)))
            } else {
                halt()
            }
        }
        // 배회를 끄면 걷던 것도 그 자리에 멈춘다.
        if !wanders, case .walking = motion { halt() }
        syncTimer()
    }

    /// 지금 움직이던 걸 멈추고 자리를 굳힌다.
    private func halt() {
        let wasMoving = isMoving
        motion = isActive ? .resting(until: Date().addingTimeInterval(.random(in: Self.restRange))) : .still
        setGait(nil)
        if wasMoving { didSettle() }
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
            // 글쓰기를 피해야 하면 시작하는 순간을 놓치지 않게 짧게 깨어난다.
            if dodgesTyping { return Self.typingPoll }
            guard wanders else { return nil }
            // 글을 쓰는 중이면 그게 멎을 때까지도 기다린다. 쓰는 내내 깨어나서
            // 시계만 확인할 이유가 없다.
            let quiet = Self.typingQuiet - Self.secondsSinceKey
            return max(Self.tick, max(until.timeIntervalSinceNow, quiet))
        case .walking, .dodging:
            return Self.tick
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
        noticeTypingStart()

        switch motion {
        case .still:
            syncTimer()

        case .resting(let until):
            guard wanders else { return syncTimer() }
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
        }
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
        setGait(gait)
    }

    /// 다 왔다. 자리를 저장하고 다음 산책까지 쉰다.
    private func rest() {
        let wasMoving = isMoving
        motion = .resting(until: Date().addingTimeInterval(.random(in: Self.restRange)))
        setGait(nil)
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
            begin(dodge: target, hurried: false)
            return
        }
    }

    // MARK: - 입력 피하기

    /// 뒤 입력창의 캐럿이 움직였다. 펫에 닿을 참이면 비킨다.
    func caretMoved(to caret: CGRect) {
        // 이미 비키는 중이면 목표를 흔들지 않는다. 글자마다 목표를 다시 잡으면
        // 캐럿을 따라 옆걸음질만 치다가 영영 도착하지 못한다.
        guard isActive, !isDodging else { return }

        let visual = visualFrame()
        let danger = visual.insetBy(dx: -Self.caretMargin, dy: -Self.caretMargin)
        guard danger.intersects(caret) else { return }
        guard let target = caretDodgeTarget(caret: caret) else { return }
        begin(dodge: target, hurried: true)
    }

    /// 안 쓰다가 **쓰기 시작한 순간**을 잡는다.
    ///
    /// 여기서만 창 목록을 물어본다. 창 목록 조회는 WindowServer에 말을 거는 일이라
    /// 틱마다 하면 비싸다. 시작하는 순간에 한 번이면 충분하다 — 한 번 창 밖으로
    /// 나가고 나면 더 밀려날 데가 없어서 타이핑이 이어져도 다시 움직일 일이 없다.
    private func noticeTypingStart() {
        let typing = isTypingQuiet
        defer { wasTyping = typing }
        guard typing, !wasTyping, dodgesTyping, !isDodging else { return }
        // 캐럿을 읽을 수 있으면 그쪽이 정확하다. 거친 판단은 쓰지 않는다.
        guard !canReadCaret() else { return }
        guard let target = windowDodgeTarget() else { return }
        begin(dodge: target, hurried: true)
    }

    /// 손쉬운 사용 권한 없이 쓰는 **거친** 회피.
    ///
    /// 캐럿이 어디 있는지는 모르지만 **앞 창이 어디까지 차지하는지는 권한 없이 읽힌다.**
    /// 글을 쓰기 시작했는데 마스코트가 그 창을 덮고 있으면 창 밖으로 물러난다.
    /// 캐럿이 닿기 전에 미리 비키는 셈이라 정밀하지는 않지만, 가릴 일이 없어진다.
    ///
    /// **왼쪽으로는 가지 않는다.** 정밀 회피와 같은 규칙이다 — 창을 가로질러 왼쪽으로
    /// 날아가면 방금 쓴 글 위를 지나가는 것으로 보인다. 오른쪽·아래·위가 다 막혔으면
    /// (전체화면 창처럼) 그냥 있는다.
    private func windowDodgeTarget() -> NSPoint? {
        guard let window = Self.frontmostWindowFrame(), let area = walkArea() else { return nil }
        let panel = frame()
        let visual = visualFrame()
        guard visual.intersects(window) else { return nil }

        // 창과 그림 사이의 여백. 창 밖으로 내보내야 하는 건 창이 아니라 그림이다.
        let leftPad = visual.minX - panel.minX
        let bottomPad = visual.minY - panel.minY
        let gap = Self.windowGap

        let right = window.maxX + gap - leftPad
        if right <= area.maxX { return NSPoint(x: right, y: panel.minY) }

        let below = window.minY - gap - visual.height - bottomPad
        if below >= area.minY { return NSPoint(x: panel.minX, y: below) }

        let above = window.maxY + gap - bottomPad
        if above <= area.maxY { return NSPoint(x: panel.minX, y: above) }

        return nil
    }

    /// 지금 앞에 나와 있는 앱의 창 자리. **아무 권한도 필요 없다.**
    ///
    /// 창 목록은 앞에서부터 오므로 그 앱의 첫 일반 창(`layer` 0)이 맨 앞 창이다.
    /// 메뉴·툴팁 같은 것들은 레이어가 달라서 걸러진다.
    private static func frontmostWindowFrame() -> CGRect? {
        guard let pid = NSWorkspace.shared.frontmostApplication?.processIdentifier,
              let list = CGWindowListCopyWindowInfo(
                  [.optionOnScreenOnly, .excludeDesktopElements],
                  kCGNullWindowID
              ) as? [[String: Any]]
        else { return nil }

        for entry in list {
            guard entry[kCGWindowOwnerPID as String] as? pid_t == pid,
                  entry[kCGWindowLayer as String] as? Int == 0,
                  let raw = entry[kCGWindowBounds as String] as? [String: Any],
                  let bounds = CGRect(dictionaryRepresentation: raw as CFDictionary),
                  // 아주 작은 창은 입력창이 아니라 팔레트·인디케이터일 가능성이 크다.
                  bounds.width > 160, bounds.height > 100
            else { continue }
            return bounds.flippedFromQuartz
        }
        return nil
    }

    /// 글은 왼쪽에서 오른쪽으로 흐른다. 그래서 **오른쪽으로 먼저** 비키고, 오른쪽이
    /// 막히면 한 줄 내려간다 — 줄바꿈과 같은 움직임이라 다음에 어디로 갈지 예측된다.
    /// 아래까지 막히면 맨 위로 돌아가서 다시 시작한다.
    ///
    /// **왼쪽으로는 어느 갈래에서도 가지 않는다.** 캐럿 왼쪽은 방금 쓴 글이 있는
    /// 자리라서, 거기로 물러나면 비켜준 게 아니라 읽던 것을 덮는 셈이 된다.
    /// 세 갈래가 내놓는 x는 전부 지금 자리보다 오른쪽이거나 같다.
    private func caretDodgeTarget(caret: CGRect) -> NSPoint? {
        guard let area = walkArea() else { return nil }
        let panel = frame()
        let visual = visualFrame()
        // 창과 그림 사이의 여백. 비켜야 하는 건 창이 아니라 그림이다.
        let leftPad = visual.minX - panel.minX

        let rightX = caret.maxX + Self.caretMargin - leftPad
        if rightX <= area.maxX, rightX - panel.minX >= Self.minimumMove {
            return NSPoint(x: rightX, y: panel.minY)
        }

        // 오른쪽 끝에 붙인 채 한 줄 내려간다. 줄 높이는 캐럿 높이를 따라간다 —
        // 큰 글씨로 쓰고 있으면 그만큼 크게 내려가야 다음 줄에서 또 부딪히지 않는다.
        //
        // 가장자리 여백(`edgeMargin`) 때문에 걸을 수 있는 오른쪽 끝이 지금 자리보다
        // 왼쪽일 수 있다. 창을 화면 맨 끝까지 끌어다 놨을 때가 그렇다. 그때는
        // 제자리에서 내려가기만 한다.
        let edgeX = max(panel.minX, area.maxX)
        let lineDrop = max(visual.height * 0.9, caret.height * 1.6)
        let down = panel.minY - lineDrop
        if down >= area.minY {
            return NSPoint(x: edgeX, y: down)
        }
        return NSPoint(x: edgeX, y: area.maxY)
    }

    // MARK: - 배회

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

extension CGRect {
    /// Quartz 좌표를 AppKit 좌표로 뒤집는다.
    ///
    /// 창 목록(`CGWindowListCopyWindowInfo`)과 손쉬운 사용이 주는 좌표는 원점이
    /// **주 화면 왼쪽 위**고, 창 프레임(`NSWindow.frame`)은 왼쪽 아래다.
    /// 뒤집지 않고 섞어 쓰면 세로가 통째로 반대인 자리가 나온다.
    var flippedFromQuartz: CGRect {
        guard let primary = NSScreen.screens.first else { return self }
        return CGRect(x: minX, y: primary.frame.maxY - maxY, width: width, height: height)
    }
}
