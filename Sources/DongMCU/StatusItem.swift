import AppKit

/// 메뉴바 아이콘.
///
/// 아이콘이 보이면 dong-mcu가 돌고 있다는 뜻이다. HUD를 숨겨도 여기로 다시 켜거나
/// 종료할 수 있어서, Dock 아이콘 없는 앱의 유일한 고정 진입점 역할을 한다.
@MainActor
final class StatusItemController: NSObject, NSMenuDelegate {
    private let item: NSStatusItem
    private let store: UsageStore
    private let populate: (NSMenu) -> Void

    init(store: UsageStore, populate: @escaping (NSMenu) -> Void) {
        self.store = store
        self.populate = populate
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()

        item.button?.image = ClawdMark.statusItemImage(height: 16)
        item.button?.imageScaling = .scaleNone
        item.button?.toolTip = "dong-mcu"

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
