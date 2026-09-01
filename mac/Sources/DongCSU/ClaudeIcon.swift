import AppKit
import SwiftUI

/// 가운데 아이콘을 묶는 단위.
///
/// dong-csu가 직접 만든 캐릭터와 Claude 쪽 그림은 출처가 다르다. 섞어 두면
/// 어느 게 이 앱 것인지 알 수 없어서 나눠 보여준다. 캐릭터를 더 만들면
/// `.character` 쪽에 붙으므로 목록을 손볼 자리는 없다.
enum IconStyleGroup: String, CaseIterable {
    case character
    case claude
    /// 예전 것. **접어 둔다** — 지우지는 않되 눈에 먼저 들어오지 않게 한다.
    ///
    /// 새로 오는 사람에게는 고를 것이 하나 늘어나는 것뿐이라 헷갈리고, 쓰던 사람은
    /// 없어졌다고 여기면 곤란하다. 접어 두면 찾는 사람만 찾는다.
    case original

    var title: String {
        switch self {
        case .character: return "캐릭터"
        case .claude: return "Claude"
        case .original: return "오리지널"
        }
    }

    /// 목록에 펼쳐 놓을지. 접힌 묶음은 눌러야 열린다.
    var isCollapsed: Bool { self == .original }

    var styles: [ClaudeIconStyle] {
        ClaudeIconStyle.allCases.filter { $0.group == self }
    }
}

enum ClaudeIconStyle: String, CaseIterable {
    /// **처음에 코드로 만든 부엉이.**
    ///
    /// 파츠를 겹쳐 매 틱 자세를 계산해서, 그림으로는 못 담는 것이 있다 —
    /// 끌 때 몸 → 얼굴 → 다리가 한 틱씩 늦게 따라오는 시차가 그것이다.
    /// 메뉴바·앱 아이콘과 `shared/owl.json` 도 계속 이 코드를 쓴다.
    case owl
    /// 그림 파일 한 장으로 도는 부엉이.
    ///
    /// 번들에 규격 시트가 구워져 있고, **파일을 바꾸면 캐릭터가 바뀐다.**
    /// 앞으로 캐릭터를 더하는 것은 전부 이쪽이다.
    case owlSheet
    /// 그림 파일 한 장으로 도는 라쿤.
    ///
    /// **부엉이와 코드를 하나도 안 나눠 쓴다.** 같은 규격 시트를 읽는 통로에
    /// 파일 이름만 다르게 물린 것이라, 캐릭터를 더하는 값은 `sheetResource` 한 줄이다.
    case raccoonSheet
    /// Claude Code 마스코트 Clawd.
    case clawd
    /// Claude 앱 아이콘. 번들에 넣어둔 이미지를 쓴다.
    case appIcon
    /// 직접 그린 벡터 버스트 마크.
    case mark

    static let `default` = ClaudeIconStyle.owlSheet

    var group: IconStyleGroup {
        switch self {
        case .owlSheet, .raccoonSheet: return .character
        case .owl: return .original
        case .clawd, .appIcon, .mark: return .claude
        }
    }

    var title: String {
        switch self {
        case .owl: return "부엉이 오리지널 (코드로 그린 첫 판)"
        case .owlSheet: return "부엉이 (dong-csu 마스코트)"
        case .raccoonSheet: return "라쿤"
        case .clawd: return "Clawd (Claude Code 마스코트)"
        case .appIcon: return "Claude 아이콘"
        case .mark: return "버스트 마크"
        }
    }

    /// 움직이는 그림인지.
    ///
    /// **Claude 쪽 그림에는 애니메이션을 넣지 않는다.** 저작권이 Anthropic에 있어서
    /// 우리가 새 자세를 만들어 붙일 그림이 아니다. 움직이는 건 이 앱이 직접 만든
    /// 캐릭터뿐이다.
    ///
    /// `group == .character`로 판단하지 않는다. 캐릭터를 새로 그려도 자세와 기분을
    /// 만들기 전까지는 정지 그림이라, 그때 여기에 한 줄을 더하는 게 맞다.
    var isAnimated: Bool {
        switch self {
        case .owl, .owlSheet, .raccoonSheet: return true
        case .clawd, .appIcon, .mark: return false
        }
    }

    /// 미리보기 타일 밑에 붙일 짧은 이름.
    var shortTitle: String {
        switch self {
        case .owl: return "오리지널"
        case .owlSheet: return "부엉이"
        case .raccoonSheet: return "라쿤"
        case .clawd: return "Clawd"
        case .appIcon: return "Claude 아이콘"
        case .mark: return "버스트"
        }
    }

    /// 번들에 구워 둔 규격 시트의 파일 이름(확장자 뺀 것). 시트로 도는 그림만 값이 있다.
    ///
    /// **캐릭터를 더할 때 손대는 곳이 여기다.** 그림을 `Resources/<이름>.png` 로 두고
    /// 여기 한 줄을 더하면 나머지(붙이기 · 미리보기 · 폭 재기)가 전부 따라온다.
    var sheetResource: String? {
        switch self {
        case .owlSheet: return "mascot"
        case .raccoonSheet: return "raccoon"
        case .owl, .clawd, .appIcon, .mark: return nil
        }
    }

    /// 아직 다듬는 중인 캐릭터인지. 타일 위에 `beta` 딱지가 붙는다.
    ///
    /// **고르는 것을 막지는 않는다.** 설정 탭의 `beta` 와 같은 뜻이다 — 써도 되는데
    /// 아직 손볼 데가 남았다는 표시다.
    var isBeta: Bool {
        switch self {
        case .raccoonSheet: return true
        case .owl, .owlSheet, .clawd, .appIcon, .mark: return false
        }
    }

    /// 그림 시트로 도는지. 창에 붙는 자세가 있는 것은 이쪽뿐이다.
    ///
    /// **캐릭터 이름으로 견주지 않는다.** `== .owlSheet` 로 적어 두면 캐릭터를 더할
    /// 때마다 흩어진 자리를 다 찾아 고쳐야 하고, 하나만 빠뜨려도 그 캐릭터에서만
    /// 붙이기가 조용히 죽는다.
    var usesSheet: Bool { sheetResource != nil }
}

enum ClaudeIcon {
    static let claudeAppPath = "/Applications/Claude.app"

    @MainActor private static var cachedImage: NSImage?
    /// 못 찾은 시각. 이만큼 지나면 한 번 더 본다.
    @MainActor private static var missedAt: Date?
    private static let retryAfterMiss: TimeInterval = 30

    /// 아이콘 이미지 해석 순서:
    /// 1) 번들에 넣어둔 claude-icon.png (직접 갈아끼운 이미지)
    /// 2) 설치된 Claude 데스크톱 앱의 실제 아이콘
    ///
    /// View의 body에서 불리므로 결과를 캐시한다. 캐시가 없으면 다시 그릴 때마다
    /// 디스크를 읽고 NSImage를 새로 만든다.
    ///
    /// **못 찾은 것도 잠깐은 기억한다.** 실패를 안 기억하면 Claude 앱이 없는 사람은
    /// 프레임마다 파일을 찾게 되는데, 캐시를 넣은 이유가 바로 그거다. 다만 **영영**
    /// 기억하면 나중에 Claude 앱을 깔아도 다시 띄우기 전에는 안 잡힌다.
    @MainActor
    static func resolveImage() -> NSImage? {
        if let cachedImage { return cachedImage }
        if let missedAt, Date().timeIntervalSince(missedAt) < retryAfterMiss { return nil }

        if let url = Bundle.main.url(forResource: "claude-icon", withExtension: "png"),
           let image = NSImage(contentsOf: url) {
            cachedImage = image
        } else if FileManager.default.fileExists(atPath: claudeAppPath) {
            cachedImage = NSWorkspace.shared.icon(forFile: claudeAppPath)
        }
        missedAt = cachedImage == nil ? Date() : nil
        return cachedImage
    }
}

struct ClaudeIconView: View {
    var style: ClaudeIconStyle
    /// **높이** 기준 크기. 격자 부엉이도 이 값을 높이로 받아서, 어느 그림을 골라도
    /// 같은 크기로 보인다.
    var size: CGFloat
    /// 옆으로 얼마나 퍼져도 되는지. nil 이면 안 막는다.
    ///
    /// **HUD 는 막고 펫은 안 막는다.** HUD 아이콘은 작은 링 **안**에 갇혀야 해서
    /// 넘치면 원을 뚫고 나오지만, 펫은 링이 장식이라 날개를 편 그림이 링 밖으로
    /// 조금 나가는 편이 오히려 맞다.
    var widthLimit: CGFloat?
    var eyeColor: Color = ClawdMark.defaultEyeColor
    /// 부엉이를 움직이게 할 애니메이터. 없으면 정지 자세로 그린다(렌더 통로).
    var owlAnimator: OwlAnimator?

    private let claudeOrange = Color(red: 0.85, green: 0.46, blue: 0.34)

    var body: some View {
        switch style {
        case .owlSheet, .raccoonSheet:
            // **파일에서 읽는다.** 번들에 규격 시트가 구워져 있고, 사용자 그림과
            // 똑같은 통로를 탄다. 시트가 없는 건 빌드가 깨진 것이므로 격자로 그리는
            // 쪽으로 떨어뜨려 화면이 비지는 않게 한다.
            spriteBody(MascotSpriteStore.bundled(style))
        case .owl:
            // 코드로 그리는 첫 판. 보관용이라 통로를 그대로 둔다.
            owl
                .frame(height: size)
                .shadow(color: .black.opacity(0.45), radius: 2, y: 1)
        case .clawd:
            ClawdMark(eyeColor: eyeColor)
                .frame(width: size)
                .shadow(color: .black.opacity(0.45), radius: 2, y: 1)
        case .appIcon:
            if let image = ClaudeIcon.resolveImage() {
                Image(nsImage: image)
                    .resizable()
                    .interpolation(.high)
                    .frame(width: size, height: size)
                    .clipShape(RoundedRectangle(cornerRadius: size * 0.24, style: .continuous))
                    .shadow(color: .black.opacity(0.45), radius: 2, y: 1)
            } else {
                mark
            }
        case .mark:
            mark
        }
    }

    /// 그림 묶음 하나를 그린다. 기본이든 사용자 것이든 같은 통로다.
    @ViewBuilder
    private func spriteBody(_ set: MascotSpriteSet?) -> some View {
        if let set {
            if let owlAnimator {
                // 지켜봐야 상태가 바뀔 때 다시 그린다.
                AnimatedMascotSpriteView(
                    animator: owlAnimator, set: set, size: size, widthLimit: widthLimit
                )
            } else {
                // 렌더 통로에는 애니메이터가 없다. 정지 자세로 그린다.
                MascotSpriteView(
                    set: set, sprite: .idle, flipped: false,
                    size: size, widthLimit: widthLimit
                )
            }
        } else {
            owl.frame(height: size).shadow(color: .black.opacity(0.45), radius: 2, y: 1)
        }
    }

    @ViewBuilder private var owl: some View {
        if let owlAnimator {
            AnimatedOwlView(animator: owlAnimator)
        } else {
            OwlMarkView(palette: .normal)
        }
    }

    private var mark: some View {
        ClaudeMark()
            .fill(claudeOrange)
            .frame(width: size, height: size)
            .shadow(color: .black.opacity(0.4), radius: 1.5)
    }
}
