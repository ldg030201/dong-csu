import AppKit

/// `dong-csu --fit-sheet <시트.png> [나올.json]` — 그린 시트를 읽어 칸 좌표를 뽑는다.
///
/// **좌표를 먼저 정해서 넘기지 않는 이유가 여기 있다.** 그리는 쪽(사람이든 AI든)은
/// 정해 준 격자를 정확히 맞추지 못한다. 나온 그림에서 칸이 실제로 어디 있는지 찾아
/// 적어 두면, 몇 픽셀 밀렸든 반 칸 넘어갔든 상관이 없어진다.
///
/// 찾는 방법이 두 가지고, 되는 쪽을 쓴다.
///
/// | | 언제 | 어떻게 |
/// | --- | --- | --- |
/// | 선 | 칸마다 상자를 그려 줬을 때 | 가로로 쭉 이어진 한 색 줄을 찾아 그 사이를 칸으로 본다 |
/// | 빈틈 | 배경이 투명할 때 | 통째로 비어 있는 줄을 찾아 그 사이를 칸으로 본다 |
///
/// 빈틈 쪽은 **줄마다 따로** 센다. 줄마다 칸 수가 달라도(5·6·6·5) 그대로 잡힌다.
enum SheetFit {
    /// 알파가 이보다 크면 그려진 픽셀로 본다.
    private static let inkAlpha: UInt8 = 8
    /// 선으로 치려면 이보다 진해야 한다.
    private static let lineAlpha: UInt8 = 200
    /// 한 줄이 같은 색인지 볼 때 봐주는 폭.
    private static let lineTolerance = 24

    struct Result {
        let atlas: MascotAtlas
        /// 어떻게 찾았는지. 사람에게 알려 준다.
        let method: String
        /// 사람이 눈으로 볼 줄들.
        let report: [String]
        /// 배치와 어긋난 것이 있으면 여기 담긴다.
        let warnings: [String]
    }

    /// 못 찾았을 때 왜 그런지.
    enum Failure: Error {
        case unreadable
        /// 투명한 자리가 하나도 없다. 편집기의 체커무늬를 진짜 픽셀로 그린 그림이 이렇다.
        case noTransparency
        /// 칸을 가를 만한 것이 안 보인다.
        case noStructure

        var advice: String {
            switch self {
            case .unreadable:
                return "그림을 읽지 못했다. PNG 인지 확인해라."
            case .noTransparency:
                return """
                투명한 자리가 하나도 없다. 배경을 진짜 투명(알파 0)으로 저장해라 —
                편집기가 투명을 보여주려고 그리는 체커무늬를 그대로 그려 넣으면 이렇게 된다.
                """
            case .noStructure:
                return "칸을 가를 빈틈도 선도 없다. 칸 사이를 띄우거나 칸마다 상자를 그려라."
            }
        }
    }

    /// 배경이 투명한지. 통째로 불투명한 그림은 마스코트로 쓸 수 없다 —
    /// 균등 격자로 그냥 잘려서 네모난 덩어리가 링 안에 박힌다.
    static func hasTransparency(imageAt path: String) -> Bool {
        guard
            let image = NSImage(contentsOfFile: path),
            let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil),
            let pixels = Pixels(cg)
        else { return false }
        return pixels.hasAnyTransparency
    }

    static func fit(imageAt path: String) -> Swift.Result<Result, Failure> {
        guard
            let image = NSImage(contentsOfFile: path),
            let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil),
            let pixels = Pixels(cg)
        else { return .failure(.unreadable) }

        let (cells, method) = findCells(pixels)
        // **칸이 하나면 못 찾은 것이다.** 그림 전체가 한 칸으로 잡혔다는 뜻이라,
        // 그대로 적어 두면 앱이 시트를 통째로 `idle` 한 장으로 읽는다.
        // 안 적느니만 못하므로 여기서 끊는다.
        guard cells.reduce(0, { $0 + $1.count }) > 1 else {
            return .failure(pixels.hasAnyTransparency ? .noStructure : .noTransparency)
        }
        return .success(assemble(cells: cells, method: method, size: pixels.size))
    }

    /// 줄 단위로 묶인 칸들. 바깥 배열이 줄, 안쪽이 그 줄의 칸이다.
    private static func findCells(_ pixels: Pixels) -> ([[CGRect]], String) {
        if let byLine = cellsByLines(pixels) { return (byLine, "선") }
        return (cellsByGaps(pixels), "빈틈")
    }

    // MARK: - 선으로 찾기

    private static func cellsByLines(_ pixels: Pixels) -> [[CGRect]]? {
        let rowBands = bands(count: pixels.height) { pixels.isLineRow($0) }
        let columnBands = bands(count: pixels.width) { pixels.isLineColumn($0) }
        // 안쪽 칸막이가 양쪽에 하나씩은 있어야 격자다. 테두리만 있는 건 격자가 아니다.
        let rowStrips = strips(between: rowBands, limit: pixels.height)
        let columnStrips = strips(between: columnBands, limit: pixels.width)
        guard rowStrips.count >= 2, columnStrips.count >= 2 else { return nil }

        return rowStrips.map { rowStrip in
            columnStrips.compactMap { columnStrip -> CGRect? in
                let rect = CGRect(
                    x: columnStrip.lowerBound, y: rowStrip.lowerBound,
                    width: columnStrip.count, height: rowStrip.count
                )
                // 선 안쪽이 통째로 비었으면 안 그린 칸이다.
                return pixels.hasInk(in: rect) ? rect : nil
            }
        }
    }

    // MARK: - 빈틈으로 찾기

    private static func cellsByGaps(_ pixels: Pixels) -> [[CGRect]] {
        let emptyRows = bands(count: pixels.height) { !pixels.rowHasInk($0) }
        let inkRows = strips(between: emptyRows, limit: pixels.height)
        guard !inkRows.isEmpty else { return [] }

        // **그 줄 안에서만 센다.** 줄마다 칸 수가 다를 수 있어서, 그림 전체로
        // 세로 빈틈을 찾으면 칸이 많은 줄에 맞춰져 적은 줄이 잘못 잘린다.
        let inkColumns = inkRows.map { inkRow in
            strips(
                between: bands(count: pixels.width) { column in
                    !pixels.hasInk(in: CGRect(
                        x: column, y: inkRow.lowerBound, width: 1, height: inkRow.count
                    ))
                },
                limit: pixels.width
            )
        }

        let rowCells = evenGrid([inkRows], divisions: inkRows.count, limit: pixels.height)?.first
            ?? expand(inkRows, limit: pixels.height)
        let columnCount = inkColumns.map(\.count).max() ?? 0
        let columnCells = evenGrid(inkColumns, divisions: columnCount, limit: pixels.width)
            ?? inkColumns.map { expand($0, limit: pixels.width) }

        return zip(rowCells, columnCells).map { rowCell, line in
            line.map { columnCell in
                CGRect(
                    x: columnCell.lowerBound, y: rowCell.lowerBound,
                    width: columnCell.count, height: rowCell.count
                )
            }
        }
    }

    /// 알맹이들이 **고른 격자**에 딱 들어맞으면 그 격자를 돌려준다. 아니면 nil.
    ///
    /// 중간점으로 나누는 쪽(`expand`)은 알맹이 자리를 따라가서 칸 크기가 제각각이
    /// 된다. 걷는 자세는 몸이 좌우로 기울어 있어서 알맹이 가운데가 칸 가운데와
    /// 다르기 때문이다. 그렇게 잘라 놓으면 **칸마다 크기가 달라져 그림 안의 기울기가
    /// 사라진다.**
    ///
    /// 고른 격자로 그렸으면 — 사람이 도구로 그렸으면 대개 그렇다 — 알맹이가 저마다
    /// 한 칸 안에 온전히 들어간다. 그때는 그 격자를 그대로 쓰는 게 맞다.
    private static func evenGrid(
        _ ink: [[Range<Int>]],
        divisions: Int,
        limit: Int
    ) -> [[Range<Int>]]? {
        guard divisions > 0 else { return nil }
        func edge(_ step: Int) -> Int { limit * step / divisions }

        var result: [[Range<Int>]] = []
        for line in ink {
            guard line.count <= divisions else { return nil }
            var cells: [Range<Int>] = []
            var taken = Set<Int>()
            for span in line {
                guard let slot = (0..<divisions).first(where: {
                    span.lowerBound >= edge($0) && span.upperBound <= edge($0 + 1)
                }), !taken.contains(slot) else { return nil }
                taken.insert(slot)
                cells.append(edge(slot)..<edge(slot + 1))
            }
            result.append(cells)
        }
        return result
    }

    /// 알맹이를 감싼 자리를 **칸**으로 넓힌다.
    ///
    /// 알맹이에 딱 맞춰 자르면 칸마다 여백이 다 달라져서, 결국 칸마다 따로 가운데를
    /// 맞추는 꼴이 된다 — 걸을 때 몸이 좌우로 기우는 것 같은 **그려 넣은 움직임이
    /// 통째로 사라진다.** 이웃과의 빈틈을 반씩 나눠 가지면 그림 안에서의 자리가 그대로
    /// 남는다.
    ///
    /// 바깥쪽 끝은 나눠 가질 이웃이 없어서 **반대쪽 여백만큼** 둔다. 그래야 줄 끝의
    /// 칸이 그림 가장자리까지 늘어나 혼자 두 배로 넓어지지 않는다.
    private static func expand(_ ink: [Range<Int>], limit: Int) -> [Range<Int>] {
        guard ink.count > 1 else { return [0..<limit] }
        var edges = [Int](repeating: 0, count: ink.count + 1)
        for index in 1..<ink.count {
            edges[index] = (ink[index - 1].upperBound + ink[index].lowerBound) / 2
        }
        edges[0] = max(0, ink[0].lowerBound - (edges[1] - ink[0].upperBound))
        let last = ink.count - 1
        edges[ink.count] = min(limit, ink[last].upperBound + (ink[last].lowerBound - edges[last]))
        return (0..<ink.count).map { edges[$0]..<max(edges[$0] + 1, edges[$0 + 1]) }
    }

    // MARK: - 이름 붙이기

    private static func assemble(
        cells: [[CGRect]],
        method: String,
        size: CGSize
    ) -> Result {
        let expected = MascotSheet.layout.map { $0.compactMap { $0 } }
        var warnings: [String] = []

        // 줄 수와 줄마다의 칸 수가 배치와 같으면 줄 단위로 맞춘다. 그 편이 안전하다 —
        // 한 줄을 통째로 비워도 나머지 줄의 이름이 안 밀린다.
        let names: [[MascotSprite]]
        if cells.count == expected.count,
           zip(cells, expected).allSatisfy({ $0.count == $1.count }) {
            names = expected
        } else {
            warnings.append(
                "찾은 칸이 배치와 다르다 (찾음 \(cells.map(\.count)) / 배치 \(expected.map(\.count)))."
                + " 읽은 순서대로 이름을 붙였으니 눈으로 맞춰 봐라."
            )
            var flat = MascotSheet.readingOrder
            names = cells.map { row in
                let take = min(row.count, flat.count)
                defer { flat.removeFirst(take) }
                return Array(flat.prefix(take))
            }
        }

        var frames: [String: MascotAtlas.Box] = [:]
        var report: [String] = []
        for (row, line) in cells.enumerated() {
            for (column, rect) in line.enumerated() {
                guard row < names.count, column < names[row].count else { continue }
                let sprite = names[row][column]
                let box = MascotAtlas.Box(
                    x: Int(rect.minX), y: Int(rect.minY),
                    w: Int(rect.width), h: Int(rect.height)
                )
                frames[sprite.rawValue] = box
                report.append(
                    "  \(sprite.rawValue.padding(toLength: 20, withPad: " ", startingAt: 0))"
                    + " x=\(box.x) y=\(box.y) w=\(box.w) h=\(box.h)"
                )
            }
        }

        let missing = MascotSheet.readingOrder.filter { frames[$0.rawValue] == nil }
        if !missing.isEmpty {
            warnings.append("자리를 못 찾은 칸: \(missing.map(\.rawValue).joined(separator: ", "))")
        }

        return Result(
            atlas: MascotAtlas(frames: frames),
            method: "\(method) (그림 \(Int(size.width))x\(Int(size.height)))",
            report: report,
            warnings: warnings
        )
    }

    // MARK: - 줄 묶기

    /// 조건에 맞는 자리들을 이어진 덩어리로 묶는다.
    private static func bands(count: Int, matches: (Int) -> Bool) -> [Range<Int>] {
        var result: [Range<Int>] = []
        var start: Int?
        for index in 0..<count {
            if matches(index) {
                if start == nil { start = index }
            } else if let begin = start {
                result.append(begin..<index)
                start = nil
            }
        }
        if let begin = start { result.append(begin..<count) }
        return result
    }

    /// 칸막이 사이에 남은 자리들. 양 끝의 칸막이는 테두리라 알아서 빠진다.
    private static func strips(between separators: [Range<Int>], limit: Int) -> [Range<Int>] {
        var result: [Range<Int>] = []
        var cursor = 0
        for separator in separators {
            if separator.lowerBound > cursor { result.append(cursor..<separator.lowerBound) }
            cursor = separator.upperBound
        }
        if cursor < limit { result.append(cursor..<limit) }
        return result
    }
}

/// 그림 한 장의 픽셀. 한 번만 풀어 놓고 계속 들여다본다.
private struct Pixels {
    let width: Int
    let height: Int
    let data: [UInt8]

    var size: CGSize { CGSize(width: width, height: height) }

    init?(_ image: CGImage) {
        width = image.width
        height = image.height
        guard width > 0, height > 0 else { return nil }
        var buffer = [UInt8](repeating: 0, count: width * height * 4)
        guard let context = CGContext(
            data: &buffer, width: width, height: height, bitsPerComponent: 8,
            bytesPerRow: width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return nil }
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        data = buffer
    }

    private func offset(_ x: Int, _ y: Int) -> Int { (y * width + x) * 4 }

    func alpha(_ x: Int, _ y: Int) -> UInt8 { data[offset(x, y) + 3] }

    /// 투명한 자리가 한 곳이라도 있는지. 배경을 안 지운 그림을 가려낸다.
    var hasAnyTransparency: Bool {
        for index in stride(from: 3, to: data.count, by: 4) where data[index] <= 8 { return true }
        return false
    }

    func rowHasInk(_ y: Int) -> Bool {
        for x in 0..<width where alpha(x, y) > 8 { return true }
        return false
    }

    func hasInk(in rect: CGRect) -> Bool {
        let x0 = max(0, Int(rect.minX)), x1 = min(width, Int(rect.maxX))
        let y0 = max(0, Int(rect.minY)), y1 = min(height, Int(rect.maxY))
        guard x0 < x1, y0 < y1 else { return false }
        for y in y0..<y1 {
            for x in x0..<x1 where alpha(x, y) > 8 { return true }
        }
        return false
    }

    /// 가로로 쭉 이어진 한 색 줄인지. 칸막이로 그어 준 선을 찾는다.
    func isLineRow(_ y: Int) -> Bool {
        uniform(count: width) { offset($0, y) }
    }

    func isLineColumn(_ x: Int) -> Bool {
        uniform(count: height) { offset(x, $0) }
    }

    private func uniform(count: Int, at index: (Int) -> Int) -> Bool {
        guard count > 0 else { return false }
        var low = [Int](repeating: 255, count: 3)
        var high = [Int](repeating: 0, count: 3)
        for step in 0..<count {
            let base = index(step)
            guard data[base + 3] > 200 else { return false }
            for channel in 0..<3 {
                let value = Int(data[base + channel])
                if value < low[channel] { low[channel] = value }
                if value > high[channel] { high[channel] = value }
            }
        }
        return (0..<3).allSatisfy { high[$0] - low[$0] <= 24 }
    }
}
