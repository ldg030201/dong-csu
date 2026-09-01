import SwiftUI

/// HUD가 보기마다 그리는 것들. `UsageHUDView.draws(_:in:)` 가 어느 보기에
/// 그려지는지 답하고, 설정 창은 그걸 보고 토글을 잠근다.
enum HUDElement {
    /// 아래 줄의 CPU · 메모리.
    case processStats
    /// 제일 안쪽 모델별 링.
    case scopedRing
    /// 모서리 버전 딱지.
    case versionBadge
}

/// 오른쪽 위에 떠 있는 사용량 HUD.
/// 왼쪽: 링(바깥부터 세션 · 주간 · 모델별) + 가운데 Claude 마크.
/// 모델별 링은 설정에서 켰을 때만 그린다.
/// 오른쪽: 세션 / 주간 사용률과 초기화까지 남은 시간.
/// 오른쪽 아래: 다음 사용량 조회까지 남은 시간(초 단위).
struct UsageHUDView: View {
    @ObservedObject var store: UsageStore
    var iconStyle: ClaudeIconStyle = .default
    /// HUD가 숨겨져 있으면 1초 타이머가 돌 이유가 없다.
    var showsCountdown: Bool = true
    /// 얼마나 보여줄지. 링만 남기거나(collapsed) 마스코트만 남긴다(pet).
    var mode: HUDMode = .expanded
    /// 펫 모드에서 마우스가 위에 있는지. `petRingDisplay`가 `.hover`일 때만 쓰인다.
    var isHovered: Bool = false
    /// 펫 모드에서 뒤에 두르는 링을 언제 보여줄지.
    var petRingDisplay: PetRingDisplay = .default
    /// 지금 집어 들려 있는지.
    var isHeld: Bool = false
    /// 들고 있는 동안 링·버튼 줄을 감출지.
    var hidesRingWhileHeld: Bool = true
    /// 모델별 한도(예: Fable)를 링과 줄에 같이 보여줄지. 기본 꺼짐.
    var showsScopedLimit: Bool = false
    var palette = HUDPalette(isDark: true)
    /// 설정 창 열기. HUDController가 꽂아준다.
    var onOpenSettings: (() -> Void)?
    /// 측정 화면을 연다. **여기서 바로 재기 시작하지 않는다** — 손이 스치면 재던 것이
    /// 끊기고, 무엇이 시작됐는지도 화면에 안 보인다.
    var onOpenMeasure: (() -> Void)?
    /// 지금 재는 중인지. 버튼 모양이 이걸 따른다.
    var isMeasuring = false
    /// 접기/펼치기 토글.
    var onToggleCollapse: (() -> Void)?
    /// 펼쳐지는 방향. 손잡이(링·버튼)가 붙는 쪽이 반대편이 된다.
    var expandSide: HUDExpandSide = .default
    /// 왼쪽 아래에 이 앱의 CPU·메모리를 표시할지.
    var usageMonitor: ProcessUsageMonitor?
    /// HUD 전체 배율. 치수와 글자 크기에 곱한다.
    var scale: CGFloat = 1
    /// 새 버전이 나와 있으면 버튼 반대편 위 모서리에 표시를 띄운다.
    var showsUpdateBadge: Bool = false
    /// 같은 모서리에 붙일 버전 딱지. nil이면 그리지 않는다.
    var versionBadge: String?
    /// 그 딱지를 테스트판 색으로 그릴지. 렌더 통로가 실제 빌드와 무관하게 넘길 수 있게
    /// `AppInfo`를 직접 읽지 않고 받는다.
    var versionBadgeIsTest: Bool = false
    /// 그 표시를 눌렀을 때. 버전 화면을 연다.
    var onOpenUpdates: (() -> Void)?
    /// 가운데 부엉이를 움직이게 할 애니메이터. 없으면 정지 자세로 그린다.
    var owlAnimator: OwlAnimator?

    /// 배율 1 기준 길이를 실제 길이로.
    private func s(_ value: CGFloat) -> CGFloat { value * scale }

    /// 배율 1 기준 글자 크기를 실제 폰트로.
    private func font(
        _ size: CGFloat,
        weight: Font.Weight = .regular,
        design: Font.Design = .default
    ) -> Font {
        .system(size: size * scale, weight: weight, design: design)
    }

    // 아래는 모두 배율 1 기준 치수다. 실제 값은 scale을 곱해서 쓴다.
    static let baseExpandedSize = CGSize(width: 240, height: 88)
    /// 자원 사용량 줄을 붙일 때 늘어나는 높이.
    static let baseStatsRowHeight: CGFloat = 17
    /// 접은 모습: 링 + 오른쪽에 버튼 세 개가 세로로 붙는다.
    static let baseCollapsedSize = CGSize(width: 108, height: 88)
    /// 펫 마스코트의 높이. 펫에서는 캐릭터가 주인공이라 크게 잡는다.
    static let basePetOwlHeight: CGFloat = 84
    /// 뒤에 두르는 링의 바깥 지름. 마스코트가 링 안쪽에 여유 있게 들어가야 한다.
    /// 안쪽 지름 = 바깥 − 두께 2겹(10) − 간격(7) 이고, 마스코트 폭은 높이 × 15/13 이다.
    /// **선 굵기(5)까지 더해 128 이 되는 값이다.** 124 로 두면 바깥 선이 창 밖으로
    /// 반 pt 나가서 링 위아래가 아주 얇게 깎인다.
    static let basePetRingDiameter: CGFloat = 123
    static let basePetRingLineWidth: CGFloat = 5
    /// 창은 링을 담을 만큼. 링을 감추고 있을 때도 크기는 그대로다 —
    /// 호버할 때 창을 늘리면 커서가 창 밖으로 밀려나 호버가 끊긴다.
    /// 링 아래에 붙는 버튼 줄의 높이.
    static let basePetButtonRow: CGFloat = 32

    /// 창은 링을 담을 만큼 + 아래 버튼 줄. 링을 감추고 있을 때도 크기는 그대로다 —
    /// 호버할 때 창을 늘리면 커서가 창 밖으로 밀려나 호버가 끊긴다.
    static let basePetSize = CGSize(width: 128, height: 128 + basePetButtonRow)

    /// 모델별 한도 줄 하나가 차지하는 높이. 세션 · 주간 줄과 같은 몫이다.
    /// (값 20 + 남은 시간 11 + 줄 사이 8 + 여백)
    static let baseScopedRowHeight: CGFloat = 46

    static func size(
        mode: HUDMode, showsStats: Bool = false, showsScopedLimit: Bool = false,
        scale: CGFloat = 1
    ) -> CGSize {
        func scaled(_ size: CGSize) -> CGSize {
            CGSize(width: size.width * scale, height: size.height * scale)
        }
        // 모델별 링이 붙어 링이 커진 만큼 카드도 넓어진다.
        let grown = showsScopedLimit ? baseScopedGrowth : 0
        switch mode {
        case .pet: return scaled(basePetSize)
        // 접은 카드에는 숫자가 없어서 자원 사용량 줄은 안 붙는다. 링은 그리므로
        // 링이 커지면 카드도 가로·세로로 같이 커진다.
        case .collapsed:
            return scaled(CGSize(
                width: baseCollapsedSize.width + grown,
                height: baseCollapsedSize.height + grown
            ))
        case .expanded:
            // 윗줄(링·숫자) + 아래 자원 사용량 줄.
            var height = expandedRowHeight(showsScopedLimit: showsScopedLimit, scale: scale)
            if showsStats { height += baseStatsRowHeight * scale }
            return CGSize(width: (baseExpandedSize.width + grown) * scale, height: height)
        }
    }

    /// 링 바깥 지름. 둘일 때와, 모델별이 붙어 셋일 때.
    ///
    /// **셋이면 키워야 한다.** 62 안에 셋을 넣으면 마스코트 자리가 12pt 밖에 안 남아
    /// 무슨 그림인지 안 보인다.
    static let baseRingDiameter: CGFloat = 62
    static let baseScopedRingDiameter: CGFloat = 84

    /// 모델별 링이 붙으면 링이 이만큼 커진다. **카드도 가로·세로로 같이 이만큼 넓힌다.**
    ///
    /// 세로만 넓히고 가로를 그대로 두면 커진 링이 옆의 글자 자리를 그만큼 먹는다.
    /// 창은 안 줄었는데 숫자가 밀려서 버튼 밑으로 파고든다 — 눈에는 "가로가 줄어든"
    /// 것처럼 보인다.
    static var baseScopedGrowth: CGFloat { baseScopedRingDiameter - baseRingDiameter }

    static func ringDiameter(showsScopedLimit: Bool, scale: CGFloat) -> CGFloat {
        (showsScopedLimit ? baseScopedRingDiameter : baseRingDiameter) * scale
    }

    /// 링 바깥을 두르는 선의 굵기. 지름 밖으로 이만큼 더 나간다.
    static func ringLineWidth(scale: CGFloat) -> CGFloat { 6 * scale }
    /// 안쪽 링들의 선 굵기.
    static func ringInnerLineWidth(scale: CGFloat) -> CGFloat { 5 * scale }
    /// 링과 링 사이에 두는 틈.
    static func ringGap(scale: CGFloat) -> CGFloat { 7 * scale }

    /// 펼친 카드의 좌우 여백과 링 · 글자 사이 틈.
    ///
    /// **여기에만 적는다.** 그리는 쪽과 진단 통로가 각자 숫자를 들고 있으면, 여백을
    /// 고쳤을 때 진단은 옛 숫자로 재면서 통과시킨다.
    static func expandedLeading(scale: CGFloat) -> CGFloat { 13 * scale }
    static func expandedGap(scale: CGFloat) -> CGFloat { 13 * scale }
    static func expandedTrailing(scale: CGFloat) -> CGFloat { 10 * scale }

    /// 접은 카드의 링 왼쪽 여백과 링 · 버튼 열 사이 틈.
    static func collapsedLeading(scale: CGFloat) -> CGFloat { 12 * scale }
    static func collapsedGap(scale: CGFloat) -> CGFloat { 8 * scale }

    /// 접은 카드에서 링 왼쪽 여백 + 오른쪽 버튼 열이 먹는 폭.
    static func collapsedChrome(scale: CGFloat) -> CGFloat {
        collapsedLeading(scale: scale) + collapsedGap(scale: scale)
            + refreshHitSize(scale: scale) + collapsedTrailing(scale: scale)
    }

    /// 펼친 카드에서 링 · 숫자가 놓이는 윗줄의 높이. 모델별 줄이 붙으면 그만큼 커진다.
    ///
    /// **`size()` 도 마스코트 판정 자리도 다 여기서 가져간다.** 따로 유도하면
    /// 그림과 판정이 어긋나서, 링 가장자리를 더블클릭해도 펫으로 안 들어간다.
    static func expandedRowHeight(showsScopedLimit: Bool, scale: CGFloat) -> CGFloat {
        (baseExpandedSize.height + (showsScopedLimit ? baseScopedRowHeight : 0)) * scale
    }

    /// 링을 겹쳐 놓았을 때 안쪽 링들의 지름과, 가운데에 남는 자리.
    ///
    /// **겹치는 규칙이 여기 하나뿐이어야 한다.** 그리는 쪽과 가운데 그림 크기를 재는
    /// 쪽이 따로 세면, 틈을 한 번 고쳤을 때 마스코트가 제일 안쪽 링을 파고든다.
    static func ringLayout(
        outer: CGFloat, outerWidth: CGFloat, innerWidth: CGFloat,
        hasScoped: Bool, scale: CGFloat
    ) -> (inner: CGFloat, third: CGFloat, free: CGFloat) {
        let gap = ringGap(scale: scale)
        let inner = innerDiameter(outer: outer, outerWidth: outerWidth, gap: gap)
        let third = innerDiameter(outer: inner, outerWidth: innerWidth, gap: gap)
        // 가운데 그림이 들어갈 자리. 제일 안쪽 링의 선 안쪽에서 조금 더 물러선다.
        let innermost = hasScoped ? third : inner
        return (inner, third, innermost - innerWidth * 2 - 4 * scale)
    }

    /// 이 보기가 실제로 그리는가.
    ///
    /// **설정 창이 토글을 잠글지 여기서 정한다.** 두 자리에 따로 적으면 반드시
    /// 어긋난다 — 모델별 링이 실제로 그 꼴이었다. 세 보기에 다 그려지는데 토글은
    /// 펼친 카드에서만 열려서, 접어 놓은 사람은 링이 눈앞에 보이는데도 못 껐다.
    ///
    /// 반대쪽도 똑같이 나쁘다. 안 그리는데 토글이 열려 있으면 눌러도 아무 일이 없다.
    static func draws(_ element: HUDElement, in mode: HUDMode) -> Bool {
        switch element {
        // 아래 줄은 펼친 카드에만 있다.
        case .processStats: return mode == .expanded
        // 링은 세 보기에 다 그린다.
        case .scopedRing: return true
        // 접은 카드는 링에 겹쳐서 안 붙이고, 펫에는 카드 자체가 없다.
        case .versionBadge: return mode == .expanded
        }
    }

    static func cornerRadius(mode: HUDMode, scale: CGFloat = 1) -> CGFloat {
        switch mode {
        // 배경이 없으니 깎을 모서리도 없다. 남겨 두면 링 가장자리가 잘린다.
        case .pet: return 0
        case .collapsed: return 26 * scale
        case .expanded: return 20 * scale
        }
    }

    /// 펫 모드에서 클릭·호버를 받는 자리. 창 가운데의 링만큼이다.
    ///
    /// 창 전체를 받으면 투명한 네 귀퉁이에서도 클릭이 먹혀서 뒤에 있는 창을 누를 수 없다.
    /// 반대로 마스코트 크기로만 잡으면, 호버해서 링이 뜬 순간 커서를 링 쪽으로 조금만
    /// 옮겨도 영역을 벗어나 링이 사라진다.
    static func petHitRect(scale: CGFloat) -> CGRect {
        let panel = size(mode: .pet, scale: scale)
        let side = basePetRingDiameter * scale
        let row = basePetButtonRow * scale
        // 뷰 좌표는 아래가 0이다. 버튼 줄이 아래에 깔리고 링은 그 **위** 영역의 가운데다.
        return CGRect(
            x: (panel.width - side) / 2,
            y: row + (panel.height - row - side) / 2,
            width: side,
            height: side
        )
    }

    /// 펫 모드에서 새 버전 배지가 앉는 자리 — **창 오른쪽 위**.
    ///
    /// 링(원)의 바깥 모서리라 마스코트를 가리지 않는다. 다만 커서 피하기를 거는
    /// `petHitRect`(사각형)와는 겹치므로, 여기 마우스가 올라오면 도망을 막아야 한다.
    /// `HUDController` 가 이 자리에 추적 영역을 따로 걸어 그렇게 한다.
    static func petUpdateRect(scale: CGFloat) -> CGRect {
        let panel = size(mode: .pet, scale: scale)
        let side = updateBadgeSize(scale: scale)
        let inset = 2 * scale
        return CGRect(
            x: panel.width - side - inset,
            y: panel.height - side - inset,
            width: side,
            height: side
        )
    }

    /// 링 아래 버튼 줄이 차지하는 자리.
    ///
    /// **커서 피하기를 거는 추적 영역(`petHitRect`)과 겹치지 않는다.** 버튼을 누르러
    /// 다가갔는데 펫이 달아나면 영영 못 누른다. 자리를 갈라 두면 특별히 예외를 두지
    /// 않아도 그 일이 생기지 않는다.
    static func petButtonsRect(scale: CGFloat) -> CGRect {
        let panel = size(mode: .pet, scale: scale)
        return CGRect(x: 0, y: 0, width: panel.width, height: basePetButtonRow * scale)
    }

    static func petOwlHeight(scale: CGFloat) -> CGFloat { basePetOwlHeight * scale }

    /// 펫 링의 선 굵기. 카드 링보다 얇다.
    /// **바깥 굵기는 `basePetRingDiameter` 와 짝이다** — 123 + 5 = 128 이 링 자리다.
    static func petRingLineWidth(scale: CGFloat) -> CGFloat { basePetRingLineWidth * scale }
    static func petRingInnerLineWidth(scale: CGFloat) -> CGFloat { 4 * scale }

    /// 마스코트 그림이 실제로 덮는 자리(뷰 좌표, 아래가 0).
    ///
    /// **`petHitRect` 와 같은 셈을 쓴다.** 둘 다 "버튼 줄 위 영역의 가운데"인데,
    /// 예전에는 이 계산이 `HUDPanel` 에 따로 적혀 있어서 버튼 줄이 생겼을 때 한쪽만
    /// 고쳐졌다 — 판정이 배율 1에서 16pt 아래로 밀렸다. 같은 파일에 나란히 둔다.
    @MainActor
    static func petMascotRect(scale: CGFloat, style: ClaudeIconStyle = .owl) -> CGRect {
        let panel = size(mode: .pet, scale: scale)
        let height = petOwlHeight(scale: scale)
        let width = height * mascotAspect(style: style)
        let row = basePetButtonRow * scale
        return CGRect(
            x: (panel.width - width) / 2,
            y: row + (panel.height - row - height) / 2,
            width: width,
            height: height
        )
    }

    /// 창 테두리에 붙었을 때 **창이 놓일 원점**(화면 좌표).
    ///
    /// **상자가 아니라 그림이 닿아야 한다.** 매달린 칸은 손이 묶음 상자 위쪽에, 앉은
    /// 칸은 발이 아래쪽에 그려져 있어서, 상자를 그대로 테두리에 대면 자세마다 수십 pt
    /// 씩 뜬다 — 매달린 것이 아니라 공중에 뜬 것으로 보인다.
    ///
    /// **자세마다 정렬을 가르는 길은 버렸다.** 링은 창 한가운데 그대로 있어야 하는데
    /// 마스코트만 옮기려면 둘의 정렬을 갈라야 하고, 그러면 `petMascotRect` 와
    /// `petHitRect` 가 한 셈에서 나오는 구조가 깨진다 — 그 구조를 왜 지키는지는
    /// `petMascotRect` 주석에 있다. 창 원점을 미는 것으로 푼다.
    @MainActor
    static func petPerchOrigin(
        perch: MascotPerch, contact: CGPoint, scale: CGFloat, style: ClaudeIconStyle
    ) -> NSPoint? {
        guard let ink = petMascotInkRect(perch: perch, scale: scale, style: style) else {
            return nil
        }
        // **붙잡는 부위만 창 안으로 넣는다.** 그만큼 원점을 창 안쪽으로 민다.
        let sink = petPerchSink(perch: perch, contact: contact, scale: scale, style: style)
        let origin: NSPoint
        switch perch {
        case .top: origin = NSPoint(x: contact.x - ink.midX, y: contact.y - ink.minY - sink)
        case .bottom: origin = NSPoint(x: contact.x - ink.midX, y: contact.y - ink.maxY + sink)
        case .right: origin = NSPoint(x: contact.x - ink.minX - sink, y: contact.y - ink.midY)
        case .left: origin = NSPoint(x: contact.x - ink.maxX + sink, y: contact.y - ink.midY)
        }
        // **테두리에 딱 맞추던 것을 그만뒀다.** 예전에는 그림 전체가 테두리 바깥에
        // 있었는데, 그러면 붙잡는 부위가 선에 닿기만 하고 넘어가질 않아서 — 옆에 붙었을
        // 때 날개로 껴안은 것이 아니라 벽에 부딪친 것으로 보였다.
        //
        // 그 전에 **그림 절반을 걸치게 해 봤고 그것도 버렸다.** 몸이 창에 잠겨서 무엇에
        // 붙어 있는지가 흐려졌기 때문이다. 지금 넘어가는 것은 몸이 아니라 다리 · 발 ·
        // 붙잡는 앞다리뿐이라(`MascotSprite.gripDepth`) 그 문제가 안 생긴다.
        // **왜 그때는 틀렸고 지금은 맞는지가 남아야 또 안 뒤집는다.**
        //
        // **그림이 화면 밖으로 나가면 붙지 않는다.** 화면 안으로 밀어 넣지 않는 이유는
        // 밀어 넣으면 테두리에서 떨어진 자리에서 붙은 척을 하기 때문이다. 메뉴 막대 위에
        // 올라서는 것도 `visibleFrame` 이 여기서 막는다.
        //
        // **자리 계산과 같은 함수에 둔다.** 두 곳에 두면 진단 통로(`--probe-perch`)가
        // 앱과 다른 답을 내서, 붙지 않는 이유를 진단으로 알 수 없게 된다.
        //
        // 상자가 아니라 **알맹이**로 잰다. 상자에는 자세마다 빈 여백이 붙어 있어서,
        // 그것까지 화면 안을 요구하면 실제로는 다 보이는 자리에서 안 붙는다.
        let visual = CGRect(
            x: origin.x + ink.minX, y: origin.y + ink.minY,
            width: ink.width, height: ink.height
        )
        guard let screen = NSScreen.screens.first(where: { $0.visibleFrame.intersects(visual) }),
              screen.visibleFrame.contains(visual)
        else { return nil }
        return origin
    }

    /// 붙잡는 부위가 창 안으로 넘어가는 깊이(pt).
    ///
    /// **잉크를 재서 비율을 곱한다.** 상수 pt 로 박아 두면 배율을 키우거나 남의 그림을
    /// 넣었을 때 몸통까지 잠긴다 — 잠기는 양은 그림 크기를 따라가야 한다.
    ///
    /// **`petMascotInkRect` 에 섞지 않는다.** 저쪽은 "그림이 상자 어디를 덮나" 이고
    /// 진단(`--probe-perch checkInk`)이 실제로 그려서 6pt 안으로 맞는지 검사한다.
    /// 여기 보정을 섞으면 그 검사가 통째로 못 쓰게 되고, 실패 문구가 "그림 자리 예측이
    /// 실제와 어긋난다" 라서 진짜 반전 실수와 구분이 안 된다.
    @MainActor
    static func petPerchSink(
        perch: MascotPerch, contact: CGPoint, scale: CGFloat, style: ClaudeIconStyle
    ) -> CGFloat {
        guard let ink = petMascotInkRect(perch: perch, scale: scale, style: style),
              let set = MascotSpriteStore.bundled(style),
              // **잉크를 잰 칸에서 깊이도 읽는다.** 시트에 그 자세가 없으면 잉크는
              // fallback 칸(선 자세)에서 나오는데, 깊이만 원래 칸에서 가져오면 붙잡는
              // 부위가 없는 그림을 있는 만큼 밀어 넣는다.
              let drawn = set.resolvedSprite(perch.sprite)
        else { return 0 }
        // 그 변에서 창 밖으로 뻗는 축의 길이.
        let span = (perch == .top || perch == .bottom) ? ink.height : ink.width
        // **사용자가 맞춘 값이 있으면 그것이 이긴다.** 규격이 요구한 자리에 그림이 정확히
        // 오지 않아서(실제로 매달리기 발이 24%에 왔다) 자세마다 맞출 통로를 뒀다.
        let base = span * (HUDSettings.storedGripDepth(perch) ?? drawn.gripDepth)

        // **자리가 모자라면 그만큼 더 깊이 앉는다.**
        //
        // 창의 위 테두리가 화면 꼭대기에 가까우면 그 위에 설 자리가 없다 — 실제로 8pt
        // 모자라서 안 붙는 창이 있었고, 그건 사용자 눈에 고장으로 보인다. 모자란 만큼만
        // 창 안으로 더 넣으면 붙는다.
        //
        // **메뉴 막대 밑으로 머리를 넣는 길은 안 쓴다.** 메뉴 막대가 위 층이라 머리가
        // 잘려 보인다 — 안 붙는 것보다 나쁘다.
        guard let screen = NSScreen.screens.first(where: { $0.frame.contains(contact) })
                ?? NSScreen.screens.first(where: { $0.frame.intersects(
                    CGRect(x: contact.x - 1, y: contact.y - 1, width: 2, height: 2)) })
        else { return base }
        let visible = screen.visibleFrame
        // 창 밖에 남는 몫(span - sink)이 이 안에 들어가야 한다.
        let room: CGFloat
        switch perch {
        case .top: room = visible.maxY - contact.y
        case .bottom: room = contact.y - visible.minY
        case .right: room = visible.maxX - contact.x
        case .left: room = contact.x - visible.minX
        }
        let outside = span - base
        guard outside > room else { return base }
        // **조금 모자랄 때만 더 넣는다.** 많이 모자란데 억지로 넣으면 다리가 아니라
        // 몸통이 잠겨서, 붙은 것이 아니라 창에 박힌 것으로 보인다 — 그때는 차라리
        // 안 붙는 편이 낫다(더 넣어도 안 들어가면 `petPerchOrigin` 이 nil 을 낸다).
        //
        // 화면 바닥에 가까이 놓인 창에서 실제로 그랬다. 아래에 39pt 밖에 없는데
        // 31pt 가 모자라서 한계까지 밀어 넣었고, 몸통 절반이 창에 잠겼다.
        // **자리가 넉넉한 창에서는 기본값 그대로**여서 그 창만 이상해 보였다.
        return base + min(span * maxAutoExtra, outside - room)
    }

    /// 자리가 모자랄 때 **더 넣어 주는 몫**의 한계. 잉크에 대한 비율이다.
    ///
    /// **깊이의 한계가 아니라 보태 주는 양의 한계다.** 깊이로 못 박아 두면 기본값이
    /// 얕은 자세(15%)는 많이 보태지고 깊은 자세(25%)는 조금만 보태져서, 같은 만큼
    /// 모자란 자리에서 자세마다 다르게 굴었다.
    ///
    /// 8pt(잉크의 10%) 모자란 창을 살리려고 넣은 값이라 그보다 조금 넉넉하다.
    /// 사람이 손으로 맞추는 한계(`HUDSettings.maxPerchDepth`)와는 다른 값이다 —
    /// 저쪽은 눈으로 보고 정하는 것이라 막을 이유가 없다.
    static let maxAutoExtra: CGFloat = 0.12

    /// 그 자세의 그림이 창 안에서 실제로 덮는 자리(뷰 좌표, 아래가 0).
    ///
    /// **그림 마스코트가 아니면 nil이다.** 격자로 그리는 부엉이에는 매달림·앉음 자세가
    /// 아예 없어서, 붙여 놓아도 테두리에 그냥 선 것으로 보인다.
    @MainActor
    static func petMascotInkRect(
        perch: MascotPerch, scale: CGFloat, style: ClaudeIconStyle
    ) -> CGRect? {
        guard let set = MascotSpriteStore.bundled(style),
              let fraction = set.inkFraction(perch.sprite)
        else { return nil }
        let box = petMascotRect(scale: scale, style: style)
        var minX = box.minX + fraction.minX * box.width
        var maxX = box.minX + fraction.maxX * box.width
        // 뒤집어 쓰는 자세는 상자 안에서 좌우가 미러링된다(`MascotSpriteView` 의
        // `scaleEffect`). 왼쪽 벽에 붙을 때 닿는 변이 반대쪽이 되므로 여기서 같이 돌린다.
        if perch.flipsSprite {
            (minX, maxX) = (box.maxX - (maxX - box.minX), box.maxX - (minX - box.minX))
        }
        // 잉크 좌표는 위가 0이고 뷰 좌표는 아래가 0이라 세로를 뒤집는다.
        return CGRect(
            x: minX,
            y: box.maxY - fraction.maxY * box.height,
            width: maxX - minX,
            height: fraction.height * box.height
        )
    }

    /// 마스코트가 세로 한 칸당 가로로 얼마나 퍼지는지.
    ///
    /// 부엉이는 그리드 15열 중 몸통이 가운데 11열만 쓴다. 나머지는 날개를 펼 여백이라
    /// 평소에는 비어 있어서, 그 폭까지 가린다고 치면 쓸데없이 멀리 비킨다.
    ///
    /// **그림으로 도는 쪽은 그 그림에서 잰다.** 격자 비율을 그대로 쓰면, 옆으로 퍼진
    /// 캐릭터는 실제로 글자를 덮고 있는데도 안 비키고, 홀쭉한 캐릭터는 아직 멀었는데
    /// 비킨다.
    @MainActor
    static func mascotAspect(style: ClaudeIconStyle) -> CGFloat {
        let owl = CGFloat(OwlMark.bodyColumns) / CGFloat(OwlMark.lines)
        guard let set = MascotSpriteStore.bundled(style) else { return owl }
        return set.extent.width / max(set.extent.height, 1)
    }

    /// 새로고침 버튼 자리. 이 영역만 드래그 오버레이가 클릭을 통과시킨다.
    static func refreshInset(scale: CGFloat) -> CGFloat { 4 * scale }
    static func refreshHitSize(scale: CGFloat) -> CGFloat { 20 * scale }

    /// 카드 위 버튼 수(접기 · 측정 · 설정 · 새로고침).
    ///
    /// **여기를 안 늘리면 버튼을 더해도 클릭이 통과하지 않는다.** 버튼은 SwiftUI가
    /// 그리지만 클릭을 흘려보낼 자리는 AppKit 쪽에서 따로 재기 때문이다.
    static let controlButtonCount = 4

    /// AppKit 좌표(원점 왼쪽 아래) 기준의 버튼 영역.
    /// 펼친 상태는 위쪽 가로 한 줄, 접은 상태는 옆쪽 세로 한 줄이다.
    ///
    /// 높이는 반드시 "실제 창 크기"에서 가져와야 한다. 자원 사용량 줄이 붙으면 창이
    /// 17pt 커지는데, 그때 펼친 기본 높이(88)로 계산하면 영역이 그만큼 아래로 밀려서
    /// 버튼을 눌러도 클릭이 통과되지 않는다.
    static func controlsHitRectInPanel(
        mode: HUDMode,
        side: HUDExpandSide,
        showsStats: Bool,
        showsScopedLimit: Bool = false,
        scale: CGFloat = 1
    ) -> CGRect {
        // 펫에는 버튼이 없다. 빈 사각형을 주면 어떤 클릭도 여기 걸리지 않는다.
        guard mode != .pet else { return .zero }

        let button = refreshHitSize(scale: scale)
        let panel = size(mode: mode, showsStats: showsStats, showsScopedLimit: showsScopedLimit, scale: scale)
        let trailing = collapsedTrailing(scale: scale)
        let inset = refreshInset(scale: scale)

        if mode == .collapsed {
            let height = button * CGFloat(controlButtonCount)
            let x = side == .right ? panel.width - trailing - button : trailing
            return CGRect(
                x: x,
                y: (panel.height - height) / 2,
                width: button,
                height: height
            )
        }

        let width = button * CGFloat(controlButtonCount)
        let x = side == .right ? panel.width - inset - width : inset
        return CGRect(
            x: x,
            y: panel.height - inset - button,
            width: width,
            height: button
        )
    }

    static func collapsedTrailing(scale: CGFloat) -> CGFloat { 6 * scale }

    /// 마스코트가 놓인 자리(AppKit 좌표, 원점 왼쪽 아래).
    ///
    /// 여기를 더블클릭하면 펫 모드로 들어간다. 링과 마스코트가 겹쳐 있으므로 링 전체를
    /// 잡는다. 펫에서는 이미 마스코트뿐이라 창 전체가 그 자리다.
    static func characterRectInPanel(
        mode: HUDMode,
        side: HUDExpandSide,
        showsStats: Bool,
        showsScopedLimit: Bool = false,
        scale: CGFloat = 1
    ) -> CGRect {
        let panel = size(mode: mode, showsStats: showsStats, showsScopedLimit: showsScopedLimit, scale: scale)
        guard mode != .pet else { return CGRect(origin: .zero, size: panel) }

        // **링이 커지면 이 자리도 같이 커져야 한다.** 62 로 못 박아 두면 모델별 링을
        // 켰을 때 링 가장자리를 더블클릭해도 펫으로 안 들어간다.
        let ring = ringDiameter(showsScopedLimit: showsScopedLimit, scale: scale)
        // 펼친 상태의 링은 위쪽 줄 안에서 세로 가운데에 놓인다. 자원 사용량 줄이
        // 붙어 창이 커져도 링은 그대로 위에 남는다.
        let rowHeight = mode == .collapsed
            ? panel.height
            : expandedRowHeight(showsScopedLimit: showsScopedLimit, scale: scale)
        let y = panel.height - rowHeight + (rowHeight - ring) / 2

        // 접힌 상태에서 왼쪽으로 펼치는 설정이면 버튼 열이 링 앞에 온다.
        let leading: CGFloat
        switch (mode, side) {
        case (.collapsed, .right): leading = collapsedLeading(scale: scale)
        case (.collapsed, .left):
            leading = collapsedTrailing(scale: scale) + refreshHitSize(scale: scale)
                + collapsedGap(scale: scale)
        case (_, .right): leading = expandedLeading(scale: scale)
        case (_, .left): leading = panel.width - expandedLeading(scale: scale) - ring
        }
        return CGRect(x: leading, y: y, width: ring, height: ring)
    }

    /// 업데이트 표시 한 변.
    static func updateBadgeSize(scale: CGFloat) -> CGFloat { 18 * scale }

    /// 업데이트 표시 자리. 버튼 묶음 반대편 위 모서리에 둔다.
    /// 기본 설정(오른쪽으로 펼치기)에서는 왼쪽 위가 된다.
    static func updateBadgeRectInPanel(
        mode: HUDMode,
        side: HUDExpandSide,
        showsStats: Bool,
        showsScopedLimit: Bool = false,
        scale: CGFloat = 1
    ) -> CGRect {
        // 펫은 배지를 그리지 않는다. 그런데도 자리를 돌려주면 그만큼이 클릭 통과
        // 구멍이 되어, 마스코트 한 귀퉁이를 눌러도 끌리지 않는다.
        guard mode != .pet else { return .zero }

        let badge = updateBadgeSize(scale: scale)
        let inset = refreshInset(scale: scale)
        let panel = size(mode: mode, showsStats: showsStats, showsScopedLimit: showsScopedLimit, scale: scale)
        let x = side == .right ? inset : panel.width - inset - badge
        return CGRect(x: x, y: panel.height - inset - badge, width: badge, height: badge)
    }

    @State private var isHoveringRefresh = false
    @State private var isHoveringSettings = false
    @State private var isHoveringMeasure = false
    @State private var isHoveringCollapse = false

    /// 지금 링을 그릴지. **버튼 줄도 이걸 따른다** — 둘은 같이 떴다 사라진다.
    private var showsPetRing: Bool {
        // **들고 있는 동안에는 감춘다.** 집어 든 순간 보고 싶은 것은 마스코트지,
        // 그 뒤에 두른 눈금과 버튼이 아니다. "항상 표시"로 해 뒀어도 이때는 비운다 —
        // 끌고 가는 내내 링이 따라다니면 어디에 놓고 있는지가 가려진다.
        if isHeld, hidesRingWhileHeld { return false }
        switch petRingDisplay {
        case .always: return true
        case .hover: return isHovered
        case .never: return false
        }
    }

    private var isDisconnected: Bool { store.isDisconnected }

    private var ringDiameter: CGFloat { Self.ringDiameter(showsScopedLimit: showsScopedLimit, scale: scale) }
    private var outerLineWidth: CGFloat { Self.ringLineWidth(scale: scale) }
    private var innerLineWidth: CGFloat { Self.ringInnerLineWidth(scale: scale) }

    var body: some View {
        switch mode {
        case .pet: petBody
        case .collapsed: collapsedBody
        case .expanded: expandedBody
        }
    }

    /// 펫 모습: 마스코트만. 마우스를 올리면 뒤에서 링이 떠오른다.
    ///
    /// 창 크기는 링에 맞춰 두고 마스코트를 가운데 놓는다. 호버할 때 창을 늘리면
    /// 커서가 창 밖으로 밀려나 호버가 끊기고, 그 자리에서 켜졌다 꺼졌다 한다.
    private var petBody: some View {
        VStack(spacing: 0) {
            petRingArea
            petButtonRow
        }
        .frame(
            width: Self.size(mode: .pet, scale: scale).width,
            height: Self.size(mode: .pet, scale: scale).height
        )
        // **여기에 사용량 요약을 붙이지 않는다.** 카드 전체를 덮는 설명이라 버튼 위에
        // 올려도 같이 떠서 버튼 설명과 겹친다. 마우스를 올리면 링이 떠오르므로
        // 사용량은 이미 눈에 보인다 — 같은 말을 글로 한 번 더 할 이유가 없다.
    }

    private var petRingArea: some View {
        ZStack(alignment: .topTrailing) {
            petRingStack
            // 새 버전이 있을 때만. 링 바깥 모서리라 마스코트를 가리지 않는다.
            if showsUpdateBadge {
                updateBadge.padding(s(2))
            }
        }
    }

    private var petRingStack: some View {
        ZStack {
            ringPair(
                diameter: s(Self.basePetRingDiameter),
                outerWidth: Self.petRingLineWidth(scale: scale),
                innerWidth: Self.petRingInnerLineWidth(scale: scale)
            )
            .opacity(showsPetRing ? (isDisconnected ? 0.4 : 0.95) : 0)
            .animation(.easeOut(duration: 0.18), value: showsPetRing)

            // **링 안쪽에 맞추지 않는다.** 펫은 마스코트가 주인공이고 링은 마우스를
            // 올렸을 때만 뒤에서 떠오르는 것이라, 마스코트가 링 위로 올라오는 것이
            // 맞다. 링에 맞춰 줄이면 켜고 끌 때마다 캐릭터 크기가 달라진다.
            ClaudeIconView(
                style: iconStyle,
                size: Self.petOwlHeight(scale: scale),
                eyeColor: palette.markEye,
                owlAnimator: owlAnimator
            )
            // 움직이는 캐릭터는 팔레트가 회색으로 바뀌어 스스로 드러낸다. 정지 그림은
            // 그럴 수 없는데, 펫에는 링도 안 보여서 조회가 끊긴 걸 알 방법이 없어진다.
            // 링에 이미 쓰는 방식대로 흐리게 해서 지금 값이 아님을 알린다.
            .opacity(!iconStyle.isAnimated && isDisconnected ? 0.4 : 1)
        }
        .frame(
            width: Self.size(mode: .pet, scale: scale).width,
            height: Self.size(mode: .pet, scale: scale).height - s(Self.basePetButtonRow)
        )
    }

    /// 링 밖 아래에 붙는 동그란 아이콘 버튼들.
    ///
    /// 링과 마찬가지로 **마우스를 올렸을 때만** 보인다 — 펫은 마스코트만 띄우는 보기라
    /// 버튼이 늘 떠 있으면 그 뜻이 사라진다.
    private var petButtonRow: some View {
        HStack(spacing: s(8)) {
            // **설명은 여기 붙이지 않는다.** 펫에서는 `HUDInteractionView` 가 이 줄
            // 위를 통째로 덮고 있어서 SwiftUI 의 `.help` 가 커서에 안 잡힌다.
            // 그 뷰에 자리별로 걸어 둔다 — `petButtonRects`.
            PetCircleButton(
                systemName: isMeasuring ? "stopwatch.fill" : "stopwatch",
                palette: palette,
                scale: scale,
                tint: isMeasuring ? .red : nil
            ) {
                onOpenMeasure?()
            }
            PetCircleButton(systemName: "gearshape.fill", palette: palette, scale: scale) {
                onOpenSettings?()
            }
            PetCircleButton(systemName: "arrow.clockwise", palette: palette, scale: scale) {
                store.refresh(force: true)
            }
            .opacity(store.isRefreshing ? 0.35 : 1)
        }
        .frame(height: s(Self.basePetButtonRow))
        .opacity(showsPetRing ? 1 : 0)
        .animation(.easeOut(duration: 0.18), value: showsPetRing)
    }

    /// 접힌 모습: 링 + 세로 버튼 열. 버튼은 펼쳐질 방향 쪽에 붙는다.
    private var collapsedBody: some View {
        HStack(spacing: Self.collapsedGap(scale: scale)) {
            if expandSide == .right {
                ringsView
                buttonColumn
            } else {
                buttonColumn
                ringsView
            }
        }
        .padding(.leading, expandSide == .right
            ? Self.collapsedLeading(scale: scale) : Self.collapsedTrailing(scale: scale))
        .padding(.trailing, expandSide == .right
            ? Self.collapsedTrailing(scale: scale) : Self.collapsedLeading(scale: scale))
        .frame(
            width: collapsedSize.width,
            height: collapsedSize.height
        )
        // 접은 카드는 108pt뿐이라 버전 딱지를 붙이면 링 위에 겹친다.
        // 테스트판인지는 마스코트 색(보라)이 알려준다.
        .overlay(alignment: badgeAlignment) { cornerBadges }
    }

    /// 접은 카드 크기. **모델별 링이 붙으면 커진다** — 링이 커진 만큼 카드도 커져야
    /// 링이 카드를 뚫고 나가지 않는다.
    private var collapsedSize: CGSize {
        Self.size(mode: .collapsed, showsScopedLimit: showsScopedLimit, scale: scale)
    }

    private var buttonColumn: some View {
        VStack(spacing: 0) {
            collapseButton
            measureButton
            settingsButton
            refreshButton
        }
    }

    /// 마지막 성공값을 보여주는 중이면 링·숫자를 흐리게 해서 지금 값이 아님을 드러낸다.
    private var ringsView: some View {
        rings.opacity(store.isStale ? 0.45 : 1)
    }

    private var expandedBody: some View {
        VStack(spacing: 0) {
            mainRow
            if let usageMonitor {
                bottomBar(monitor: usageMonitor)
                    .frame(height: s(Self.baseStatsRowHeight))
            }
        }
        .frame(
            width: expandedSize.width,
            height: Self.size(
                mode: .expanded, showsStats: usageMonitor != nil,
                showsScopedLimit: showsScopedLimit, scale: scale
            ).height
        )
    }

    /// 펼친 카드 크기. 모델별 링이 붙으면 가로도 그만큼 넓어진다.
    private var expandedSize: CGSize {
        Self.size(mode: .expanded, showsScopedLimit: showsScopedLimit, scale: scale)
    }

    private var expandedRowHeight: CGFloat {
        Self.expandedRowHeight(showsScopedLimit: showsScopedLimit, scale: scale)
    }

    private var mainRow: some View {
        HStack(spacing: Self.expandedGap(scale: scale)) {
            if expandSide == .right {
                ringsView
                metricsView
                Spacer(minLength: 0)
            } else {
                Spacer(minLength: 0)
                metricsView
                ringsView
            }
        }
        .padding(.leading, expandSide == .right
            ? Self.expandedLeading(scale: scale) : Self.expandedTrailing(scale: scale))
        .padding(.trailing, expandSide == .right
            ? Self.expandedTrailing(scale: scale) : Self.expandedLeading(scale: scale))
        .frame(width: expandedSize.width, height: expandedRowHeight)
        .overlay(alignment: expandSide == .right ? .topTrailing : .topLeading) { controlButtons }
        .overlay(alignment: badgeAlignment) { cornerBadges }
        // 아래 줄이 생기면 카운트다운도 거기로 내려가 자원 사용량과 같은 높이에 놓인다.
        .overlay(alignment: expandSide == .right ? .bottomTrailing : .bottomLeading) {
            if usageMonitor == nil { resetCountdown }
        }
    }

    /// 자원 사용량과 조회 카운트다운을 한 줄에 놓는다. 방향 설정에 따라 좌우가 뒤집힌다.
    private func bottomBar(monitor: ProcessUsageMonitor) -> some View {
        HStack(spacing: 0) {
            if expandSide == .right {
                ProcessStatsRow(monitor: monitor, palette: palette, scale: scale)
                Spacer(minLength: s(8))
                countdownContent
            } else {
                countdownContent
                Spacer(minLength: s(8))
                ProcessStatsRow(monitor: monitor, palette: palette, scale: scale)
            }
        }
        .padding(.horizontal, s(13))
        .padding(.bottom, s(4))
    }

    /// 남은 시간 문구만 시간에 따라 바뀐다. 링·아이콘은 타임라인 밖에 두어
    /// 주기적 갱신 때 다시 평가되지 않게 한다. 표시 단위가 분이라 60초면 충분하다.
    private var metricsView: some View {
        TimelineView(.periodic(from: .now, by: 60)) { context in
            VStack(alignment: .leading, spacing: s(8)) {
                metric(
                    title: "세션",
                    window: store.snapshot?.fiveHour,
                    now: context.date,
                    isSpent: store.isSpent
                )
                metric(
                    title: "주간",
                    window: store.snapshot?.sevenDay,
                    now: context.date,
                    isSpent: store.isWeeklySpent
                )
                // 링과 같은 순서로 맨 아래. 켜져 있고 서버가 줄 때만 나온다.
                if showsScopedLimit, let limit = store.scopedLimit,
                   let name = limit.modelName {
                    metric(
                        title: name,
                        window: UsageWindow(utilization: limit.percent, resetsAt: limit.resetsAt),
                        now: context.date,
                        isSpent: store.isWeeklySpent
                    )
                }
            }
            .shadow(color: palette.textShadow, radius: s(2), y: s(0.5))
            .opacity(store.isStale ? 0.45 : 1)
        }
    }

    // MARK: - 다음 조회 카운트다운

    /// 다음 사용량 조회까지 남은 시간.
    /// 초까지 움직여야 하므로 1초 주기지만, 이 작은 텍스트만 다시 그린다.
    @ViewBuilder private var resetCountdown: some View {
        if showsCountdown {
            countdownBody
        }
    }

    private var countdownBody: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            Group {
                if let warning = staleLabel(now: context.date) {
                    // 화면 숫자가 지금 값이 아니면, 남은 시간 대신 그 사실을 알린다.
                    Text(warning)
                        .font(font(9.5, weight: .semibold, design: .rounded))
                        .foregroundStyle(palette.warning)
                } else {
                    HStack(spacing: s(4)) {
                        Text("조회")
                            .font(font(8.5, weight: .semibold))
                            .foregroundStyle(palette.faintText)
                        Text(countdownText(now: context.date))
                            .font(font(9.5, weight: .medium, design: .rounded))
                            .monospacedDigit()
                            .foregroundStyle(palette.tertiaryText.opacity(store.isRefreshing ? 0.55 : 1))
                    }
                }
            }
        }
        // help 문구는 시간과 무관하다. 안에 두면 1초마다 문자열을 새로 만든다.
        .help(countdownHelp)
        .padding(.horizontal, s(10))
        .padding(.bottom, s(7))
    }

    /// 아래 줄에 들어갈 때 쓰는, 패딩 없는 카운트다운.
    @ViewBuilder private var countdownContent: some View {
        if showsCountdown { countdownBody.padding(.bottom, s(-7)).padding(.horizontal, s(-10)) }
    }

    /// 재로그인이 필요하거나 마지막 성공값을 보여주는 중이면 그 문구를 돌려준다.
    private func staleLabel(now: Date) -> String? {
        if store.needsReauth { return "재로그인 필요" }
        guard store.isStale, let fetchedAt = store.snapshot?.fetchedAt else { return nil }
        return RemainingTime.ageText(since: fetchedAt, now: now)
    }

    private func countdownText(now: Date) -> String {
        guard let next = store.nextPollDate else { return "멈춤" }
        // 타이머에 tolerance를 크게 줬기 때문에 예정 시각이 지나도 잠시 뒤에 울린다.
        // 그동안 0:00으로 멈춘 것처럼 보이지 않게 한다.
        guard next.timeIntervalSince(now) > 0 else { return "곧" }
        return RemainingTime.clockText(until: next, now: now)
    }

    private var countdownHelp: String {
        if store.needsReauth {
            return "Claude Code 재로그인이 필요하다 — \(store.errorText ?? "")"
        }
        if let error = store.errorText {
            return "갱신 실패로 마지막 성공값을 보여주는 중 — \(error)"
        }
        guard store.nextPollDate != nil else { return "화면이 꺼져 있어 조회를 멈춘 상태" }
        return "다음 사용량 조회까지 남은 시간"
    }

    // MARK: - 위 모서리 표시 (업데이트 · 버전)

    private var badgeAlignment: Alignment {
        expandSide == .right ? .topLeading : .topTrailing
    }

    /// 버튼 묶음 반대편 위 모서리에 붙는 것들.
    ///
    /// 업데이트 표시와 버전 딱지가 같은 자리를 노려서, 각자 오버레이로 얹으면 겹친다.
    /// 한 줄에 묶어 두면 새 버전이 잡히는 순간 버전 딱지가 옆으로 밀려난다.
    /// **업데이트 표시가 늘 바깥쪽이다** — 클릭 통과 영역이 모서리 기준으로 계산된다
    /// (`updateBadgeRectInPanel`).
    /// **어느 보기에 버전 딱지를 붙이는지는 `draws` 만 안다.** 부르는 쪽이 정하면
    /// 설정 창의 잠금과 어긋난다 — 접은 카드에도 붙이기로 하면 한쪽만 고쳐도 컴파일은
    /// 통과한다.
    @ViewBuilder private var cornerBadges: some View {
        let showsVersion = Self.draws(.versionBadge, in: mode)
        let version = showsVersion ? versionBadge : nil
        if showsUpdateBadge || version != nil {
            HStack(spacing: s(3)) {
                if expandSide == .right {
                    updateBadge
                    versionLabel(version)
                } else {
                    versionLabel(version)
                    updateBadge
                }
            }
            .padding(Self.refreshInset(scale: scale))
        }
    }

    @ViewBuilder private var updateBadge: some View {
        if showsUpdateBadge {
            Button {
                onOpenUpdates?()
            } label: {
                Image(systemName: "arrow.down.circle.fill")
                    .font(font(13, weight: .semibold))
                    .symbolRenderingMode(.palette)
                    .foregroundStyle(.white, palette.updateBadge)
                    .frame(
                        width: Self.updateBadgeSize(scale: scale),
                        height: Self.updateBadgeSize(scale: scale)
                    )
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .help("새 버전이 나왔다 — 눌러서 확인")
        }
    }

    /// 지금 버전. 테스트판은 색을 입힌 알약으로 그려서 곁눈으로도 걸린다.
    @ViewBuilder private func versionLabel(_ text: String?) -> some View {
        if let text {
            Text(text)
                .font(font(9, weight: .semibold, design: .rounded))
                .monospacedDigit()
                .lineLimit(1)
                // 좁은 카드에서 줄바꿈되거나 말줄임표가 뜨지 않게 제 크기를 지킨다.
                .fixedSize()
                .foregroundStyle(versionBadgeIsTest ? palette.testBadge : palette.faintText)
                .shadow(color: palette.textShadow, radius: s(1.5))
                // 카드의 둥근 모서리가 글자를 깎지 않게 띄운다. 모서리 반지름이
                // 20pt라 글자 높이쯤에서 카드 경계가 x≈5.7pt까지 들어와 있는데,
                // 여백 없이 두면 **첫 글자 왼쪽이 잘린다.** 테스트판은 알약 배경
                // 덕에 우연히 여백이 있어서 멀쩡했고, 정식판만 깨져 보였다.
                .padding(.horizontal, s(5))
                .padding(.vertical, s(1))
                .background {
                    if versionBadgeIsTest {
                        Capsule().fill(palette.testBadge.opacity(0.18))
                    }
                }
                .help(versionBadgeIsTest ? "테스트 빌드다 — 배포본이 아니다" : "지금 버전")
        }
    }

    // MARK: - 우측 상단 버튼

    private var controlButtons: some View {
        HStack(spacing: 0) {
            collapseButton
            measureButton
            settingsButton
            refreshButton
        }
        .padding(Self.refreshInset(scale: scale))
    }

    private var collapseButton: some View {
        Button {
            onToggleCollapse?()
        } label: {
            controlLabel(
                systemName: chevronName,
                tint: isHoveringCollapse ? palette.controlActive : palette.controlIdle,
                hovering: isHoveringCollapse
            )
        }
        .buttonStyle(.plain)
        .onHover { isHoveringCollapse = $0 }
        .help(mode == .collapsed ? "펼치기" : "접기")
    }

    /// 측정 시작·중지. 재는 동안에는 빨갛게 채워 둔다.
    private var measureButton: some View {
        Button {
            onOpenMeasure?()
        } label: {
            controlLabel(
                systemName: isMeasuring ? "stopwatch.fill" : "stopwatch",
                tint: measureTint,
                hovering: isHoveringMeasure
            )
        }
        .buttonStyle(.plain)
        .onHover { isHoveringMeasure = $0 }
        .help(measureHelp)
    }

    private var measureTint: Color {
        if isMeasuring { return .red }
        return isHoveringMeasure ? palette.controlActive : palette.controlIdle
    }

    private var settingsButton: some View {
        Button {
            onOpenSettings?()
        } label: {
            controlLabel(
                systemName: "gearshape.fill",
                tint: isHoveringSettings ? palette.controlActive : palette.controlIdle,
                hovering: isHoveringSettings
            )
        }
        .buttonStyle(.plain)
        .onHover { isHoveringSettings = $0 }
        .help("설정")
    }

    /// 눌렀을 때 패널이 움직일 방향을 가리킨다.
    private var chevronName: String {
        ((mode == .collapsed) == (expandSide == .right)) ? "chevron.right" : "chevron.left"
    }

    private func controlLabel(systemName: String, tint: Color, hovering: Bool) -> some View {
        Image(systemName: systemName)
            .font(font(9.5, weight: .bold))
            .foregroundStyle(tint)
            .frame(
                width: Self.refreshHitSize(scale: scale),
                height: Self.refreshHitSize(scale: scale)
            )
            .background {
                Circle().fill(hovering ? palette.controlHoverFill : .clear)
            }
            .contentShape(Circle())
    }

    private var refreshButton: some View {
        Button {
            store.refresh(force: true)
        } label: {
            controlLabel(
                systemName: "arrow.clockwise",
                tint: refreshTint,
                hovering: isHoveringRefresh
            )
        }
        .buttonStyle(.plain)
        // 갱신 중에는 흐리게. 회전 애니메이션은 유휴 상태에서 계속 도는 위험이 있어 쓰지 않는다.
        .opacity(store.isRefreshing ? 0.35 : 1)
        .onHover { isHoveringRefresh = $0 }
        .help(refreshHelp)
    }

    /// 갱신에 실패해 화면 숫자가 오래된 값이면 버튼 자체를 경고색으로 물들인다.
    private var refreshTint: Color {
        if store.errorText != nil { return palette.warning }
        return isHoveringRefresh ? palette.controlActive : palette.controlIdle
    }

    private var measureHelp: String { Self.measureHelp(isMeasuring: isMeasuring) }
    private var refreshHelp: String { Self.refreshHelp(store: store) }

    // 버튼 설명 문구.
    //
    // **펼친 보기·펫 모드·패널이 같이 쓴다.** 펫 쪽은 SwiftUI 가 아니라 겹쳐 있는
    // AppKit 뷰가 띄우므로(`HUDInteractionView`), 여기 한 곳에 두지 않으면 세 자리에
    // 같은 말을 적게 되고 한 곳만 고쳐진다.
    static func measureHelp(isMeasuring: Bool) -> String {
        isMeasuring ? "측정 중 — 측정 화면 열기" : "측정"
    }

    @MainActor
    static func refreshHelp(store: UsageStore) -> String {
        if let error = store.errorText {
            return "갱신 실패: \(error) — 클릭해서 다시 시도"
        }
        // **잇달아 누르면 요청 제한에 걸린다.** 그 사이에는 눌러도 안 나가므로,
        // 마우스를 올렸을 때 몇 초 남았는지 알려 준다.
        let remaining = Int(store.fetchCooldown().rounded(.up))
        return remaining > 0 ? "새로고침 — \(remaining)초 뒤에 가능" : "새로고침"
    }

    /// 펫 버튼 세 개의 자리. 왼쪽부터 **측정 · 설정 · 새로고침**.
    ///
    /// `petButtonsRect` 와 같은 좌표계다. 겹쳐 있는 AppKit 뷰가 버튼마다 다른 설명을
    /// 띄우려면 이 자리를 알아야 한다 — 줄 전체 사각형만으로는 셋을 가를 수 없다.
    /// **레이아웃과 같은 값에서 나온다**(`petButtonRow` 의 HStack 간격 8, 버튼 크기).
    static func petButtonRects(scale: CGFloat) -> [CGRect] {
        let row = petButtonsRect(scale: scale)
        let button = refreshHitSize(scale: scale)
        let gap = 8 * scale
        let total = button * 3 + gap * 2
        let startX = row.minX + (row.width - total) / 2
        let y = row.minY + (row.height - button) / 2
        return (0..<3).map { index in
            CGRect(x: startX + CGFloat(index) * (button + gap), y: y, width: button, height: button)
        }
    }

    // MARK: - 링

    private var rings: some View {
        ZStack {
            ringPair(diameter: ringDiameter, outerWidth: outerLineWidth, innerWidth: innerLineWidth)
            // **폭까지 막는다.** HUD 아이콘은 작은 링 안에 갇혀 있어서, 옆으로 퍼진
            // 그림이 그대로 나오면 원을 뚫고 숫자 위로 올라온다.
            // 링이 하나 더 생기면 아이콘도 그만큼 안으로 들어간다.
            let iconSize = Self.ringLayout(
                outer: ringDiameter, outerWidth: outerLineWidth, innerWidth: innerLineWidth,
                hasScoped: scopedRingWindow != nil, scale: scale
            ).free
            ClaudeIconView(
                style: iconStyle,
                size: iconSize,
                widthLimit: iconSize,
                eyeColor: palette.markEye,
                owlAnimator: owlAnimator
            )
        }
        .frame(width: ringDiameter, height: ringDiameter)
    }

    /// 안쪽 링은 바깥 링 두께(양쪽) + 간격만큼 줄인다.
    private static func innerDiameter(
        outer: CGFloat,
        outerWidth: CGFloat,
        gap: CGFloat
    ) -> CGFloat {
        outer - outerWidth * 2 - gap
    }

    /// 링. 바깥부터 ** 세션 · 주간 · 모델별 ** 순이다.
    ///
    /// **모델별은 켰을 때만 그린다.** 서버가 줄 때만 있는 값이라, 늘 자리를 비워 두면
    /// 링이 있는 사람과 없는 사람의 가운데 아이콘 크기가 달라진다.
    /// 펫 모드는 가운데 아이콘을 따로 크게 그리므로 여기 붙이지 않는다.
    private func ringPair(
        diameter: CGFloat,
        outerWidth: CGFloat,
        innerWidth: CGFloat
    ) -> some View {
        let layout = Self.ringLayout(
            outer: diameter, outerWidth: outerWidth, innerWidth: innerWidth,
            hasScoped: scopedRingWindow != nil, scale: scale
        )
        let inner = layout.inner
        let third = layout.third
        return ZStack {
            // 주간을 다 썼으면 **둘 다** 색을 뺀다. 세션은 쓸 수 없어서고, 주간은
            // 그 자신이 죽은 이유라서다. 하나만 빨갛게 남으면 마스코트는 죽었는데
            // 링은 살아 있어서, 아직 뭔가 되는 것처럼 읽힌다.
            //
            // **세션만 다 썼을 때는 세션 링만 뺀다.** 주간은 다음 창이 열리면 실제로
            // 쓸 수 있는 양이라, 그것까지 회색으로 만들면 있는 여유를 숨기는 셈이다.
            ring(
                window: store.snapshot?.fiveHour,
                diameter: diameter,
                lineWidth: outerWidth,
                isSpent: store.isSpent
            )
            ring(
                window: store.snapshot?.sevenDay,
                diameter: inner,
                lineWidth: innerWidth,
                isSpent: store.isWeeklySpent
            )
            // **제일 안쪽이 모델별이다.** 세션 · 주간은 누구에게나 있고 이건 없을 수도
            // 있어서, 없을 때 자리가 비지 않는 쪽이 안쪽이다.
            if let scoped = scopedRingWindow {
                ring(
                    window: scoped,
                    diameter: third,
                    lineWidth: innerWidth,
                    isSpent: store.isWeeklySpent
                )
            }
        }
        .frame(width: diameter, height: diameter)
    }

    /// 모델별 링에 그릴 값. 꺼져 있거나 서버가 안 주면 nil.
    private var scopedRingWindow: UsageWindow? {
        guard showsScopedLimit, let limit = store.scopedLimit else { return nil }
        return UsageWindow(utilization: limit.percent, resetsAt: limit.resetsAt)
    }

    private func ring(
        window: UsageWindow?,
        diameter: CGFloat,
        lineWidth: CGFloat,
        isSpent: Bool = false
    ) -> some View {
        let fraction = (window?.utilization ?? 0) / 100
        // 다 쓴 링은 사용률 색 대신 회색이다. 숫자는 그대로 두고 색만 뺀다 —
        // 얼마나 썼는지는 여전히 알아야 하고, 쓸 수 없다는 것만 더 알려주면 된다.
        let color = isSpent ? palette.ringSpent : UsageColor.color(for: window?.utilization ?? 0)

        return ZStack {
            Circle()
                .stroke(palette.ringTrack, lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: max(0.004, fraction))
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .shadow(color: color.opacity(0.30), radius: s(1.5))
                .opacity(window == nil ? 0 : 1)
        }
        // 선이 프레임 밖으로 삐져나가지 않게 두께의 절반만큼 안쪽으로 넣는다.
        .padding(lineWidth / 2)
        .frame(width: diameter, height: diameter)
        .animation(.easeOut(duration: 0.5), value: fraction)
    }

    // MARK: - 수치

    private func metric(
        title: String,
        window: UsageWindow?,
        now: Date,
        isSpent: Bool = false
    ) -> some View {
        let utilization = window?.utilization
        // 링과 같은 규칙이다. 링만 회색이고 점은 초록이면 앞뒤가 안 맞는다.
        let color = isSpent ? palette.ringSpent : UsageColor.color(for: utilization ?? 0)

        return VStack(alignment: .leading, spacing: s(1)) {
            HStack(spacing: s(5)) {
                Circle()
                    .fill(window == nil ? palette.mutedDot : color)
                    .frame(width: s(5), height: s(5))
                Text(title)
                    .font(font(10, weight: .semibold))
                    .foregroundStyle(palette.secondaryText)
                Text(utilization.map { "\(Int($0.rounded()))%" } ?? "—")
                    .font(font(14, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(palette.primaryText)
            }
            Text(RemainingTime.text(until: window?.resetsAt, now: now))
                .font(font(9.5, weight: .medium))
                .foregroundStyle(palette.tertiaryText)
        }
    }

}

/// HUD 왼쪽 아래에 붙는 이 앱의 자원 사용량.
/// 링 밖 아래에 붙는 동그란 아이콘 버튼.
///
/// **호버 상태를 제가 들고 있다.** 펼침 보기의 버튼들과 `@State` 를 나눠 쓰면, 그
/// 상태를 적어 주는 `.onHover` 가 펼침 보기에만 달려 있어서 펫 모드에서는 영영
/// 바뀌지 않는다 — 마우스를 올려도 표시가 안 바뀌고, 펼침에서 올린 채로 펫으로
/// 넘어오면 켜진 채 굳는다.
private struct PetCircleButton: View {
    let systemName: String
    let palette: HUDPalette
    let scale: CGFloat
    /// 색을 못 박고 싶을 때. 재는 중인 측정 버튼이 빨갛게 남는 자리다.
    var tint: Color?
    let action: () -> Void

    @State private var isHovering = false

    private func s(_ value: CGFloat) -> CGFloat { value * scale }

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: s(11), weight: .semibold))
                .foregroundStyle(tint ?? (isHovering ? palette.controlActive : palette.controlIdle))
                .frame(width: s(24), height: s(24))
                .background {
                    // 투명한 배경 위에 뜨는 버튼이라 제 바탕이 있어야 읽힌다.
                    Circle().fill(Color(nsColor: palette.backdrop(opacity: 0.92)))
                }
                .overlay {
                    Circle().strokeBorder(palette.ringTrack, lineWidth: s(1))
                }
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .onHover { isHovering = $0 }
    }
}

struct ProcessStatsRow: View {
    @ObservedObject var monitor: ProcessUsageMonitor
    let palette: HUDPalette
    var scale: CGFloat = 1

    var body: some View {
        HStack(spacing: 6 * scale) {
            label("CPU", value: monitor.usage.cpuText)
            label("MEM", value: monitor.usage.footprintText)
            Spacer(minLength: 0)
        }
        .help("dong-csu 자신이 쓰는 CPU와 메모리")
    }

    private func label(_ title: String, value: String) -> some View {
        HStack(spacing: 3 * scale) {
            Text(title)
                .font(.system(size: 8 * scale, weight: .semibold))
                .foregroundStyle(palette.faintText)
            Text(value)
                .font(.system(size: 9 * scale, weight: .medium, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(palette.tertiaryText)
        }
    }
}
