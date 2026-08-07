import Foundation
import CryptoKit

/// 5시간 / 7일 사용량 창 하나.
struct UsageWindow: Sendable {
    let utilization: Double  // 0...100
    let resetsAt: Date?
}

/// 서버가 따로 내려주는 한도 하나.
///
/// `five_hour`·`seven_day` 두 개만 읽으면 **모델별로 갈린 한도를 놓친다.** 응답의
/// `limits` 배열에는 그것까지 들어 있다(`weekly_scoped`). HUD는 두 개만 그리면 되지만
/// 측정 기록은 이쪽을 센다 — 나중에 "오퍼스에 얼마 썼나"를 물을 수 있어야 한다.
struct UsageLimit: Sendable, Hashable {
    /// `session` · `weekly_all` · `weekly_scoped`
    let kind: String
    /// 모델별 한도일 때만 채워진다.
    let modelName: String?
    let percent: Double
    let resetsAt: Date?

    /// 창이 새로 열려도 같은 한도를 가리키는 이름. 기록을 이 값으로 묶는다.
    var id: String { modelName.map { "\(kind)/\($0)" } ?? kind }

    var title: String {
        if let modelName { return "주간 · \(modelName)" }
        switch kind {
        case "session": return "세션 (5시간)"
        case "weekly_all": return "주간 (7일)"
        default: return kind
        }
    }
}

struct UsageSnapshot: Sendable {
    let planName: String?
    let fiveHour: UsageWindow?
    let sevenDay: UsageWindow?
    let fetchedAt: Date
    /// 서버가 준 한도 전부. 옛 응답에는 없을 수 있어서 비어 있을 수 있다.
    var limits: [UsageLimit] = []
}

enum UsageError: Error, CustomStringConvertible {
    case noCredentials
    case tokenExpired
    case rateLimited(retryAfter: TimeInterval?)
    case http(Int)
    case network(String)
    case decode

    var description: String {
        switch self {
        case .noCredentials: return "Claude 로그인 정보 없음"
        case .tokenExpired: return "토큰 만료 — Claude Code 재로그인 필요"
        case .rateLimited: return "요청 제한 (429)"
        case .http(let code): return "HTTP \(code)"
        case .network(let message): return "네트워크: \(message)"
        case .decode: return "응답 파싱 실패"
        }
    }

    /// 재시도해도 결과가 달라지지 않는 오류인지.
    var isTerminal: Bool {
        switch self {
        case .noCredentials, .tokenExpired: return true
        default: return false
        }
    }
}

// MARK: - 키체인

struct OAuthCredentials: Sendable {
    let accessToken: String
    let subscriptionType: String?
    let expiresAt: Date?

    var isExpired: Bool {
        guard let expiresAt else { return false }
        return expiresAt <= Date()
    }

    /// 만료 직전이면 캐시를 버리고 다시 읽는다.
    var isUsableForAWhile: Bool {
        guard let expiresAt else { return true }
        return expiresAt.timeIntervalSinceNow > 60
    }
}

/// 자격증명을 메모리에 들고 있는 캐시.
///
/// 키체인 조회는 `/usr/bin/security` 프로세스를 띄우기 때문에 폴링마다 하면
/// 프로세스 생성 비용이 계속 든다. 토큰은 만료될 때까지 유효하므로 한 번만 읽는다.
actor CredentialStore {
    static let shared = CredentialStore()

    private var cached: OAuthCredentials?

    func current() -> OAuthCredentials? {
        if let cached, !cached.isExpired, cached.isUsableForAWhile {
            return cached
        }
        let fresh = ClaudeKeychain.readCredentials()
        cached = fresh
        return fresh
    }

    /// 서버가 401/403을 주면 캐시된 토큰이 더 이상 유효하지 않다는 뜻이다.
    func invalidate() {
        cached = nil
    }
}

/// Claude Code가 macOS 키체인에 저장한 OAuth 자격증명을 읽는다.
///
/// `/usr/bin/security`를 쓰는 이유: Apple이 서명한 고정 바이너리라 키체인 ACL에
/// "항상 허용"을 한 번 눌러두면 dong-csu를 다시 빌드해도 접근 권한이 유지된다.
/// (직접 SecItemCopyMatching을 쓰면 재빌드마다 코드 서명이 바뀌어 매번 다시 물어본다.)
enum ClaudeKeychain {
    private static let baseService = "Claude Code-credentials"

    static func readCredentials() -> OAuthCredentials? {
        let account = NSUserName()
        for service in serviceNames() {
            for accountName in [account, nil] {
                guard let raw = runSecurity(service: service, account: accountName),
                      let parsed = parse(raw) else { continue }
                return parsed
            }
        }
        return nil
    }

    /// 기본 서비스명 + CLAUDE_CONFIG_DIR을 쓰는 경우의 해시 접미사 변형.
    private static func serviceNames() -> [String] {
        var names: [String] = []
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        let defaultDir = (home as NSString).appendingPathComponent(".claude")

        if let configDir = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"]?
            .trimmingCharacters(in: .whitespacesAndNewlines), !configDir.isEmpty {
            let normalized = (configDir as NSString).standardizingPath
            if normalized != defaultDir {
                let digest = SHA256.hash(data: Data(normalized.utf8))
                let hex = digest.map { String(format: "%02x", $0) }.joined().prefix(8)
                names.append("\(baseService)-\(hex)")
            }
        }
        names.append(baseService)
        return names
    }

    private static func runSecurity(service: String, account: String?) -> String? {
        var arguments = ["find-generic-password", "-s", service]
        if let account { arguments += ["-a", account] }
        arguments.append("-w")

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = arguments
        let stdout = Pipe()
        let stderr = Pipe()
        process.standardOutput = stdout
        process.standardError = stderr

        do { try process.run() } catch { return nil }
        let data = stdout.fileHandleForReading.readDataToEndOfFile()
        stderr.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else { return nil }

        guard let text = String(data: data, encoding: .utf8)?
            .trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty
        else { return nil }
        return text
    }

    private static func parse(_ raw: String) -> OAuthCredentials? {
        guard let data = raw.data(using: .utf8),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let oauth = root["claudeAiOauth"] as? [String: Any],
              let token = oauth["accessToken"] as? String, !token.isEmpty
        else { return nil }

        let expiresAt = (oauth["expiresAt"] as? Double).map { Date(timeIntervalSince1970: $0 / 1000) }
        return OAuthCredentials(
            accessToken: token,
            subscriptionType: oauth["subscriptionType"] as? String,
            expiresAt: expiresAt
        )
    }

    /// subscriptionType → 표시용 플랜 이름. API 사용자는 nil.
    static func planName(for subscriptionType: String?) -> String? {
        guard let raw = subscriptionType?.trimmingCharacters(in: .whitespacesAndNewlines), !raw.isEmpty
        else { return nil }
        let lower = raw.lowercased()
        if lower.contains("max") { return "Max" }
        if lower.contains("pro") { return "Pro" }
        if lower.contains("team") { return "Team" }
        if lower.contains("api") { return nil }
        return raw.prefix(1).uppercased() + raw.dropFirst()
    }
}

// MARK: - 사용량 API

enum UsageAPI {
    private static let endpoint = URL(string: "https://api.anthropic.com/api/oauth/usage")!

    static func fetch() async throws -> UsageSnapshot {
        guard let credentials = await CredentialStore.shared.current() else { throw UsageError.noCredentials }
        if credentials.isExpired { throw UsageError.tokenExpired }

        var request = URLRequest(url: endpoint, timeoutInterval: 15)
        request.httpMethod = "GET"
        // 매번 서버 값을 받아야 한다. 캐시된 응답이 돌아오면 새로고침이 먹히지 않는 것처럼 보인다.
        request.cachePolicy = .reloadIgnoringLocalCacheData
        request.setValue("Bearer \(credentials.accessToken)", forHTTPHeaderField: "Authorization")
        request.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        request.setValue("claude-code/2.1", forHTTPHeaderField: "User-Agent")

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await URLSession.shared.data(for: request)
        } catch {
            throw UsageError.network((error as NSError).localizedDescription)
        }

        guard let http = response as? HTTPURLResponse else { throw UsageError.decode }
        switch http.statusCode {
        case 200:
            break
        case 401, 403:
            await CredentialStore.shared.invalidate()
            throw UsageError.tokenExpired
        case 429:
            let retryAfter = http.value(forHTTPHeaderField: "Retry-After").flatMap(TimeInterval.init)
            throw UsageError.rateLimited(retryAfter: retryAfter)
        default:
            throw UsageError.http(http.statusCode)
        }

        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw UsageError.decode
        }
        return UsageSnapshot(
            planName: ClaudeKeychain.planName(for: credentials.subscriptionType),
            fiveHour: window(from: root["five_hour"]),
            sevenDay: window(from: root["seven_day"]),
            fetchedAt: Date(),
            limits: limits(from: root["limits"])
        )
    }

    private static func limits(from value: Any?) -> [UsageLimit] {
        guard let array = value as? [[String: Any]] else { return [] }
        return array.compactMap { dict in
            guard let kind = dict["kind"] as? String,
                  let percent = (dict["percent"] as? NSNumber)?.doubleValue, percent.isFinite
            else { return nil }

            let scope = dict["scope"] as? [String: Any]
            let model = (scope?["model"] as? [String: Any])?["display_name"] as? String
            return UsageLimit(
                kind: kind,
                modelName: model,
                percent: min(100, max(0, percent)),
                resetsAt: (dict["resets_at"] as? String).flatMap(parseDate)
            )
        }
    }

    private static func window(from value: Any?) -> UsageWindow? {
        guard let dict = value as? [String: Any] else { return nil }
        guard let raw = dict["utilization"] as? Double, raw.isFinite else { return nil }
        return UsageWindow(
            utilization: min(100, max(0, raw)),
            resetsAt: (dict["resets_at"] as? String).flatMap(parseDate)
        )
    }

    // 포매터 생성이 파싱보다 비싸다(98µs vs 22µs). 한 번만 만들어 쓴다.
    private static let iso8601WithFraction: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    private static let iso8601 = ISO8601DateFormatter()

    private static func parseDate(_ text: String) -> Date? {
        iso8601WithFraction.date(from: text) ?? iso8601.date(from: text)
    }
}
