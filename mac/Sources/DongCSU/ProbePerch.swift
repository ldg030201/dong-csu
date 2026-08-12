import AppKit
import SwiftUI

/// `dong-csu --probe-perch` — 창 테두리에 붙는 계산을 화면 없이 잰다.
///
/// **렌더 통로로는 이걸 볼 수 없다.** 그쪽은 그림 한 장을 뽑을 뿐이고, 여기서 알고
/// 싶은 것은 *그 그림이 남의 창 테두리에 정말 닿는가* 다. 재는 것이 셋이다.
///
/// | | 왜 |
/// | --- | --- |
/// | 창 목록이 권한 없이 읽히는가 | 안 읽히면 기능 전체가 죽는다. 개수가 0이면 실패 |
/// | 좌표를 뒤집은 것이 맞는가 | 세로를 잘못 뒤집으면 창이 화면 밖에 있는 것으로 나온다 |
/// | **그림 자리를 맞게 예측했는가** | 아래 참고 |
///
/// 셋째가 핵심이다. 붙는 자리는 `MascotSpriteSet.ink`(그림에서 잰 알맹이 상자)로
/// 계산하는데, 그 예측이 틀려도 숫자만 봐서는 알 수 없다 — 같은 식으로 검산하면 늘
/// 맞는다. 그래서 **실제로 한 번 그려서** 알파를 재고 예측과 견준다. 좌우 반전이나
/// 위아래 뒤집기를 실수하면 여기서 잡힌다.
@MainActor
enum ProbePerch {
    /// 그림자(`shadow`)가 알파를 몇 px 번지게 한다. 그만큼은 봐준다.
    private static let tolerance: CGFloat = 6

    static func run(selftestOnly: Bool = false) -> Bool {
        if selftestOnly { return selftest() }
        var passed = true
        passed = surveyWindows() && passed
        passed = checkInk() && passed
        surveySpots()
        passed = selftest() && passed
        return passed
    }

    // MARK: - 붙어 있는 상태가 버티는지

    /// **붙여 놓은 것이 무엇 때문에 떨어지는지**를 화면 없이 돌려본다.
    ///
    /// 눈으로 볼 수 없는 검사다. 붙었는지는 그림을 보면 알지만, *클릭했을 때 떨어지는가*
    /// 는 떨어진 뒤 몇 초를 지켜봐야 알 수 있고 그때는 이미 걸어나가 있다.
    ///
    /// 실제로 여기서 잡은 것: `isPressed`(클릭·우클릭 메뉴)가 "움직여도 되는지"에 들어
    /// 있어서, 붙여 놓은 것을 **한 번 누르기만 해도** `motion` 이 `.resting` 으로 덮여
    /// 자세만 매달린 채 배회가 시작됐다.
    private static func selftest() -> Bool {
        guard let window = WindowSurvey.onScreenWindows().first else {
            print("\n자체 검사를 못 했다 — 붙일 창이 하나도 없다")
            return false
        }

        let motion = PetMotionController()
        var perchLog: [MascotPerch?] = []
        var moves: [NSPoint] = []
        let panel = NSRect(x: 400, y: 400, width: 128, height: 160)
        motion.frame = { panel }
        motion.visualFrame = { panel.insetBy(dx: 22, dy: 38) }
        motion.move = { moves.append($0) }
        motion.setPerch = { perchLog.append($0) }
        motion.perchOrigin = { _ in NSPoint(x: 400, y: 400) }
        motion.perches = true
        motion.dodgesCursor = true
        motion.wanders = true
        motion.canStayPerched = true
        motion.update(active: true)

        let spot = PerchSpot(
            window: window.id, edge: .right,
            offset: window.frame.height / 2, windowFrame: window.frame
        )

        print("\n붙여 놓은 것이 버티는지 (창: \(window.owner))")
        var passed = true

        func check(_ name: String, _ ok: Bool, _ note: String = "") {
            passed = ok && passed
            print("  \(pad(name, 34))\(ok ? "통과" : "**실패**")\(note.isEmpty ? "" : "  \(note)")")
        }

        check("붙는다", motion.perch(at: spot) && perchLog.last == .right,
              "자세 \(perchLog.last.map { "\($0.map(String.init(describing:)) ?? "없음")" } ?? "안 바뀜")")

        // **모서리 맨 끝을 겨냥해도 오프셋이 가둬져 나온다.** 안 가두면 그림 절반이 창
        // 밖으로 나간 자리에 앉은 뒤 첫 추적 틱에서 42pt 옆으로 튄다 — 미리보기와 착지는
        // 맞았는데 곧 옮겨가는 것으로 보여서 눈으로 원인을 잡기 어렵다.
        let box = UsageHUDView.petMascotRect(scale: HUDScale.normal.factor, style: .owlSheet)
        let aim = CGRect(
            x: window.frame.minX - box.width / 2, y: window.frame.maxY - 5,
            width: box.width, height: box.height
        )
        if let edgeSpot = WindowSurvey.snap(mascot: aim, within: 40) {
            check("모서리 끝을 겨냥해도 안 튄다",
                  edgeSpot.offset >= box.width / 2 - 0.5,
                  String(format: "오프셋 %.1f (하한 %.1f)", edgeSpot.offset, box.width / 2))
        } else {
            print("  \(pad("모서리 끝을 겨냥해도 안 튄다", 34))**판정 못 함**  그 자리에 후보가 안 잡혔다")
        }

        // **가려진 테두리에는 안 붙는다.** 실제 창 구성에 기대면 검사가 매번 달라지므로
        // 창 둘을 꾸며서 넣는다 — 앞 창이 뒤 창의 위 테두리를 덮고 있는 배치다.
        let hidden = (id: CGWindowID(1),
                      frame: CGRect(x: 300, y: 300, width: 400, height: 300), owner: "뒤")
        let cover = (id: CGWindowID(2),
                     frame: CGRect(x: 200, y: 200, width: 600, height: 500), owner: "앞")
        let aimAtHidden = CGRect(x: 458, y: 600, width: box.width, height: box.height)
        check("가려진 테두리에는 안 붙는다",
              WindowSurvey.snap(mascot: aimAtHidden, within: 40, windows: [cover, hidden]) == nil)
        check("가리는 창이 없으면 붙는다",
              WindowSurvey.snap(mascot: aimAtHidden, within: 40, windows: [hidden])?.edge == .top)

        // **붙은 창이 맨 앞인지 알아내야 한다.** 붙어 있는 펫은 그 창과 같은 층으로
        // 내려가므로, 그 창이 앞으로 왔을 때만 같이 올려 준다. 이 판정이 뒤집히면
        // 펫이 영영 창 뒤에 숨거나 반대로 늘 앞에 떠 있다.
        let all = WindowSurvey.onScreenWindows()
        if all.count >= 2 {
            let front = WindowSurvey.locate(all[0].id)
            let behind = WindowSurvey.locate(all[1].id)
            check("맨 앞 창을 맨 앞으로 본다", front?.isFront == true, "\(all[0].owner)")
            check("뒤 창을 앞으로 보지 않는다", behind?.isFront == false, "\(all[1].owner)")
        }
        check("닫힌 창은 못 찾는다", WindowSurvey.locate(CGWindowID(999_999_999)) == nil)

        // 클릭·우클릭 메뉴가 만드는 상황: 움직임은 멈추지만 붙은 것은 그대로여야 한다.
        let beforePress = perchLog.count
        motion.update(active: false)
        check("눌러도 안 떨어진다", perchLog.count == beforePress)
        motion.update(active: true)
        check("놓아도 그대로 붙어 있다", perchLog.count == beforePress)

        // 붙어 있는 동안에는 커서를 피하지 않는다.
        //
        // **대조군이 없으면 이 검사는 아무것도 말해 주지 못한다.** 붙어서 안 움직인 것과
        // 타이핑 정숙·화면 경계 때문에 안 움직인 것이 결과가 같기 때문이다. 같은 조건에서
        // 붙지 않은 것 하나를 나란히 돌려서, 그쪽은 실제로 비키는지 먼저 확인한다.
        // **키 입력이 멎기를 먼저 기다린다.** 글을 쓰는 동안에는 붙었든 안 붙었든 비키지
        // 않아서(`isTypingQuiet`), 그 상태로 재면 대조가 성립하지 않는다. 이 검사를
        // 터미널에서 돌리는 순간이 정확히 그 상태다 — 방금 명령을 입력했다.
        if motion.isTypingQuiet {
            print("  키 입력이 멎기를 기다린다 (5초)…")
            settle(5.4)
        }

        let control = PetMotionController()
        var controlMoves: [NSPoint] = []
        control.frame = { panel }
        control.visualFrame = { panel.insetBy(dx: 22, dy: 38) }
        control.move = { controlMoves.append($0) }
        control.dodgesCursor = true
        control.update(active: true)
        control.dodgeCursor()
        // **바로 움직이지 않는다.** `dodgeCursor` 는 "비키기로 정했다"까지만 하고 실제
        // 이동은 다음 틱에서 일어난다. 런루프를 잠깐 돌려 주지 않으면 비킨 것과 안 비킨
        // 것이 똑같이 "이동 없음"으로 보여서, 대조가 늘 실패한다 — 실제로 그랬다.
        settle(0.45)
        let controlDodged = !controlMoves.isEmpty

        let beforeDodge = moves.count
        motion.dodgeCursor()
        settle(0.45)
        let stayed = moves.count == beforeDodge
        if controlDodged {
            check("붙어 있으면 커서를 안 피한다", stayed, "(안 붙은 쪽은 비켰다 — 대조 성립)")
        } else {
            // 대조가 성립하지 않으면 통과로 세지 않는다. 모르는 것을 통과로 적으면
            // 다음 사람이 검사됐다고 믿는다.
            print("  \(pad("붙어 있으면 커서를 안 피한다", 34))**판정 못 함**  "
                  + "안 붙은 쪽도 안 비켰다"
                  + (motion.isTypingQuiet ? " (방금 키를 눌러서 정숙 상태다)" : ""))
        }

        // 붙어 있을 수 없게 되면(펫 모드에서 나감·화면 잠김·집어 듦) 떨어진다.
        motion.canStayPerched = false
        motion.update(active: true)
        check("붙어 있을 수 없으면 떨어진다", perchLog.last == MascotPerch?.none)

        // 설정을 끄면 붙어 있던 것도 놓는다.
        let again = PetMotionController()
        var offLog: [MascotPerch?] = []
        again.frame = { panel }
        again.visualFrame = { panel.insetBy(dx: 22, dy: 38) }
        again.move = { _ in }
        again.setPerch = { offLog.append($0) }
        again.perchOrigin = { _ in NSPoint(x: 400, y: 400) }
        again.perches = true
        again.canStayPerched = true
        again.update(active: true)
        _ = again.perch(at: spot)
        again.perches = false
        again.update(active: true)
        check("설정을 끄면 놓는다", offLog.last == MascotPerch?.none)

        print(passed ? "  전부 통과" : "  실패 — 붙여 놓은 것이 버티지 못한다")
        return passed
    }

    /// 지금 떠 있는 창들에 **실제로 붙을 수 있는 자리**를 늘어놓는다.
    ///
    /// 붙지 않는다는 말을 들었을 때 제일 먼저 볼 자리다. 자세 계산이 맞아도 붙을 자리가
    /// 없을 수 있다 — 창이 화면 위에 딱 붙어 있으면 그 **위 테두리에 앉을 공간이 없다.**
    /// 그건 고장이 아니라 물리적으로 자리가 없는 것이라, 숫자로 보여줘야 구분이 된다.
    private static func surveySpots() {
        let scale = HUDScale.normal.factor
        let box = UsageHUDView.petMascotRect(scale: scale, style: .owlSheet)
        let windows = WindowSurvey.onScreenWindows()
        guard !windows.isEmpty else { return }

        print("\n어느 창 어느 테두리에 붙을 수 있나 (배율 1, 모서리 가운데에 놓는다고 치고)")
        var open = 0
        for window in windows.prefix(10) {
            var parts: [String] = []
            for edge in [MascotPerch.top, .bottom, .left, .right] {
                let blocked = obstacle(
                    id: window.id, edge: edge, window: window.frame,
                    mascot: box.size, scale: scale
                )
                if blocked == nil { open += 1 }
                parts.append("\(label(edge).replacingOccurrences(of: " ", with: "")) \(blocked ?? "가능")")
            }
            print("  \(pad(window.owner, 20))\(parts.joined(separator: " · "))")
        }
        print("  붙을 수 있는 자리 \(open)개")
        if open == 0 {
            print("  붙을 자리가 없다 — 창이 전부 화면에 꽉 차 있으면 그렇다. 창을 하나 줄여 보라")
        }
    }

    /// 그 모서리에 못 붙는 이유. 붙을 수 있으면 nil.
    ///
    /// **마지막 판정은 실제 통로(`snap`)에 맡긴다.** 여기서 따로 셈하면 표에는 "가능"이
    /// 뜨는데 실제로는 안 붙는 자리가 생긴다 — 가림 판정이 그렇게 어긋났다.
    private static func obstacle(
        id: CGWindowID, edge: MascotPerch, window: CGRect, mascot: CGSize, scale: CGFloat
    ) -> String? {
        let horizontal = edge == .top || edge == .bottom
        let span = horizontal ? window.width : window.height
        let need = horizontal ? mascot.width : mascot.height
        if span < need { return "모서리가 \(Int((need - span).rounded()))pt 짧다" }
        let middle = PerchSpot(window: id, edge: edge, offset: span / 2, windowFrame: window)
        guard UsageHUDView.petPerchOrigin(
            perch: edge, contact: middle.contact(in: window), scale: scale, style: .owlSheet
        ) != nil else { return "화면 밖" }
        // 그 자리에 그림을 놓았다고 치고 실제로 이 모서리가 뽑히는지 본다.
        // 안 뽑히면 남의 창에 가려졌거나 더 가까운 모서리가 있다는 뜻이다.
        let aim = WindowSurvey.landingArea(middle, mascot: mascot)
        let picked = WindowSurvey.snap(mascot: aim, within: 40)
        guard picked?.window == id, picked?.edge == edge else { return "가려짐" }
        return nil
    }

    /// 타이머가 몇 번 돌 만큼 런루프를 굴린다. 펫의 한 틱은 0.1초다.
    private static func settle(_ seconds: TimeInterval) {
        RunLoop.current.run(until: Date().addingTimeInterval(seconds))
    }

    /// 한글은 터미널에서 두 칸을 쓴다. `%-20@` 로는 줄이 안 맞는다.
    private static func pad(_ text: String, _ width: Int) -> String {
        let visual = text.reduce(0) { $0 + ($1.isASCII ? 1 : 2) }
        return text + String(repeating: " ", count: max(width - visual, 0))
    }

    // MARK: - 창 목록

    private static func surveyWindows() -> Bool {
        let windows = WindowSurvey.onScreenWindows()
        print("창 목록 — \(windows.count)개 (손쉬운 사용·화면 기록 권한 없이)")
        guard !windows.isEmpty else {
            print("  하나도 안 잡혔다 — 창을 하나 띄우고 다시 돌려라")
            return false
        }

        var offScreen = 0
        for window in windows.prefix(12) {
            let screen = NSScreen.screens.firstIndex { $0.frame.intersects(window.frame) }
            if screen == nil { offScreen += 1 }
            print(String(
                format: "  %@(%6.0f, %6.0f) %5.0fx%-5.0f %@",
                pad(window.owner, 20), window.frame.minX, window.frame.minY,
                window.frame.width, window.frame.height,
                screen.map { "화면 \($0 + 1)" } ?? "**어느 화면에도 없다**"
            ))
        }
        if windows.count > 12 { print("  … 그 밖 \(windows.count - 12)개") }

        // 화면 밖에 있는 것으로 나오면 세로를 뒤집는 기준이 틀렸다는 뜻이다.
        // (모니터를 방금 뺐다면 잠깐 그럴 수 있어서 전부일 때만 실패로 친다.)
        guard offScreen < min(windows.count, 12) else {
            print("  실패 — 창이 전부 화면 밖이다. 좌표 뒤집기(primaryHeight)가 틀렸다")
            return false
        }
        return true
    }

    // MARK: - 그림 자리

    private static func checkInk() -> Bool {
        guard let set = MascotSpriteStore.bundled else {
            print("\n실패 — 번들에 마스코트 시트가 없다")
            return false
        }
        let scale = HUDScale.normal.factor
        let box = UsageHUDView.petMascotRect(scale: scale, style: .owlSheet)
        print(String(format: "\n그림 묶음 상자 %.1f x %.1f pt (배율 1)", box.width, box.height))
        print("자세마다 그 상자 안 어디에 그려져 있나 — 예측(계산) vs 실제(그려서 잰 것)")

        var passed = true
        for perch in [MascotPerch.top, .bottom, .right, .left] {
            guard let predicted = UsageHUDView.petMascotInkRect(
                perch: perch, scale: scale, style: .owlSheet
            ) else {
                print("  \(label(perch)) — 예측을 못 냈다")
                passed = false
                continue
            }
            guard let drawn = render(sprite: perch.sprite, flipped: perch.flipsSprite, set: set) else {
                print("  \(label(perch)) — 그려 보지 못했다")
                passed = false
                continue
            }

            // 그린 그림은 상자 좌표(왼쪽 위가 0)로 나온다. 뷰 좌표(아래가 0)로 맞춘다.
            let actual = CGRect(
                x: box.minX + drawn.minX,
                y: box.maxY - drawn.maxY,
                width: drawn.width, height: drawn.height
            )
            let gap = max(
                abs(actual.minX - predicted.minX), abs(actual.maxX - predicted.maxX),
                abs(actual.minY - predicted.minY), abs(actual.maxY - predicted.maxY)
            )
            let ok = gap <= tolerance
            passed = ok && passed
            print(String(
                format: "  %@예측 x %.0f~%.0f  y %.0f~%.0f │ 실제 x %.0f~%.0f  y %.0f~%.0f │ %@",
                pad(label(perch), 16),
                predicted.minX, predicted.maxX, predicted.minY, predicted.maxY,
                actual.minX, actual.maxX, actual.minY, actual.maxY,
                ok ? String(format: "%.1fpt", gap) : String(format: "**%.1fpt 어긋남**", gap)
            ))
        }

        print(passed ? "\n통과" : "\n실패 — 그림 자리 예측이 실제와 어긋난다")
        return passed
    }

    /// 그 자세를 실제로 한 번 그려서 알맹이가 상자 어디를 덮는지 잰다(상자 좌표, 위가 0).
    private static func render(
        sprite: MascotSprite, flipped: Bool, set: MascotSpriteSet
    ) -> CGRect? {
        let height = UsageHUDView.petOwlHeight(scale: HUDScale.normal.factor)
        let view = MascotSpriteView(
            set: set, sprite: sprite, flipped: flipped, testLook: false, size: height
        )
        let renderer = ImageRenderer(content: view)
        renderer.scale = 1
        guard let cg = renderer.cgImage else { return nil }
        return MascotSheet.opaqueBounds(cg)
    }

    private static func label(_ perch: MascotPerch) -> String {
        switch perch {
        case .top: return "위에 앉기"
        case .bottom: return "아래 매달리기"
        case .right: return "오른쪽 붙기"
        case .left: return "왼쪽 붙기"
        }
    }
}
