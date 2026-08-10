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

    /// 버튼 설명이 뜨기까지 기다리는 시간을 줄인다.
    ///
    /// AppKit 기본값은 **3초에 가깝다.** 설정 창처럼 글자가 함께 있는 화면에서는 그래도
    /// 되지만, HUD·펫은 그림뿐이라 설명이 안 뜨면 눌러 보는 수밖에 없다. 3초를 기다리느니
    /// 그냥 눌러 보게 되므로 있으나 마나 한 설명이 된다.
    ///
    /// **덮어쓰지 않고 등록만 한다.** 사용자가 이 값을 직접 정해 뒀다면 그쪽이 이긴다.
    private static func speedUpToolTips() {
        UserDefaults.standard.register(defaults: ["NSInitialToolTipDelay": 1000])
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)  // Dock 아이콘 없음
        Self.speedUpToolTips()

        let hud = HUDController(store: store, settings: settings, updates: updates, meter: meter)
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
        // 시작·계속·중지를 누르면 다음 폴링을 기다리지 않고 곧바로 기준점을 잡는다.
        // **force 로 쏘지 않는다** — 429 백오프를 무시하게 되어 요청 제한을 더 부른다.
        meter.onNeedsSample = { [weak store] in store?.refresh() }

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
