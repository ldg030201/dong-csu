import AppKit
import SwiftUI

/// `dong-mcu --render out.png` — HUD를 고정값으로 그려 PNG로 저장한다.
/// 앱을 띄우지 않고 레이아웃·색·아이콘을 확인하려고 둔 디버그 통로.
@MainActor
enum HUDPreviewRenderer {
    /// 미리보기에서 재현할 상태.
    enum State: String {
        case ok
        /// 갱신에 실패해 마지막 성공값을 보여주는 중.
        case stale
        /// 토큰 만료 등으로 재로그인이 필요한 상태.
        case reauth
    }

    static func write(
        to path: String,
        utilization: (session: Double, weekly: Double),
        iconStyle: ClaudeIconStyle,
        state: State,
        collapsed: Bool = false,
        isDark: Bool = true
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
            fetchedAt: Date().addingTimeInterval(state == .ok ? 0 : -13 * 3600)
        )

        // 실제 창과 같은 배경(반투명 단색)을 쓰고, 그 뒤에 회색 바탕을 깔아
        // 데스크톱 위에 얹힌 상태를 흉내낸다.
        let store = UsageStore(
            preview: snapshot,
            nextPoll: Date().addingTimeInterval(7 * 60 + 12),
            error: state == .ok ? nil : "토큰 만료 — Claude Code 재로그인 필요",
            needsReauth: state == .reauth
        )
        let palette = HUDPalette(isDark: isDark)
        let content = UsageHUDView(
            store: store,
            iconStyle: iconStyle,
            isCollapsed: collapsed,
            palette: palette
        )
            .background {
                ZStack {
                    Color(white: isDark ? 0.42 : 0.55)
                    Color(nsColor: palette.backdrop)
                }
            }
            .clipShape(
                RoundedRectangle(
                    cornerRadius: UsageHUDView.cornerRadius(collapsed: collapsed),
                    style: .continuous
                )
            )

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

    /// 설정 창을 PNG로 렌더한다.
    static func writeSettings(to path: String, isDark: Bool) -> Bool {
        let snapshot = UsageSnapshot(
            planName: "Max",
            fiveHour: UsageWindow(utilization: 34, resetsAt: Date().addingTimeInterval(3 * 3600)),
            sevenDay: UsageWindow(utilization: 61, resetsAt: Date().addingTimeInterval(26 * 3600)),
            fetchedAt: Date()
        )
        let view = SettingsView(
            settings: HUDSettings(defaults: UserDefaults(suiteName: "dong-mcu.preview") ?? .standard),
            store: UsageStore(preview: snapshot),
            actions: SettingsActions(refresh: {}, resetPosition: {}, login: {}, quit: {}),
            version: dongMCUVersion
        )
        .preferredColorScheme(isDark ? .dark : .light)
        .background(Color(nsColor: .windowBackgroundColor))

        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        guard let image = renderer.nsImage,
              let tiff = image.tiffRepresentation,
              let bitmap = NSBitmapImageRep(data: tiff),
              let png = bitmap.representation(using: .png, properties: [:])
        else { return false }
        return (try? png.write(to: URL(fileURLWithPath: path))) != nil
    }
}
