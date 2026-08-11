import AppKit

let dongCSUVersion = "2.3.1"

if CommandLine.arguments.contains("--version") {
    print("dong-csu \(AppInfo.version)")
    exit(0)
}

// GUI 없이 사용량 조회만 확인하는 진단 모드: dong-csu --probe
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

// 로그인 항목 등록 상태를 확인하거나 바꾼다: dong-csu --probe-login [on|off]
//
// 앱을 띄우고 설정 창을 눌러 보지 않고도 등록이 실제로 되는지 확인한다.
// `SMAppService.mainApp` 은 **번들** 을 가리키므로 터미널에서 불러도 같은 것을 본다.
if CommandLine.arguments.contains("--probe-login") {
    if CommandLine.arguments.contains("on") {
        print("register: \(LoginItem.setEnabled(true) ? "ok" : "failed")")
    } else if CommandLine.arguments.contains("off") {
        print("unregister: \(LoginItem.setEnabled(false) ? "ok" : "failed")")
    }
    print("enabled: \(LoginItem.isEnabled)")
    print("needsSystemSettings: \(LoginItem.needsSystemSettings)")
    exit(0)
}

// 토큰 갱신이 실제로 되는지 확인한다: dong-csu --probe-refresh [write]
//
// **토큰 값은 찍지 않는다.** 만료 시각과 회전 여부만 본다. `write`를 붙이면 회전한 값을
// 키체인에 되돌려 쓰는 것까지 한다 — 그때는 키체인이 허락을 한 번 묻는다.
if CommandLine.arguments.contains("--probe-refresh") {
    // 갱신은 하지 않고, 우리가 들고 있는 토큰을 키체인에 맞춰 넣기만 한다.
    // 되돌려 쓰기가 한 번 실패했을 때 되살리는 자리다.
    if CommandLine.arguments.contains("sync") {
        guard let stored = RefreshedTokenStore.load() else {
            print("우리 저장소에 토큰이 없다")
            exit(1)
        }
        guard let credentials = ClaudeKeychain.readCredentials(), let origin = credentials.origin else {
            print("키체인 자격증명 없음")
            exit(1)
        }
        let result = ClaudeKeychain.write(stored, to: origin)
        if result == .ok { KeychainWriteBack.allowed() }
        print("키체인 되돌려 쓰기: \(result)")
        exit(0)
    }

    let writesBack = CommandLine.arguments.contains("write")
    let done = DispatchSemaphore(value: 0)
    Task.detached {
        func stamp(_ date: Date?) -> String {
            date.map { ISO8601DateFormatter().string(from: $0) } ?? "없음"
        }

        guard let credentials = ClaudeKeychain.readCredentials() else {
            print("자격증명 없음")
            done.signal()
            return
        }
        print("지금 토큰: \(stamp(credentials.expiresAt)) \(credentials.isExpired ? "(만료)" : "(살아 있음)")")

        guard let refreshToken = credentials.refreshToken else {
            print("refreshToken 없음 — 재로그인 말고 길이 없다")
            done.signal()
            return
        }

        guard let renewed = await OAuthTokenRefresher.refresh(using: refreshToken) else {
            print("갱신 실패 (리프레시 토큰까지 죽었으면 재로그인해야 한다)")
            done.signal()
            return
        }
        print("갱신 성공 · 새 만료 \(stamp(renewed.expiresAt))")

        let rotated = renewed.refreshToken != nil && renewed.refreshToken != refreshToken
        print("리프레시 토큰 회전: \(rotated ? "했다" : "안 했다")")

        RefreshedTokenStore.save(renewed)
        print("우리 저장소에 기록: \(RefreshedTokenStore.fileURL?.path ?? "실패")")

        if rotated, let origin = credentials.origin {
            if writesBack {
                let result = ClaudeKeychain.write(renewed, to: origin)
                if result == .ok { KeychainWriteBack.allowed() }
                print("키체인 되돌려 쓰기: \(result)")
            } else {
                print("키체인 되돌려 쓰기: 건너뜀 (write 를 붙이면 한다)")
            }
        }
        done.signal()
    }
    done.wait()
    exit(0)
}

// 최근 몇 분 동안 Claude Code가 쓴 토큰을 센다: dong-csu --probe-tokens [분]
//
// 측정 탭이 쓰는 계산과 같은 코드다. 파일을 처음부터 훑고 시각으로만 걸러서,
// 오프셋을 쓰는 실제 동작과 값이 맞는지 견줄 수 있다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--probe-tokens") {
    let minutes = CommandLine.arguments.count > flagIndex + 1
        ? Double(CommandLine.arguments[flagIndex + 1]) ?? 60
        : 60
    let since = Date().addingTimeInterval(-minutes * 60)

    print("기록 폴더: \(ClaudeCodeUsage.projectsDirectory.path)")
    guard ClaudeCodeUsage.isAvailable else {
        print("찾지 못했다")
        exit(1)
    }
    print("파일: \(ClaudeCodeUsage.transcripts().count)개")
    // 대조 계산과 견주려면 기준 시각이 정확히 같아야 한다. 쓴 값을 그대로 찍는다.
    let stamp = ISO8601DateFormatter()
    stamp.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    print("기준: \(stamp.string(from: since)) (\(minutes)분 전)")

    let result = TokenScan(since: since, offsets: [:], seenIDs: []).run()
    print("응답: \(result.added.responses)")
    print("input: \(result.added.input)")
    print("output: \(result.added.output)")
    print("cache_creation: \(result.added.cacheCreation)")
    print("cache_read: \(result.added.cacheRead)")
    for (model, tally) in result.addedByModel.sorted(by: { $0.value.output > $1.value.output }) {
        print("  \(model): 응답 \(tally.responses) output \(tally.output)")
    }
    exit(0)
}

// 측정 기록을 확인한다: dong-csu --probe-meter [selftest]
//
// `selftest`는 리셋을 넘겨서도 계속 쌓는 계산이 맞는지 스스로 검사한다. 5시간 창이
// 새로 열리는 걸 실제로 기다리면 확인에 다섯 시간이 걸린다.
if CommandLine.arguments.contains("--probe-meter") {
    if CommandLine.arguments.contains("selftest") {
        let base = Date()
        let first = base.addingTimeInterval(5 * 3600)
        let second = base.addingTimeInterval(10 * 3600)

        func limit(_ percent: Double, _ resetsAt: Date) -> UsageLimit {
            UsageLimit(kind: "session", modelName: nil, percent: percent, resetsAt: resetsAt)
        }

        var track = UsageMeter.LimitTrack(title: "세션", lastPercent: 20, lastResetsAt: first)
        let steps: [(Double, Date, String)] = [
            (55, first, "그냥 늘었다"),
            (92, first, "그냥 늘었다"),
            (4, second, "창이 새로 열렸다"),
            (30, second, "그냥 늘었다"),
            (28, second, "서버 보정 — 더하지 않는다"),
            (30, second.addingTimeInterval(5), "resets_at 지터 — 리셋이 아니다"),
        ]
        for (percent, resetsAt, note) in steps {
            track = UsageMeter.advance(track, with: limit(percent, resetsAt))
            print(String(format: "  %5.0f%% → 누적 %6.0f%%p  리셋 %d회   (%@)",
                         percent, track.accumulated, track.resets, note))
        }

        let ok = track.accumulated == 104 && track.resets == 1
        print(ok ? "통과 (누적 104%p, 리셋 1회)" : "실패: 누적 \(track.accumulated)%p, 리셋 \(track.resets)회")
        exit(ok ? 0 : 1)
    }

    // 한 번 훑어서 기록에 얹는다. 앱이 1분마다 하는 것과 같은 일이다.
    // 두 번 연달아 부르면 두 번째는 0이 더해져야 한다 — 그게 증분 읽기의 조건이다.
    if CommandLine.arguments.contains("scan") {
        let store = MeterStore()
        guard let loaded = store.load(), let since = loaded.startedAt else {
            print("기록 없음")
            exit(1)
        }
        let result = TokenScan(since: since, offsets: loaded.offsets, seenIDs: loaded.seenIDs).run()
        let updated = UsageMeter.applying(result, to: loaded)
        store.save(updated)
        print("더함: 응답 \(result.added.responses) output \(result.added.output) "
              + "cache_read \(result.added.cacheRead)")
        print("누적: 응답 \(updated.tokens.responses) output \(updated.tokens.output) "
              + "cache_read \(updated.tokens.cacheRead)")
        exit(0)
    }

    let state = MeterStore().load()
    guard let state, let startedAt = state.startedAt else {
        print("기록 없음")
        exit(0)
    }
    print("시작: \(startedAt)")
    print("중지: \(state.stoppedAt.map(String.init(describing:)) ?? "재는 중")")
    print("표본: \(state.samples)회")
    for id in state.order {
        guard let track = state.tracks[id] else { continue }
        print(String(format: "  %@: %.0f%%p (리셋 %d회)", track.title, track.accumulated, track.resets))
    }
    print("토큰: 응답 \(state.tokens.responses) output \(state.tokens.output) "
          + "cache_read \(state.tokens.cacheRead)")
    exit(0)
}

// 업데이트가 무엇을 돌릴지 찍어 본다: dong-csu --probe-upgrade
//
// **실제로 돌리지 않는다.** 한 번 누르면 앱이 갈아끼워져서 눌러 보며 확인할 수 없는
// 자리라, brew 를 찾았는지와 두 스크립트의 문법만이라도 본다.
if CommandLine.arguments.contains("--probe-upgrade") {
    exit(Upgrader.probe() ? 0 : 1)
}

// 부엉이를 그림 마스코트 형식으로 뽑는다: dong-csu --dump-sprites <디렉터리>
//
// **그림 없이 커스텀 통로를 검증하려는 자리다.** 뽑아서 mascot 폴더에 넣고
// 아이콘 탭에서 "내 그림"을 고르면, 격자로 그린 부엉이와 같아야 한다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--dump-sprites"),
   flagIndex + 1 < CommandLine.arguments.count {
    let directory = CommandLine.arguments[flagIndex + 1]
    // 뒤에 `tint=RRGGBB` 를 붙이면 그 색으로 뽑는다. 통로가 살아 있는지 눈으로 가를 때 쓴다.
    let tint = CommandLine.arguments
        .first { $0.hasPrefix("tint=") }
        .map { String($0.dropFirst("tint=".count)) }
        .flatMap { hex -> NSColor? in
            guard hex.count == 6, let value = Int(hex, radix: 16) else { return nil }
            return NSColor(
                srgbRed: CGFloat((value >> 16) & 0xFF) / 255,
                green: CGFloat((value >> 8) & 0xFF) / 255,
                blue: CGFloat(value & 0xFF) / 255,
                alpha: 1
            )
        }
    // `cell=32` 처럼 붙이면 한 칸을 그만큼 크게 뽑는다. 예시로 넘길 때 쓴다.
    let cell = CommandLine.arguments
        .first { $0.hasPrefix("cell=") }
        .flatMap { CGFloat(Int(String($0.dropFirst("cell=".count))) ?? 0) }
        .flatMap { $0 > 0 ? $0 : nil } ?? MascotSpriteExport.cell
    // `sheet` 를 붙이면 규격 크기 한 장으로 뽑는다. 그게 사용자에게 줄 형식이다.
    // `rules` 를 붙이면 칸 경계에 선을 긋고, `empty` 를 붙이면 그림 없이 틀만 뽑는다.
    // `x2` 처럼 배율을 붙이면 규격의 정수배로 뽑는다.
    if CommandLine.arguments.contains("sheet") {
        let path = directory.hasSuffix(".png") ? directory : directory + "/" + MascotSheet.fileName
        try? FileManager.default.createDirectory(
            at: URL(fileURLWithPath: path).deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        let multiple = CommandLine.arguments
            .first { $0.hasPrefix("x") && Int($0.dropFirst()) != nil }
            .flatMap { Int($0.dropFirst()) }
            .flatMap { $0 > 0 ? $0 : nil } ?? 1
        let rules = CommandLine.arguments.contains("rules")
        let empty = CommandLine.arguments.contains("empty")
        // `labels` 를 붙이면 칸마다 이름을 박는다. **그리는 쪽에 줄 규격 그림용이다** —
        // 앱이 읽는 시트에 넣으면 글자가 마스코트에 딸려 들어간다.
        let labels = CommandLine.arguments.contains("labels")
        let ok = MainActor.assumeIsolated {
            MascotSpriteExport.writeSheet(
                to: path, tint: tint, multiple: multiple,
                rules: rules, empty: empty, labels: labels
            )
        }
        // 이름표를 붙이면 칸 위에 띠가 생겨서 규격 크기가 아니다. **문서용 그림이라
        // 그래도 된다** — 앱에 넣는 시트는 이름표 없이 뽑는다.
        let size = labels
            ? "규격 아님 (문서용)"
            : "\(Int(MascotSheet.canonicalSize.width) * multiple)x"
              + "\(Int(MascotSheet.canonicalSize.height) * multiple)"
        print(ok ? "시트: \(path) (\(size))" : "뽑지 못했다: \(path)")
        exit(ok ? 0 : 1)
    }

    let written = MainActor.assumeIsolated {
        MascotSpriteExport.writeAll(to: directory, tint: tint, cell: cell)
    }
    guard let written else {
        print("뽑지 못했다: \(directory)")
        exit(1)
    }
    print("\(written.count)장:")
    for path in written { print("  \(path)") }
    exit(0)
}

// 번들에 든 마스코트 시트를 확인한다: dong-csu --probe-mascot
//
// 몇 칸이 읽혔는지, 어떤 방법으로 읽었는지, 안 그린 칸이 어디로 떨어지는지 본다.
// 새 그림을 `Resources/mascot.png` 에 넣고 빌드한 뒤 이걸로 확인한다.
if CommandLine.arguments.contains("--probe-mascot") {
    MainActor.assumeIsolated {
        guard let set = MascotSpriteStore.bundled else {
            print("번들에 mascot.png 가 없다 — 빌드가 깨졌다")
            exit(1)
        }
        print("읽은 방법: \(set.readingMethod)")
        print("크기: \(Int(set.extent.width))x\(Int(set.extent.height))")
        print("칸 \(set.available.count)개: \(set.available.map(\.rawValue).joined(separator: ", "))")
        // 안 넣은 칸이 어디로 떨어지는지도 같이 본다. 대체 사슬이 끊기면 여기서 드러난다.
        let missing = MascotSprite.allCases.filter { !set.available.contains($0) }
        if !missing.isEmpty {
            print("없는 칸 \(missing.count)개:")
            for sprite in missing {
                var chain: [String] = []
                var step = sprite.fallback
                while let current = step {
                    chain.append(current.rawValue)
                    if set.available.contains(current) { break }
                    step = current.fallback
                }
                print("  \(sprite.rawValue) → \(chain.isEmpty ? "떨어질 곳 없음" : chain.joined(separator: " → "))")
            }
        }
    }
    exit(0)
}

// 그려 받은 시트를 규격 시트로 만든다:
//   dong-csu --prep-sheet <받은.png> <나올.png> [cols=6] [rows=4] [keep=4] [noalign]
if let flagIndex = CommandLine.arguments.firstIndex(of: "--prep-sheet"),
   flagIndex + 2 < CommandLine.arguments.count {
    func number(_ key: String, _ fallback: Int) -> Int {
        CommandLine.arguments
            .first { $0.hasPrefix("\(key)=") }
            .flatMap { Int($0.dropFirst(key.count + 1)) } ?? fallback
    }
    var options = SheetPrep.Options()
    options.columns = number("cols", options.columns)
    options.rows = number("rows", options.rows)
    options.keep = number("keep", options.keep)
    options.speck = number("speck", options.speck)
    options.aligns = !CommandLine.arguments.contains("noalign")

    let ok = MainActor.assumeIsolated {
        SheetPrep.run(
            from: CommandLine.arguments[flagIndex + 1],
            to: CommandLine.arguments[flagIndex + 2],
            options: options
        )
    }
    exit(ok ? 0 : 1)
}

// 그린 시트에서 칸 좌표를 뽑는다: dong-csu --fit-sheet <시트.png> [나올.json]
//
// **좌표를 손으로 적지 않게 하려고 둔 자리다.** 그리는 쪽이 격자를 정확히 못 맞추므로,
// 나온 그림에서 칸이 실제로 어디 있는지 찾아 적어 둔다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--fit-sheet"),
   flagIndex + 1 < CommandLine.arguments.count {
    let source = CommandLine.arguments[flagIndex + 1]
    let destination = flagIndex + 2 < CommandLine.arguments.count
        && !CommandLine.arguments[flagIndex + 2].hasPrefix("-")
        ? CommandLine.arguments[flagIndex + 2]
        : URL(fileURLWithPath: source)
            .deletingLastPathComponent()
            .appendingPathComponent(MascotAtlas.fileName).path

    let result: SheetFit.Result
    switch SheetFit.fit(imageAt: source) {
    case .success(let found):
        result = found
    case .failure(let why):
        print("칸을 못 찾았다: \(source)")
        print(why.advice)
        exit(1)
    }
    print("찾은 방법: \(result.method)")
    for line in result.report { print(line) }
    for warning in result.warnings { print("주의: \(warning)") }
    guard result.atlas.write(to: URL(fileURLWithPath: destination)) else {
        print("적지 못했다: \(destination)")
        exit(1)
    }
    print("적었다: \(destination)")
    // 눈으로 맞춰 볼 것이 있으면 0이 아닌 값으로 알린다.
    exit(result.warnings.isEmpty ? 0 : 2)
}

// 설정 창이 탭마다 얼마나 길어지는지 잰다: dong-csu --probe-layout
//
// 렌더 통로는 스크롤을 벗겨서 그리므로 **스크롤이 걸렸는지는 이걸로만 알 수 있다.**
// 목록이 길어져도 창이 안 늘어나는지 검사하고, 어긋나면 1로 끝난다.
if CommandLine.arguments.contains("--probe-layout") {
    _ = NSApplication.shared
    NSApp.setActivationPolicy(.accessory)
    exit(MainActor.assumeIsolated { ProbeLayout.run() } ? 0 : 1)
}

// 설정 창을 PNG로 그려서 확인. 탭 이름은 SettingsTab 의 rawValue 를 그대로 쓴다:
//   dong-csu --render-settings out.png [light] [status|measure|display|icon|pet|account|version]
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

// 변경 내역을 JSON으로 뽑는다: dong-csu --dump-changelog [out.json]
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

// 부엉이를 파일 하나로 뽑는다: dong-csu --dump-owl [out.json]
// 윈도우판이 같은 그림을 그리도록 그리드·색·프레임표를 내보낸다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--dump-owl") {
    do {
        let data = try MainActor.assumeIsolated { try OwlExport.jsonData() }
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

// 메뉴바 아이콘을 PNG로 뽑는다: dong-csu --render-menubar out.png [높이] [test]
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

// 부엉이 애니메이션을 한 장에 펼친다: dong-csu --render-owl out.png [칸높이]
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

// 기분마다 움직이는 GIF를 만든다: dong-csu --render-owl-gif <디렉터리> [칸높이]
// 문서에 넣을 그림이다. 자세를 고치면 다시 돌려서 갱신한다.
if let flagIndex = CommandLine.arguments.firstIndex(of: "--render-owl-gif"),
   flagIndex + 1 < CommandLine.arguments.count {
    let arguments = CommandLine.arguments
    let directory = arguments[flagIndex + 1]
    let cell = arguments.count > flagIndex + 2 ? Double(arguments[flagIndex + 2]) ?? 120 : 120
    // `grid` 를 붙이면 코드로 그리는 오리지널 부엉이로 뽑는다. 기본은 기본 캐릭터와
    // 같은 그림 마스코트다 — 문서가 화면과 다른 부엉이를 보여주면 안 된다.
    let usesGrid = arguments.contains("grid")
    let written = MainActor.assumeIsolated {
        OwlGIFRenderer.writeAll(
            to: directory, cell: cell,
            sheet: usesGrid ? nil : MascotSpriteStore.bundled
        )
    }
    guard let written else {
        print("render failed")
        exit(1)
    }
    written.forEach { print("rendered: \($0)") }
    exit(0)
}

// 앱 아이콘을 PNG로 뽑는다: dong-csu --render-icon out.png [한변]
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

// HUD를 PNG로 그려서 확인: dong-csu --render out.png [세션%] [주간%] [appIcon|mark]
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
