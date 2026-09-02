import AppKit
import SwiftUI

/// `dong-csu --probe-hud` — HUD 카드가 **모든 조합에서 안 넘치는지** 잰다.
///
/// 눈으로는 못 잡는다. 카드 밖으로 넘친 그림은 창 경계에서 잘려 나가서, 스크린샷을
/// 봐도 "원래 저렇게 생긴 것" 처럼 보인다. `--render` 는 더 못 잡는다 — 그림 크기를
/// 내용에 맞춰 잡으므로 넘칠 자리 자체가 없다.
///
/// 그래서 화면이 아니라 **치수**를 잰다. 창 크기와 그 안에 놓이는 것들을 뷰가 쓰는
/// 상수 그대로 계산해서, 하나라도 창 밖으로 나가면 실패시킨다.
///
/// 보기 3가지 x 모델별 한도 x 자원 사용량 x 배율 4단계 = 48 가지를 전부 돈다.
@MainActor
enum ProbeHUD {
    static func run() -> Bool {
        var failed: [String] = []
        var rows = 0

        for mode in [HUDMode.expanded, .collapsed, .pet] {
            print("\n\(label(mode))")
            print("  모델별 CPU 배율        창          링  가운데  검사")
            for scoped in [false, true] {
                for stats in [false, true] {
                    for scale in HUDScale.allCases {
                        rows += 1
                        let notes = check(mode: mode, scoped: scoped, stats: stats, scale: scale)
                        let panel = UsageHUDView.size(.init(mode, showsStats: stats, showsScopedLimit: scoped, scale: scale.factor))
                        // 펫은 다른 링을 쓴다. 안 쓰는 숫자를 찍으면 표가 거짓말한다.
                        let ring = mode == .pet
                            ? UsageHUDView.basePetRingDiameter * scale.factor
                            : UsageHUDView.ringDiameter(
                                showsScopedLimit: scoped, scale: scale.factor
                            )
                        // 링 안쪽에 남는 자리. 카드에서는 마스코트가 여기에 맞춰 줄고,
                        // 펫에서는 마스코트가 그 위로 올라온다.
                        let free = mode == .pet
                            ? UsageHUDView.petOwlHeight(scale: scale.factor)
                            : UsageHUDView.ringLayout(
                                outer: ring,
                                outerWidth: UsageHUDView.ringLineWidth(scale: scale.factor),
                                innerWidth: UsageHUDView.ringInnerLineWidth(scale: scale.factor),
                                hasScoped: scoped, scale: scale.factor
                            ).free
                        print(String(format: "  %-6@ %-3@ %-10@ %4dx%-4d  %5.1f %5.1f   %@",
                                     scoped ? "켬" : "끔", stats ? "켬" : "끔", scale.rawValue,
                                     Int(panel.width), Int(panel.height), ring, free,
                                     notes.isEmpty ? "통과" : notes.joined(separator: " · ")))
                        if !notes.isEmpty {
                            failed.append("\(label(mode)) 모델별\(scoped ? "켬" : "끔")/CPU\(stats ? "켬" : "끔")/\(scale.rawValue)")
                        }
                    }
                }
            }
        }

        printToggles()

        print("\n\(rows)가지 중 \(rows - failed.count)가지 통과")
        for name in failed { print("  실패 — \(name)") }
        print(failed.isEmpty ? "\n전부 통과" : "\n실패")
        return failed.isEmpty
    }

    /// 보기마다 무엇을 그리는지 = 설정 창에서 무엇을 만질 수 있는지.
    ///
    /// **둘이 같아야 한다.** 그리는데 잠겨 있으면 눈앞에 보이는 것을 못 끄고,
    /// 안 그리는데 열려 있으면 눌러도 아무 일이 없다. `draws(_:in:)` 한 곳에서
    /// 나오므로 이 표가 곧 설정 창의 잠금 상태다.
    private static func printToggles() {
        print("\n보기마다 그리는 것 (= 설정 창에서 만질 수 있는 것)")
        let elements: [(String, HUDElement)] = [
            ("CPU·메모리 줄", .processStats),
            ("모델별 링", .scopedRing),
            ("버전 딱지", .versionBadge),
        ]
        print("  " + ProbePerch.pad("", 16) + "펼치기  접기   펫")
        for (name, element) in elements {
            let marks = [HUDMode.expanded, .collapsed, .pet].map {
                UsageHUDView.draws(element, in: $0) ? "  O     " : "  -     "
            }
            print("  " + ProbePerch.pad(name, 16) + marks.joined())
        }
    }

    private static func label(_ mode: HUDMode) -> String {
        switch mode {
        case .expanded: return "펼친 카드"
        case .collapsed: return "접은 카드"
        case .pet: return "펫"
        }
    }

    /// 어긋난 것만 골라 돌려준다. 빈 배열이면 통과다.
    private static func check(
        mode: HUDMode, scoped: Bool, stats: Bool, scale: HUDScale
    ) -> [String] {
        let f = scale.factor
        let panel = UsageHUDView.size(.init(mode, showsStats: stats, showsScopedLimit: scoped, scale: f))
        let ring = UsageHUDView.ringDiameter(showsScopedLimit: scoped, scale: f)
        let line = UsageHUDView.ringLineWidth(scale: f)
        var notes: [String] = []
        func over(_ what: String, _ used: CGFloat, _ have: CGFloat) {
            if used > have + 0.5 {
                notes.append(String(format: "%@ %.0f > %.0f", what, used, have))
            }
        }

        switch mode {
        case .collapsed:
            // 링 + 왼쪽 여백 + 버튼 열이 가로에 다 들어가야 한다.
            over("가로", ring + UsageHUDView.collapsedChrome(scale: f), panel.width)
            over("세로", ring + line, panel.height)
        case .expanded:
            // 링은 위쪽 줄 안에 놓인다. 아래 자원 사용량 줄은 그 밖이다.
            over("세로", ring + line, UsageHUDView.expandedRowHeight(showsScopedLimit: scoped, scale: f))
            let chrome = UsageHUDView.expandedLeading(scale: f)
                + UsageHUDView.expandedGap(scale: f) + UsageHUDView.expandedTrailing(scale: f)
            over("가로", ring + chrome, panel.width)
            // **글자 자리가 줄어들면 안 된다.** 링이 커진 만큼 카드를 안 넓히면
            // 여기가 좁아지고, 좁아진 만큼 숫자가 버튼 밑으로 파고든다.
            let text = panel.width - ring - chrome
            let want = UsageHUDView.baseExpandedSize.width * f
                - UsageHUDView.baseRingDiameter * f - chrome
            if text < want - 0.5 {
                notes.append(String(format: "글자 자리 %.0f < %.0f (링에 먹힘)", text, want))
            }
        case .pet:
            // 펫은 다른 링(더 크고 선이 얇다)을 쓴다. 선도 지름 밖으로 나가므로 같이 센다.
            let petOuter = UsageHUDView.basePetRingDiameter * f
            let petRing = petOuter + UsageHUDView.petRingLineWidth(scale: f)
            over("세로", petRing, panel.height - UsageHUDView.basePetButtonRow * f)
            over("가로", petRing, panel.width)
            // **가운데 자리는 안 잰다.** 펫은 마스코트가 링 위로 올라오는 것이 맞다.
        }

        // 클릭을 흘려보낼 자리들이 창 안에 있어야 한다. 밖으로 나가면 버튼이 안 눌린다.
        for side in [HUDExpandSide.right, .left] {
            let bounds = CGRect(origin: .zero, size: panel)
            let controls = UsageHUDView.controlsHitRectInPanel(.init(mode, side: side, showsStats: stats,
                showsScopedLimit: scoped, scale: f))
            if mode != .pet, !bounds.insetBy(dx: -0.5, dy: -0.5).contains(controls) {
                notes.append("버튼 자리가 창 밖 (\(side == .right ? "오른쪽" : "왼쪽"))")
            }
            let character = UsageHUDView.characterRectInPanel(.init(mode, side: side, showsStats: stats,
                showsScopedLimit: scoped, scale: f))
            if !bounds.insetBy(dx: -0.5, dy: -0.5).contains(character) {
                notes.append("마스코트 자리가 창 밖 (\(side == .right ? "오른쪽" : "왼쪽"))")
            }
            // 더블클릭 자리와 버튼 자리가 겹치면 버튼을 눌러도 펫으로 들어간다.
            if mode != .pet, character.intersects(controls) {
                notes.append("마스코트 자리가 버튼과 겹침")
            }
        }
        return notes
    }
}
