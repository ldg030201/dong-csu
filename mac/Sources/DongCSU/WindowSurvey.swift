import AppKit

/// 화면에 떠 있는 창 목록. 앞에서 뒤 순서다.
typealias WindowList = [(id: CGWindowID, frame: CGRect, owner: String)]

/// 다른 앱 창이 화면 어디에 있는지 읽는다. 펫을 창 테두리에 붙일 때만 쓴다.
///
/// **권한이 하나도 들지 않는다.** `CGWindowListCopyWindowInfo` 는 손쉬운 사용도 화면
/// 기록도 묻지 않고 `bounds`·`pid`·`layer`·`alpha` 를 준다 — 이 기계에서 창 46개로
/// 직접 재 봤고 TCC 허락 창은 한 번도 안 떴다. 못 받는 것은 **창 제목 하나뿐**인데
/// 우리는 제목을 안 본다. `mac/CLAUDE.md` "서명" 절의 전제(권한을 하나도 안 써서
/// 자체 서명 인증서가 아직 필요 없다)가 이 기능으로 깨지지 않는다.
///
/// **화면 기록 권한을 받아 제목까지 얻는 길은 버렸다.** 그 순간
/// `make-signing-cert.sh` 가 필수가 되고, Sequoia 이후로는 주기적으로 다시 묻는
/// 창까지 딸려 온다. 창 하나 고르자고 치를 값이 아니다.
@MainActor
enum WindowSurvey {
    // MARK: - 창 목록

    /// 지금 화면에 떠 있는 **보통 앱 창들.** 앞에 있는 것이 먼저 온다.
    ///
    /// **`.optionAll` 은 절대 쓰지 않는다.** 53개가 385개가 되는데 그중 대부분은
    /// 다른 스페이스에 있거나 한 번도 안 뜬 유령 창이고, `kCGWindowIsOnscreen` 키가
    /// 아예 없어서 걸러낼 수도 없다. 게다가 **앞뒤 순서가 보장되는 것도, 지금 스페이스만
    /// 나오는 것도 `.optionOnScreenOnly` 일 때뿐이다** — 붙어 있던 창이 다른 스페이스로
    /// 넘어간 것을 이 목록에서 사라지는 것으로 알아챈다.
    static func onScreenWindows() -> WindowList {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let raw = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]]
        else { return [] }

        let ownPID = Int(ProcessInfo.processInfo.processIdentifier)
        var found: [(id: CGWindowID, frame: CGRect, owner: String)] = []
        for entry in raw {
            // 보통 앱 창만. 이 한 줄로 배경화면·Dock·메뉴 막대·알림 센터·제어 센터가
            // 통째로 빠진다.
            guard entry[kCGWindowLayer as String] as? Int == 0 else { continue }
            // **`== true` 로 못 박는다.** 기본값을 참으로 두면 언젠가 목록 종류를
            // 바꿨을 때 키가 없는 유령 창이 전부 통과한다.
            guard entry[kCGWindowIsOnscreen as String] as? Bool == true else { continue }
            guard (entry[kCGWindowAlpha as String] as? Double ?? 1) > 0.01 else { continue }
            // **우리 창을 빼는 것은 이 줄뿐이다.** HUD 패널은 layer 3 이라 위에서 저절로
            // 빠지지만 **설정 창은 보통 창이라 layer 0 으로 뜬다** — 이게 없으면 설정 창을
            // 여는 순간 펫이 제 설정 창에 매달린다.
            guard entry[kCGWindowOwnerPID as String] as? Int != ownPID else { continue }
            // **이름으로도 한 번 더 뺀다.** pid 만 보면 같은 앱의 **다른 프로세스**가
            // 띄운 창이 남는다 — 진단 통로(`--probe-perch`)는 앱과 따로 도는 프로세스라
            // 떠 있는 펫과 설정 창을 남의 창으로 보고 표에 올렸다.
            guard entry[kCGWindowOwnerName as String] as? String != AppInfo.name else { continue }
            guard let id = entry[kCGWindowNumber as String] as? CGWindowID,
                  let bounds = entry[kCGWindowBounds as String] as? [String: CGFloat],
                  let rect = rect(from: bounds)
            else { continue }
            // 띠·조각 창은 뺀다. 한 앱이 진짜 창(1920x1050)과 띠(1920x32)를 둘 다
            // layer 0 으로 올리는 일이 흔하고, 띠에 붙으면 붙은 게 아니라 걸쳐진 것으로
            // 보인다.
            guard rect.width >= minimumWindowSize.width,
                  rect.height >= minimumWindowSize.height
            else { continue }
            // 앱 이름은 붙는 데 안 쓰지만 `--probe-perch` 가 사람에게 보여준다.
            // **창 제목(`kCGWindowName`)이 아니다** — 그건 권한이 걸린다.
            let owner = entry[kCGWindowOwnerName as String] as? String ?? "?"
            found.append((id, toAppKit(rect), owner))
        }
        return found
    }

    /// 그 창이 아직 지금 스페이스에 떠 있으면 자리와 **맨 앞인지**. 닫혔거나
    /// 최소화됐거나 다른 스페이스로 갔으면 nil.
    ///
    /// **창 하나만 집어 묻는 `CGWindowListCreateDescriptionFromArray` 를 안 쓴다.**
    /// 그쪽은 스페이스와 무관하게 대답해서, 다른 스페이스로 넘어간 창을 여전히
    /// 살아 있는 것으로 돌려준다 — 펫이 아무것도 없는 허공에 매달린 채로 남는다.
    /// 전체 목록은 한 번에 0.3ms 도 안 걸려서 아낄 값어치가 없다.
    ///
    /// **맨 앞인지가 같이 필요하다.** 붙어 있는 펫은 그 창과 같은 층에서 보여야 하는데,
    /// 그 창이 앞으로 왔는지는 목록 순서로만 알 수 있다(`.optionOnScreenOnly` 가
    /// 앞에서 뒤 순서를 보장한다). 자리를 재는 것과 같은 조회라 공짜다.
    static func locate(_ window: CGWindowID) -> (frame: CGRect, isFront: Bool)? {
        locate(window, in: onScreenWindows())
    }

    /// **이미 떠 온 목록으로 답한다.** 한 틱 안에서 자리와 묻힘을 같이 봐야 하는데,
    /// 각자 목록을 뜨면 같은 답을 두 번 산다 — 붙어 있는 내내 그 값을 낸다.
    static func locate(_ window: CGWindowID, in list: WindowList) -> (frame: CGRect, isFront: Bool)? {
        guard let index = list.firstIndex(where: { $0.id == window }) else { return nil }
        return (list[index].frame, index == 0)
    }

    /// 왜 안 붙었는지 창·변마다 한 줄씩. **진단에만 쓴다.**
    ///
    /// `snap` 은 되는 자리 하나만 돌려주고 나머지는 조용히 버린다. 안 붙는다는 말을
    /// 들었을 때 그 "조용히" 가 문제라, 같은 걸러내기를 순서대로 다시 걸으면서
    /// 어디서 걸렸는지 남긴다.
    static func explain(
        mascot: CGRect, within limit: CGFloat,
        sink: (PerchSpot) -> CGFloat,
        placeable: (PerchSpot) -> Bool
    ) -> [String] {
        let list = onScreenWindows()
        guard !list.isEmpty else { return ["창이 하나도 안 잡혔다"] }
        var lines: [String] = []
        for (rank, window) in list.enumerated() {
            var parts: [String] = []
            for edge in [MascotPerch.top, .bottom, .left, .right] {
                let name = label(edge)
                guard let distance = gap(from: mascot, to: window.frame, edge: edge) else {
                    parts.append("\(name) 빗나감")
                    continue
                }
                guard distance <= limit else {
                    parts.append("\(name) \(Int(distance))pt 떨어짐")
                    continue
                }
                guard let spot = PerchSpot(
                    window: window.id, edge: edge,
                    offset: offset(of: mascot, on: window.frame, edge: edge),
                    windowFrame: window.frame
                ).clamped(to: window.frame, mascot: mascot.size) else {
                    parts.append("\(name) 모서리가 짧음")
                    continue
                }
                if isCovered(spot, mascot: mascot.size, sink: sink(spot), by: list[..<rank]) {
                    parts.append("\(name) 가려짐")
                } else if !placeable(spot) {
                    parts.append("\(name) 화면 밖")
                } else {
                    parts.append("\(name) **가능(\(Int(distance))pt)**")
                }
            }
            lines.append("  \(window.owner) [\(window.id)] \(box(window.frame)) — "
                         + parts.joined(separator: " · "))
        }
        return lines
    }

    private static func label(_ edge: MascotPerch) -> String {
        switch edge {
        case .top: return "위"
        case .bottom: return "아래"
        case .left: return "왼쪽"
        case .right: return "오른쪽"
        }
    }

    private static func box(_ rect: CGRect) -> String {
        String(format: "(%.0f,%.0f) %.0fx%.0f", rect.minX, rect.minY, rect.width, rect.height)
    }

    /// 붙어 있는 자리가 지금 **남의 창에 묻혔는지.**
    ///
    /// **붙일 때만 보던 것을 붙어 있는 동안에도 본다.** 붙은 창이 다른 창에 가려지면
    /// 펫도 같이 가려져야 하는데(그래서 붙는 동안 `.normal` 층으로 내려간다), 앞으로
    /// 끌어올리는 코드가 그걸 뒤집어 놓는다 — **아무것도 없는 자리에 매달린 것으로
    /// 보인다.** 전체화면 창 위에 펫이 떠 있는 것이 그래서 생겼다.
    static func isBuried(_ spot: PerchSpot, mascot: CGSize, sink: CGFloat) -> Bool {
        isBuried(spot, mascot: mascot, sink: sink, in: onScreenWindows())
    }

    /// 이미 떠 온 목록으로 답하는 쪽. `locate` 와 같은 틱이면 이걸 쓴다.
    static func isBuried(
        _ spot: PerchSpot, mascot: CGSize, sink: CGFloat, in list: WindowList
    ) -> Bool {
        guard let rank = list.firstIndex(where: { $0.id == spot.window }) else { return false }
        return isCovered(spot, mascot: mascot, sink: sink, by: list[..<rank])
    }

    /// 우리 창이 그 창보다 **앞에 있는지.** 둘 중 하나라도 못 찾으면 nil.
    ///
    /// **`onScreenWindows()` 로는 못 본다** — 거기는 우리 PID 를 통째로 걸러낸다.
    /// 그래서 여기서만 걸러내지 않은 목록을 본다.
    ///
    /// 이게 필요한 이유: 붙어 있는 동안 펫은 `.normal` 층으로 내려가 있는데, 사용자가
    /// **이미 맨 앞인 창을 한 번 더 누르면** OS 가 그 창을 우리 위로 올린다. 그때는
    /// "뒤→앞 전이" 가 없어서 `raisePerched` 의 조건에 안 걸리고, 창 안으로 넘어간
    /// 다리·날개가 그대로 창 뒤에 묻힌다 — **잡고 있는 것으로 안 보인다.**
    static func isAhead(_ mine: CGWindowID, of other: CGWindowID) -> Bool? {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let raw = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]]
        else { return nil }
        var mineRank: Int?
        var otherRank: Int?
        for (rank, entry) in raw.enumerated() {
            guard let id = entry[kCGWindowNumber as String] as? CGWindowID else { continue }
            if id == mine, mineRank == nil { mineRank = rank }
            if id == other, otherRank == nil { otherRank = rank }
            if mineRank != nil, otherRank != nil { break }
        }
        guard let mineRank, let otherRank else { return nil }
        return mineRank < otherRank
    }

    /// 놓은 자리가 테두리에서 이만큼 안이면 붙는다. **그림 크기를 따라간다.**
    ///
    /// 한동안 40pt 로 못 박아 뒀는데 조준을 너무 잘해야 했다 — 기록을 보니 42pt ·
    /// 46pt 처럼 **몇 pt 차이로 놓치는 일**이 잦았다. 게다가 붙잡는 부위가 창 안으로
    /// 넘어가면서 실제 착지 지점이 12~15pt 안쪽으로 옮겨졌는데 문턱은 그대로여서,
    /// 조준 범위가 한쪽으로 쏠려 있었다.
    ///
    /// **그림 높이의 4/5.** 배율을 키우면 그림이 커지는 만큼 조준도 편해져야 한다.
    /// 절대값으로 두면 큰 그림에서 상대적으로 더 정확히 겨눠야 한다.
    static func snapDistance(mascot: CGSize) -> CGFloat {
        max(mascot.width, mascot.height) * 0.8
    }

    /// 이보다 작은 창에는 안 붙는다.
    private static let minimumWindowSize = CGSize(width: 120, height: 80)

    // MARK: - 붙을 자리 고르기

    /// 마스코트를 놓은 자리에서 가장 가까운 창 테두리. 닿는 것이 없으면 nil.
    ///
    /// **놓은 자리에서만 찾는다.** 화면을 통째로 뒤져 "제일 그럴듯한" 창을 고르는 길은
    /// 버렸다 — 사용자가 여기 놓겠다고 끌어다 놓은 것이라, 우리가 다른 자리를 더 좋게
    /// 볼 이유가 없다.
    ///
    /// - Parameters:
    ///   - mascot: 그림이 실제로 덮는 자리(화면 좌표). **창이 아니다** — 펫의 창은
    ///     링만큼 커서 그것으로 재면 아직 한참 떨어져 있는데도 붙는다.
    ///   - limit: 이 거리 안이어야 붙는다.
    ///   - windows: 창 목록. 비워 두면 지금 화면을 읽는다 — 검사에서만 넣어 준다.
    ///   - sink: 그 변에 붙었을 때 붙잡는 부위가 **창 안으로 넘어가는 깊이**(pt).
    ///     그림 사정을 여기서 알 수 없어서 뷰 쪽이 재서 숫자로 건네준다
    ///     (`UsageHUDView.petPerchSink`). 안 주면 0 — 예전처럼 테두리에 딱 맞춘다.
    ///     **변이 아니라 자리(`PerchSpot`)를 받는다** — 화면 가장자리에서는 자리마다
    ///     더 깊이 들어가야 해서 깊이가 접점에 따라 달라진다.
    ///   - placeable: 그 자리에 **실제로 놓을 수 있는지.** 자리 계산(`petPerchOrigin`)이
    ///     화면 밖이라 거절하는 변이 있는데, 그걸 여기서 안 물어보면 후보로 골라 놓고
    ///     나중에 못 놓는다 — 사용자 눈에는 **아무 데도 안 붙는 것**으로 보인다.
    ///     걸러 내면 그 다음으로 가까운 변으로 넘어간다.
    static func snap(
        mascot: CGRect, within limit: CGFloat,
        sink: (PerchSpot) -> CGFloat = { _ in 0 },
        placeable: (PerchSpot) -> Bool = { _ in true },
        windows: [(id: CGWindowID, frame: CGRect, owner: String)]? = nil
    ) -> PerchSpot? {
        let list = windows ?? onScreenWindows()
        var best: (spot: PerchSpot, distance: CGFloat)?
        for (rank, window) in list.enumerated() {
            for edge in [MascotPerch.top, .bottom, .left, .right] {
                guard let distance = gap(from: mascot, to: window.frame, edge: edge),
                      distance <= limit
                else { continue }
                // **가려진 테두리에는 안 붙는다.** 목록이 앞에서 뒤 순서라 자기보다 앞에
                // 있는 창만 보면 된다. 이걸 안 보면 다른 창에 덮여 **보이지도 않는 창**의
                // 테두리에 붙어서, 사용자 눈에는 아무것도 없는 자리에 매달린 것으로 보인다.
                //
                // 한동안 이 판정을 뺐다 — "사용자가 직접 끌어다 놓는 것이라 우리가 자리를
                // 고를 이유가 없다"고 봤는데 거꾸로였다. **놓는 사람은 보이는 것에 겨냥한다.**
                // 그 자리에 가려진 창의 테두리가 숨어 있으면 겨냥하지 않은 것에 붙는다.
                //
                // 판정은 아래에서 오프셋을 확정한 뒤에 한다 — **놓은 자리로 재야 한다.**
                // 모서리 가운데로 재면 긴 창에서 엉뚱한 자리의 가림을 보게 된다.
                // **더 가까운 것만 이긴다.** 같으면 먼저 본 것 — 목록이 앞에 있는 창부터
                // 오므로, 겹쳐 있는 두 창의 같은 자리에서는 사용자가 보고 있는 쪽이 된다.
                if let best, distance >= best.distance { continue }
                // **여기서 오프셋을 가둔다.** 안 가두면 모서리 맨 끝에 놓았을 때 그림
                // 절반이 창 밖으로 나간 자리에 그대로 앉고, 뒤이은 첫 추적 틱이
                // `clamped` 로 밀어 넣어 **50ms 뒤에 42pt 옆으로 튄다** — 미리보기와
                // 착지는 맞았는데 곧 옮겨가는 것으로 보인다. 미리보기·착지·추적이
                // 한 셈에서 나오게 이 자리에서 한 번만 가둔다.
                guard let spot = PerchSpot(
                    window: window.id,
                    edge: edge,
                    offset: offset(of: mascot, on: window.frame, edge: edge),
                    windowFrame: window.frame
                ).clamped(to: window.frame, mascot: mascot.size) else { continue }
                guard !isCovered(
                    spot, mascot: mascot.size, sink: sink(spot), by: list[..<rank]
                ) else { continue }
                // **놓을 수 있는지 마지막에 묻는다.** 앞의 걸러내기를 다 통과해도 화면
                // 가장자리라 자리가 안 나오는 변이 있다 — 창 위 테두리가 화면 꼭대기에
                // 가까우면 그 위에 앉을 자리가 없다.
                guard placeable(spot) else { continue }
                best = (spot, distance)
            }
        }
        return best?.spot
    }

    /// 그 모서리까지 얼마나 떨어져 있는지. 나란한 방향으로 아예 빗나가 있으면 nil.
    ///
    /// 재는 것은 **닿아야 할 변까지의 거리**다. 위 테두리에 앉으려면 그림의 발이,
    /// 아래 테두리에 매달리려면 손이 닿아야 해서 변마다 보는 쪽이 다르다.
    private static func gap(from mascot: CGRect, to window: CGRect, edge: MascotPerch) -> CGFloat? {
        switch edge {
        case .top, .bottom:
            // 모서리가 그림보다 짧으면 붙은 게 아니라 걸쳐진 것으로 보인다.
            guard window.width >= mascot.width else { return nil }
            // 가로로 창을 벗어난 자리에는 안 붙는다. 그림 한가운데가 기준이다.
            guard mascot.midX >= window.minX, mascot.midX <= window.maxX else { return nil }
            return edge == .top
                ? abs(mascot.minY - window.maxY)
                : abs(mascot.maxY - window.minY)
        case .left, .right:
            guard window.height >= mascot.height else { return nil }
            guard mascot.midY >= window.minY, mascot.midY <= window.maxY else { return nil }
            return edge == .right
                ? abs(mascot.minX - window.maxX)
                : abs(mascot.maxX - window.minX)
        }
    }

    /// 붙을 자리가 **더 앞에 있는 창**에 덮여 있는지.
    ///
    /// 재는 것은 그림이 놓일 자리에 테두리 안쪽 몇 pt 를 더한 것이다. 안쪽을 같이 봐야
    /// **붙을 테두리 자체가 가려진 것**이 걸린다 — 그림이 놓일 자리만 보면, 테두리는
    /// 남의 창에 덮였는데 그 바깥은 비어 있는 자리가 통과한다.
    private static func isCovered(
        _ spot: PerchSpot, mascot: CGSize, sink: CGFloat,
        by inFront: ArraySlice<(id: CGWindowID, frame: CGRect, owner: String)>
    ) -> Bool {
        guard !inFront.isEmpty else { return false }
        let landing = landingArea(spot, mascot: mascot, sink: sink)
            .insetBy(dx: -edgePeek, dy: -edgePeek)
        return inFront.contains { $0.frame.intersects(landing) }
    }

    /// 그 자리에 붙었을 때 그림이 덮을 자리(대략).
    ///
    /// **알맹이가 아니라 상자로 잰다.** 가림을 보려는 것이라 넉넉한 쪽이 안전하고,
    /// 자세별 알맹이는 뷰 쪽(`UsageHUDView`)만 아는 값이라 여기로 끌어올 수 없다.
    ///
    /// 진단 통로(`--probe-perch`)도 이걸 쓴다 — 거기서 따로 셈하면 표가 실제와 어긋난다.
    ///
    /// `sink` 만큼 **창 안쪽으로 밀어 놓는다.** 실제 자리(`petPerchOrigin`)가 그만큼
    /// 들어가 있어서, 여기만 바깥에 두면 진단 통로의 `landingArea → snap` 왕복이
    /// 앱과 다른 답을 낸다.
    static func landingArea(_ spot: PerchSpot, mascot: CGSize, sink: CGFloat = 0) -> CGRect {
        let contact = spot.contact(in: spot.windowFrame)
        switch spot.edge {
        case .top:
            return CGRect(x: contact.x - mascot.width / 2, y: contact.y - sink,
                          width: mascot.width, height: mascot.height)
        case .bottom:
            return CGRect(x: contact.x - mascot.width / 2, y: contact.y - mascot.height + sink,
                          width: mascot.width, height: mascot.height)
        case .right:
            return CGRect(x: contact.x - sink, y: contact.y - mascot.height / 2,
                          width: mascot.width, height: mascot.height)
        case .left:
            return CGRect(x: contact.x - mascot.width + sink, y: contact.y - mascot.height / 2,
                          width: mascot.width, height: mascot.height)
        }
    }

    /// 테두리 안쪽으로 이만큼까지 남의 창이 덮고 있으면 가려진 것으로 본다.
    private static let edgePeek: CGFloat = 6

    /// 모서리 시작점에서 얼마나 떨어진 자리에 붙었는지.
    private static func offset(of mascot: CGRect, on window: CGRect, edge: MascotPerch) -> CGFloat {
        switch edge {
        case .top, .bottom: return mascot.midX - window.minX
        case .left, .right: return mascot.midY - window.minY
        }
    }

    // MARK: - 좌표

    /// 화면 구성이 바뀌면 버린다. `NSApplication.didChangeScreenParametersNotification`.
    static func invalidateScreens() { cachedPrimaryHeight = nil }

    private static var cachedPrimaryHeight: CGFloat?

    /// 주 화면 높이. **`NSScreen.main` 이 아니다** — 그건 키보드 포커스가 있는 화면이라
    /// 창을 다른 모니터에서 쓰는 동안 기준이 통째로 달라진다.
    /// (`HUDController.defaultOrigin()` 이 같은 이유로 `NSScreen.screens.first` 를 쓴다.)
    private static var primaryHeight: CGFloat {
        if let cachedPrimaryHeight { return cachedPrimaryHeight }
        let height = CGDisplayBounds(CGMainDisplayID()).height
        cachedPrimaryHeight = height
        return height
    }

    /// Quartz 좌표를 AppKit 좌표로.
    ///
    /// 창 목록은 **좌상단이 원점**이고 원점 자체가 주 화면 왼쪽 위다. `NSWindow`·`NSScreen`
    /// 은 좌하단이 원점이다. 가로는 그대로고 **세로만 주 화면 높이 하나로 뒤집으면**
    /// 모니터가 몇 대든 맞는다 — 보조 화면은 음수 좌표로 정상적으로 나온다.
    ///
    /// **배율로 나누지 마라.** `bounds` 는 픽셀이 아니라 포인트라 AppKit 과 같은 단위다.
    private static func toAppKit(_ rect: CGRect) -> CGRect {
        CGRect(
            x: rect.minX,
            y: primaryHeight - rect.minY - rect.height,
            width: rect.width,
            height: rect.height
        )
    }

    private static func rect(from bounds: [String: CGFloat]) -> CGRect? {
        guard let x = bounds["X"], let y = bounds["Y"],
              let width = bounds["Width"], let height = bounds["Height"]
        else { return nil }
        return CGRect(x: x, y: y, width: width, height: height)
    }
}

/// 창 테두리 어디에 붙어 있는지.
struct PerchSpot: Equatable {
    /// 붙은 창. **번호로 따라간다** — pid 로 잡으면 창을 여럿 띄운 앱에서 어느 것인지
    /// 알 수 없고, 자리로 잡으면 창을 옮기는 순간 다른 창이 된다.
    let window: CGWindowID
    let edge: MascotPerch
    /// 모서리 시작점(창의 왼쪽 또는 아래)에서 떨어진 거리(pt).
    ///
    /// **비율(0~1)로 기억하는 길은 버렸다.** 창을 옆으로 넓히면 가만히 있어야 할 펫이
    /// 스르륵 미끄러진다. 절대 거리로 두면 창을 넓혀도 제자리고, 좁혀서 모서리가
    /// 짧아졌을 때만 끌려온다 — 그쪽이 자연스럽다.
    var offset: CGFloat
    /// 마지막으로 본 창 자리(AppKit 좌표).
    var windowFrame: CGRect

    /// 그 창에서 붙는 지점(화면 좌표). 그림의 **닿는 변**이 여기에 온다.
    func contact(in window: CGRect) -> CGPoint {
        switch edge {
        case .top: return CGPoint(x: window.minX + offset, y: window.maxY)
        case .bottom: return CGPoint(x: window.minX + offset, y: window.minY)
        case .left: return CGPoint(x: window.minX, y: window.minY + offset)
        case .right: return CGPoint(x: window.maxX, y: window.minY + offset)
        }
    }

    /// 창이 좁아져서 모서리 밖으로 나간 오프셋을 안으로 끌어온다.
    /// 모서리가 그림보다 짧아졌으면 nil — 그때는 붙어 있을 자리가 없다.
    func clamped(to window: CGRect, mascot: CGSize) -> PerchSpot? {
        var moved = self
        moved.windowFrame = window
        switch edge {
        case .top, .bottom:
            guard window.width >= mascot.width else { return nil }
            moved.offset = min(max(offset, mascot.width / 2), window.width - mascot.width / 2)
        case .left, .right:
            guard window.height >= mascot.height else { return nil }
            moved.offset = min(max(offset, mascot.height / 2), window.height - mascot.height / 2)
        }
        return moved
    }
}
