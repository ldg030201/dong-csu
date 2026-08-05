import AppKit

let dongMCUVersion = "1.4.0.2"

if CommandLine.arguments.contains("--version") {
    print("dong-mcu \(AppInfo.version)")
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

// 손쉬운 사용 권한이 실제로 붙었는지 확인: dong-mcu --probe-accessibility
//
// 권한은 **코드 서명에 걸린다.** 테스트판은 빌드할 때마다 ad-hoc 서명이 달라져서
// 허용해 둔 게 풀릴 수 있는데, 그러면 앱은 조용히 거친 방식으로 내려앉는다.
// 밖에서 들여다볼 방법이 없어서 이 통로를 둔다.
if CommandLine.arguments.contains("--probe-accessibility") {
    print("trusted: \(CaretWatcher.isTrusted)")
    print("bundle: \(Bundle.main.bundleIdentifier ?? "(없음)")")
    exit(CaretWatcher.isTrusted ? 0 : 1)
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
    let palette: OwlPalette = arguments.contains("test")
        ? .tinted(body: AppInfo.testBuildTint)
        : .normal
    let ok = OwlMark.statusItemImage(height: height, palette: palette).writePNG(to: path)
    print(ok ? "rendered: \(path)" : "render failed")
    exit(ok ? 0 : 1)
}

// 부엉이 애니메이션을 한 장에 펼친다: dong-mcu --render-owl out.png [칸높이]
// 움직이는 그림은 정지 화면 한 장으로는 확인할 수 없어서, 기분별 프레임을 전부 늘어놓는다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--render-owl"),
   flagIndex + 1 < CommandLine.arguments.count {
    let arguments = CommandLine.arguments
    let path = arguments[flagIndex + 1]
    let cell = arguments.count > flagIndex + 2 ? Double(arguments[flagIndex + 2]) ?? 64 : 64
    let ok = MainActor.assumeIsolated { OwlSheetRenderer.write(to: path, cell: cell) }
    print(ok ? "rendered: \(path)" : "render failed")
    exit(ok ? 0 : 1)
}

// 기분마다 움직이는 GIF를 만든다: dong-mcu --render-owl-gif <디렉터리> [칸높이]
// 문서에 넣을 그림이다. 자세를 고치면 다시 돌려서 갱신한다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--render-owl-gif"),
   flagIndex + 1 < CommandLine.arguments.count {
    let arguments = CommandLine.arguments
    let directory = arguments[flagIndex + 1]
    let cell = arguments.count > flagIndex + 2 ? Double(arguments[flagIndex + 2]) ?? 120 : 120
    let written = MainActor.assumeIsolated {
        OwlGIFRenderer.writeAll(to: directory, cell: cell)
    }
    guard let written else {
        print("render failed")
        exit(1)
    }
    written.forEach { print("rendered: \($0)") }
    exit(0)
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
    let mode: HUDMode = extras.compactMap(HUDMode.init(rawValue:)).first ?? .expanded
    // 펫에 마우스를 올린 모습(뒤에 링이 뜬 상태)을 그린다.
    let isHovered = extras.contains("hover")
    let isDark = !extras.contains("light")
    let side: HUDExpandSide = extras.contains("expandLeft") ? .left : .right
    // 0~1 사이 숫자를 하나 끼워 넣으면 배경 불투명도로 쓴다.
    let opacity = extras.compactMap(Double.init).first { $0 > 0 && $0 <= 1 } ?? 0.92
    let showsStats = extras.contains("stats")
    // small|normal|large|extraLarge 중 하나를 끼워 넣으면 그 배율로 그린다.
    let scale = extras.compactMap(HUDScale.init(rawValue:)).first ?? .normal
    // update 를 끼워 넣으면 새 버전이 나온 상태로 그린다.
    let showsUpdateBadge = extras.contains("update")
    // 왼쪽 위 버전 딱지: version 은 정식판 모습, test 는 테스트판 모습으로 그린다.
    let versionBadgeIsTest = extras.contains("test")
    let versionBadge: String? = versionBadgeIsTest
        ? "\(AppInfo.version) test"
        : (extras.contains("version") ? AppInfo.version : nil)

    let succeeded = MainActor.assumeIsolated {
        HUDPreviewRenderer.write(
            to: path,
            utilization: (session, weekly),
            iconStyle: iconStyle,
            state: state,
            mode: mode,
            isHovered: isHovered,
            isDark: isDark,
            side: side,
            opacity: opacity,
            showsStats: showsStats,
            scale: scale,
            showsUpdateBadge: showsUpdateBadge,
            versionBadge: versionBadge,
            versionBadgeIsTest: versionBadgeIsTest
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
