import AppKit

let dongMCUVersion = "0.2.0.1"

if CommandLine.arguments.contains("--version") {
    let bundled = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String
    print("dong-mcu \(bundled ?? dongMCUVersion)")
    exit(0)
}

// GUI 없이 사용량 조회만 확인하는 진단 모드: dong-mcu --probe
if CommandLine.arguments.contains("--probe") {
    let done = DispatchSemaphore(value: 0)
    // main.swift 최상위 코드는 @MainActor라서 Task {}로 띄우면
    // done.wait()가 잡고 있는 메인 스레드를 기다리다 데드락 난다. 반드시 detached.
    Task.detached {
        do {
            let snapshot = try await UsageAPI.fetch()
            let plan = snapshot.planName ?? "(플랜 불명)"
            print("plan: \(plan)")
            if let fiveHour = snapshot.fiveHour {
                print("five_hour: \(fiveHour.utilization)% resets_at=\(fiveHour.resetsAt?.description ?? "-")")
            } else {
                print("five_hour: -")
            }
            if let sevenDay = snapshot.sevenDay {
                print("seven_day: \(sevenDay.utilization)% resets_at=\(sevenDay.resetsAt?.description ?? "-")")
            } else {
                print("seven_day: -")
            }
        } catch {
            print("error: \(error)")
        }
        done.signal()
    }
    done.wait()
    exit(0)
}

// 설정 창을 PNG로 그려서 확인: dong-mcu --render-settings out.png [light] [status|display|icon|account]
if let flagIndex = CommandLine.arguments.firstIndex(of: "--render-settings"),
   flagIndex + 1 < CommandLine.arguments.count {
    let path = CommandLine.arguments[flagIndex + 1]
    let isDark = !CommandLine.arguments.contains("light")
    let tab = CommandLine.arguments
        .compactMap(SettingsTab.init(rawValue:))
        .first ?? .status
    let ok = HUDPreviewRenderer.writeSettings(to: path, isDark: isDark, tab: tab)
    print(ok ? "rendered: \(path)" : "render failed")
    exit(ok ? 0 : 1)
}

// 앱 아이콘을 PNG로 뽑는다: dong-mcu --render-icon out.png [한변]
// .icns는 이 PNG들을 iconutil로 묶어 만든다. make-icon.sh 참고.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--render-icon"),
   flagIndex + 1 < CommandLine.arguments.count {
    let arguments = CommandLine.arguments
    let path = arguments[flagIndex + 1]
    let side = arguments.count > flagIndex + 2 ? Double(arguments[flagIndex + 2]) ?? 1024 : 1024
    let ok = AppIconRenderer.write(to: path, side: side)
    print(ok ? "rendered: \(path)" : "render failed")
    exit(ok ? 0 : 1)
}

// HUD를 PNG로 그려서 확인: dong-mcu --render out.png [세션%] [주간%] [appIcon|mark]
if let flagIndex = CommandLine.arguments.firstIndex(of: "--render"),
   flagIndex + 1 < CommandLine.arguments.count {
    let arguments = CommandLine.arguments
    let path = arguments[flagIndex + 1]
    let session = arguments.count > flagIndex + 2 ? Double(arguments[flagIndex + 2]) ?? 8 : 8
    let weekly = arguments.count > flagIndex + 3 ? Double(arguments[flagIndex + 3]) ?? 60 : 60
    let iconStyle = arguments.count > flagIndex + 4
        ? ClaudeIconStyle(rawValue: arguments[flagIndex + 4]) ?? .default
        : .default

    let state = arguments.count > flagIndex + 5
        ? HUDPreviewRenderer.State(rawValue: arguments[flagIndex + 5]) ?? .ok
        : .ok

    let extras = arguments.dropFirst(flagIndex + 5)
    let collapsed = extras.contains("collapsed")
    let isDark = !extras.contains("light")
    let side: HUDExpandSide = extras.contains("expandLeft") ? .left : .right
    // 0~1 사이 숫자를 하나 끼워 넣으면 배경 불투명도로 쓴다.
    let opacity = extras.compactMap(Double.init).first { $0 > 0 && $0 <= 1 } ?? 0.92
    let showsStats = extras.contains("stats")

    let succeeded = HUDPreviewRenderer.write(
        to: path,
        utilization: (session, weekly),
        iconStyle: iconStyle,
        state: state,
        collapsed: collapsed,
        isDark: isDark,
        side: side,
        opacity: opacity,
        showsStats: showsStats
    )
    print(succeeded ? "rendered: \(path)" : "render failed")
    exit(succeeded ? 0 : 1)
}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.run()
