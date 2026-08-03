import Combine
import Foundation

/// 사용자가 바꿀 수 있는 설정을 한곳에 모은다.
///
/// 메뉴·설정 창·HUD가 모두 이 객체를 보고 움직인다. 예전처럼 컨트롤러 곳곳에서
/// UserDefaults를 직접 읽고 쓰면 화면마다 상태가 어긋나기 쉽다.
@MainActor
final class HUDSettings: ObservableObject {
    @Published var appearance: HUDAppearance {
        didSet { defaults.set(appearance.rawValue, forKey: Keys.appearance) }
    }

    @Published var iconStyle: ClaudeIconStyle {
        didSet { defaults.set(iconStyle.rawValue, forKey: Keys.iconStyle) }
    }

    @Published var isCollapsed: Bool {
        didSet { defaults.set(isCollapsed, forKey: Keys.collapsed) }
    }

    @Published var isHUDVisible: Bool {
        didSet { defaults.set(!isHUDVisible, forKey: Keys.hidden) }
    }

    private let defaults: UserDefaults

    private enum Keys {
        static let appearance = "hud.appearance"
        static let iconStyle = "hud.iconStyle"
        static let collapsed = "hud.collapsed"
        static let hidden = "hud.hidden"
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        appearance = HUDAppearance(rawValue: defaults.string(forKey: Keys.appearance) ?? "") ?? .default
        iconStyle = ClaudeIconStyle(rawValue: defaults.string(forKey: Keys.iconStyle) ?? "") ?? .default
        isCollapsed = defaults.bool(forKey: Keys.collapsed)
        isHUDVisible = !defaults.bool(forKey: Keys.hidden)
    }
}

/// 설정 창에서 눌렀을 때 실제로 무언가를 하는 동작들.
/// 창이 HUDController를 직접 알 필요가 없게 클로저로 넘긴다.
@MainActor
struct SettingsActions {
    var refresh: () -> Void
    var resetPosition: () -> Void
    var login: () -> Void
    var quit: () -> Void
}
