import AppKit
import SwiftUI

/// `dong-mcu --render out.png` — HUD를 고정값으로 그려 PNG로 저장한다.
/// 앱을 띄우지 않고 레이아웃·색·아이콘을 확인하려고 둔 디버그 통로.
@MainActor
enum HUDPreviewRenderer {
    static func write(
        to path: String,
        utilization: (session: Double, weekly: Double),
        iconStyle: ClaudeIconStyle
    ) -> Bool {
        let snapshot = UsageSnapshot(
            planName: "Max",
            fiveHour: UsageWindow(
                utilization: utilization.session,
                resetsAt: Date().addingTimeInterval(3 * 3600 + 12 * 60)
            ),
            sevenDay: UsageWindow(
                utilization: utilization.weekly,
                resetsAt: Date().addingTimeInterval(26 * 3600)
            ),
            fetchedAt: Date()
        )

        // 실제 창과 같은 배경(반투명 단색)을 쓰고, 그 뒤에 회색 바탕을 깔아
        // 데스크톱 위에 얹힌 상태를 흉내낸다.
        let store = UsageStore(
            preview: snapshot,
            nextPoll: Date().addingTimeInterval(7 * 60 + 12)
        )
        let content = UsageHUDView(store: store, iconStyle: iconStyle)
            .background {
                ZStack {
                    Color(white: 0.42)
                    Color(white: 0.09).opacity(0.92)
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: UsageHUDView.cornerRadius, style: .continuous))

        let renderer = ImageRenderer(content: content)
        renderer.scale = 3

        guard let image = renderer.nsImage,
              let tiff = image.tiffRepresentation,
              let bitmap = NSBitmapImageRep(data: tiff),
              let png = bitmap.representation(using: .png, properties: [:])
        else { return false }

        do {
            try png.write(to: URL(fileURLWithPath: path))
            return true
        } catch {
            return false
        }
    }
}
