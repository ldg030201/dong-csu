import AppKit

let dongMCUVersion = "1.3.0"

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
    // 버전 탭을 확인할 때 새 버전이 있는 상태를 흉내내려면 update=1.2.3 을 붙인다.
    let update = CommandLine.arguments
        .first { $0.hasPrefix("update=") }
        .map { String($0.dropFirst("update=".count)) }
    let ok = MainActor.assumeIsolated {
        HUDPreviewRenderer.writeSettings(to: path, isDark: isDark, tab: tab, update: update)
    }
    print(ok ? "rendered: \(path)" : "render failed")
    exit(ok ? 0 : 1)
}

// 변경 내역을 JSON으로 뽑는다: dong-mcu --dump-changelog [out.json]
// 앱에 박혀 있는 내역을 원격에서도 볼 수 있게 파일로 내보낸다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--dump-changelog") {
    do {
        let data = try Changelog.jsonData()
        if flagIndex + 1 < CommandLine.arguments.count {
            let path = CommandLine.arguments[flagIndex + 1]
            try data.write(to: URL(fileURLWithPath: path))
            print("wrote: \(path)")
        } else {
            FileHandle.standardOutput.write(data)
        }
        exit(0)
    } catch {
        print("dump failed: \(error)")
        exit(1)
    }
}

// 메뉴바 아이콘을 PNG로 뽑는다: dong-mcu --render-menubar out.png [높이] [test]
// 한 칸이 1pt까지 작아지는 자리라 눈으로 확인할 통로가 필요하다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--render-menubar"),
   flagIndex + 1 < CommandLine.arguments.count {
    let arguments = CommandLine.arguments
    let path = arguments[flagIndex + 1]
    let height = arguments.count > flagIndex + 2 ? Double(arguments[flagIndex + 2]) ?? 16 : 16
    let bodyTint: NSColor? = arguments.contains("test")
        ? NSColor(srgbRed: 0.54, green: 0.34, blue: 0.85, alpha: 1)
        : nil
    let image = OwlMark.statusItemImage(height: height, bodyTint: bodyTint)
    let ok: Bool
    if let tiff = image.tiffRepresentation,
       let bitmap = NSBitmapImageRep(data: tiff),
       let png = bitmap.representation(using: .png, properties: [:]) {
        ok = (try? png.write(to: URL(fileURLWithPath: path))) != nil
    } else {
        ok = false
    }
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
    let ok = MainActor.assumeIsolated { AppIconRenderer.write(to: path, side: side) }
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
    // small|normal|large|extraLarge 중 하나를 끼워 넣으면 그 배율로 그린다.
    let scale = extras.compactMap(HUDScale.init(rawValue:)).first ?? .normal
    // update 를 끼워 넣으면 새 버전이 나온 상태로 그린다.
    let showsUpdateBadge = extras.contains("update")

    let succeeded = MainActor.assumeIsolated {
        HUDPreviewRenderer.write(
            to: path,
            utilization: (session, weekly),
            iconStyle: iconStyle,
            state: state,
            collapsed: collapsed,
            isDark: isDark,
            side: side,
            opacity: opacity,
            showsStats: showsStats,
            scale: scale,
            showsUpdateBadge: showsUpdateBadge
        )
    }
    print(succeeded ? "rendered: \(path)" : "render failed")
    exit(succeeded ? 0 : 1)
}

let application = NSApplication.shared
// NSApplication.delegate는 약한 참조다. 전역에 두어 앱이 도는 동안 살아있게 한다.
let delegate = MainActor.assumeIsolated { AppDelegate() }
application.delegate = delegate
application.run()
