import AppKit
import Combine
import Foundation

/// 앱 안에서 `brew upgrade` 를 돌린다.
///
/// 예전에는 터미널 창을 띄워서 거기서 돌렸다. 진행 상황을 보여줄 데가 필요했기
/// 때문인데, **그 창을 우리가 다시 닫아야 하는 게 문제였다** — 터미널 설정("창 닫기
/// 전에 확인")이나 사용자가 열어 둔 다른 창에 따라 남기도 하고, Dock 에 터미널이
/// 남는다. 남의 앱 창을 우리가 여닫으려 드는 구조 자체가 약하다.
///
/// **`brew upgrade` 는 Homebrew 폴더에만 쓴다.** `/Applications` 는 안 건드리므로
/// 그 단계는 앱이 살아 있는 채로 다 돌릴 수 있다. 앱이 못 하는 건 마지막 한 가지,
/// **자기 자신을 갈아끼우는 것**뿐이라 그것만 떼어 놓는다(`Handoff`).
///
/// 앱 하나에 업그레이드도 하나뿐이고, 끝나면 앱이 꺼진다. 설정 창을 닫으면 사라지는
/// 자리에 두면 그때 업그레이드가 끊기므로 **앱 수명만큼 사는 자리**에 둔다.
@MainActor
final class Upgrader: ObservableObject {
    static let shared = Upgrader()

    enum Phase: Equatable {
        case idle
        /// brew 가 도는 중.
        case running
        /// 다 받았고, 갈아끼우려고 곧 꺼진다.
        case swapping
        case failed(String)
    }

    @Published private(set) var phase: Phase = .idle
    /// brew 가 뱉는 것을 그대로 담는다. 화면이 이걸 보여준다.
    @Published private(set) var log = ""

    var isBusy: Bool { phase == .running || phase == .swapping }

    /// 로그를 이만큼까지만 들고 있는다. 소스 빌드로 떨어지면 컴파일 줄이 수천 줄 쏟아진다.
    private static let logLimit = 40_000

    private var process: Process?

    private init() {}

    // MARK: - 시작 · 멈춤

    func start() {
        guard !isBusy else { return }
        // 테스트판은 자체 업데이트를 하지 않는다. 여기까지 올 일이 없지만 막아 둔다.
        guard !AppInfo.isTestBuild else {
            phase = .failed("테스트판은 자체 업데이트를 하지 않습니다.")
            return
        }
        guard let brew = Self.resolveBrew() else {
            phase = .failed("Homebrew를 찾지 못했습니다. 터미널에서 brew upgrade dong-csu 를 직접 실행해 주세요.")
            return
        }

        log = ""
        phase = .running
        run(script: Self.upgradeScript(brew: brew), brew: brew)
    }

    /// 도는 중에 그만둔다. Homebrew 는 중간에 끊겨도 다음 번에 이어서 한다.
    func cancel() {
        process?.terminate()
        process = nil
        phase = .idle
    }

    /// 실패 화면을 닫는다.
    func dismiss() {
        guard !isBusy else { return }
        phase = .idle
    }

    // MARK: - brew 돌리기

    private func run(script: String, brew: String) {
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/bin/bash")
        task.arguments = ["-c", script]
        // **앱에는 로그인 셸의 환경이 없다.** brew 가 제 도구를 찾을 수 있게 맞춰 준다.
        var environment = ProcessInfo.processInfo.environment
        environment["PATH"] = "\(URL(fileURLWithPath: brew).deletingLastPathComponent().path)"
            + ":/usr/bin:/bin:/usr/sbin:/sbin"
        // Homebrew 6부터 업그레이드 전에 y/n 을 묻는다. 물어볼 사람이 없으므로 꺼 둔다.
        // **플래그(-y)가 아니라 환경변수를 쓴다** — 옛 Homebrew 는 모르는 변수를 그냥
        // 무시하지만, 모르는 플래그를 만나면 통째로 실패한다.
        environment["HOMEBREW_NO_ASK"] = "1"
        environment["HOMEBREW_NO_ENV_HINTS"] = "1"
        environment["HOMEBREW_NO_AUTO_UPDATE"] = "1"
        task.environment = environment
        // 지금 앱이 놓인 자리를 잡고 있으면 갈아끼울 때 걸린다.
        task.currentDirectoryURL = URL(fileURLWithPath: NSTemporaryDirectory())

        // stdout·stderr 를 한 통으로 받는다. brew 는 진행 상황을 stderr 로도 뱉는다.
        let pipe = Pipe()
        task.standardOutput = pipe
        task.standardError = pipe
        pipe.fileHandleForReading.readabilityHandler = { handle in
            let data = handle.availableData
            guard !data.isEmpty else { return }
            let text = String(decoding: data, as: UTF8.self)
            Task { @MainActor [weak self] in self?.append(text) }
        }

        task.terminationHandler = { finished in
            // 핸들러를 놓지 않으면 파이프가 안 닫혀서 프로세스가 좀비로 남는다.
            pipe.fileHandleForReading.readabilityHandler = nil
            let status = finished.terminationStatus
            Task { @MainActor [weak self] in self?.finish(status: status, brew: brew) }
        }

        do {
            try task.run()
            process = task
        } catch {
            phase = .failed("brew 를 실행하지 못했습니다: \(error.localizedDescription)")
        }
    }

    private func append(_ text: String) {
        log += text
        if log.count > Self.logLimit {
            log = String(log.suffix(Self.logLimit))
        }
    }

    private func finish(status: Int32, brew: String) {
        process = nil
        // 취소로 끊긴 것은 여기서 다시 실패로 만들지 않는다.
        guard phase == .running else { return }

        guard status == 0 else {
            phase = .failed("업데이트가 끝나지 않았습니다 (종료 코드 \(status)). 아래 기록을 보세요.")
            return
        }

        phase = .swapping
        append("\n앱을 새 버전으로 갈아끼웁니다. 잠시 뒤 다시 뜹니다.\n")

        guard Handoff.launch(brew: brew) else {
            phase = .failed("새 버전을 받았지만 앱을 갈아끼우지 못했습니다. "
                            + "터미널에서 다음을 실행해 주세요:\n"
                            + "rm -rf /Applications/DongCSU.app && "
                            + "cp -R \"$(brew --prefix dong-csu)/DongCSU.app\" /Applications/")
            return
        }

        // 갈아끼우는 쪽이 우리가 꺼지기를 기다리고 있다. 화면이 한 번 그려지게 두고 끈다.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
            NSApp.terminate(nil)
        }
    }

    // MARK: - brew 찾기

    /// **앱에는 로그인 셸의 PATH 가 없다.** 터미널에서 되던 것이 앱에서 안 되는 이유가
    /// 대개 이거라, 알려진 자리를 먼저 보고 없으면 로그인 셸에게 물어본다.
    nonisolated static func resolveBrew() -> String? {
        let known = ["/opt/homebrew/bin/brew", "/usr/local/bin/brew"]
        if let found = known.first(where: { FileManager.default.isExecutableFile(atPath: $0) }) {
            return found
        }

        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/bin/bash")
        task.arguments = ["-lc", "command -v brew"]
        let pipe = Pipe()
        task.standardOutput = pipe
        task.standardError = FileHandle.nullDevice
        guard (try? task.run()) != nil else { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        task.waitUntilExit()

        let path = String(decoding: data, as: UTF8.self)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return FileManager.default.isExecutableFile(atPath: path) ? path : nil
    }

    /// `dong-csu --probe-upgrade` — 실제로 돌리지 않고 무엇을 돌릴지만 찍는다.
    ///
    /// **업데이트는 눌러 보기 전에는 확인할 방법이 없는 자리다.** 한 번 누르면 앱이
    /// 갈아끼워지고, 실패하면 되돌리기 번거롭다. brew 를 찾았는지와 돌아갈 두 스크립트가
    /// 문법에 맞는지만이라도 여기서 본다.
    nonisolated static func probe() -> Bool {
        guard let brew = resolveBrew() else {
            print("brew 를 찾지 못했다")
            return false
        }
        print("brew: \(brew)")
        print("갈아끼우기 기록: \(Handoff.logURL?.path ?? "(없음)")")

        var ok = true
        for (name, script) in [("받기", upgradeScript(brew: brew)),
                               ("갈아끼우기", Handoff.script(brew: brew))] {
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("dong-csu-probe-\(name).sh")
            try? script.write(to: url, atomically: true, encoding: .utf8)
            let check = Process()
            check.executableURL = URL(fileURLWithPath: "/bin/bash")
            check.arguments = ["-n", url.path]
            try? check.run()
            check.waitUntilExit()
            let passed = check.terminationStatus == 0
            print("  \(name) 스크립트 문법: \(passed ? "ok" : "실패")")
            ok = ok && passed
            try? FileManager.default.removeItem(at: url)
        }
        return ok
    }

    /// tap 을 당기고 새 버전을 받는다. **`/Applications` 는 여기서 안 건드린다.**
    nonisolated static func upgradeScript(brew: String) -> String {
        """
        set -o pipefail
        BREW=\(shellQuoted(brew))

        # **`brew update` 를 먼저 돌리지 않는다.** 그건 Homebrew 자신과 깔려 있는 tap 을
        # 전부 갱신해서 10~30초가 걸린다. 우리한테 필요한 건 우리 tap 하나뿐이다.
        TAP="$("$BREW" --repository ldg030201/dong-csu 2>/dev/null)"
        if [ -d "$TAP/.git" ] && git -C "$TAP" pull --quiet 2>/dev/null; then
          echo "tap 갱신 완료"
        else
          echo "tap 만으로는 갱신하지 못해 brew update 로 넘어갑니다. 조금 걸립니다."
          "$BREW" update || exit 1
        fi

        echo
        "$BREW" upgrade dong-csu || exit 1
        """
    }

    /// 홑따옴표로 감싸서 셸에 그대로 넘긴다. 화면을 타지 않으므로 액터 밖에서도 쓴다.
    nonisolated static func shellQuoted(_ value: String) -> String {
        "'" + value.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }
}

/// 앱을 갈아끼우고 다시 띄우는 쪽.
///
/// **떼어 놓아야 한다.** 지금 도는 앱이 `/Applications/DongCSU.app` 안에서 실행되고
/// 있어서, 그 폴더를 지우려면 앱이 먼저 꺼져야 한다. 앱이 스스로 할 수 없는 유일한
/// 단계라 여기만 밖으로 낸다.
///
/// 터미널을 쓰지 않는다. `open` 이 아니라 프로세스로 바로 띄우므로 창이 안 뜬다.
enum Handoff {
    /// 갈아끼우다 실패했을 때 볼 자리. 화면이 없으니 이거라도 남긴다.
    static var logURL: URL? { AppSupport.folder?.appendingPathComponent("upgrade.log") }

    static func launch(brew: String) -> Bool {
        guard let scriptURL = write(brew: brew) else { return false }

        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/bin/bash")
        task.arguments = [scriptURL.path]
        // 앱이 놓인 자리를 잡고 있으면 지울 때 걸린다.
        task.currentDirectoryURL = URL(fileURLWithPath: NSTemporaryDirectory())
        task.standardOutput = FileHandle.nullDevice
        task.standardError = FileHandle.nullDevice
        // 부모가 꺼져도 이 프로세스는 launchd 밑으로 옮겨 붙어 계속 돈다.
        return (try? task.run()) != nil
    }

    private static func write(brew: String) -> URL? {
        _ = AppSupport.prepared()
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("dong-csu-handoff.sh")
        do {
            try script(brew: brew).write(to: url, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: url.path)
        } catch {
            return nil
        }
        return url
    }

    static func script(brew: String) -> String {
        let quotedBrew = Upgrader.shellQuoted(brew)
        let logPath = logURL.map { Upgrader.shellQuoted($0.path) } ?? "/dev/null"
        return """
        #!/bin/bash
        exec >>\(logPath) 2>&1
        echo "=== $(date) 갈아끼우기 시작 ==="

        BREW=\(quotedBrew)
        APP="/Applications/DongCSU.app"

        # 앱이 완전히 꺼질 때까지 기다린다. 열려 있는 파일을 지우면 반쯤 깨진 번들이 남는다.
        for _ in $(seq 1 100); do
          pgrep -f "$APP/Contents/MacOS/DongCSU" >/dev/null 2>&1 || break
          sleep 0.1
        done

        NEW="$("$BREW" --prefix dong-csu)/DongCSU.app"
        # **지우기 전에 새것부터 확인한다.** 지운 뒤에 복사가 실패하면 앱이 통째로 사라진다.
        if [ ! -d "$NEW" ]; then
          echo "새 번들을 찾지 못했다: $NEW"
          exit 1
        fi

        if [ -d "$APP" ]; then
          rm -rf "$APP" || exit 1
          cp -R "$NEW" /Applications/ || exit 1
          open "$APP"
        else
          # /Applications 에 복사본이 없으면 brew 쪽에서 바로 쓰고 있던 것이다.
          open "$NEW"
        fi
        echo "=== 끝 ==="
        """
    }
}
