import Combine
import Foundation

/// HUD가 펼쳐지는 방향. 접힌 링을 손잡이 삼아 서랍처럼 열린다.
enum HUDExpandSide: String, CaseIterable {
    /// 왼쪽 가장자리를 고정하고 오른쪽으로 펼친다.
    case right
    /// 오른쪽 가장자리를 고정하고 왼쪽으로 펼친다.
    case left

    static let `default` = HUDExpandSide.right

    var title: String {
        switch self {
        case .right: return "오른쪽으로 펼치기"
        case .left: return "왼쪽으로 펼치기"
        }
    }
}

/// HUD가 얼마나 보일지. 더블클릭하면 이 순서로 돈다.
enum HUDMode: String, CaseIterable, TitledOption {
    /// 링 + 사용량 숫자.
    case expanded
    /// 링만. 접힌 링을 손잡이 삼아 서랍처럼 열린다.
    case collapsed
    /// 마스코트만. 배경도 링도 없이 캐릭터 하나만 떠 있는다.
    case pet

    static let `default` = HUDMode.expanded

    var title: String {
        switch self {
        case .expanded: return "펼침"
        case .collapsed: return "링만"
        case .pet: return "펫"
        }
    }

    /// 더블클릭으로 접었다 폈다 할 때의 반대쪽.
    ///
    /// **펫은 여기 끼우지 않는다.** 셋을 한 줄로 돌리면 접으려다 펫으로 넘어가서,
    /// 원래 있던 접기 동작이 무엇을 할지 예측할 수 없어진다.
    /// 펫으로는 마스코트를 직접 더블클릭해서 들어간다.
    var toggled: HUDMode {
        self == .expanded ? .collapsed : .expanded
    }

    /// 카드 배경·테두리를 그리는지. 펫은 캐릭터만 떠 있어야 한다.
    var showsBackdrop: Bool { self != .pet }
}

/// 펫 모드에서 뒤에 두르는 사용량 링을 언제 보여줄지.
enum PetRingDisplay: String, CaseIterable, TitledOption {
    /// 마우스를 올렸을 때만. 평소에는 마스코트만 남는다.
    case hover
    /// 늘 보인다.
    case always
    /// 보이지 않는다. 사용량은 메뉴바나 설정 창에서 본다.
    case never

    static let `default` = PetRingDisplay.hover

    var title: String {
        switch self {
        case .hover: return "마우스를 올리면"
        case .always: return "항상 표시"
        case .never: return "표시 안 함"
        }
    }
}

/// HUD 전체 크기.
///
/// 치수와 글자 크기에 이 배율을 곱한다. `scaleEffect`로 확대하지 않는 이유는
/// 마스코트가 픽셀 그림이라 확대하면 흐려지기 때문이다. 배율을 곱해서 다시 그리면
/// 한 칸도 그만큼 커져서 큰 크기에서 오히려 더 선명해진다.
enum HUDScale: String, CaseIterable, TitledOption {
    case small
    case normal
    case large
    case extraLarge

    static let `default` = HUDScale.normal

    var factor: CGFloat {
        switch self {
        case .small: return 0.85
        case .normal: return 1
        case .large: return 1.25
        case .extraLarge: return 1.5
        }
    }

    var title: String {
        switch self {
        case .small: return "작게"
        case .normal: return "보통"
        case .large: return "크게"
        case .extraLarge: return "매우 크게"
        }
    }
}

/// 사용량을 얼마나 자주 조회할지.
/// 너무 조이면 429가 나므로 임의의 초가 아니라 정해진 값 중에서 고르게 한다.
enum PollInterval: Int, CaseIterable {
    case oneMinute = 60
    case threeMinutes = 180
    case fiveMinutes = 300
    case tenMinutes = 600
    case thirtyMinutes = 1800

    static let `default` = PollInterval.tenMinutes

    var seconds: TimeInterval { TimeInterval(rawValue) }

    var title: String {
        rawValue < 3600 ? "\(rawValue / 60)분마다" : "\(rawValue / 3600)시간마다"
    }
}

/// 사용자가 바꿀 수 있는 설정을 한곳에 모은다.
///
/// 메뉴·설정 창·HUD가 모두 이 객체를 보고 움직인다. 예전처럼 컨트롤러 곳곳에서
/// UserDefaults를 직접 읽고 쓰면 화면마다 상태가 어긋나기 쉽다.
@MainActor
final class HUDSettings: ObservableObject {
    @Published var appearance: HUDAppearance = .default {
        didSet { defaults.set(appearance.rawValue, forKey: Keys.appearance) }
    }

    @Published var iconStyle: ClaudeIconStyle = .default {
        didSet { defaults.set(iconStyle.rawValue, forKey: Keys.iconStyle) }
    }


    @Published var mode: HUDMode = .default {
        didSet { defaults.set(mode.rawValue, forKey: Keys.mode) }
    }

    @Published var petRingDisplay: PetRingDisplay = .default {
        didSet { defaults.set(petRingDisplay.rawValue, forKey: Keys.petRingDisplay) }
    }

    /// 펫 모드로 들어가기 직전의 보기. 나올 때 여기로 돌아간다.
    /// 접어 두고 쓰다 펫에 들렀는데 나올 때 펼쳐져 있으면 놀란다.
    @Published var modeBeforePet: HUDMode = .expanded

    @Published var isHUDVisible: Bool = true {
        didSet { defaults.set(!isHUDVisible, forKey: Keys.hidden) }
    }

    @Published var expandSide: HUDExpandSide = .default {
        didSet { defaults.set(expandSide.rawValue, forKey: Keys.expandSide) }
    }

    @Published var scale: HUDScale = .default {
        didSet { defaults.set(scale.rawValue, forKey: Keys.scale) }
    }

    /// 새 버전이 나왔는지 하루 한 번 GitHub에 물어볼지.
    /// 끄면 이 앱은 Anthropic API 외에 아무 데도 접속하지 않는다.
    @Published var checksForUpdates: Bool = true {
        didSet { defaults.set(!checksForUpdates, forKey: Keys.updateCheckOff) }
    }

    /// HUD 배경 불투명도. 너무 투명하면 글자가 안 읽혀서 아래를 막아둔다.
    @Published var backdropOpacity: Double = HUDSettings.defaultOpacity {
        didSet {
            let clamped = min(Self.maxOpacity, max(Self.minOpacity, backdropOpacity))
            if clamped != backdropOpacity {
                backdropOpacity = clamped
                return
            }
            defaults.set(backdropOpacity, forKey: Keys.backdropOpacity)
        }
    }

    @Published var pollInterval: PollInterval = .default {
        didSet { defaults.set(pollInterval.rawValue, forKey: Keys.pollInterval) }
    }

    /// 설정 창에서 열려 있는 탭. 메뉴에서 특정 탭을 바로 열 수 있어야 해서
    /// 창 밖에 둔다. 다음 실행까지 기억할 값은 아니라 저장하지 않는다.
    @Published var settingsTab: SettingsTab = .status

    /// 로그인할 때 저절로 뜰지.
    ///
    /// **UserDefaults에 저장하지 않는다.** 등록 상태는 시스템이 갖고 있고 사용자가 시스템
    /// 설정에서 직접 끌 수 있어서, 여기 값은 그걸 비추어 보여 주는 것뿐이다. 따로 적어 두면
    /// 그쪽에서 끈 뒤에도 켜진 것으로 보인다.
    @Published var startsAtLogin: Bool = LoginItem.isEnabled {
        didSet {
            // 실제와 이미 같으면 아무것도 하지 않는다. 아래 되돌리기가 여기서 멈춘다.
            guard startsAtLogin != LoginItem.isEnabled else { return }
            if !LoginItem.setEnabled(startsAtLogin) {
                // 켜졌다고 보이는데 안 뜨는 게 제일 나쁘다. 표시를 실제로 되돌린다.
                startsAtLogin = LoginItem.isEnabled
            }
        }
    }

    /// 창을 열 때마다 실제 등록 상태로 다시 맞춘다.
    /// 사용자가 시스템 설정에서 껐다 켰을 수 있다.
    func refreshLoginItem() {
        startsAtLogin = LoginItem.isEnabled
    }

    /// HUD 왼쪽 아래에 이 앱의 CPU·메모리를 표시할지.
    @Published var showsProcessStats: Bool = false {
        didSet { defaults.set(showsProcessStats, forKey: Keys.showsProcessStats) }
    }

    /// 측정 화면에서 캐시 토큰까지 세어 보여줄지.
    ///
    /// **기본은 꺼짐이다.** 캐시 읽기가 보통 전체의 90% 넘게 차지해서, 켜 두면 어느
    /// 측정이나 억 단위로 보이고 실제로 주고받은 양이 묻힌다. 캐시는 같은 글을 다시
    /// 보내지 않으려고 서버가 들고 있는 것이라 단가도 입력의 1/10이다.
    @Published var measureIncludesCache: Bool = false {
        didSet { defaults.set(measureIncludesCache, forKey: Keys.measureIncludesCache) }
    }

    /// 가운데 마스코트를 움직일지.
    ///
    /// 움직임 자체가 거슬리는 사람이 있고, 배터리를 아끼고 싶을 때도 끈다.
    /// **정지 그림인 아이콘에는 애초에 걸리지 않는다** — `ClaudeIconStyle.isAnimated` 참고.
    @Published var animatesIcon: Bool = true {
        didSet { defaults.set(!animatesIcon, forKey: Keys.iconAnimationOff) }
    }

    /// HUD 왼쪽 위에 버전을 표시할지. 테스트판은 뒤에 `test`가 붙는다.
    @Published var showsVersionBadge: Bool = true {
        didSet { defaults.set(!showsVersionBadge, forKey: Keys.versionBadgeOff) }
    }

    /// 펫이 가만히 두면 혼자 화면을 걸어다닐지.
    @Published var petWanders: Bool = true {
        didSet { defaults.set(!petWanders, forKey: Keys.petWandersOff) }
    }

    /// 모델별로 갈린 주간 한도(예: Fable)를 같이 보여줄지.
    ///
    /// **기본은 꺼짐이다.** 서버가 줄 때만 있는 값이라 켜 놓으면 어떤 사람에게는
    /// 아무것도 안 늘고, 링만 하나 더 그려서 셋이 겹쳐 보인다. 쓰는 사람이 켠다.
    @Published var showsScopedLimit: Bool = false {
        didSet { defaults.set(showsScopedLimit, forKey: Keys.showsScopedLimit) }
    }

    /// 배회할 때 다른 화면으로 걸어 넘어갈지.
    ///
    /// **기본은 꺼짐이다.** 켜 두면 마스코트가 옆 화면으로 사라져서, 보려고 켜 둔
    /// 사람이 어디 갔는지 찾게 된다. 화면이 하나뿐인 사람에게는 아무 일도 안 한다.
    @Published var petCrossesScreens: Bool = false {
        didSet { defaults.set(petCrossesScreens, forKey: Keys.petCrossesScreens) }
    }

    /// 커서를 펫 위에 올려둔 채 잡지 않으면 비켜줄지.
    @Published var petDodgesCursor: Bool = true {
        didSet { defaults.set(!petDodgesCursor, forKey: Keys.petDodgesCursorOff) }
    }

    /// 끌어다 놓으면 다른 앱 창 테두리에 붙을지.
    @Published var petPerches: Bool = true {
        didSet { defaults.set(!petPerches, forKey: Keys.petPerchesOff) }
    }

    /// 집어 들고 있는 동안 링과 버튼 줄을 감출지.
    @Published var petHidesRingWhileHeld: Bool = true {
        didSet { defaults.set(!petHidesRingWhileHeld, forKey: Keys.petRingWhileHeldOff) }
    }

    /// 창 위 테두리에 걸터앉을 때 창 안으로 넘어가는 깊이(잉크 세로에 대한 비율).
    ///
    /// **그림마다 맞는 값이 다르다.** 규격은 "걸터앉기의 아래 15%는 다리와 발"이라고
    /// 못 박아 두었지만, 그리는 쪽이 그걸 정확히 맞추지 못한다 — 실제로 받아 본 시트는
    /// 매달리기 칸의 발·다리가 위 24%를 차지해서 15%로는 다리가 창 밖에 삐져나왔다.
    /// 그림을 다시 받는 대신 여기서 맞춘다.
    @Published var perchDepthTop: Double = MascotSprite.sit.gripDepth {
        didSet { defaults.set(perchDepthTop, forKey: Keys.perchDepthTop) }
    }
    /// 창 아래 테두리에 매달릴 때의 깊이.
    @Published var perchDepthBottom: Double = MascotSprite.ledge.gripDepth {
        didSet { defaults.set(perchDepthBottom, forKey: Keys.perchDepthBottom) }
    }
    /// 창 좌우 테두리를 껴안을 때의 깊이(잉크 **가로**에 대한 비율).
    @Published var perchDepthSide: Double = MascotSprite.cling.gripDepth {
        didSet { defaults.set(perchDepthSide, forKey: Keys.perchDepthSide) }
    }

    /// 손으로 맞출 수 있는 깊이의 위 끝.
    ///
    /// **자리가 모자랄 때 앱이 보태 주는 몫의 한계(`UsageHUDView.maxAutoExtra`, 12%)와
    /// 다른 값이다.** 저쪽은 "조금 모자라면 보태 준다"는 자동 보정이라 몸이 잠기기 전에
    /// 멈춰야 하지만, 여기는 사람이 눈으로 보고 정하는 값이라 막을 이유가 없다.
    /// 그림마다 붙잡는 부위가 어디까지인지가 달라서 60% 가 필요한 그림도 있다.
    static let maxPerchDepth: Double = 0.6

    /// 붙는 깊이를 규격 기본값으로 되돌린다.
    func resetPerchDepths() {
        perchDepthTop = MascotSprite.sit.gripDepth
        perchDepthBottom = MascotSprite.ledge.gripDepth
        perchDepthSide = MascotSprite.cling.gripDepth
    }

    /// 저장된 붙는 깊이. **설정 인스턴스 없이 읽는다.**
    ///
    /// 자리 계산(`UsageHUDView.petPerchSink`)이 이걸 봐야 하는데 거기는 정적 함수라
    /// 설정을 들고 있지 않다. 값을 인자로 흘려보내는 길도 있었지만, 그러면 진단
    /// 통로(`--probe-perch`)가 설정을 모른 채 다른 숫자를 내서 **표와 실제가 갈린다.**
    /// 읽기만 하므로 `init` 의 되쓰기(마이그레이션)를 타지 않는다.
    static func storedGripDepth(
        _ perch: MascotPerch, defaults: UserDefaults = .standard
    ) -> CGFloat? {
        let key: String
        switch perch {
        case .top: key = Keys.perchDepthTop
        case .bottom: key = Keys.perchDepthBottom
        case .left, .right: key = Keys.perchDepthSide
        }
        guard let value = defaults.object(forKey: key) as? Double else { return nil }
        return CGFloat(value)
    }

    /// 보기를 바꾼다. **펫에서 나올 때 어디로 돌아갈지 기억한다.**
    ///
    /// 설정 창과 메뉴 두 곳에서 부르므로 여기 한 곳에 둔다 — 두 곳에 적으면 한쪽에서만
    /// 바꿨을 때 펫에서 나오는 자리가 달라진다.
    func setMode(_ next: HUDMode) {
        if next == .pet, mode != .pet { modeBeforePet = mode }
        mode = next
    }

    static let minOpacity = 0.35
    static let maxOpacity = 1.0
    /// 배경 불투명도 기본값.
    ///
    /// **이미 쓰던 사람에게는 적용되지 않는다** — 저장된 값이 있으면 그걸 쓴다.
    /// 새로 깔거나 설정을 초기화했을 때의 값이다.
    static let defaultOpacity = 1.0

    private let defaults: UserDefaults

    /// 옛 이름(`dong-mcu`)에서 쓰던 설정을 **한 번만** 옮겨 온다.
    ///
    /// 이름을 바꾸면 번들 ID가 달라지고, 번들 ID가 달라지면 UserDefaults 도메인이
    /// 통째로 갈린다. 그대로 두면 쓰던 사람의 창 위치·아이콘·크기·펫 설정이 전부
    /// 초기화된다. 이 앱은 샌드박스가 아니라서 옛 도메인을 그냥 읽을 수 있다.
    ///
    /// 새 도메인에 이미 있는 키는 건드리지 않는다 — 옮기기 전에 사용자가 손댄 값을
    /// 옛 값으로 되돌리면 안 된다.
    private static func migrateLegacyDefaults(into defaults: UserDefaults) {
        guard !defaults.bool(forKey: Keys.migratedFromLegacy) else { return }
        defaults.set(true, forKey: Keys.migratedFromLegacy)

        // 정식판은 정식판에서, 테스트판은 테스트판에서 가져온다.
        guard let id = Bundle.main.bundleIdentifier, id.contains("dong-csu") else { return }
        let legacy = id.replacingOccurrences(of: "dong-csu", with: "dong-mcu")
        guard let stored = defaults.persistentDomain(forName: legacy) else { return }

        for (key, value) in stored where defaults.object(forKey: key) == nil {
            defaults.set(value, forKey: key)
        }
    }

    private enum Keys {
        /// 옛 도메인에서 한 번 옮겨 왔는지. 두 번 옮기면 사용자가 그 사이에 바꾼 값이 밀린다.
        static let migratedFromLegacy = "migratedFromDongMCU"

        static let appearance = "hud.appearance"
        static let iconStyle = "hud.iconStyle"
        /// 그림 부엉이로 한 번 옮겼는지. **한 번만 옮긴다** — 두 번 옮기면 그 사이에
        /// 오리지널로 되돌려 놓은 사람의 선택을 매번 덮는다.
        static let movedToSheetOwl = "hud.movedToSheetOwl"
        static let mode = "hud.mode"
        /// 모드가 생기기 전에 쓰던 접힘 여부. 새로 저장하지는 않고 읽기만 한다.
        static let collapsed = "hud.collapsed"
        static let hidden = "hud.hidden"
        static let expandSide = "hud.expandSide"
        static let scale = "hud.scale"
        static let petRingDisplay = "hud.petRingDisplay"
        // 기본값을 켜짐으로 두려고 반대 의미로 저장한다.
        static let updateCheckOff = "hud.updateCheckOff"
        static let backdropOpacity = "hud.backdropOpacity"
        static let showsProcessStats = "hud.showsProcessStats"
        static let measureIncludesCache = "measure.includesCache"
        static let pollInterval = "hud.pollInterval"
        // 버전 표시도 기본값이 켜짐이라 반대 의미로 저장한다.
        static let versionBadgeOff = "hud.versionBadgeOff"
        // 애니메이션도 기본값이 켜짐이라 반대 의미로 저장한다.
        static let iconAnimationOff = "hud.iconAnimationOff"
        // 셋 다 기본값이 켜짐이라 반대 의미로 저장한다.
        static let petWandersOff = "pet.wandersOff"
        static let petDodgesCursorOff = "pet.dodgesCursorOff"
        static let petPerchesOff = "pet.perchesOff"
        static let petRingWhileHeldOff = "pet.hidesRingWhileHeldOff"
        // 이건 기본값이 꺼짐이라 그대로 저장한다.
        static let petCrossesScreens = "pet.crossesScreens"
        static let showsScopedLimit = "hud.showsScopedLimit"
        // 붙는 깊이는 기본값이 0이 아니라 그대로 저장한다. 없으면 규격 기본값.
        static let perchDepthTop = "pet.gripDepth.top"
        static let perchDepthBottom = "pet.gripDepth.bottom"
        static let perchDepthSide = "pet.gripDepth.side"
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        // 앱 이름이 바뀌면 UserDefaults 도메인이 갈린다. 읽기 전에 옛 것을 옮겨 온다.
        if defaults == .standard { Self.migrateLegacyDefaults(into: defaults) }
        load()
    }

    /// 저장된 값을 전부 다시 읽어 온다. `init`과 `resetAll()`이 같이 쓴다.
    private func load() {
        appearance = HUDAppearance(rawValue: defaults.string(forKey: Keys.appearance) ?? "") ?? .default
        iconStyle = ClaudeIconStyle(rawValue: defaults.string(forKey: Keys.iconStyle) ?? "") ?? .default
        // **쓰던 사람도 새 부엉이로 한 번 옮긴다.** 기본값만 바꾸면 아무도 안 바뀐다 —
        // `load()` 가 읽자마자 didSet 으로 되쓰기 때문에, 손도 안 댄 사람까지 `owl` 이
        // 저장돼 있다. 그래서 "고른 것"과 "그냥 깔린 것"을 가릴 방법이 없다.
        // 오리지널을 일부러 쓰던 사람은 아이콘 탭에서 한 번에 되돌린다.
        if !defaults.bool(forKey: Keys.movedToSheetOwl) {
            defaults.set(true, forKey: Keys.movedToSheetOwl)
            if iconStyle == .owl { iconStyle = .owlSheet }
        }
        // 모드가 생기기 전에 접어 두고 쓰던 사람은 그대로 접힌 채로 시작한다.
        // 이걸 빠뜨리면 업데이트하는 순간 HUD가 제멋대로 펼쳐진다.
        if let stored = HUDMode(rawValue: defaults.string(forKey: Keys.mode) ?? "") {
            mode = stored
        } else {
            mode = defaults.bool(forKey: Keys.collapsed) ? .collapsed : .default
        }
        isHUDVisible = !defaults.bool(forKey: Keys.hidden)
        expandSide = HUDExpandSide(rawValue: defaults.string(forKey: Keys.expandSide) ?? "") ?? .default
        scale = HUDScale(rawValue: defaults.string(forKey: Keys.scale) ?? "") ?? .default
        petRingDisplay = PetRingDisplay(rawValue: defaults.string(forKey: Keys.petRingDisplay) ?? "")
            ?? .default
        checksForUpdates = !defaults.bool(forKey: Keys.updateCheckOff)
        let stored = defaults.object(forKey: Keys.backdropOpacity) as? Double
        backdropOpacity = stored.map { min(Self.maxOpacity, max(Self.minOpacity, $0)) } ?? Self.defaultOpacity
        showsProcessStats = defaults.bool(forKey: Keys.showsProcessStats)
        // 없으면 false — 기본이 미포함이라 그대로 맞다.
        measureIncludesCache = defaults.bool(forKey: Keys.measureIncludesCache)
        pollInterval = PollInterval(rawValue: defaults.integer(forKey: Keys.pollInterval)) ?? .default
        showsVersionBadge = !defaults.bool(forKey: Keys.versionBadgeOff)
        animatesIcon = !defaults.bool(forKey: Keys.iconAnimationOff)
        // 펫 모드를 고른 사람은 마스코트를 보려고 고른 것이다. 꺼 둔 채로 내보니
        // 설정을 열어보기 전까지 이런 게 있는 줄도 모른 채 지나갔다.
        petWanders = !defaults.bool(forKey: Keys.petWandersOff)
        petDodgesCursor = !defaults.bool(forKey: Keys.petDodgesCursorOff)
        petPerches = !defaults.bool(forKey: Keys.petPerchesOff)
        petHidesRingWhileHeld = !defaults.bool(forKey: Keys.petRingWhileHeldOff)
        petCrossesScreens = defaults.bool(forKey: Keys.petCrossesScreens)
        showsScopedLimit = defaults.bool(forKey: Keys.showsScopedLimit)
        // 저장된 값이 없으면 규격 기본값. `?? 0` 으로 두면 안 붙는 것처럼 보인다.
        perchDepthTop = Self.storedGripDepth(.top, defaults: defaults)
            .map(Double.init) ?? MascotSprite.sit.gripDepth
        perchDepthBottom = Self.storedGripDepth(.bottom, defaults: defaults)
            .map(Double.init) ?? MascotSprite.ledge.gripDepth
        perchDepthSide = Self.storedGripDepth(.left, defaults: defaults)
            .map(Double.init) ?? MascotSprite.cling.gripDepth
    }

    /// 저장해 둔 설정을 전부 지우고 기본값으로 되돌린다.
    ///
    /// **키를 하나씩 지우지 않는다.** 창 위치(`hud.origin.*`)나 지난 기능이 남긴 값처럼
    /// `Keys`에 없는 것들이 있어서, 목록을 손으로 관리하면 반드시 빠뜨린다. 도메인을
    /// 통째로 비우고 **다시 옮겨 오지 않게 하는 표시만** 남긴다 — 그게 지워지면
    /// 다음 실행 때 옛 `dong-mcu` 설정이 되살아나서 초기화가 아니게 된다.
    func resetAll() {
        guard let domain = Bundle.main.bundleIdentifier else { return }
        let alreadyMigrated = defaults.bool(forKey: Keys.migratedFromLegacy)

        defaults.removePersistentDomain(forName: domain)
        if alreadyMigrated { defaults.set(true, forKey: Keys.migratedFromLegacy) }

        // 로그인 항목은 UserDefaults가 아니라 시스템이 들고 있다. 설정 창에서 켜고 끄는
        // 것으로 보이므로 여기서도 함께 끈다.
        LoginItem.setEnabled(false)

        load()
        startsAtLogin = LoginItem.isEnabled
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
