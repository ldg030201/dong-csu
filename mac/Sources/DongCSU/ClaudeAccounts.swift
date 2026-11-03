import AppKit
import Foundation

/// 키체인에 로그인해 둔 Claude Code 계정 하나.
///
/// **키체인 자리 이름이 곧 신원이다.** Claude Code는 설정 폴더(`CLAUDE_CONFIG_DIR`)마다
/// 로그인을 따로 저장한다 — 기본 폴더(`~/.claude`)는 `Claude Code-credentials`, 다른 폴더는
/// 거기에 경로 해시 8자리를 붙인 자리다. 그래서 계정을 둘 쓰려면 폴더를 둘 쓰면 된다.
///
/// 데스크톱 앱으로 쓰는 계정은 여기 안 잡힌다. 그쪽은 세션을 앱 쿠키에 암호화해 두고
/// 키체인에 OAuth 토큰을 안 남긴다. **쿠키를 풀어 읽는 길은 접었다** — 남의 앱 비밀을
/// 꺼내는 것이고, 저장 형식이 바뀌면 조용히 깨진다. 같은 계정으로 한 번 더 로그인해 두면
/// 한도가 계정 단위라 데스크톱 앱에서 쓴 몫까지 같이 보인다.
struct ClaudeAccount: Identifiable, Hashable, Sendable {
    let service: String
    /// 키체인 항목의 account 속성. 지울 때 이름으로 집어야 남의 항목을 안 건드린다.
    let keychainAccount: String?
    /// 키체인에 적힌 요금제(`team`, `max` …). 프로필을 못 받았을 때 이름 대신 쓴다.
    let subscriptionType: String?
    /// 계정 추가로 우리가 만든 폴더. **우리 것만 안다** — 남이 만든 폴더는 해시만 남아서
    /// 경로를 거꾸로 알 수 없다.
    let configDir: URL?

    var id: String { service }
    var isDefault: Bool { service == ClaudeKeychain.baseService }
    /// 앱에서 뺄 수 있는지. 우리가 추가한 것만 — 남이 해 둔 로그인은 안 건드린다.
    var isRemovable: Bool { configDir != nil }

    /// 목록 첫 줄. 이메일을 못 받았으면 어디서 온 로그인인지라도 말한다.
    func title(profile: AccountProfile?) -> String {
        if let email = profile?.email, !email.isEmpty { return email }
        if isDefault { return "Claude Code 기본 로그인" }
        return isRemovable ? "추가한 계정" : "다른 설정 폴더의 로그인"
    }

    /// 목록 둘째 줄. `Team · Hygino` 처럼 플랜과 팀.
    func detail(profile: AccountProfile?) -> String {
        var parts: [String] = []
        if let plan = ClaudeKeychain.planName(for: subscriptionType) { parts.append(plan) }
        if let team = profile?.teamName { parts.append(team) }
        if isDefault { parts.append("Claude Code 기본") }
        return parts.isEmpty ? "—" : parts.joined(separator: " · ")
    }
}

/// 서버가 알려 준 계정 정보. 키체인에는 이메일도 팀 이름도 없어서 따로 묻는다.
struct AccountProfile: Codable, Equatable, Sendable {
    let email: String?
    let organizationName: String?
    /// `claude_team` · `claude_max` 같은 조직 종류.
    let organizationType: String?
    let fetchedAt: Date

    /// 하루가 지나면 다시 묻는다. 팀 이름은 거의 안 바뀌지만, 바뀌면 그날 안에는 따라간다.
    static let maxAge: TimeInterval = 24 * 3600

    var isStale: Bool { Date().timeIntervalSince(fetchedAt) > Self.maxAge }

    /// 팀·기업 계정일 때만 조직 이름.
    ///
    /// **개인 계정의 조직은 보여주지 않는다.** 이름이 `<이메일>'s Organization` 으로 자동
    /// 생성돼서, 붙여 봐야 이메일을 한 번 더 적는 것밖에 안 된다.
    var teamName: String? {
        guard let type = organizationType?.lowercased(),
              type.contains("team") || type.contains("enterprise"),
              let name = organizationName?.trimmingCharacters(in: .whitespacesAndNewlines),
              !name.isEmpty
        else { return nil }
        return name
    }
}

/// 받아 둔 프로필. 키체인 자리 이름으로 찾는다.
///
/// 비밀이 아니고(이메일·팀 이름) 잃어도 다시 물으면 되는 값이라 UserDefaults에 둔다.
/// "모든 설정 초기화"에 같이 지워져도 다음 조회에서 다시 채워진다.
enum AccountProfileStore {
    private static let key = "account.profiles"

    static func all() -> [String: AccountProfile] {
        guard let data = UserDefaults.standard.data(forKey: key) else { return [:] }
        return (try? JSONDecoder().decode([String: AccountProfile].self, from: data)) ?? [:]
    }

    static func profile(for service: String) -> AccountProfile? { all()[service] }

    static func save(_ profile: AccountProfile, for service: String) {
        var profiles = all()
        profiles[service] = profile
        store(profiles)
    }

    static func remove(_ service: String) {
        var profiles = all()
        profiles[service] = nil
        store(profiles)
    }

    private static func store(_ profiles: [String: AccountProfile]) {
        guard let data = try? JSONEncoder().encode(profiles) else { return }
        UserDefaults.standard.set(data, forKey: key)
    }
}

/// 계정을 찾고 · 더하고 · 빼는 곳.
enum ClaudeAccounts {
    /// 계정 추가로 만든 설정 폴더들이 사는 곳.
    ///
    /// **번들 ID 폴더 안에 둔다.** 테스트판에서 추가한 계정은 정식판에서 "다른 설정 폴더의
    /// 로그인"으로 보인다 — 키체인은 하나라 보이기는 하지만, 폴더를 몰라서 재로그인과
    /// 빼기는 그 계정을 추가한 쪽에서만 된다.
    static var accountsFolder: URL? {
        AppSupport.folder?.appendingPathComponent("accounts", isDirectory: true)
    }

    /// 키체인에 로그인이 실제로 들어 있는 계정 전부. 기본 로그인이 맨 앞이다.
    ///
    /// **값을 하나씩 읽어서 거른다** — `security` 를 항목마다 한 번 띄운다(1회 80ms).
    /// 이름만 보면 MCP 로그인만 남은 빈 자리까지 계정으로 잡힌다. 그래서 폴링에서는
    /// 부르지 않고, 설정 창을 열거나 계정을 더했을 때만 부른다.
    static func scan() -> [ClaudeAccount] {
        let dirs = knownConfigDirs()
        let accounts = ClaudeKeychain.credentialServices().compactMap { item -> ClaudeAccount? in
            guard let credentials = ClaudeKeychain.readCredentials(service: item.service) else { return nil }
            return ClaudeAccount(
                service: item.service,
                keychainAccount: item.account,
                subscriptionType: credentials.subscriptionType,
                configDir: dirs[item.service]
            )
        }
        return accounts.sorted { lhs, rhs in
            if lhs.isDefault != rhs.isDefault { return lhs.isDefault }
            // 우리가 추가한 것을 남이 만든 것보다 앞에. 그 안에서는 이름 순으로 고정한다.
            if lhs.isRemovable != rhs.isRemovable { return lhs.isRemovable }
            return lhs.service < rhs.service
        }
    }

    /// 우리가 만든 폴더 → 그 폴더가 쓰는 키체인 자리.
    static func knownConfigDirs() -> [String: URL] {
        guard let folder = accountsFolder,
              let entries = try? FileManager.default.contentsOfDirectory(
                at: folder, includingPropertiesForKeys: [.isDirectoryKey]
              )
        else { return [:] }

        var result: [String: URL] = [:]
        for dir in entries where (try? dir.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true {
            result[ClaudeKeychain.service(forConfigDir: dir.path)] = dir
        }
        return result
    }

    static func configDir(for service: String) -> URL? { knownConfigDirs()[service] }

    /// 새 계정을 로그인시킬 빈 폴더.
    ///
    /// **로그인을 안 끝내고 닫은 폴더가 있으면 그걸 다시 쓴다.** 누를 때마다 새로 만들면
    /// 그만둔 시도마다 빈 폴더가 쌓인다. 키체인에 자리가 없는 폴더가 그런 폴더다.
    static func prepareNewConfigDir() -> URL? {
        guard AppSupport.prepared() != nil, let folder = accountsFolder else { return nil }
        let manager = FileManager.default
        try? manager.createDirectory(at: folder, withIntermediateDirectories: true,
                                     attributes: [.posixPermissions: 0o700])

        let used = Set(ClaudeKeychain.credentialServices().map(\.service))
        if let abandoned = knownConfigDirs().first(where: { !used.contains($0.key) })?.value {
            return abandoned
        }

        let dir = folder.appendingPathComponent(
            String(UUID().uuidString.prefix(8)).lowercased(), isDirectory: true
        )
        do {
            try manager.createDirectory(at: dir, withIntermediateDirectories: true,
                                        attributes: [.posixPermissions: 0o700])
        } catch {
            return nil
        }
        return dir
    }

    /// 우리가 추가한 계정을 뺀다. 키체인 자리 · 폴더 · 갱신해 둔 토큰 · 프로필을 같이 지운다.
    ///
    /// **키체인 자리부터 지운다.** 목록은 키체인에서 만들어서, 폴더만 지우면 계정이 그대로
    /// 남은 채 "다른 설정 폴더의 로그인"으로 이름만 바뀐다.
    @discardableResult
    static func remove(_ account: ClaudeAccount) -> Bool {
        guard let dir = account.configDir else { return false }
        guard ClaudeKeychain.delete(service: account.service, account: account.keychainAccount) else {
            return false
        }
        RefreshedTokenStore.clear(service: account.service)
        AccountProfileStore.remove(account.service)
        try? FileManager.default.removeItem(at: dir)
        return true
    }
}

/// 설정 창이 그리는 계정 목록.
///
/// 앱 수명만큼 산다(`Upgrader.shared` 와 같은 꼴). 설정 창을 닫아도 계정 추가 뒤
/// 새 로그인을 기다리던 것이 끊기지 않는다.
@MainActor
final class ClaudeAccountDirectory: ObservableObject {
    static let shared = ClaudeAccountDirectory()

    @Published private(set) var accounts: [ClaudeAccount] = []
    @Published private(set) var profiles: [String: AccountProfile] = AccountProfileStore.all()
    @Published private(set) var isScanning = false
    /// 계정 추가로 띄운 로그인이 끝나기를 기다리는 중.
    @Published private(set) var isWaitingForLogin = false
    /// 방금 계정 추가가 로그인 없이 끝났다. 다시 누르면 지운다.
    @Published private(set) var loginFailed = false

    func profile(for account: ClaudeAccount) -> AccountProfile? { profiles[account.service] }

    /// 렌더 확인용. 키체인도 네트워크도 안 건드리고 정해 준 목록을 그대로 쓴다.
    func showPreview(_ accounts: [ClaudeAccount], profiles: [String: AccountProfile]) {
        self.accounts = accounts
        self.profiles = profiles
    }

    /// 키체인을 다시 훑고, 이름을 모르는 계정은 프로필을 묻는다.
    func rescan() {
        guard !isScanning else { return }
        isScanning = true
        Task.detached(priority: .userInitiated) {
            let found = ClaudeAccounts.scan()
            await MainActor.run {
                self.accounts = found
                self.isScanning = false
            }
            await self.fillMissingProfiles(found)
        }
    }

    /// 아직 이름을 모르는 계정만 묻는다. **토큰이 살아 있을 때만.**
    ///
    /// 만료된 계정을 이름 하나 때문에 갱신하지 않는다(`CredentialStore.peek` 참고).
    /// 그 계정을 고르면 조회가 갱신하고, 그때 이름도 따라 채워진다.
    private nonisolated func fillMissingProfiles(_ accounts: [ClaudeAccount]) async {
        let known = AccountProfileStore.all()
        for account in accounts {
            if let stored = known[account.service], !stored.isStale { continue }
            guard let credentials = CredentialStore.peek(service: account.service),
                  credentials.isUsableForAWhile,
                  let profile = await UsageAPI.fetchProfile(accessToken: credentials.accessToken)
            else { continue }
            AccountProfileStore.save(profile, for: account.service)
        }
        let profiles = AccountProfileStore.all()
        await MainActor.run { self.profiles = profiles }
    }

    /// 조회가 프로필을 새로 받았을 수 있다. 화면을 저장된 값에 맞춘다.
    func reloadProfiles() {
        profiles = AccountProfileStore.all()
    }

    /// 새 폴더로 로그인을 띄운다. 끝나면 목록을 다시 훑는다.
    func addAccount() {
        guard let dir = ClaudeAccounts.prepareNewConfigDir() else { return }
        loginFailed = false
        let started = ClaudeLogin.shared.start(configDir: dir) { [weak self] succeeded in
            guard let self else { return }
            self.isWaitingForLogin = false
            self.loginFailed = !succeeded
            self.rescan()
        }
        guard started else {
            ClaudeCLI.alertMissingExecutable()
            return
        }
        isWaitingForLogin = true
    }

    /// 기다리던 계정 추가를 그만둔다. 브라우저에서 그냥 닫았을 때 버튼을 되살린다.
    func cancelAddAccount() {
        ClaudeLogin.shared.cancel()
        loginFailed = false
    }

    /// 우리가 추가한 계정을 뺀다. 성공하면 목록에서도 바로 지운다.
    @discardableResult
    func remove(_ account: ClaudeAccount) -> Bool {
        guard ClaudeAccounts.remove(account) else { return false }
        accounts.removeAll { $0.service == account.service }
        profiles[account.service] = nil
        return true
    }
}
