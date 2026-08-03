import AppKit
import SwiftUI

enum ClaudeIconStyle: String, CaseIterable {
    /// Claude Code 마스코트 Clawd.
    case clawd
    /// Claude 앱 아이콘. 번들에 넣어둔 이미지를 쓴다.
    case appIcon
    /// 직접 그린 벡터 버스트 마크.
    case mark

    static let `default` = ClaudeIconStyle.clawd

    var title: String {
        switch self {
        case .clawd: return "Clawd (Claude Code 마스코트)"
        case .appIcon: return "Claude 앱 아이콘"
        case .mark: return "버스트 마크"
        }
    }

    /// 미리보기 타일 밑에 붙일 짧은 이름.
    var shortTitle: String {
        switch self {
        case .clawd: return "Clawd"
        case .appIcon: return "앱 아이콘"
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
