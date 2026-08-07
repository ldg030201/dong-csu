import AppKit
import Combine

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let store = UsageStore()
    private let settings = HUDSettings()
    private let updates = UpdateChecker()
    private let meter = UsageMeter()
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
            meter: meter,
            actions: SettingsActions(
                refresh: { [weak store] in store?.refresh(force: true) },
                resetPosition: { [weak hud] in hud?.resetPosition() },
                login: { [weak hud] in hud?.startLogin() },
                quit: { Self.confirmQuit() }
            ),
            preferredScreen: { [weak hud] in hud?.currentScreen }
        )
        self.settingsWindow = settingsWindow
        hud.onOpenSettings = { [weak settingsWindow] in settingsWindow?.show() }

        hud.show()

        // 메뉴바 아이콘은 HUD와 같은 메뉴를 쓴다.
        statusItem = StatusItemController { [weak hud] menu in
            hud?.populateMenu(menu)
        }

        // 조회 주기는 설정을 따른다.
        store.setPollInterval(settings.pollInterval.seconds)
        settings.$pollInterval
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak store] interval in store?.setPollInterval(interval.seconds) }
            .store(in: &cancellables)

        // 측정이 도는 동안 조회 결과를 기록에 붙인다.
        store.onSnapshot = { [weak meter] snapshot in meter?.record(snapshot) }

        store.start()

        // 업데이트 확인은 설정을 따른다. 꺼두면 아무 데도 접속하지 않는다.
        //
        // 테스트판은 확인하지 않는다. 개발 중인 빌드라 릴리스 태그와 비교하는 게 무의미하고,
        // 아직 안 나간 변경을 들고 있으면서 "업데이트 있음"이라고 뜬다.
        if settings.checksForUpdates, !AppInfo.isTestBuild { updates.start() }
        settings.$checksForUpdates
            .dropFirst()
            .receive(on: DispatchQueue.main)
            .sink { [weak updates] enabled in
                guard !AppInfo.isTestBuild else { return }
                if enabled { updates?.start() } else { updates?.stop() }
            }
            .store(in: &cancellables)
    }

    /// 설정 창의 종료 버튼은 실수로 누르기 쉬운 자리라 한 번 확인한다.
    /// 종료하면 메뉴바 아이콘까지 사라져서 다시 켤 방법을 찾아야 한다.
    private static func confirmQuit() {
        let alert = NSAlert()
        alert.messageText = "\(AppInfo.name)를 종료할까요?"
        alert.informativeText = "종료하면 사용량 표시와 메뉴바 아이콘이 모두 사라집니다."
        alert.alertStyle = .warning
        alert.addButton(withTitle: "종료")
        alert.addButton(withTitle: "취소")
        // 실수로 Enter를 눌러도 종료되지 않게 취소를 기본 버튼으로 둔다.
        alert.buttons.first?.keyEquivalent = ""
        alert.buttons.last?.keyEquivalent = "\r"

        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn {
            NSApp.terminate(nil)
        }
    }
}
