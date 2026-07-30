import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let store = UsageStore()
    private var hud: HUDController?
    private var statusItem: StatusItemController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)  // Dock 아이콘 없음

        let hud = HUDController(store: store)
        hud.show()
        self.hud = hud

        // 메뉴바 아이콘은 HUD와 같은 메뉴를 쓴다.
        statusItem = StatusItemController(store: store) { [weak hud] menu in
            hud?.populateMenu(menu)
        }

        store.start()
    }
}
