import AppKit
import Combine

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let store = UsageStore()
    private let settings = HUDSettings()
    private let updates = UpdateChecker()
    private var hud: HUDController?
    private var statusItem: StatusItemController?
    private var settingsWindow: SettingsWindowController?
    private var cancellables: Set<AnyCancellable> = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)  // Dock 아이콘 없음

        let hud = HUDController(store: store, settings: settings, updates: updates)
        self.hud = hud

        let settingsWindow = SettingsWindowController(
            settings: settings,
            store: store,
            updates: updates,
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

        // 조회 주기는 설정을 따른다.
        store.setPollInterval(settings.pollInterval.seconds)
        settings.$pollInterval
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak store] interval in store?.setPollInterval(interval.seconds) }
            .store(in: &cancellables)

        store.start()

        // 업데이트 확인은 설정을 따른다. 꺼두면 아무 데도 접속하지 않는다.
        if settings.checksForUpdates { updates.start() }
        settings.$checksForUpdates
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak updates] enabled in
                if enabled { updates?.start() } else { updates?.stop() }
            }
            .store(in: &cancellables)
    }
}
