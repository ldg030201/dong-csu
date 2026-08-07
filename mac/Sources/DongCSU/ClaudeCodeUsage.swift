import Foundation

/// 토큰 수 묶음 하나.
struct TokenTally: Codable, Sendable, Equatable {
    var responses = 0
    var input = 0
    var output = 0
    var cacheCreation = 0
    var cacheRead = 0

    var total: Int { input + output + cacheCreation + cacheRead }
    var isEmpty: Bool { responses == 0 }

    static func + (lhs: Self, rhs: Self) -> Self {
        Self(
            responses: lhs.responses + rhs.responses,
            input: lhs.input + rhs.input,
            output: lhs.output + rhs.output,
            cacheCreation: lhs.cacheCreation + rhs.cacheCreation,
            cacheRead: lhs.cacheRead + rhs.cacheRead
        )
    }

    static func += (lhs: inout Self, rhs: Self) { lhs = lhs + rhs }
}

/// Claude Code가 남긴 기록에서 실제로 쓴 토큰을 센다.
///
/// **여기서 세는 것은 Claude Code 것뿐이다.** 클로드 앱·웹은 이 기계에 아무것도
/// 남기지 않는다. 계정 전체를 보려면 한도 %(사용량 API)를 봐야 한다 — 그쪽은
/// 어디서 쓰든 같은 창을 깎는다. 측정 화면이 두 숫자를 나란히 두는 이유가 이거다.
enum ClaudeCodeUsage {
    /// `~/.claude/projects`. `CLAUDE_CONFIG_DIR`을 쓰면 그쪽을 본다(키체인 쪽과 같은 규칙).
    static var projectsDirectory: URL {
        let configured = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"]?
            .trimmingCharacters(in: .whitespacesAndNewlines)
        let base: URL
        if let configured, !configured.isEmpty {
            base = URL(fileURLWithPath: (configured as NSString).expandingTildeInPath)
        } else {
            base = FileManager.default.homeDirectoryForCurrentUser
                .appendingPathComponent(".claude", isDirectory: true)
        }
        return base.appendingPathComponent("projects", isDirectory: true)
    }

    static var isAvailable: Bool {
        FileManager.default.fileExists(atPath: projectsDirectory.path)
    }

    /// 기록 파일 전부. 프로젝트마다 폴더가 갈려 있어서 훑어 내려간다.
    static func transcripts() -> [URL] {
        guard let walker = FileManager.default.enumerator(
            at: projectsDirectory,
            includingPropertiesForKeys: [.fileSizeKey],
            options: [.skipsHiddenFiles, .skipsPackageDescendants]
        ) else { return [] }

        return walker.compactMap { $0 as? URL }.filter { $0.pathExtension == "jsonl" }
    }

    /// 지금 파일 끝.
    ///
    /// **측정을 시작할 때 이걸로 기준을 잡는다.** 0에서 읽기 시작하면 며칠 치 옛 기록을
    /// 전부 훑어야 하고, 시각으로 걸러도 수십 MB를 읽는 값이 나온다.
    static func endOffsets() -> [String: UInt64] {
        var offsets: [String: UInt64] = [:]
        for url in transcripts() {
            let size = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
            offsets[url.path] = UInt64(max(0, size))
        }
        return offsets
    }

    /// `claude-opus-5` → `Opus 5`. 화면에 그대로 쓰기엔 길고 안 예쁘다.
    static func displayName(forModel raw: String) -> String {
        var name = raw
        if name.hasPrefix("claude-") { name.removeFirst("claude-".count) }
        // 끝에 붙는 날짜(`-20251001`)는 사람에게 의미가 없다.
        var parts = name.split(separator: "-").map(String.init)
        if let last = parts.last, last.count == 8, last.allSatisfy(\.isNumber) { parts.removeLast() }
        guard !parts.isEmpty else { return raw }

        // 숫자가 이어지면 판 번호다. `haiku 4 5` 보다 `Haiku 4.5` 가 읽힌다.
        var words: [String] = []
        for part in parts {
            if part.allSatisfy(\.isNumber), let previous = words.last,
               previous.allSatisfy({ $0.isNumber || $0 == "." }) {
                words[words.count - 1] = previous + "." + part
            } else {
                words.append(part.prefix(1).uppercased() + part.dropFirst())
            }
        }
        return words.joined(separator: " ")
    }
}

/// 기록 파일을 **덧붙은 부분만** 읽어서 토큰을 더한다.
///
/// 파일이 8~13MB씩 되기 때문에 폴링마다 통째로 다시 읽을 수 없다. 기록은 줄 단위로
/// 덧붙기만 하므로 파일마다 어디까지 읽었는지 기억해 두고 그 뒤만 읽는다.
///
/// 화면 밖에서 도는 값이라 메인 액터를 타지 않는다.
struct TokenScan: Sendable {
    /// 이 시각보다 앞선 기록은 세지 않는다.
    ///
    /// 오프셋만으로는 부족하다 — 세션을 이어가면 **옛 응답이 새 파일로 통째로 복사**되고,
    /// 그건 새로 쓴 토큰이 아니다. 복사본은 원래 시각을 그대로 달고 오므로 여기서 걸린다.
    var since: Date
    var offsets: [String: UInt64]
    var seenIDs: Set<String>

    struct Result: Sendable {
        var added = TokenTally()
        var addedByModel: [String: TokenTally] = [:]
        var offsets: [String: UInt64] = [:]
        var seenIDs: Set<String> = []
    }

    /// 중복 제거용 id를 이만큼까지만 들고 있는다.
    ///
    /// 측정 구간 안의 응답 수만큼만 쌓이므로 실제로는 몇천을 넘지 않는다. 그래도
    /// 몇 주씩 켜 두는 사람이 있을 수 있어서 위를 막아 둔다 — 넘으면 중복 제거만
    /// 느슨해지고 합계는 계속 쌓인다.
    static let seenLimit = 50_000

    func run() -> Result {
        var result = Result(offsets: offsets, seenIDs: seenIDs)

        for url in ClaudeCodeUsage.transcripts() {
            let offset = result.offsets[url.path] ?? 0

            // **덧붙은 게 없으면 열지도 않는다.** 기록 폴더에 파일이 수백 개씩 쌓이는데
            // 그걸 1분마다 전부 열면 열고 닫는 값만으로도 비싸진다. 크기는 훑을 때
            // 이미 받아 둔 값이라 공짜다.
            if let size = try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize,
               UInt64(max(0, size)) <= offset {
                continue
            }

            guard let (data, next) = Self.readAppended(url, from: offset) else { continue }
            result.offsets[url.path] = next
            guard !data.isEmpty else { continue }

            for line in data.split(separator: UInt8(ascii: "\n")) {
                guard let entry = Self.parse(Data(line), since: since) else { continue }
                // **같은 응답이 두세 줄에 걸쳐 적힌다.** 값은 매번 같으므로 처음 것만 센다.
                guard !result.seenIDs.contains(entry.id) else { continue }
                if result.seenIDs.count < Self.seenLimit { result.seenIDs.insert(entry.id) }

                result.added += entry.tally
                result.addedByModel[entry.model, default: TokenTally()] += entry.tally
            }
        }
        return result
    }

    // MARK: - 읽기

    /// 오프셋 뒤에 덧붙은 부분. **완성된 줄까지만** 돌려주고 그만큼만 오프셋을 옮긴다.
    /// 마침 쓰는 중이면 마지막 줄이 잘려 있는데, 그걸 파싱하면 그 응답을 영영 놓친다.
    private static func readAppended(_ url: URL, from offset: UInt64) -> (Data, UInt64)? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }

        guard let size = try? handle.seekToEnd() else { return nil }
        // 파일이 줄었다. 지워졌다 다시 만들어진 것이니 지금 끝을 새 기준으로 잡는다.
        // 0부터 다시 읽으면 이미 센 것을 또 센다.
        guard size > offset else { return (Data(), min(offset, size)) }

        guard (try? handle.seek(toOffset: offset)) != nil,
              let data = try? handle.readToEnd(), !data.isEmpty else { return (Data(), offset) }

        guard let lastNewline = data.lastIndex(of: UInt8(ascii: "\n")) else { return (Data(), offset) }
        let complete = Data(data[...lastNewline])
        return (complete, offset + UInt64(complete.count))
    }

    private struct Entry {
        let id: String
        let model: String
        let tally: TokenTally
    }

    private static func parse(_ line: Data, since: Date) -> Entry? {
        guard let root = try? JSONSerialization.jsonObject(with: line) as? [String: Any],
              let message = root["message"] as? [String: Any],
              let usage = message["usage"] as? [String: Any],
              let id = message["id"] as? String
        else { return nil }

        guard let stamp = (root["timestamp"] as? String).flatMap(parseDate), stamp >= since else { return nil }

        let model = (message["model"] as? String).map(ClaudeCodeUsage.displayName(forModel:)) ?? "(불명)"
        let tally = TokenTally(
            responses: 1,
            input: number(usage["input_tokens"]),
            output: number(usage["output_tokens"]),
            cacheCreation: number(usage["cache_creation_input_tokens"]),
            cacheRead: number(usage["cache_read_input_tokens"])
        )
        return Entry(id: id, model: model, tally: tally)
    }

    private static func number(_ value: Any?) -> Int {
        (value as? NSNumber)?.intValue ?? 0
    }

    // 포매터 생성이 파싱보다 비싸다. 한 번만 만들어 쓴다(UsageAPI와 같은 이유).
    private static let iso8601WithFraction: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    private static let iso8601Plain = ISO8601DateFormatter()

    private static func parseDate(_ text: String) -> Date? {
        iso8601WithFraction.date(from: text) ?? iso8601Plain.date(from: text)
    }
}
