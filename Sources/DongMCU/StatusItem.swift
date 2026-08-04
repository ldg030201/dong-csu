import AppKit

/// 메뉴바 아이콘.
///
/// 아이콘이 보이면 dong-mcu가 돌고 있다는 뜻이다. HUD를 숨겨도 여기로 다시 켜거나
/// 종료할 수 있어서, Dock 아이콘 없는 앱의 유일한 고정 진입점 역할을 한다.
@MainActor
final class StatusItemController: NSObject, NSMenuDelegate {
    private let item: NSStatusItem
    private let populate: (NSMenu) -> Void

    init(populate: @escaping (NSMenu) -> Void) {
        self.populate = populate
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()

        // 정식판은 앱 아이콘과 같은 색 부엉이를 그대로 쓴다.
        // 테스트판은 메뉴바에 나란히 떠도 구분되도록 몸 색만 바꾼다.
        let bodyTint: NSColor? = AppInfo.isTestBuild ? AppInfo.testBuildTint : nil
        item.button?.image = OwlMark.statusItemImage(height: 16, bodyTint: bodyTint)
        item.button?.imageScaling = .scaleNone
        item.button?.toolTip = AppInfo.name

        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
    }

    /// 메뉴는 열릴 때마다 현재 사용량·HUD 표시 상태로 다시 만든다.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        populate(menu)
    }
}
