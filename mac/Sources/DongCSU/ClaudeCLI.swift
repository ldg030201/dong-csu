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

    /// 터미널 창에서 `claude auth login`을 실행한다.
    /// 대화형 로그인 플로우라 GUI 앱 안에서 처리할 수 없어 터미널에 넘긴다.
    static func openLogin() -> Bool {
        guard let executable = resolveExecutable() else { return false }

        return TerminalScript.run(
            """
            #!/bin/sh
            echo "\(AppInfo.name): Claude Code 로그인을 시작합니다."
            echo
            "\(executable)" auth login
            """,
            fileName: "dong-csu-login.command"
        )
    }
}
