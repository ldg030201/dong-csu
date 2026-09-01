import AppKit
import SwiftUI

/// 끌고 있는 동안 **"놓으면 이 줄에 걸린다"** 를 보여주는 얇은 막대.
///
/// **이게 없으면 어디에 걸리는지 놓아 봐야 안다.** 붙는 문턱은 그림에서 40pt 안인데
/// 펫의 창은 그보다 훨씬 커서(링이 128pt) 눈으로는 얼마나 가까운지 가늠이 안 된다.
///
/// **막대 하나뿐이다.** 예전에는 그림이 놓일 자리를 옅은 사각형으로 같이 덮어
/// 보여줬는데, 어두운 창 위에서는 그게 흰 판때기로 보였다 — 뒤 창을 안 가리려고
/// 옅게 둔 것이 어두운 배경에서 정반대로 나왔다. 어디에 걸리는지는 이 막대가 말해
/// 주고, 어떤 자세로 붙을지는 마스코트가 그때 그 자세로 바뀌면서 말해 준다.
///
/// 창을 따로 두는 이유: 펫의 창은 128x160 이라 남의 창 테두리를 담을 수 없다.
/// 표시를 펫 창 안에 그리면 붙을 자리가 창 밖에 있어서 잘린다.
@MainActor
final class PerchHint {
    private let panel: NSPanel
    private let hosting: NSHostingView<PerchHintView>
    private var shownEdge: MascotPerch?
    private var shownSink: CGFloat?

    /// 그림자가 번질 여백. 표시가 놓일 자리보다 이만큼 크다.
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

        hosting = NSHostingView(rootView: PerchHintView(edge: .top, bleed: Self.bleed, sink: 0))
        hosting.frame = panel.contentRect(forFrameRect: panel.frame)
        hosting.autoresizingMask = [.width, .height]
        panel.contentView = hosting
    }

    /// 걸릴 줄을 표시한다. `rect` 는 **그림이 덮을 화면 사각형** 이다.
    ///
    /// `sink` 는 붙잡는 부위가 창 안으로 넘어가는 깊이다. 막대는 사각형 끝이 아니라
    /// **거기서 안쪽으로 그만큼 들어온 자리** 에 온다 — 막대가 곧 창 테두리 선이라,
    /// 안 맞추면 끄는 동안 보이는 자리와 손 떼고 앉는 자리가 달라진다.
    func show(rect: NSRect, edge: MascotPerch, sink: CGFloat) {
        let frame = rect.insetBy(dx: -Self.bleed, dy: -Self.bleed)
        if shownEdge != edge || shownSink != sink {
            shownEdge = edge
            shownSink = sink
            hosting.rootView = PerchHintView(edge: edge, bleed: Self.bleed, sink: sink)
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
        shownSink = nil
    }
}

/// 테두리에 닿는 변 하나를 그린다.
///
/// **어느 변인지가 자세를 말해 준다** — 아래 줄이면 발이 닿는 것(걸터앉기),
/// 위 줄이면 손이 닿는 것(아래매달리기)이다.
private struct PerchHintView: View {
    let edge: MascotPerch
    /// 바깥에 남겨 둔 여백. 실제 자리는 이만큼 안쪽이다.
    let bleed: CGFloat
    /// 붙잡는 부위가 창 안으로 넘어가는 깊이. 막대가 그만큼 안쪽에 온다.
    let sink: CGFloat

    private static let barThickness: CGFloat = 5

    var body: some View {
        GeometryReader { geometry in
            let inner = CGSize(
                width: max(geometry.size.width - bleed * 2, 0),
                height: max(geometry.size.height - bleed * 2, 0)
            )
            let horizontal = edge == .top || edge == .bottom
            let length = horizontal ? inner.width : inner.height
            let center = position(in: geometry.size, inner: inner)
            Capsule()
                .fill(Color.accentColor)
                .frame(
                    width: horizontal ? length : Self.barThickness,
                    height: horizontal ? Self.barThickness : length
                )
                .shadow(color: .black.opacity(0.5), radius: 3, y: 1)
                .position(x: center.x, y: center.y)
        }
        // 이 창은 아무것도 받지 않는다. SwiftUI 쪽에서도 못 박아 둔다.
        .allowsHitTesting(false)
    }

    /// 막대 중심 — **창 테두리 선이 지나는 자리** 다. 그림 끝이 아니라 거기서 `sink`
    /// 만큼 안쪽이다. 붙잡는 부위가 그 선을 넘어가 창 면 위에 얹히기 때문이다.
    /// **SwiftUI 는 위가 0** 이라 앉기·매달리기가 화면과 반대로 간다.
    private func position(in full: CGSize, inner: CGSize) -> CGPoint {
        let center = CGPoint(x: full.width / 2, y: full.height / 2)
        switch edge {
        // 창 위 테두리에 앉는다 → 그림 **아래** 가 닿는다 → 아래쪽 줄에서 sink 만큼 위로.
        case .top: return CGPoint(x: center.x, y: bleed + inner.height - sink)
        // 창 아래 테두리에 매달린다 → 그림 **위** 가 닿는다 → 위쪽 줄에서 sink 만큼 아래로.
        case .bottom: return CGPoint(x: center.x, y: bleed + sink)
        // 창 오른쪽 테두리에 붙는다 → 그림 **왼쪽** 이 닿는다.
        case .right: return CGPoint(x: bleed + sink, y: center.y)
        case .left: return CGPoint(x: bleed + inner.width - sink, y: center.y)
        }
    }
}
