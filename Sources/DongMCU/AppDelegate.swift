import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let store = UsageStore()
    private let settings = HUDSettings()
    private var hud: HUDController?
    private var statusItem: StatusItemController?
    private var settingsWindow: SettingsWindowController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)  // Dock 아이콘 없음

        let hud = HUDController(store: store, settings: settings)
        self.hud = hud

        let settingsWindow = SettingsWindowController(
            settings: settings,
            store: store,
            actions: SettingsActions(
                refresh: { [weak store] in store?.refresh(force: true) },
                resetPosition: { [weak hud] in hud?.resetPosition() },
                login: { [weak hud] in hud?.startLogin() },
                quit: { NSApp.terminate(nil) }
            ),
            preferredScreen: { [weak hud] in hud?.currentScreen }
        )
        self.settingsWindow = settingsWindow
        hud.onOpenSettings = { [weak settingsWindow] in settingsWindow?.show() }

        hud.show()

        // 메뉴바 아이콘은 HUD와 같은 메뉴를 쓴다.
        statusItem = StatusItemController(store: store) { [weak hud] menu in
            hud?.populateMenu(menu)
        }

        store.start()
    }
}
