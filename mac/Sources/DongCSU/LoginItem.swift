import AppKit
import ServiceManagement

/// 로그인할 때 앱이 저절로 뜨게 한다.
///
/// **값을 우리가 들고 있지 않는다.** 등록 상태는 시스템이 갖고 있고, 사용자가 시스템
/// 설정 > 일반 > 로그인 항목에서 직접 끌 수 있다. UserDefaults에 따로 적어 두면 그쪽에서
/// 끈 뒤에도 설정 창에는 켜진 것으로 보인다 — 켜졌다고 하는데 안 뜨는 게 제일 나쁘다.
/// 그래서 항상 `SMAppService`에 물어본다.
enum LoginItem {
    /// 지금 실제로 등록되어 있나.
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    /// 사용자가 시스템 설정에서 꺼 둔 상태. 여기서 다시 켤 수 없고 그쪽에서 켜야 한다.
    static var needsSystemSettings: Bool { SMAppService.mainApp.status == .requiresApproval }

    /// 켜거나 끈다. 성공했으면 `true`.
    ///
    /// 실패해도 던지지 않는다 — 부르는 쪽이 할 수 있는 일이 "표시를 되돌린다" 뿐이라
    /// 오류 종류를 구분할 값어치가 없다.
    @discardableResult
    static func setEnabled(_ enabled: Bool) -> Bool {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            return true
        } catch {
            return false
        }
    }

    static func openSystemSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
