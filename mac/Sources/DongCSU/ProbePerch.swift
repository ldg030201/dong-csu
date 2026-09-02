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

    /// 어느 캐릭터로 잴지. 캐릭터마다 그림이 달라서 붙는 자리도 다르다.
    ///
    /// **여기가 화면과 같은 통로를 타야 한다.** 잉크 상자도 잡는 깊이도 시트에서
    /// 나오므로, 새 캐릭터를 넣고 이걸 안 돌려 보면 그 캐릭터에서만 발이 뜬다.
    private(set) static var style: ClaudeIconStyle = .owlSheet

    static func run(selftestOnly: Bool = false) -> Bool {
        if selftestOnly { return selftest() }
        style = ClaudeIconStyle.allCases.first {
            $0.usesSheet && CommandLine.arguments.contains($0.rawValue)
        } ?? .owlSheet
        print("캐릭터: \(style.shortTitle) (\(style.rawValue))")
        if CommandLine.arguments.contains("windows") { dumpWindows(); return true }
        var passed = true
        passed = surveyWindows() && passed
        passed = checkInk() && passed
        passed = checkGrip() && passed
        passed = checkPlacements() && passed
        surveySpots()
        passed = selftest() && passed
        passed = checkScreens() && passed
        return passed
    }

    // MARK: - 다른 화면으로 넘어가기

    /// **화면이 여럿일 때만 뜻이 있는 검사다.** 켜면 옆 화면 자리를 그대로 받아들이고,
    /// 끄면 지금 화면 안으로 되당겨야 한다.
    ///
    /// 눈으로는 못 본다 — 배회는 3~11초에 한 번, 26pt/s 로 움직이고 키를 누르는 동안은
    /// 멈춘다. 옆 화면까지 걸어가는 것을 지켜보려면 몇 분이 걸린다.
    private static func checkScreens() -> Bool {
        print("\n다른 화면으로 넘어가기")
        let screens = NSScreen.screens
        guard screens.count > 1 else {
            print("  화면이 하나뿐이라 잴 것이 없다 — 이 기능은 아무 일도 하지 않는다")
            return true
        }
        for (index, screen) in screens.enumerated() {
            let f = screen.visibleFrame
            print(String(format: "  화면 %d — x %.0f~%.0f  y %.0f~%.0f",
                         index + 1, f.minX, f.maxX, f.minY, f.maxY))
        }

        let panel = NSRect(origin: .zero, size: UsageHUDView.size(.init(.pet)))
        let motion = PetMotionController()
        motion.frame = { panel }
        motion.visualFrame = { panel }

        // 지금 창이 있는 화면과 **다른** 화면 한가운데를 목표로 삼는다.
        let here = screens.first { $0.frame.intersects(panel) } ?? screens[0]
        guard let there = screens.first(where: { $0 != here }) else { return true }
        let target = NSPoint(
            x: there.visibleFrame.midX - panel.width / 2,
            y: there.visibleFrame.midY - panel.height / 2
        )
        func onOtherScreen(_ p: NSPoint) -> Bool {
            NSRect(origin: p, size: panel.size).intersects(there.frame)
        }

        motion.crossesScreens = false
        let blocked = motion.clamped(target, crossing: true)
        motion.crossesScreens = true
        let allowed = motion.clamped(target, crossing: true)
        // **켜 두어도 제 걸음이 아니면 안 넘어간다.** 붙어 있던 데서 떨어져 자리를
        // 잡을 때가 이 길로 온다.
        let dodged = motion.clamped(target)

        let offOK = blocked.map { !onOtherScreen($0) } ?? true
        let onOK = allowed.map(onOtherScreen) ?? false
        let dodgeOK = dodged.map { !onOtherScreen($0) } ?? true
        print("  꺼짐 — 옆 화면 자리를 요구하면 지금 화면으로 되당긴다   \(offOK ? "통과" : "실패")")
        print("  켜짐 · 걸어감 — 옆 화면 자리를 그대로 받아들인다        \(onOK ? "통과" : "실패")")
        print("  켜짐 · 떼어놓기 — 지금 화면 안에 남는다                 \(dodgeOK ? "통과" : "실패")")
        let ok = offOK && onOK && dodgeOK
        print(ok ? "\n통과" : "\n실패")
        return ok
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
        let box = UsageHUDView.petMascotRect(scale: HUDScale.normal.factor, style: style)
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
        let box = UsageHUDView.petMascotRect(scale: scale, style: style)
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
            // **같은 창을 화면 한가운데로 옮겨 본다.** 안 붙는 이유가 그 앱 때문인지
            // 그 창이 놓인 자리 때문인지 가리는 유일한 방법이다 — 앱은 그대로 두고
            // 자리만 바꿔 보는 것이라 둘 중 하나만 남는다.
            if let screen = NSScreen.main {
                let visible = screen.visibleFrame
                let centered = CGRect(
                    x: visible.midX - window.frame.width / 2,
                    y: visible.midY - window.frame.height / 2,
                    width: window.frame.width, height: window.frame.height
                )
                var moved: [String] = []
                for edge in [MascotPerch.top, .bottom, .left, .right] {
                    let blocked = obstacle(
                        id: window.id, edge: edge, window: centered,
                        mascot: box.size, scale: scale
                    )
                    moved.append("\(label(edge).replacingOccurrences(of: " ", with: ""))"
                                 + " \(blocked.map { $0.hasPrefix("화면 밖") ? "화면 밖" : $0 } ?? "가능")")
                }
                print("  \(pad("  ↳ 한가운데로 옮기면", 20))\(moved.joined(separator: " · "))")
            }
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
            perch: edge, contact: middle.contact(in: window), scale: scale, style: style
        ) != nil else {
            return "화면 밖" + shortfall(
                edge: edge, window: window, scale: scale,
                contact: middle.contact(in: window)
            )
        }
        // 그 자리에 그림을 놓았다고 치고 실제로 이 모서리가 뽑히는지 본다.
        // 안 뽑히면 남의 창에 가려졌거나 더 가까운 모서리가 있다는 뜻이다.
        let sink = { (spot: PerchSpot) in
            UsageHUDView.petPerchSink(
                perch: spot.edge, contact: spot.contact(in: spot.windowFrame),
                scale: scale, style: style
            )
        }
        let aim = WindowSurvey.landingArea(middle, mascot: mascot, sink: sink(middle))
        // **앱과 같은 술어를 넘긴다.** `sink` 만 넘기고 `placeable` 을 빼면, 앱은 놓을 수
        // 없는 변을 걸러내고 다음 변으로 가는데 진단은 그 변을 골라서 "가려짐"을 찍는다 —
        // 이 함수 주석이 못 박아 둔 어긋남이 그대로 생긴다.
        let placeable = { (spot: PerchSpot) in
            UsageHUDView.petPerchOrigin(
                perch: spot.edge, contact: spot.contact(in: spot.windowFrame),
                scale: scale, style: style
            ) != nil
        }
        let picked = WindowSurvey.snap(mascot: aim, within: WindowSurvey.snapDistance(mascot: mascot),
                                     sink: sink, placeable: placeable)
        guard picked?.window == id, picked?.edge == edge else { return "가려짐" }
        return nil
    }

    /// "화면 밖" 이 몇 pt 모자라서인지. 못 재면 빈 글자.
    ///
    /// **이것 때문에 안 붙는다는 말을 제일 많이 듣는다.** 창을 조금만 옮기면 되는데
    /// 숫자가 안 보이면 고장으로 읽힌다.
    private static func shortfall(
        edge: MascotPerch, window: CGRect, scale: CGFloat, contact: CGPoint
    ) -> String {
        guard let ink = UsageHUDView.petMascotInkRect(
                perch: edge, scale: scale, style: style
              ),
              let screen = NSScreen.screens.first(where: { $0.frame.intersects(window) })
        else { return "" }
        let sink = UsageHUDView.petPerchSink(
            perch: edge, contact: contact, scale: scale, style: style
        )
        let visible = screen.visibleFrame
        // 창 밖에 남는 몫만큼 자리가 있어야 한다.
        let need: CGFloat
        let have: CGFloat
        switch edge {
        case .top: need = ink.height - sink; have = visible.maxY - window.maxY
        case .bottom: need = ink.height - sink; have = window.minY - visible.minY
        case .right: need = ink.width - sink; have = visible.maxX - window.maxX
        case .left: need = ink.width - sink; have = window.minX - visible.minX
        }
        guard need > have else { return "" }
        return String(format: " (%.0fpt 모자람 — %.0f 필요한데 %.0f)", need - have, need, have)
    }

    /// 타이머가 몇 번 돌 만큼 런루프를 굴린다. 펫의 한 틱은 0.1초다.
    private static func settle(_ seconds: TimeInterval) {
        RunLoop.current.run(until: Date().addingTimeInterval(seconds))
    }

    /// 한글은 터미널에서 두 칸을 쓴다. `%-20@` 로는 줄이 안 맞는다.
    /// 한글은 터미널에서 두 칸을 먹는다. 글자 수로 맞추면 표가 어긋난다.
    /// `ProbeHUD` 도 같이 쓴다.
    static func pad(_ text: String, _ width: Int) -> String {
        let visual = text.reduce(0) { $0 + ($1.isASCII ? 1 : 2) }
        return text + String(repeating: " ", count: max(width - visual, 0))
    }

    // MARK: - 창 목록

    /// 걸러내기 전의 **날것 목록.** 안 보이는 창에 붙는다는 말을 들었을 때 볼 자리다.
    ///
    /// `onScreenWindows()` 는 층 0 · 화면에 뜸 · 알파 · 최소 크기 · 우리 앱을 걸러낸
    /// 뒤를 준다. 무엇이 왜 남았는지 보려면 거르기 전을 봐야 한다.
    private static func dumpWindows() {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let raw = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]]
        else { return print("창 목록을 못 읽었다") }
        let kept = Set(WindowSurvey.onScreenWindows().map(\.id))
        print("날것 창 목록 — \(raw.count)개 (앞이 위)")
        print("  \(pad("앱", 22))\(pad("번호", 8))\(pad("층", 5))\(pad("알파", 6))"
              + "\(pad("자리 (Quartz 좌표)", 30))쓰나")
        for entry in raw {
            let owner = entry[kCGWindowOwnerName as String] as? String ?? "?"
            let id = entry[kCGWindowNumber as String] as? CGWindowID ?? 0
            let layer = entry[kCGWindowLayer as String] as? Int ?? -999
            let alpha = entry[kCGWindowAlpha as String] as? Double ?? -1
            let onscreen = entry[kCGWindowIsOnscreen as String] as? Bool ?? false
            let bounds = entry[kCGWindowBounds as String] as? [String: CGFloat]
            let place = bounds.map {
                String(format: "(%5.0f,%5.0f) %5.0fx%-5.0f",
                       $0["X"] ?? 0, $0["Y"] ?? 0, $0["Width"] ?? 0, $0["Height"] ?? 0)
            } ?? "?"
            var why = "—"
            if kept.contains(id) { why = "**붙을 수 있음**" }
            else if layer != 0 { why = "층 0 아님" }
            else if !onscreen { why = "화면에 없음" }
            else if alpha <= 0.01 { why = "투명" }
            else if let b = bounds, (b["Width"] ?? 0) < 120 || (b["Height"] ?? 0) < 80 {
                why = "너무 작음"
            } else if owner == AppInfo.name { why = "우리 앱" }
            print("  \(pad(owner, 22))\(pad(String(id), 8))\(pad(String(layer), 5))"
                  + "\(pad(String(format: "%.2f", alpha), 6))\(pad(place, 30))\(why)")
        }
    }

    private static func surveyWindows() -> Bool {
        // **쓸 수 있는 화면 넓이를 먼저 보여준다.** 붙을 자리가 없는 이유가 거의 항상
        // 여기서 나온다 — 창의 위 테두리가 화면 꼭대기에 가까우면 그 위에 앉을 자리가
        // 없다. 숫자가 없으면 "화면 밖" 이라는 말만 남아서 왜 그런지 알 수가 없다.
        for (index, screen) in NSScreen.screens.enumerated() {
            let visible = screen.visibleFrame
            print(String(
                format: "화면 %d — 전체 %.0fx%.0f, 쓸 수 있는 자리 y %.0f~%.0f (메뉴 막대·Dock 제외)",
                index + 1, screen.frame.width, screen.frame.height,
                visible.minY, visible.maxY
            ))
        }

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
        guard let set = MascotSpriteStore.bundled(style) else {
            print("\n실패 — 번들에 마스코트 시트가 없다")
            return false
        }
        let scale = HUDScale.normal.factor
        let box = UsageHUDView.petMascotRect(scale: scale, style: style)
        print(String(format: "\n그림 묶음 상자 %.1f x %.1f pt (배율 1)", box.width, box.height))
        print("자세마다 그 상자 안 어디에 그려져 있나 — 예측(계산) vs 실제(그려서 잰 것)")

        var passed = true
        for perch in [MascotPerch.top, .bottom, .right, .left] {
            guard let predicted = UsageHUDView.petMascotInkRect(
                perch: perch, scale: scale, style: style
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

    /// 화면 곳곳에 창이 있다고 치고 **붙인 자리가 흔들리지 않는지.**
    ///
    /// 진짜 창으로는 몇 가지 자리밖에 못 본다. 여기서는 창 목록을 만들어 넣어
    /// 가장자리 · 구석 · 아주 작은 창 · 화면을 넘는 창까지 훑는다.
    ///
    /// **재는 것은 왕복이다.** 어떤 자리에 붙였다고 치고 그 자리에서 다시 찾으면
    /// **같은 창의 같은 변이 같은 오프셋으로** 나와야 한다. 안 그러면 붙이자마자
    /// 다음 틱에 딴 데로 옮겨간다 — 사용자 눈에는 "놓으면 튄다" 로 보인다.
    private static func checkPlacements() -> Bool {
        guard NSScreen.main != nil else {
            print("\n실패 — 화면을 못 찾았다")
            return false
        }
        // **크기 설정 네 단계를 다 훑는다.** 그림이 커지면 잡는 깊이도 같이 커져서,
        // 배율 1 에서 붙던 자리가 1.5 에서는 안 붙을 수 있다.
        var all = true
        for step in HUDScale.allCases {
            all = placements(scale: step.factor) && all
        }
        return all
    }

    private static func placements(scale: CGFloat) -> Bool {
        let box = UsageHUDView.petMascotRect(scale: scale, style: style)
        guard let screen = NSScreen.main else { return false }
        let visible = screen.visibleFrame
        let sink = { (spot: PerchSpot) in
            UsageHUDView.petPerchSink(
                perch: spot.edge, contact: spot.contact(in: spot.windowFrame),
                scale: scale, style: style
            )
        }
        let placeable = { (spot: PerchSpot) in
            UsageHUDView.petPerchOrigin(
                perch: spot.edge, contact: spot.contact(in: spot.windowFrame),
                scale: scale, style: style
            ) != nil
        }

        // 화면 안팎에 창을 흩어 놓는다. 이름이 곧 무엇을 보는지다.
        var cases: [(String, CGRect)] = [
            ("화면 한가운데", CGRect(x: visible.midX - 300, y: visible.midY - 200,
                                     width: 600, height: 400)),
            ("왼쪽 끝에 붙음", CGRect(x: visible.minX, y: visible.midY - 200,
                                      width: 400, height: 400)),
            ("오른쪽 끝에 붙음", CGRect(x: visible.maxX - 400, y: visible.midY - 200,
                                        width: 400, height: 400)),
            ("위쪽 끝에 붙음", CGRect(x: visible.midX - 200, y: visible.maxY - 300,
                                      width: 400, height: 300)),
            ("아래쪽 끝에 붙음", CGRect(x: visible.midX - 200, y: visible.minY,
                                        width: 400, height: 300)),
            ("왼쪽 아래 구석", CGRect(x: visible.minX, y: visible.minY,
                                      width: 300, height: 250)),
            ("오른쪽 위 구석", CGRect(x: visible.maxX - 300, y: visible.maxY - 250,
                                      width: 300, height: 250)),
            ("화면을 꽉 채움", visible),
            ("아주 작은 창", CGRect(x: visible.midX - 70, y: visible.midY - 50,
                                    width: 140, height: 100)),
            ("화면보다 큰 창", visible.insetBy(dx: -200, dy: -150)),
        ]
        // 화면 밖으로 나간 창. 여기 붙으면 허공에 매달린 것으로 보인다.
        cases.append(("화면 밖", CGRect(x: visible.maxX + 100, y: visible.midY,
                                        width: 400, height: 300)))

        var passed = true
        var tried = 0
        var settled = 0
        print(String(format: "\n여러 자리에 붙여 보고 그 자리에서 다시 찾아본다 (배율 %.2g)", scale))
        for (name, frame) in cases {
            let windows = [(id: CGWindowID(9001), frame: frame, owner: "가짜")]
            var notes: [String] = []
            for edge in [MascotPerch.top, .bottom, .left, .right] {
                let span = (edge == .top || edge == .bottom) ? frame.width : frame.height
                let start = PerchSpot(
                    window: 9001, edge: edge, offset: span / 2, windowFrame: frame
                )
                guard let landed = start.clamped(to: frame, mascot: box.size),
                      placeable(landed)
                else { continue }
                tried += 1
                // 붙인 자리에서 그림이 덮는 사각형을 만들고, 거기서 다시 찾는다.
                let aim = WindowSurvey.landingArea(landed, mascot: box.size, sink: sink(landed))
                let again = WindowSurvey.snap(
                    mascot: aim, within: WindowSurvey.snapDistance(mascot: box.size),
                    sink: sink, placeable: placeable, windows: windows
                )
                if again?.window == landed.window, again?.edge == landed.edge,
                   abs((again?.offset ?? 0) - landed.offset) <= 0.5 {
                    settled += 1
                } else {
                    passed = false
                    let got = again.map { "\(label($0.edge)) 오프셋 \(Int($0.offset))" } ?? "못 찾음"
                    notes.append("**\(label(edge)) → \(got)**")
                }
            }
            print("  \(pad(name, 18))\(notes.isEmpty ? "그대로" : notes.joined(separator: " · "))")
        }
        print("  붙여 본 자리 \(tried)곳 중 \(settled)곳이 제자리")

        // **창을 확 옮겨도 같은 자리에 남아야 한다.** 창을 끌면 매 틱 `follow` 가
        // `clamped` → `perchOrigin` 을 다시 도는데, 거기서 오프셋이 흔들리면 창을
        // 옮길 때마다 펫이 모서리를 따라 스르륵 미끄러진다.
        var moved = 0
        var kept = 0
        // 옮겨 간 자리에 설 곳이 없어서 떨어진 것. 고장이 아니다 —
        // 그림이 커지는 배율에서 늘어난다.
        var dropped = 0
        let base = CGRect(x: visible.midX - 300, y: visible.midY - 200, width: 600, height: 400)
        for edge in [MascotPerch.top, .bottom, .left, .right] {
            let span = (edge == .top || edge == .bottom) ? base.width : base.height
            let start = PerchSpot(window: 9001, edge: edge, offset: span / 2, windowFrame: base)
            guard let landed = start.clamped(to: base, mascot: box.size), placeable(landed)
            else { continue }
            for delta in [CGPoint(x: -220, y: 0), CGPoint(x: 260, y: -140),
                          CGPoint(x: 0, y: 170), CGPoint(x: -80, y: -90)] {
                let after = base.offsetBy(dx: delta.x, dy: delta.y)
                moved += 1
                guard let followed = landed.clamped(to: after, mascot: box.size),
                      placeable(followed)
                else { dropped += 1; continue }   // 못 붙는 자리로 갔으면 떨어지는 것이 맞다
                if abs(followed.offset - landed.offset) <= 0.5 { kept += 1 } else {
                    passed = false
                    print("  **창을 옮기니 \(label(edge)) 오프셋이 "
                          + "\(Int(landed.offset)) → \(Int(followed.offset)) 로 밀렸다**")
                }
            }
        }
        print("  창을 확 옮겨 본 \(moved)번 — 오프셋 그대로 \(kept)번"
              + (dropped > 0 ? " · 설 자리가 없어 떨어짐 \(dropped)번" : ""))
        print(passed ? "  통과" : "  **실패 — 붙인 자리에서 다시 찾으면 딴 데가 나온다 (놓으면 튄다)**")
        return passed
    }

    /// 붙잡는 부위가 창 안으로 **정말 넘어가는지.**
    ///
    /// `checkInk` 는 "그림이 상자 어디에 그려져 있나" 만 본다. 겹침은 그 위에 얹히는
    /// 것이라 저기서는 안 잡힌다 — 실제로 겹침을 `petMascotInkRect` 안에 넣으면
    /// `checkInk` 가 깨지도록 일부러 갈라 놓았다(`UsageHUDView.petPerchSink` 주석).
    /// 그래서 넘어간 깊이는 여기서 따로 잰다.
    ///
    /// **부호를 잡는 검사다.** 네 변이 각각 창 안쪽 방향이 달라서, 한 곳만 뒤집혀도
    /// 그 변에서만 그림이 창 밖으로 더 밀려난다 — 눈으로는 "조금 떠 있네" 로 보인다.
    private static func checkGrip() -> Bool {
        let scale = HUDScale.normal.factor
        guard let screen = NSScreen.main else {
            print("\n실패 — 화면을 못 찾았다")
            return false
        }
        // 화면 한가운데를 접점으로 삼는다. 가장자리로 잡으면 화면 밖 판정에 걸린다.
        let contact = CGPoint(x: screen.frame.midX, y: screen.frame.midY)

        print("\n붙잡는 부위가 창 안으로 넘어가는 깊이")
        var passed = true
        for perch in [MascotPerch.top, .bottom, .right, .left] {
            let want = UsageHUDView.petPerchSink(
                perch: perch, contact: contact, scale: scale, style: style
            )
            guard
                let origin = UsageHUDView.petPerchOrigin(
                    perch: perch, contact: contact, scale: scale, style: style
                ),
                let ink = UsageHUDView.petMascotInkRect(
                    perch: perch, scale: scale, style: style
                )
            else {
                print("  \(label(perch)) — 자리를 못 냈다")
                passed = false
                continue
            }
            // 뷰 좌표의 잉크를 화면 좌표로 옮긴다.
            let visual = CGRect(
                x: origin.x + ink.minX, y: origin.y + ink.minY,
                width: ink.width, height: ink.height
            )
            // 접점 선을 넘어 **창 안쪽으로** 들어간 길이.
            let crossed: CGFloat
            switch perch {
            case .top: crossed = contact.y - visual.minY
            case .bottom: crossed = visual.maxY - contact.y
            case .right: crossed = contact.x - visual.minX
            case .left: crossed = visual.maxX - contact.x
            }
            let ok = abs(crossed - want) <= 0.5
            passed = ok && passed
            let span = (perch == .top || perch == .bottom) ? ink.height : ink.width
            print(String(
                format: "  %@%.1fpt / 잉크 %.0fpt = %.0f%%  │ 실제로 넘어간 것 %.1fpt │ %@",
                pad(label(perch), 16), want, span, span > 0 ? want / span * 100 : 0,
                crossed, ok ? "맞다" : "**어긋남**"
            ))
        }
        print(passed ? "\n통과" : "\n실패 — 넘어간 깊이가 예측과 다르다 (부호를 뒤집었을 수 있다)")
        return passed
    }

    /// 그 자세를 실제로 한 번 그려서 알맹이가 상자 어디를 덮는지 잰다(상자 좌표, 위가 0).
    private static func render(
        sprite: MascotSprite, flipped: Bool, set: MascotSpriteSet
    ) -> CGRect? {
        let height = UsageHUDView.petOwlHeight(scale: HUDScale.normal.factor)
        let view = MascotSpriteView(
            set: set, sprite: sprite, flipped: flipped, size: height
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
