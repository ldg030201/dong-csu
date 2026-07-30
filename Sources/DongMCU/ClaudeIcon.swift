import AppKit
import SwiftUI

enum ClaudeIconStyle: String {
    /// 설치된 Claude 데스크톱 앱의 공식 아이콘을 그대로 쓴다.
    case appIcon
    /// 직접 그린 벡터 마크.
    case mark
}

enum ClaudeIcon {
    static let claudeAppPath = "/Applications/Claude.app"

    /// 아이콘 이미지 해석 순서:
    /// 1) 번들에 넣어둔 claude-icon.png (직접 갈아끼운 이미지)
    /// 2) 설치된 Claude 데스크톱 앱의 실제 아이콘
    @MainActor
    static func resolveImage() -> NSImage? {
        if let url = Bundle.main.url(forResource: "claude-icon", withExtension: "png"),
           let image = NSImage(contentsOf: url) {
            return image
        }
        if FileManager.default.fileExists(atPath: claudeAppPath) {
            return NSWorkspace.shared.icon(forFile: claudeAppPath)
        }
        return nil
    }
}

struct ClaudeIconView: View {
    var style: ClaudeIconStyle
    var size: CGFloat

    private let claudeOrange = Color(red: 0.85, green: 0.46, blue: 0.34)

    var body: some View {
        switch style {
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
