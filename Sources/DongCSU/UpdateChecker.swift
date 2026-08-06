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

/// 새 버전이 나왔는지 확인하고, 버전별 변경 내역을 받아온다.
///
/// 앱에 박혀 있는 내역은 그 버전까지밖에 모른다. `docs/changelog.json`을 받아오면
/// 아직 설치하지 않은 버전에 무엇이 들어갔는지 미리 볼 수 있다. 릴리스 API 대신
/// 이 파일 하나를 쓰므로 요청도 한 번으로 끝난다.
///
/// 앱이 스스로 자기를 교체하지는 않는다. Homebrew가 소스를 받아 빌드해 설치한
/// 번들이라, 앱이 그 자리를 덮어쓰면 brew의 설치 기록과 어긋난다. 대신 업그레이드
/// 명령을 터미널에 넘긴다(재로그인과 같은 방식).
@MainActor
final class UpdateChecker: ObservableObject {
    /// 원격에서 받아온 변경 내역. 비어 있으면 앱에 박혀 있는 것을 쓴다.
    @Published private(set) var remoteEntries: [ChangelogEntry] = [] {
        didSet { entries = Self.merged(remote: remoteEntries) }
    }

    /// 화면에 보여줄 내역. 원격이 바뀔 때만 다시 합친다.
    /// 계산 프로퍼티로 두면 버전 탭을 한 번 그릴 때마다 여섯 번 다시 정렬된다.
    @Published private(set) var entries: [ChangelogEntry] = Changelog.entries
    @Published private(set) var isChecking = false
    @Published private(set) var lastCheckedAt: Date?
    @Published private(set) var errorText: String?

    /// 앱에 박힌 내역과 원격에서 받은 것을 합친다.
    ///
    /// 원격을 그대로 쓰면 안 된다. raw.githubusercontent.com은 몇 분간 캐시되므로
    /// 방금 올린 버전을 쓰는 앱이 자기보다 뒤처진 목록을 받을 수 있고, 그러면 자기
    /// 버전 항목이 화면에서 사라진다. 같은 버전은 원격 쪽을 택하고 버전 내림차순으로 세운다.
    private static func merged(remote: [ChangelogEntry]) -> [ChangelogEntry] {
        guard !remote.isEmpty else { return Changelog.entries }

        var byVersion: [String: ChangelogEntry] = [:]
        for entry in Changelog.entries { byVersion[entry.version] = entry }
        for entry in remote { byVersion[entry.version] = entry }

        return byVersion.values.sorted { left, right in
            guard
                let leftVersion = AppVersion(left.version),
                let rightVersion = AppVersion(right.version)
            else { return left.version > right.version }
            return leftVersion > rightVersion
        }
    }

    /// 이미 나온 것 중 가장 높은 버전. 날짜가 없는 항목은 아직 안 나간 것이라 뺀다.
    var latest: AppVersion? {
        entries.first { $0.date != nil }.flatMap { AppVersion($0.version) }
    }

    /// 자동 확인 주기. 개인 도구라 하루 한 번이면 충분하고,
    /// 인증 없는 GitHub API의 시간당 60회 제한에도 여유가 있다.
    static let interval: TimeInterval = 24 * 3600

    private var timer: Timer?
    private var inFlight = false

    /// 지금 쓰는 버전보다 새 버전이 나와 있는지.
    ///
    /// 지금 버전이 더 높으면(직접 빌드했거나 원격이 캐시로 뒤처졌을 때) 알리지 않는다.
    var hasUpdate: Bool {
        guard let latest, let current = AppVersion(AppInfo.version) else { return false }
        return current < latest
    }

    init() {}

    /// 렌더 확인용. 주어진 버전이 방금 나온 것처럼 목록 맨 앞에 끼운다.
    init(preview latest: String?, lastCheckedAt: Date? = nil) {
        if let latest, AppVersion(latest) != nil {
            remoteEntries = [
                ChangelogEntry(
                    version: latest,
                    date: "2026-08-05",
                    notes: ["미리보기용 항목"]
                )
            ] + Changelog.entries
        }
        self.lastCheckedAt = lastCheckedAt
    }

    // MARK: - 확인

    func start() {
        guard timer == nil else { return }
        check()
        let timer = Timer(timeInterval: Self.interval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.check() }
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
            let result = await Self.fetchFeed()
            guard let self else { return }
            self.inFlight = false
            self.isChecking = false
            self.lastCheckedAt = Date()

            switch result {
            case .success(let entries):
                self.errorText = nil
                self.remoteEntries = entries
            case .failure(let message):
                self.errorText = message
            }
        }
    }

    /// 성공하면 내역 목록, 실패하면 사람이 읽을 사유.
    private enum FetchOutcome {
        case success([ChangelogEntry])
        case failure(String)
    }

    private static func fetchFeed() async -> FetchOutcome {
        var request = URLRequest(url: Changelog.feedURL)
        request.timeoutInterval = 15
        request.setValue("dong-csu/\(dongCSUVersion)", forHTTPHeaderField: "User-Agent")
        // 방금 올린 내역이 캐시에 가려지지 않게 한다.
        request.cachePolicy = .reloadIgnoringLocalCacheData

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse else { return .failure("응답 없음") }
            guard http.statusCode == 200 else { return .failure("HTTP \(http.statusCode)") }
            let feed = try JSONDecoder().decode(ChangelogFeed.self, from: data)
            guard !feed.entries.isEmpty else { return .failure("내역이 비어 있다") }
            return .success(feed.entries)
        } catch let error as DecodingError {
            return .failure("내역 파싱 실패: \(error)")
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
        // /bin/sh 로 두면 `read -n 1`(한 글자만 받기)이 없다. bash 는 macOS 에 늘 있다.
        let script = """
        #!/bin/bash
        echo "DongCSU 업데이트"
        echo
        # Homebrew 6부터 업그레이드 전에 y/n 을 묻는다(ask 모드가 기본).
        # 그대로 두면 이 창이 물음표 앞에서 멈춰 선 채로 끝난 것처럼 보인다.
        # 플래그(-y) 대신 환경변수를 쓴다 — 옛 Homebrew 는 모르는 변수를 그냥 무시하지만,
        # 모르는 플래그를 만나면 통째로 실패한다.
        export HOMEBREW_NO_ASK=1
        brew update && brew upgrade dong-csu || exit 1

        if [ -d /Applications/DongCSU.app ]; then
          echo
          echo "/Applications 의 DongCSU를 새 버전으로 교체합니다."
          pkill -f "/Applications/DongCSU.app/Contents/MacOS/DongCSU" 2>/dev/null
          sleep 1
          rm -rf /Applications/DongCSU.app
          cp -R "$(brew --prefix dong-csu)/DongCSU.app" /Applications/ || exit 1
          open /Applications/DongCSU.app
        fi

        echo
        echo "업데이트가 끝났습니다."
        echo
        read -n 1 -s -r -p "아무 키나 누르면 이 창이 닫힙니다…"
        echo

        # 스크립트가 끝나도 터미널 설정에 따라 창이 남는다. 직접 닫는다.
        # **지금 셸이 붙어 있는 tty로 창을 찾는다** — 제목으로 찾으면 사용자가 열어둔
        # 다른 창까지 닫힐 수 있다.
        # 셸이 아직 살아 있는 동안 닫으면 "실행 중인 프로세스를 끝낼까요?"를 묻기 때문에,
        # 잠깐 미뤘다가 닫도록 떼어 놓고 곧바로 빠져나온다.
        TTY="$(tty)"
        (
          sleep 0.3
          osascript <<APPLESCRIPT
        tell application "Terminal"
          repeat with w in windows
            repeat with t in tabs of w
              if tty of t is "$TTY" then close w saving no
            end repeat
          end repeat
        end tell
        APPLESCRIPT
        ) >/dev/null 2>&1 &
        exit 0
        """
        return TerminalScript.run(script, fileName: "dong-csu-upgrade.command")
    }

}
