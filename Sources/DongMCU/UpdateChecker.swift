import AppKit
import Foundation

/// `1.2.3` 또는 `1.2.3.4` 형태의 버전. 자리 수가 달라도 비교된다.
///
/// 네 번째 자리는 이미 나간 버전을 급히 고칠 때만 쓴다(CLAUDE.md 참고).
/// `1.2.3`과 `1.2.3.0`은 같은 버전으로 본다.
struct AppVersion: Comparable, CustomStringConvertible {
    let parts: [Int]

    init?(_ text: String) {
        let trimmed = text.hasPrefix("v") ? String(text.dropFirst()) : text
        let pieces = trimmed.split(separator: ".").map { Int($0) }
        guard !pieces.isEmpty, !pieces.contains(where: { $0 == nil }) else { return nil }
        parts = pieces.compactMap { $0 }
    }

    var description: String { parts.map(String.init).joined(separator: ".") }

    static func < (lhs: AppVersion, rhs: AppVersion) -> Bool {
        for index in 0..<max(lhs.parts.count, rhs.parts.count) {
            let left = index < lhs.parts.count ? lhs.parts[index] : 0
            let right = index < rhs.parts.count ? rhs.parts[index] : 0
            if left != right { return left < right }
        }
        return false
    }
}

/// 새 버전이 나왔는지 GitHub 릴리스를 보고 확인한다.
///
/// 앱이 스스로 자기를 교체하지는 않는다. Homebrew가 소스를 받아 빌드해 설치한
/// 번들이라, 앱이 그 자리를 덮어쓰면 brew의 설치 기록과 어긋난다. 대신 업그레이드
/// 명령을 터미널에 넘긴다(재로그인과 같은 방식).
@MainActor
final class UpdateChecker: ObservableObject {
    /// GitHub이 알려준 최신 릴리스 태그.
    @Published private(set) var latest: AppVersion?
    @Published private(set) var isChecking = false
    @Published private(set) var lastCheckedAt: Date?
    @Published private(set) var errorText: String?

    /// 자동 확인 주기. 개인 도구라 하루 한 번이면 충분하고,
    /// 인증 없는 GitHub API의 시간당 60회 제한에도 여유가 있다.
    static let interval: TimeInterval = 24 * 3600

    private static let releaseURL = URL(
        string: "https://api.github.com/repos/ldg030201/dong-mcu/releases/latest"
    )!

    private var timer: Timer?
    private var inFlight = false

    /// 지금 쓰는 버전보다 새 버전이 나와 있는지.
    var hasUpdate: Bool {
        guard let latest, let current = AppVersion(AppInfo.version) else { return false }
        return current < latest
    }

    init() {}

    /// 렌더 확인용.
    init(preview latest: String?, lastCheckedAt: Date? = nil) {
        self.latest = latest.flatMap(AppVersion.init)
        self.lastCheckedAt = lastCheckedAt
    }

    // MARK: - 확인

    func start() {
        guard timer == nil else { return }
        check()
        let timer = Timer(timeInterval: Self.interval, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.check() }
        }
        // 하루짜리 타이머를 정확히 맞출 이유가 없다. 여유를 크게 주면 절전에 유리하다.
        timer.tolerance = 60 * 60
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    func check() {
        guard !inFlight else { return }
        inFlight = true
        isChecking = true

        Task { [weak self] in
            let result = await Self.fetchLatestTag()
            guard let self else { return }
            self.inFlight = false
            self.isChecking = false
            self.lastCheckedAt = Date()

            switch result {
            case .success(let tag):
                self.errorText = nil
                self.latest = AppVersion(tag)
            case .failure(let message):
                self.errorText = message
            }
        }
    }

    /// 성공하면 태그, 실패하면 사람이 읽을 사유.
    private enum FetchOutcome {
        case success(String)
        case failure(String)
    }

    private static func fetchLatestTag() async -> FetchOutcome {
        var request = URLRequest(url: releaseURL)
        request.timeoutInterval = 15
        // GitHub API는 이 헤더가 없으면 응답 형식을 보장하지 않는다.
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("dong-mcu/\(dongMCUVersion)", forHTTPHeaderField: "User-Agent")
        // 방금 올린 릴리스가 캐시에 가려지지 않게 한다.
        request.cachePolicy = .reloadIgnoringLocalCacheData

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse else { return .failure("응답 없음") }
            guard http.statusCode == 200 else {
                // 릴리스가 하나도 없으면 404가 온다.
                if http.statusCode == 404 { return .failure("릴리스가 아직 없다") }
                return .failure("HTTP \(http.statusCode)")
            }
            guard
                let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                let tag = json["tag_name"] as? String
            else { return .failure("응답 파싱 실패") }
            return .success(tag)
        } catch {
            return .failure("네트워크: \(error.localizedDescription)")
        }
    }

    // MARK: - 업그레이드

    /// 터미널 창에서 `brew upgrade`를 실행한다.
    ///
    /// 앱 안에서 brew를 직접 부르면 PATH·권한 환경이 달라 실패하기 쉽고,
    /// 빌드가 몇십 초 걸려서 진행 상황을 보여줄 데도 필요하다. 터미널에 넘기면
    /// 둘 다 해결된다.
    static func openUpgrade() -> Bool {
        // /Applications 에 있는 건 복사본이라 brew upgrade만으로는 갱신되지 않는다.
        // 있으면 새 것으로 덮고 다시 띄운다.
        let script = """
        #!/bin/sh
        echo "DongMCU 업데이트"
        echo
        brew update && brew upgrade dong-mcu || exit 1

        if [ -d /Applications/DongMCU.app ]; then
          echo
          echo "/Applications 의 DongMCU를 새 버전으로 교체합니다."
          pkill -f "/Applications/DongMCU.app/Contents/MacOS/DongMCU" 2>/dev/null
          sleep 1
          rm -rf /Applications/DongMCU.app
          cp -R "$(brew --prefix dong-mcu)/DongMCU.app" /Applications/ || exit 1
          open /Applications/DongMCU.app
        fi

        echo
        echo "업데이트가 끝났습니다."
        """
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("dong-mcu-upgrade.command")

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

    /// 릴리스 페이지를 브라우저로 연다.
    static func openReleasePage() {
        guard let url = URL(string: "https://github.com/ldg030201/dong-mcu/releases/latest")
        else { return }
        NSWorkspace.shared.open(url)
    }
}
