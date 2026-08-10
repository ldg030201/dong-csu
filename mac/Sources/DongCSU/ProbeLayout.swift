import AppKit
import SwiftUI

/// `dong-csu --probe-layout` — 설정 창이 탭마다 실제로 몇 pt를 차지하는지 잰다.
///
/// **`--render-settings` 로는 이걸 못 본다.** 그쪽은 `ImageRenderer` 가 `ScrollView`
/// 안을 못 그려서 스크롤을 벗긴 모습을 그리는데, 여기서 알고 싶은 것이 바로 그 스크롤이
/// 제대로 걸렸는지다. 그래서 진짜 창에 얹어 놓고 잰다.
///
/// 검사하는 것은 **목록이 길어져도 창이 그만큼 길어지지 않는가** 하나다. 넘치고도 남을
/// 만큼(20개)과 최대치(50개)를 넣어 보고 높이가 같아야 한다 — 다르면 그 목록에 스크롤이
/// 안 걸린 것이고, 쓰다 보면 창이 화면을 넘어간다.
///
/// **적을 때 짧아지는 것은 정상이다.** 짧은데도 자리를 다 차지하면 빈 자리가 남고,
/// 그 빈 자리 때문에 도리어 창에 스크롤이 붙는다. 그래서 4개일 때 높이도 같이 찍는다.
@MainActor
enum ProbeLayout {
    static func run() -> Bool {
        print("창 안쪽 높이 \(Int(SettingsView.size.height))pt 기준")

        var allPassed = true
        for probe in Probe.all {
            let few = height(of: probe, records: 4)
            let many = height(of: probe, records: 20)
            let most = height(of: probe, records: 50)

            var notes: [String] = []
            if most - many > 1 {
                notes.append("기록 20→50개에 \(Int(most - many))pt 늘어남 — 스크롤이 안 걸렸다")
                allPassed = false
            } else if probe.hasList {
                notes.append("기록 20→50개에도 그대로")
                if few < many - 1 { notes.append("4개면 \(Int(few))pt 로 줄어듦") }
            }
            if most > SettingsView.size.height + 1 {
                notes.append("창보다 \(Int(most - SettingsView.size.height))pt 길다")
            }

            print(String(format: "  %-16@ %5dpt  %@",
                         probe.label, Int(most), notes.joined(separator: " · ")))
        }

        print(allPassed ? "통과" : "실패 — 목록에 스크롤이 안 걸린 탭이 있다")
        return allPassed
    }

    private struct Probe {
        let tab: SettingsTab
        let isRunning: Bool
        let label: String
        /// 안에서 따로 스크롤하는 목록이 있는 탭인지.
        let hasList: Bool

        static let all: [Probe] = SettingsTab.allCases.map {
            Probe(tab: $0, isRunning: true, label: $0.rawValue,
                  hasList: $0 == .measure || $0 == .version)
        } + [
            // 측정 탭은 재는 중이냐에 따라 화면이 통째로 다르다. 둘 다 잰다.
            Probe(tab: .measure, isRunning: false, label: "measure(멈춤)", hasList: true),
        ]
    }

    /// 진짜 창에 얹고 잰다. **그리기까지 시켜야 한다** — 안 그리면 레이아웃이 끝까지 안 간다.
    private static func height(of probe: Probe, records: Int) -> CGFloat {
        let snapshot = UsageSnapshot(
            planName: "Max",
            fiveHour: UsageWindow(utilization: 34, resetsAt: Date().addingTimeInterval(3 * 3600)),
            sevenDay: UsageWindow(utilization: 61, resetsAt: Date().addingTimeInterval(26 * 3600)),
            fetchedAt: Date()
        )
        let view = SettingsView(
            settings: HUDSettings(defaults: UserDefaults(suiteName: "dong-csu.probe") ?? .standard),
            store: UsageStore(preview: snapshot),
            updates: UpdateChecker(preview: nil, lastCheckedAt: Date()),
            meter: UsageMeter(preview: HUDPreviewRenderer.probeMeterState(
                running: probe.isRunning, records: records
            )),
            actions: SettingsActions(refresh: {}, resetPosition: {}, login: {}, quit: {}),
            version: AppInfo.displayVersion,
            initialTab: probe.tab
        )

        let window = NSWindow(
            contentRect: NSRect(origin: .zero, size: SettingsView.size),
            styleMask: [.titled, .resizable],
            backing: .buffered,
            defer: false
        )
        window.contentViewController = NSHostingController(rootView: view)
        window.setContentSize(SettingsView.size)
        // **화면 밖에 둔다.** 앞으로 내보내면 재는 동안 창이 스물넷 번쩍인다.
        // 그리기는 화면에 없어도 도므로 자리만 치우면 된다.
        window.setFrameOrigin(NSPoint(x: -30_000, y: -30_000))
        for _ in 0..<8 {
            window.contentView?.layoutSubtreeIfNeeded()
            window.contentView?.display()
            RunLoop.main.run(until: Date().addingTimeInterval(0.02))
        }
        return documentHeight(window.contentView)
    }

    /// 바깥 스크롤이 들고 있는 알맹이 높이. 이게 창보다 크면 세로 스크롤이 생긴다.
    private static func documentHeight(_ root: NSView?) -> CGFloat {
        guard let root else { return 0 }
        if let scroll = root as? NSScrollView, let document = scroll.documentView {
            return document.frame.height
        }
        for child in root.subviews {
            let found = documentHeight(child)
            if found > 0 { return found }
        }
        return 0
    }
}
