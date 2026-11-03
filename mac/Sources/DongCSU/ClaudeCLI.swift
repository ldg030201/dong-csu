import AppKit
import Foundation

/// 터미널 창에서 셸 스크립트를 실행한다.
///
/// 대화형이거나(로그인) 몇십 초 걸리는(brew 업그레이드) 작업은 GUI 앱 안에서
/// 처리하기 어렵다. `.command` 확장자를 열면 터미널이 실행하므로 별도 자동화
/// 권한도 필요 없다.
enum TerminalScript {
    static func run(_ body: String, fileName: String) -> Bool {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(fileName)
        do {
            try body.write(to: url, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes(
                [.posixPermissions: 0o755],
                ofItemAtPath: url.path
            )
        } catch {
            return false
        }
        return NSWorkspace.shared.open(url)
    }
}

/// Claude Code CLI를 찾아 로그인 플로우를 띄운다.
///
/// **평소에는 여기까지 오지 않는다.** 토큰이 만료되면 앱이 스스로 갱신한다
/// (`CredentialStore.current`). 리프레시 토큰까지 죽었을 때만 이 길이 남는다.
enum ClaudeCLI {
    /// 설치 방식마다 위치가 달라서 알려진 경로를 순서대로 확인한다.
    static func resolveExecutable() -> String? {
        let fileManager = FileManager.default
        let home = fileManager.homeDirectoryForCurrentUser.path

        var candidates = [
            "\(home)/.local/bin/claude",
            "\(home)/.claude/local/claude",
            "/opt/homebrew/bin/claude",
            "/usr/local/bin/claude",
        ]

        // 네이티브 설치본은 버전 디렉터리 안에 들어있다. 최신 버전을 먼저 본다.
        let versionsRoot = "\(home)/Library/Application Support/Claude/claude-code"
        if let versions = try? fileManager.contentsOfDirectory(atPath: versionsRoot) {
            candidates += versions
                .sorted { $0.compare($1, options: .numeric) == .orderedDescending }
                .map { "\(versionsRoot)/\($0)/claude.app/Contents/MacOS/claude" }
        }

        return candidates.first { fileManager.isExecutableFile(atPath: $0) }
    }

    /// 실행 파일을 못 찾았을 때. HUD 메뉴와 설정 창이 같은 말을 한다.
    @MainActor
    static func alertMissingExecutable() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Claude Code 실행 파일을 찾지 못했습니다"
        alert.informativeText = "터미널에서 직접 claude auth login 을 실행해 주세요."
        alert.runModal()
    }
}

/// `claude auth login` 을 **터미널 없이** 띄운다. 브라우저만 열린다.
///
/// 예전에는 `.command` 를 열어 터미널에 맡겼는데, 로그인이 끝나도 창이 남았다 — 터미널은
/// 스크립트가 끝나도 창을 안 닫는 것이 기본이다. 로그인은 브라우저에서 끝나고 터미널은
/// 주소를 띄워 주는 것 말고 하는 일이 없어서 앱이 직접 띄운다.
///
/// **터미널 없이도 끝까지 간다 — 확인했다.** 표준 입력이 비어 있어도 `open` 으로 브라우저를
/// 열고 로컬 콜백을 기다린다(`open` 을 가짜로 끼워 넣고 재 봤다). 못 하는 것은 하나,
/// 브라우저가 콜백을 못 돌려줄 때 코드를 붙여 넣는 길이다. 그때는 `timeLimit` 이 끊는다.
///
/// `configDir` 를 주면 그 폴더로 로그인한다 — 키체인에 그 폴더 몫의 자리가 따로 생겨서
/// 기본 로그인과 섞이지 않는다(`ClaudeAccount`). 계정 추가와, 추가한 계정의 재로그인이 쓴다.
@MainActor
final class ClaudeLogin {
    static let shared = ClaudeLogin()

    /// 브라우저에서 아무것도 안 하고 닫았을 때 기다리는 한계. 그냥 두면 로컬 콜백을 받는
    /// 프로세스가 영원히 떠 있는다.
    static let timeLimit: TimeInterval = 5 * 60

    private var process: Process?
    private var completion: ((Bool) -> Void)?
    private var deadline: DispatchWorkItem?

    var isRunning: Bool { process != nil }

    /// 로그인을 띄운다. 실행 파일이 없으면 false 이고 아무것도 안 띄운다.
    ///
    /// **하나만 띄운다.** 돌던 것이 있으면 끊고(그쪽 `completion` 은 실패로 불린다) 새로 띄운다
    /// — 둘이 같은 브라우저에서 콜백을 다투면 어느 폴더에 로그인됐는지 알 수 없다.
    func start(configDir: URL?, completion: @escaping (Bool) -> Void) -> Bool {
        guard let executable = ClaudeCLI.resolveExecutable() else { return false }
        cancel()

        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = ["auth", "login"]

        var environment = ProcessInfo.processInfo.environment
        // **경로를 고치지 않고 그대로 넘긴다.** Claude Code가 받은 문자열을 해시해서 키체인
        // 자리를 정하므로, 우리가 찾을 때 쓰는 `dir.path` 와 글자 하나까지 같아야 한다.
        if let configDir { environment["CLAUDE_CONFIG_DIR"] = configDir.path }
        // 브라우저는 `open`, 키체인은 `security` 로 연다. 둘 다 PATH 에서 찾는다.
        if !(environment["PATH"] ?? "").split(separator: ":").contains("/usr/bin") {
            environment["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin"
        }
        process.environment = environment
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        process.terminationHandler = { [weak self] finished in
            let succeeded = finished.terminationReason == .exit && finished.terminationStatus == 0
            DispatchQueue.main.async {
                MainActor.assumeIsolated { self?.finish(finished, succeeded: succeeded) }
            }
        }

        do { try process.run() } catch { return false }
        self.process = process
        self.completion = completion

        let deadline = DispatchWorkItem { [weak self] in self?.cancel() }
        self.deadline = deadline
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.timeLimit, execute: deadline)
        return true
    }

    /// 기다리던 로그인을 그만둔다. 브라우저 탭은 남지만 눌러도 아무 데도 안 간다.
    func cancel() {
        guard let process else { return }
        let completion = self.completion
        clear()
        process.terminate()
        completion?(false)
    }

    private func finish(_ finished: Process, succeeded: Bool) {
        // 끊고 새로 띄운 뒤에 옛 것이 끝났다는 소식이 늦게 올 수 있다. 그건 이미 실패로 알렸다.
        guard finished === process else { return }
        let completion = self.completion
        clear()
        completion?(succeeded)
    }

    private func clear() {
        deadline?.cancel()
        deadline = nil
        process = nil
        completion = nil
    }
}
