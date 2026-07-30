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

        // 실제 배경은 블러 + 어두운 막이므로 여기서는 어두운 단색으로 근사한다.
        let content = UsageHUDView(store: UsageStore(preview: snapshot), iconStyle: iconStyle)
            .background(Color(red: 0.11, green: 0.11, blue: 0.12))
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
