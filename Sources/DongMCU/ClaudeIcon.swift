import AppKit
import SwiftUI

/// 가운데 아이콘을 묶는 단위.
///
/// dong-mcu가 직접 만든 캐릭터와 Claude 쪽 그림은 출처가 다르다. 섞어 두면
/// 어느 게 이 앱 것인지 알 수 없어서 나눠 보여준다. 캐릭터를 더 만들면
/// `.character` 쪽에 붙으므로 목록을 손볼 자리는 없다.
enum IconStyleGroup: String, CaseIterable {
    case character
    case claude

    var title: String {
        switch self {
        case .character: return "캐릭터"
        case .claude: return "Claude"
        }
    }

    var styles: [ClaudeIconStyle] {
        ClaudeIconStyle.allCases.filter { $0.group == self }
    }
}

enum ClaudeIconStyle: String, CaseIterable {
    /// dong-mcu 마스코트 부엉이.
    case owl
    /// Claude Code 마스코트 Clawd.
    case clawd
    /// Claude 앱 아이콘. 번들에 넣어둔 이미지를 쓴다.
    case appIcon
    /// 직접 그린 벡터 버스트 마크.
    case mark

    static let `default` = ClaudeIconStyle.owl

    var group: IconStyleGroup {
        switch self {
        case .owl: return .character
        case .clawd, .appIcon, .mark: return .claude
        }
    }

    var title: String {
        switch self {
        case .owl: return "부엉이 (dong-mcu 마스코트)"
        case .clawd: return "Clawd (Claude Code 마스코트)"
        case .appIcon: return "Claude 아이콘"
        case .mark: return "버스트 마크"
        }
    }

    /// 미리보기 타일 밑에 붙일 짧은 이름.
    var shortTitle: String {
        switch self {
        case .owl: return "부엉이"
        case .clawd: return "Clawd"
        case .appIcon: return "Claude 아이콘"
        case .mark: return "버스트"
        }
    }
}

enum ClaudeIcon {
    static let claudeAppPath = "/Applications/Claude.app"

    @MainActor private static var cachedImage: NSImage?
    @MainActor private static var didResolve = false

    /// 아이콘 이미지 해석 순서:
    /// 1) 번들에 넣어둔 claude-icon.png (직접 갈아끼운 이미지)
    /// 2) 설치된 Claude 데스크톱 앱의 실제 아이콘
    ///
    /// View의 body에서 불리므로 결과를 캐시한다. 캐시가 없으면 다시 그릴 때마다
    /// 디스크를 읽고 NSImage를 새로 만든다.
    @MainActor
    static func resolveImage() -> NSImage? {
        if didResolve { return cachedImage }
        didResolve = true

        if let url = Bundle.main.url(forResource: "claude-icon", withExtension: "png"),
           let image = NSImage(contentsOf: url) {
            cachedImage = image
        } else if FileManager.default.fileExists(atPath: claudeAppPath) {
            cachedImage = NSWorkspace.shared.icon(forFile: claudeAppPath)
        }
        return cachedImage
    }
}

struct ClaudeIconView: View {
    var style: ClaudeIconStyle
    var size: CGFloat
    var eyeColor: Color = ClawdMark.defaultEyeColor

    private let claudeOrange = Color(red: 0.85, green: 0.46, blue: 0.34)

    var body: some View {
        switch style {
        case .owl:
            // 부엉이는 날개를 펼 좌우 여백까지 그리므로 가로가 더 길다.
            // 링 안에 들어가야 하니 높이를 기준으로 맞춘다.
            OwlMarkView()
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

    private var mark: some View {
        ClaudeMark()
            .fill(claudeOrange)
            .frame(width: size, height: size)
            .shadow(color: .black.opacity(0.4), radius: 1.5)
    }
}
