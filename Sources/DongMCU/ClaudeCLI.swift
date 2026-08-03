import AppKit
import Foundation

/// Claude Code CLI를 찾아 로그인 플로우를 띄운다.
///
/// 이 앱은 OAuth 토큰을 직접 갱신하지 않는다. 키체인에는 리프레시 토큰이 같이 들어있지만,
/// 리프레시 토큰은 사용할 때 회전되는 경우가 많아서 우리가 먼저 써버리면 Claude Code가
/// 들고 있던 값이 무효가 되고 사용자의 Claude Code 로그인이 풀릴 수 있다.
/// 그래서 갱신은 Claude Code에게 맡기고, 우리는 그 과정을 시작만 해준다.
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

    /// 터미널 창에서 `claude auth login`을 실행한다.
    /// 대화형 로그인 플로우라 GUI 앱 안에서 처리할 수 없어 터미널에 넘긴다.
    static func openLogin() -> Bool {
        guard let executable = resolveExecutable() else { return false }

        // .command 확장자를 열면 터미널이 실행한다. 별도 자동화 권한이 필요 없다.
        let script = """
        #!/bin/sh
        echo "dong-mcu: Claude Code 로그인을 시작합니다."
        echo
        "\(executable)" auth login
        """
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("dong-mcu-login.command")

        do {
            try script.write(to: url, atomically: true, encoding: .utf8)
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
