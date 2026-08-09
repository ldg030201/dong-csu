import Foundation

/// 갱신해서 받아 온 토큰 한 벌.
///
/// `OAuthCredentials`와 따로 두는 이유는 **출처가 다르기 때문**이다. 저쪽은 Claude Code가
/// 키체인에 적어 둔 것이고, 이쪽은 우리가 서버에 물어서 받은 것이다. 섞어 두면 어느 것을
/// 어디에 저장해야 하는지가 흐려진다.
struct RefreshedToken: Codable, Sendable {
    let accessToken: String
    /// 다음 갱신에 쓸 것. **서버가 회전시키면 새 값이 온다.**
    let refreshToken: String?
    let expiresAt: Date?

    var isExpired: Bool {
        guard let expiresAt else { return false }
        return expiresAt <= Date()
    }

    /// 만료 직전이면 쓰지 않는다. 쓰려는 순간 만료돼 있으면 헛조회가 된다.
    var isUsableForAWhile: Bool {
        guard let expiresAt else { return true }
        return expiresAt.timeIntervalSinceNow > 60
    }
}

/// 만료된 토큰을 리프레시 토큰으로 갱신한다.
///
/// **토큰 값은 어디에도 남기지 않는다.** 성공·실패만 알린다.
enum OAuthTokenRefresher {
    static let endpoint = URL(string: "https://console.anthropic.com/v1/oauth/token")!

    /// Claude Code의 공개 OAuth 클라이언트 ID. 비밀이 아니다.
    static let clientID = "9d1c250a-e61b-44d9-88ed-5944d1962f5e"

    static func refresh(using refreshToken: String) async -> RefreshedToken? {
        guard !refreshToken.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }

        var request = URLRequest(url: endpoint, timeoutInterval: 15)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONSerialization.data(withJSONObject: [
            "grant_type": "refresh_token",
            "refresh_token": refreshToken,
            "client_id": clientID,
        ])

        guard let (data, response) = try? await URLSession.shared.data(for: request),
              let http = response as? HTTPURLResponse
        else { return nil }

        // 리프레시 토큰까지 죽었으면 400이 온다. 그때는 재로그인 말고 길이 없다.
        guard http.statusCode == 200 else { return nil }
        return parse(data)
    }

    static func parse(_ data: Data) -> RefreshedToken? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let accessToken = root["access_token"] as? String, !accessToken.isEmpty
        else { return nil }

        let rotated = (root["refresh_token"] as? String).flatMap { $0.isEmpty ? nil : $0 }

        // `expires_in`은 **초**다. 밀리초로 읽으면 만료가 한참 뒤로 밀려서, 죽은 토큰을
        // 살아 있다고 믿고 계속 헛조회한다.
        var expiresAt: Date?
        if let seconds = (root["expires_in"] as? NSNumber)?.doubleValue,
           seconds > 0, seconds < 365 * 24 * 3600 {
            expiresAt = Date().addingTimeInterval(seconds)
        }

        return RefreshedToken(accessToken: accessToken, refreshToken: rotated, expiresAt: expiresAt)
    }
}

/// 갱신해 둔 토큰을 우리 폴더에 둔다.
///
/// **키체인에 되돌려 쓰는 것과 별개로 항상 여기 둔다.** 되돌려 쓰기는 사용자가 키체인
/// 접근을 막으면 실패할 수 있는데, 그때도 우리는 계속 돌아야 한다. 여기 있는 것이
/// "우리가 마지막으로 받아 낸 값"이고, 키체인 쪽은 Claude Code와 나눠 쓰는 자리다.
enum RefreshedTokenStore {
    static var fileURL: URL? { AppSupport.folder?.appendingPathComponent("token.json") }

    static func load() -> RefreshedToken? {
        guard let fileURL, let data = try? Data(contentsOf: fileURL) else { return nil }
        return try? JSONDecoder().decode(RefreshedToken.self, from: data)
    }

    static func save(_ token: RefreshedToken) {
        // 폴더부터 조인다. 파일 권한은 쓰고 난 뒤에야 바꿀 수 있어서 그 사이가 잠깐 열리는데,
        // 폴더가 닫혀 있으면 그 틈에도 남이 들어오지 못한다.
        guard AppSupport.prepared() != nil, let fileURL else { return }
        guard let data = try? JSONEncoder().encode(token) else { return }
        try? data.write(to: fileURL, options: .atomic)
        // 자격증명이다. 본인 외에는 못 읽게 한다.
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: fileURL.path)
    }

    static func clear() {
        guard let fileURL else { return }
        try? FileManager.default.removeItem(at: fileURL)
    }
}


/// 우리 파일들이 사는 폴더.
///
/// **본인만 읽을 수 있게 만든다.** 여기에 갱신한 토큰이 들어간다. `~/Library/Application
/// Support` 자체가 보통 닫혀 있지만, 그건 우리가 정한 것이 아니라 기대일 뿐이다.
enum AppSupport {
    static var folder: URL? {
        // 번들 ID로 갈라서 테스트판과 정식판이 서로의 것을 건드리지 않게 한다.
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?
            .appendingPathComponent(Bundle.main.bundleIdentifier ?? "com.ldg.dong-csu", isDirectory: true)
    }

    /// 폴더를 만들고 권한을 조인다. 이미 있으면 권한만 맞춘다.
    @discardableResult
    static func prepared() -> URL? {
        guard let folder else { return nil }
        let manager = FileManager.default
        try? manager.createDirectory(
            at: folder,
            withIntermediateDirectories: true,
            attributes: [.posixPermissions: 0o700]
        )
        // 이미 있던 폴더는 위에서 권한이 안 바뀐다. 옛 판이 만들어 둔 것까지 조인다.
        try? manager.setAttributes([.posixPermissions: 0o700], ofItemAtPath: folder.path)
        return folder
    }
}
