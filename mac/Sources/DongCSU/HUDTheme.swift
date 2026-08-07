import AppKit
import SwiftUI

/// HUD 배색 모드.
enum HUDAppearance: String, CaseIterable, TitledOption {
    case system
    case light
    case dark

    static let `default` = HUDAppearance.system

    var title: String {
        switch self {
        case .system: return "시스템 설정 따름"
        case .light: return "라이트"
        case .dark: return "다크"
        }
    }

    /// `.system`이면 현재 시스템 설정을 읽는다.
    @MainActor
    var isDark: Bool {
        switch self {
        case .dark: return true
        case .light: return false
        case .system:
            return NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        }
    }

    /// 패널에 지정할 외형. `.system`은 nil로 두어 시스템을 따라가게 한다.
    var nsAppearance: NSAppearance? {
        switch self {
        case .system: return nil
        case .light: return NSAppearance(named: .aqua)
        case .dark: return NSAppearance(named: .darkAqua)
        }
    }
}

/// 밝은 배경/어두운 배경에서 쓸 색을 한곳에 모아둔다.
/// 링의 사용률 색(초록~빨강)은 두 모드에서 동일하다.
struct HUDPalette {
    let isDark: Bool

    /// 글자·아이콘의 기본 잉크색.
    private var ink: Color { isDark ? .white : Color(white: 0.10) }

    var primaryText: Color { ink }
    var secondaryText: Color { ink.opacity(isDark ? 0.68 : 0.60) }
    var tertiaryText: Color { ink.opacity(isDark ? 0.62 : 0.55) }
    var faintText: Color { ink.opacity(isDark ? 0.38 : 0.40) }
    var mutedDot: Color { ink.opacity(0.28) }

    var ringTrack: Color { ink.opacity(isDark ? 0.15 : 0.13) }

    /// 다 써서 더는 쓸 수 없는 링의 색. 사용률 색 대신 이걸 쓴다.
    /// 트랙보다는 진해야 "0%"가 아니라 "다 찼는데 죽었다"로 읽힌다.
    var ringSpent: Color { ink.opacity(isDark ? 0.38 : 0.32) }

    var controlIdle: Color { ink.opacity(0.45) }
    var controlActive: Color { ink.opacity(0.95) }
    var controlHoverFill: Color { ink.opacity(0.13) }

    /// 밝은 배경에서 검은 그림자를 쓰면 지저분해진다.
    var textShadow: Color { isDark ? .black.opacity(0.55) : .black.opacity(0.12) }

    /// 갱신 실패·재로그인 경고색. 밝은 배경에서는 조금 더 어둡게 잡아야 읽힌다.
    var warning: Color {
        isDark
            ? Color(red: 0.95, green: 0.72, blue: 0.27)
            : Color(red: 0.72, green: 0.47, blue: 0.05)
    }

    /// 새 버전 알림. 링 색(초록·노랑·빨강)과 겹치지 않는 파랑을 쓴다.
    var updateBadge: Color {
        isDark
            ? Color(red: 0.29, green: 0.60, blue: 0.99)
            : Color(red: 0.11, green: 0.44, blue: 0.90)
    }

    /// 테스트판 버전 딱지. 메뉴바 아이콘의 몸 색(`AppInfo.testBuildTint`)과 같은
    /// 보라 계열이되, 카드 배경 위에서 읽히도록 밝기만 각 모드에 맞춰 잡는다.
    var testBadge: Color {
        isDark
            ? Color(red: 0.74, green: 0.60, blue: 0.99)
            : Color(red: 0.40, green: 0.22, blue: 0.72)
    }

    /// Clawd의 눈. 배경이 밝아도 어둡게 유지한다.
    var markEye: Color { Color.black.opacity(isDark ? 0.88 : 0.75) }

    // MARK: - 창 레이어에 쓰는 AppKit 색

    func backdrop(opacity: Double = 0.92) -> NSColor {
        NSColor(calibratedWhite: isDark ? 0.09 : 0.97, alpha: opacity)
    }

    var border: NSColor {
        isDark
            ? NSColor.white.withAlphaComponent(0.10)
            : NSColor.black.withAlphaComponent(0.10)
    }
}
