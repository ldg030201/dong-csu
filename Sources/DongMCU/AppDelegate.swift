import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let store = UsageStore()
    private var hud: HUDController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)  // Dock 아이콘 없음
        let hud = HUDController(store: store)
        hud.show()
        self.hud = hud
        store.start()
    }
}
