import CryptoKit
import Foundation
import Security

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

    // 아래 둘은 서버가 아니라 **키체인 자격증명에서 온다.** 계정 탭이 보여준다.
    // 조회할 때 자격증명을 이미 읽으므로 같이 실어 보내면 따로 읽을 일이 없다.

    /// `default_claude_max_5x` 같은 요금제 등급 원문.
    var rateLimitTier: String?
    /// 지금 쓰는 액세스 토큰이 언제까지인지.
    var tokenExpiresAt: Date?
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
    /// `default_claude_max_5x` 처럼 몇 배 플랜인지까지 들어 있다. 계정 탭이 쓴다.
    var rateLimitTier: String?
    let expiresAt: Date?
    /// 만료됐을 때 이걸로 새 토큰을 받는다. 없으면 재로그인 말고 길이 없다.
    let refreshToken: String?
    /// 키체인의 어느 자리에서 읽었는지. 회전한 토큰을 되돌려 쓸 때 쓴다.
    let origin: ClaudeKeychain.Item?

    /// 갱신해서 받은 것을 얹는다. 플랜 이름과 읽어 온 자리는 키체인 쪽 것을 그대로 둔다.
    func applying(_ token: RefreshedToken) -> OAuthCredentials {
        OAuthCredentials(
            accessToken: token.accessToken,
            subscriptionType: subscriptionType,
            rateLimitTier: rateLimitTier,
            expiresAt: token.expiresAt,
            refreshToken: token.refreshToken ?? refreshToken,
            origin: origin
        )
    }

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

    /// 401을 받았다. 시계로는 멀쩡해 보여도 다음번에는 갱신부터 한다.
    private var needsRefresh = false

    /// 갱신 요청 사이 최소 간격.
    ///
    /// 갱신은 사용량 조회 안에서만 일어나므로 대개 그쪽 바닥에 함께 걸린다. 다만
    /// **리프레시 토큰까지 죽으면 조회마다 갱신을 다시 시도하게 되어**, 조회를 막아도
    /// 갱신 쪽만 계속 나갈 수 있다. 여기서도 한 번 더 막는다.
    private static let minRefreshInterval: TimeInterval = 10

    private var lastRefreshAt: Date?

    private var canRefreshNow: Bool {
        guard let lastRefreshAt else { return true }
        return Date().timeIntervalSince(lastRefreshAt) >= Self.minRefreshInterval
    }

    /// 지금 쓸 수 있는 자격증명. 만료됐으면 **여기서 갱신까지 한다.**
    ///
    /// 예전에는 갱신을 Claude Code에게 맡겼다. 리프레시 토큰은 쓸 때 회전하는 일이 있어서
    /// 우리가 먼저 써버리면 Claude Code 로그인이 풀릴 수 있기 때문이었다. **그런데 CLI를
    /// 띄워도 키체인이 갱신되지 않는다** — Claude Code를 데스크톱 앱으로만 쓰면 키체인의
    /// 토큰을 갱신해 줄 사람이 아무도 없어서, 다섯 시간마다 재로그인 안내만 뜬다.
    ///
    /// 그래서 직접 갱신하되, **회전한 경우에만** 새 값을 키체인에 되돌려 쓴다. 그러면
    /// Claude Code 쪽도 같이 최신이 되어 갈라지지 않는다.
    func current() async -> OAuthCredentials? {
        if !needsRefresh, let cached, cached.isUsableForAWhile { return cached }

        var effective = Self.merge(ClaudeKeychain.readCredentials(), with: RefreshedTokenStore.load())

        if needsRefresh || !(effective?.isUsableForAWhile ?? false),
           let credentials = effective,
           let refreshToken = credentials.refreshToken,
           canRefreshNow {
            lastRefreshAt = Date()
            if let renewed = await OAuthTokenRefresher.refresh(using: refreshToken) {
                Self.persist(renewed, replacing: refreshToken, origin: credentials.origin)
                effective = credentials.applying(renewed)
            }
        }

        needsRefresh = false
        cached = effective
        return effective
    }

    /// 서버가 401/403을 주면 캐시된 토큰이 더 이상 유효하지 않다는 뜻이다.
    func invalidate() {
        cached = nil
        needsRefresh = true
    }

    /// 키체인에서 읽은 것과 우리가 갱신해 둔 것 중 **더 새것**을 쓴다.
    ///
    /// 플랜 이름과 읽어 온 자리는 키체인에만 있으므로 그쪽이 없으면 아무것도 못 한다 —
    /// 그 상태는 로그인 자체가 없는 것이다.
    private static func merge(
        _ base: OAuthCredentials?,
        with stash: RefreshedToken?
    ) -> OAuthCredentials? {
        guard let base else { return nil }
        guard let stash else { return base }
        guard (stash.expiresAt ?? .distantPast) > (base.expiresAt ?? .distantPast) else { return base }
        return base.applying(stash)
    }

    private static func persist(
        _ token: RefreshedToken,
        replacing previous: String,
        origin: ClaudeKeychain.Item?
    ) {
        RefreshedTokenStore.save(token)

        // **값이 실제로 바뀌었을 때만** 남의 자리를 건드린다. 같은 것을 돌려줬다면
        // 키체인 값은 아직 유효하고, 우리가 쓸 이유가 없다.
        guard let rotated = token.refreshToken, rotated != previous, let origin else { return }
        guard !KeychainWriteBack.isCoolingDown else { return }

        switch ClaudeKeychain.write(token, to: origin) {
        case .ok: KeychainWriteBack.allowed()
        case .failed: KeychainWriteBack.denied()
        }
    }
}

/// 되돌려 쓰기를 거절당했으면 한동안 다시 묻지 않는다.
///
/// **이게 없으면 갱신할 때마다(여덟 시간에 한 번쯤) 키체인 창이 다시 뜬다.** 거절한
/// 사람에게 같은 것을 계속 묻는 꼴이고, 우리는 우리 사본으로 계속 도니 급할 것도 없다.
/// 대신 Claude Code 쪽 토큰은 그동안 낡은 채로 남는다 — 그게 거절의 뜻이다.
///
/// 비밀이 아니라 "언제 거절당했나" 하나뿐이라 UserDefaults에 둔다.
enum KeychainWriteBack {
    private static let key = "keychain.writeBackDeniedAt"
    private static let cooldown: TimeInterval = 7 * 24 * 3600

    static var isCoolingDown: Bool {
        let stamp = UserDefaults.standard.double(forKey: key)
        guard stamp > 0 else { return false }
        return Date().timeIntervalSince1970 - stamp < cooldown
    }

    static func denied() { UserDefaults.standard.set(Date().timeIntervalSince1970, forKey: key) }
    static func allowed() { UserDefaults.standard.removeObject(forKey: key) }
}

/// Claude Code가 macOS 키체인에 저장한 OAuth 자격증명을 읽는다.
///
/// `/usr/bin/security`를 쓰는 이유: Apple이 서명한 고정 바이너리라 키체인 ACL에
/// "항상 허용"을 한 번 눌러두면 dong-csu를 다시 빌드해도 접근 권한이 유지된다.
/// (직접 SecItemCopyMatching을 쓰면 재빌드마다 코드 서명이 바뀌어 매번 다시 물어본다.)
enum ClaudeKeychain {
    private static let baseService = "Claude Code-credentials"

    /// 자격증명이 들어 있던 키체인 항목.
    struct Item: Sendable {
        let service: String
        let account: String?
        /// 들어 있던 원문. 되돌려 쓸 때 우리가 모르는 항목(`mcpOAuth` 같은 것)을
        /// 지우지 않으려고 통째로 들고 있는다.
        let raw: String
    }

    static func readCredentials() -> OAuthCredentials? {
        let account = NSUserName()
        for service in serviceNames() {
            for accountName in [account, nil] {
                guard let raw = runSecurity(service: service, account: accountName),
                      let parsed = parse(raw, from: Item(service: service, account: accountName, raw: raw))
                else { continue }
                return parsed
            }
        }
        return nil
    }

    enum WriteResult {
        case ok
        case failed
    }

    /// 회전한 토큰을 원래 자리에 되돌려 쓴다.
    ///
    /// **회전했을 때만 부른다.** 서버가 리프레시 토큰을 바꿔 주면 키체인에 남은 옛 값은
    /// 그 순간 무효가 되고(써 본 토큰은 즉시 죽는다), 그대로 두면 Claude Code가 다음에
    /// 갱신하려다 로그인이 풀린다. 회전하지 않았다면 키체인 값이 아직 유효하므로
    /// 남의 저장소를 건드릴 이유가 없다.
    ///
    /// **읽기와 같은 `/usr/bin/security`를 쓴다.** 이유가 두 가지다.
    ///
    /// 하나, 키체인 허락은 **코드 서명 신원**에 걸리는데 이 앱은 ad-hoc 서명이라 신원이
    /// 바이너리 해시다. 우리가 직접 `SecItemUpdate`를 부르면 **버전을 올릴 때마다 해시가
    /// 바뀌어 사용자에게 다시 묻는다.** `security`는 Apple이 서명해서 신원이 고정이고,
    /// 읽기로 이미 허용돼 있어서 아무것도 묻지 않는다.
    ///
    /// 둘, 명령을 **표준 입력으로** 준다(`security -i`). 인자로 넘기면 토큰이 프로세스
    /// 목록에 드러나고, `-w`만 주고 값을 표준 입력에 흘리면 **128바이트에서 잘린다**
    /// (항목이 3KB를 넘는다). 둘 다 실제로 재 보고 버린 길이다.
    @discardableResult
    static func write(_ token: RefreshedToken, to item: Item) -> WriteResult {
        guard let data = item.raw.data(using: .utf8),
              var root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              var oauth = root["claudeAiOauth"] as? [String: Any]
        else { return .failed }

        oauth["accessToken"] = token.accessToken
        if let refreshToken = token.refreshToken { oauth["refreshToken"] = refreshToken }
        if let expiresAt = token.expiresAt {
            // 키체인에는 밀리초로 들어 있다.
            oauth["expiresAt"] = expiresAt.timeIntervalSince1970 * 1000
        }
        root["claudeAiOauth"] = oauth

        // 읽어 둔 원문에 얹어서 통째로 다시 쓴다. `mcpOAuth`처럼 우리가 모르는 항목이
        // 같이 들어 있어서, 새로 만들면 그것들이 지워진다.
        guard let updated = try? JSONSerialization.data(withJSONObject: root),
              let json = String(data: updated, encoding: .utf8)
        else { return .failed }

        // **account를 반드시 붙인다.** 서비스 이름만 주면 같은 이름을 쓰는 다른 항목까지
        // 우리 토큰으로 덮어쓴다.
        guard let account = item.account ?? soleAccount(forService: item.service) else { return .failed }

        let command = "add-generic-password -U -s \(quoted(item.service)) "
            + "-a \(quoted(account)) -w \(quoted(json))\n"
        return runSecurityInteractive(command) ? .ok : .failed
    }

    /// `security -i`의 한 줄 토큰. 역슬래시와 큰따옴표만 막으면 된다.
    private static func quoted(_ value: String) -> String {
        let escaped = value
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "\"", with: "\\\"")
        return "\"\(escaped)\""
    }

    /// 명령을 표준 입력으로 흘려 넣는다. **인자에는 아무 값도 싣지 않는다.**
    private static func runSecurityInteractive(_ command: String) -> Bool {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = ["-i"]

        let input = Pipe()
        let output = Pipe()
        let errors = Pipe()
        process.standardInput = input
        process.standardOutput = output
        process.standardError = errors

        do { try process.run() } catch { return false }

        input.fileHandleForWriting.write(Data(command.utf8))
        try? input.fileHandleForWriting.close()
        // 파이프가 차서 막히지 않게 먼저 비운다. 그다음에 끝나기를 기다린다.
        output.fileHandleForReading.readDataToEndOfFile()
        errors.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()

        return process.terminationStatus == 0
    }

    /// 이 서비스를 쓰는 항목이 **딱 하나일 때만** 그 account를 알려 준다.
    ///
    /// 값이 아니라 속성만 물어보므로 허락을 묻지 않는다. 여럿이면 어느 것을 고쳐야
    /// 하는지 알 수 없으니 아무것도 하지 않는다 — 남의 항목을 덮어쓰느니 갱신한 토큰을
    /// 우리 사본에만 두는 편이 낫다.
    private static func soleAccount(forService service: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecMatchLimit as String: kSecMatchLimitAll,
            kSecReturnAttributes as String: true,
        ]
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let items = result as? [[String: Any]], items.count == 1
        else { return nil }
        return items[0][kSecAttrAccount as String] as? String
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

    private static func parse(_ raw: String, from item: Item) -> OAuthCredentials? {
        guard let data = raw.data(using: .utf8),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let oauth = root["claudeAiOauth"] as? [String: Any],
              let token = oauth["accessToken"] as? String, !token.isEmpty
        else { return nil }

        let expiresAt = (oauth["expiresAt"] as? Double).map { Date(timeIntervalSince1970: $0 / 1000) }
        return OAuthCredentials(
            accessToken: token,
            subscriptionType: oauth["subscriptionType"] as? String,
            rateLimitTier: oauth["rateLimitTier"] as? String,
            expiresAt: expiresAt,
            refreshToken: (oauth["refreshToken"] as? String).flatMap { $0.isEmpty ? nil : $0 },
            origin: item
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
            limits: limits(from: root["limits"]),
            rateLimitTier: credentials.rateLimitTier,
            tokenExpiresAt: credentials.expiresAt
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
