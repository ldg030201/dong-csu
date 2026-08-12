import AppKit
import SwiftUI

/// 끌고 있는 동안 **"놓으면 여기 붙는다"** 를 보여주는 얇은 오버레이 창.
///
/// **이게 없으면 붙는지 안 붙는지 놓아 봐야 안다.** 붙는 문턱은 그림에서 40pt 안인데
/// 펫의 창은 그보다 훨씬 커서(링이 128pt) 눈으로는 얼마나 가까운지 가늠이 안 된다.
/// 실제로 "닿은 것 같은데 안 붙는" 자리가 넓다.
///
/// 보여주는 것이 둘이다.
///
/// | | 무엇 |
/// | --- | --- |
/// | 흐린 사각형 | 그림이 **놓일 자리**. 크기까지 그대로라 얼마나 걸치는지 보인다 |
/// | 진한 막대 | 테두리에 **닿는 변**. 발이 닿을 줄인지 손이 닿을 줄인지가 이걸로 갈린다 |
///
/// 창을 따로 두는 이유: 펫의 창은 128x160 이라 남의 창 테두리를 담을 수 없다.
/// 표시를 펫 창 안에 그리면 붙을 자리가 창 밖에 있어서 잘린다.
@MainActor
final class PerchHint {
    private let panel: NSPanel
    private let hosting: NSHostingView<PerchHintView>
    private var shownEdge: MascotPerch?

    /// 그림자와 광이 번질 여백. 표시가 놓일 자리보다 이만큼 크다.
    private static let bleed: CGFloat = 10

    init() {
        panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 10, height: 10),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isFloatingPanel = true
        // **펫보다 한 단계 아래.** 같은 레벨에 두면 어느 것이 위인지 정해지지 않아서,
        // 표시가 마스코트를 덮는 판이 생긴다.
        panel.level = NSWindow.Level(rawValue: NSWindow.Level.floating.rawValue - 1)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        // **절대 마우스를 받지 않는다.** 끌고 있는 중에 뜨는 것이라, 하나라도 받으면
        // 끌던 것이 놓아진다.
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]

        hosting = NSHostingView(rootView: PerchHintView(edge: .top, bleed: Self.bleed))
        hosting.frame = panel.contentRect(forFrameRect: panel.frame)
        hosting.autoresizingMask = [.width, .height]
        panel.contentView = hosting
    }

    /// 그림이 놓일 자리를 표시한다. `rect` 는 **그림이 덮을 화면 사각형**이다.
    func show(rect: NSRect, edge: MascotPerch) {
        let frame = rect.insetBy(dx: -Self.bleed, dy: -Self.bleed)
        if shownEdge != edge {
            shownEdge = edge
            hosting.rootView = PerchHintView(edge: edge, bleed: Self.bleed)
        }
        panel.setFrame(frame, display: true)
        guard !panel.isVisible else { return }
        // 자리를 옮길 때는 그대로 따라가고 **처음 뜰 때만** 부드럽게 나타난다.
        // 옮길 때마다 페이드를 걸면 끄는 내내 깜빡인다.
        panel.alphaValue = 0
        panel.orderFront(nil)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.12
            panel.animator().alphaValue = 1
        }
    }

    func hide() {
        guard panel.isVisible else { return }
        panel.orderOut(nil)
        shownEdge = nil
    }
}

/// 붙을 자리 하나를 그린다.
private struct PerchHintView: View {
    let edge: MascotPerch
    /// 바깥에 남겨 둔 여백. 실제 자리는 이만큼 안쪽이다.
    let bleed: CGFloat

    /// 닿는 변에 얹는 막대의 두께.
    private static let barThickness: CGFloat = 5

    var body: some View {
        ZStack {
            // 그림이 놓일 자리. **채움을 옅게 둔다** — 진하게 칠하면 그 아래 창 내용이
            // 안 보여서, 어디에 붙는지 보여주려고 그 자리를 가리는 셈이 된다.
            RoundedRectangle(cornerRadius: 8)
                .fill(Color.white.opacity(0.14))
                .overlay(
                    RoundedRectangle(cornerRadius: 8)
                        .strokeBorder(Color.white.opacity(0.5), lineWidth: 1)
                )
                .padding(bleed)

            // 테두리에 닿는 변. 이 줄이 곧 창 테두리에 온다.
            bar
                .shadow(color: .black.opacity(0.5), radius: 3, y: 1)
        }
        // 이 창은 아무것도 받지 않는다. SwiftUI 쪽에서도 못 박아 둔다.
        .allowsHitTesting(false)
    }

    /// 닿는 변에 얹는 막대. **어느 변인지가 자세를 말해 준다** — 아래 줄이면 발이 닿는
    /// 것(앉기), 위 줄이면 손이 닿는 것(매달리기)이다.
    private var bar: some View {
        GeometryReader { geometry in
            let inner = CGSize(
                width: max(geometry.size.width - bleed * 2, 0),
                height: max(geometry.size.height - bleed * 2, 0)
            )
            let horizontal = edge == .top || edge == .bottom
            let length = horizontal ? inner.width : inner.height
            Capsule()
                .fill(Color.accentColor)
                .frame(
                    width: horizontal ? length : Self.barThickness,
                    height: horizontal ? Self.barThickness : length
                )
                .position(
                    x: position(in: geometry.size, inner: inner).x,
                    y: position(in: geometry.size, inner: inner).y
                )
        }
    }

    /// 막대 중심 — **창 테두리 선이 지나는 자리**다. 그림은 테두리에 딱 맞춰 놓이므로
    /// 막대도 그림 끝에 온다. **SwiftUI 는 위가 0** 이라 앉기·매달리기가 화면과 반대로 간다.
    private func position(in full: CGSize, inner: CGSize) -> CGPoint {
        let center = CGPoint(x: full.width / 2, y: full.height / 2)
        switch edge {
        // 창 위 테두리에 앉는다 → 그림 **아래**가 닿는다 → 아래쪽 줄.
        case .top: return CGPoint(x: center.x, y: bleed + inner.height)
        // 창 아래 테두리에 매달린다 → 그림 **위**가 닿는다.
        case .bottom: return CGPoint(x: center.x, y: bleed)
        // 창 오른쪽 테두리에 붙는다 → 그림 **왼쪽**이 닿는다.
        case .right: return CGPoint(x: bleed, y: center.y)
        case .left: return CGPoint(x: bleed + inner.width, y: center.y)
        }
    }
}
