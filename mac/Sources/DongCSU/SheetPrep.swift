import AppKit

/// `dong-csu --prep-sheet <받은.png> <나올.png> [cols=6] [rows=4] [keep=4]`
/// — 그려 받은 시트를 규격 시트로 만든다.
///
/// **AI에게 받은 그림은 그대로 못 쓴다.** 배경이 진짜 픽셀로 그려져 오고, 워터마크가
/// 박히고, 칸마다 캐릭터가 몇 픽셀씩 밀려 있다. 그걸 부탁으로 고치려 해 봤는데 매번
/// 다시 밀린다 — **받아서 고치는 편이 확실하다.**
///
/// 하는 일이 넷이다.
///
/// | | |
/// | --- | --- |
/// | 배경 빼기 | 가장자리에서 번져 들어간다. 안쪽 회색(죽음 칸)은 안 건드린다 |
/// | 칸 경계선 지우기 | 눈으로 보라고 그어 준 선을 뺀다 — 배경색이 아니라 안 지워진다 |
/// | 부스러기 털기 | 워터마크 조각처럼 작은 덩어리를 지운다 |
/// | 안 쓰는 칸 버리기 | 배치에 없는 자리는 통째로 뺀다 — **워터마크가 여기서 빠진다** |
/// | 자리 맞추기 | 칸마다 **바닥 · 가로 가운데**를 맞춘다 |
///
/// **자리 맞추기는 여기서만 한다.** 앱은 칸마다 따로 맞추지 않는다 — 그리는 사람이
/// 칸 안에서 일부러 옮겨 놓은 것(걸을 때 기울기)이 사라지기 때문이다. 하지만 AI가
/// 그린 것에는 그런 의도가 없고 어긋남만 있어서, 준비 단계에서 잡는 게 맞다.
@MainActor
enum SheetPrep {
    /// 캐릭터가 칸 안에서 위아래로 남길 여백(규격 칸 256 기준).
    ///
    /// **0이다.** 앉거나 매달린 자세는 모서리에 닿아 있어야 그렇게 읽히는데, 여백을
    /// 두면 붕 뜬다. 나머지 자세는 칸 둘레 여백을 읽을 때 다 같이 걷어내므로
    /// (`MascotSheet.trimTogether`) 여기서 남겨 둘 이유가 없다.
    private static let margin = 0

    struct Options {
        // **배치에서 읽는다.** 손으로 적어 두면 배치를 고쳤을 때 따라오지 않아서,
        // 엉뚱한 자리로 나눠 놓고도 칸 수는 맞아 보인다 — 실제로 그랬다.
        var columns = MascotSheet.columns
        var rows = MascotSheet.rows
        /// 위에서 이만큼 줄만 쓴다. 나머지는 버린다.
        var keep = MascotSheet.rows
        /// 이보다 작은 덩어리는 부스러기로 본다(원본 픽셀 수).
        var speck = 1500
        /// 칸마다 바닥·가운데를 맞출지.
        var aligns = true
    }

    static func run(from source: String, to destination: String, options: Options) -> Bool {
        guard
            let image = NSImage(contentsOfFile: source),
            let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil),
            var canvas = Canvas(cg)
        else {
            print("그림을 못 읽었다: \(source)")
            return false
        }
        print("받은 그림: \(canvas.width)x\(canvas.height)")

        let wiped = canvas.clearBackground()
        print("배경으로 지운 픽셀: \(wiped) (\(wiped * 100 / (canvas.width * canvas.height))%)")
        let specks = canvas.clearSpecks(smallerThan: options.speck)
        if specks > 0 { print("턴 부스러기: \(specks)픽셀") }

        let cells = canvas.cells(columns: options.columns, rows: options.rows, keep: options.keep)
        let drawn = cells.filter { $0.ink != nil }.count
        print("칸 \(cells.count)개 중 \(drawn)개에 그림이 있다")
        guard drawn > 1 else {
            print("칸을 못 갈랐다 — 배경이 안 빠졌거나 격자가 다르다")
            return false
        }

        guard let png = compose(canvas: canvas, cells: cells, options: options) else {
            print("규격 시트를 못 만들었다")
            return false
        }
        guard (try? png.write(to: URL(fileURLWithPath: destination))) != nil else {
            print("못 적었다: \(destination)")
            return false
        }
        let size = MascotSheet.canonicalSize
        print("규격 시트: \(destination) (\(Int(size.width))x\(Int(size.height)))")
        return true
    }

    /// 잘라낸 칸들을 규격 자리에 앉힌다.
    private static func compose(canvas: Canvas, cells: [Cell], options: Options) -> Data? {
        let side = MascotSheet.canonicalCell
        let pitch = side + MascotSheet.canonicalRule
        let inset = MascotSheet.canonicalRule
        let full = MascotSheet.canonicalSize

        // **배율은 하나만 쓴다.** 칸마다 제 크기에 맞춰 늘리면 작게 그린 칸이 부풀어서
        // 상태가 바뀔 때마다 캐릭터가 커졌다 작아졌다 한다.
        let inks = cells.compactMap(\.ink)
        let tallest = inks.map(\.height).max() ?? 1
        let widest = inks.map(\.width).max() ?? 1
        let room = CGFloat(side - margin * 2)
        let scale = min(room / CGFloat(tallest), room / CGFloat(widest))

        guard let context = CGContext(
            data: nil, width: Int(full.width), height: Int(full.height),
            bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return nil }
        context.clear(CGRect(origin: .zero, size: full))
        context.interpolationQuality = .high

        // 위에 붙는 칸을 어느 줄에 맞출지.
        //
        // **칸의 맨 위가 아니라 선 자세의 머리 높이에 맞춘다.** 칸 맨 위에 붙이면
        // 모든 칸을 감싸는 상자가 칸 높이 전체로 커지고, 그러면 서 있는 자세가 상자
        // 아래쪽에 처져서 링 안에서 위가 텅 빈다. 실제로 그렇게 나왔다.
        //
        // 여기 맞추면 상자가 곧 선 자세의 크기가 되고, 매달린 자세의 발끝은 여전히
        // 그 상자의 맨 위에 온다 — 앱이 보는 것은 칸이 아니라 이 상자다.
        let standingHeights = cells.compactMap { cell -> Int? in
            guard cell.row < MascotSheet.rows, cell.column < MascotSheet.columns,
                  let sprite = MascotSheet.layout[cell.row][cell.column],
                  sprite.anchor == .bottom, let ink = cell.ink else { return nil }
            return ink.height
        }
        let standingTop = standingHeights.max() ?? side
        // 세로 모서리에 붙는 칸이 맞출 왼쪽 줄. **칸의 왼쪽 끝이 아니다** —
        // 거기 붙이면 모든 칸을 감싸는 상자가 옆으로 넓어져서 서 있는 자세가
        // 오른쪽으로 밀린다. `standingTop` 과 같은 이유다.
        let standingWidths = cells.compactMap { cell -> Int? in
            guard cell.row < MascotSheet.rows, cell.column < MascotSheet.columns,
                  let sprite = MascotSheet.layout[cell.row][cell.column],
                  sprite.anchor == .bottom, let ink = cell.ink else { return nil }
            return ink.width
        }
        let standingWidest = standingWidths.max() ?? side
        // 걷기·뛰기 칸끼리의 땅. **가장 낮게 그려진 칸을 땅으로 삼는다** —
        // 절대 좌표로 쓰면 그림마다 여백이 달라 마스코트가 칸 밖으로 뜬다.
        let gaitFloor = cells.compactMap { cell -> Int? in
            guard cell.row < MascotSheet.rows, cell.column < MascotSheet.columns,
                  let sprite = MascotSheet.layout[cell.row][cell.column],
                  sprite.keepsLift, let ink = cell.ink else { return nil }
            return ink.y + ink.height - cell.sourceY
        }.max()

        for cell in cells {
            // **배치에 없는 자리는 버린다.** 도구가 남긴 표식이 여기서 함께 빠진다.
            guard cell.row < MascotSheet.rows, cell.column < MascotSheet.columns,
                  MascotSheet.layout[cell.row][cell.column] != nil else { continue }
            guard let ink = cell.ink, let piece = canvas.crop(ink) else { continue }
            let width = CGFloat(ink.width) * scale
            let height = CGFloat(ink.height) * scale
            let cellX = CGFloat(inset + cell.column * pitch)
            let cellTop = CGFloat(inset + cell.row * pitch)
            // 가로는 가운데, 세로는 바닥. **바닥을 맞춰야 발이 한 줄에 선다** —
            // 가운데로 맞추면 주저앉은 자세에서 발이 공중에 뜬다.
            let sprite = MascotSheet.layout[cell.row][cell.column]
            // 걷는 자세는 머리를 칸 한가운데에 두고, 나머지는 잉크 상자를 가운데에 둔다.
            let x: CGFloat
            if options.aligns, sprite?.anchor == .leading {
                x = cellX + (CGFloat(side) - CGFloat(standingWidest) * scale) / 2
            } else if options.aligns, sprite?.centersOnHead == true, let head = cell.headCenterX {
                let headInInk = CGFloat(head - ink.x) * scale
                x = cellX + CGFloat(side) / 2 - headInInk
            } else if options.aligns {
                x = cellX + (CGFloat(side) - width) / 2
            } else {
                x = cellX + CGFloat(ink.x - cell.sourceX) * scale
            }
            // **칸 밖으로 못 나가게 막는다.** 머리를 가운데로 끌어오면 몸이 긴 자세는
            // 꼬리 쪽이 옆 칸으로 밀려난다 — 뛰는 자세가 실제로 133px 새서, 걸을 때
            // 옆 칸 그림이 같이 보였다. 넘칠 때만 되밀어서 안쪽에 붙인다.
            let bounded = min(max(x, cellX), cellX + CGFloat(side) - width)
            // 붙는 모서리가 자세마다 다르다. 매달린 것은 위, 나머지는 아래.
            let anchor = sprite?.anchor ?? .bottom
            let bottomLine = cellTop + CGFloat(side - margin)
            // 걷기·뛰기는 그려진 대로 띄워 놓는다. 나머지는 바닥에 붙인다.
            let lift: CGFloat = {
                guard sprite?.keepsLift == true, let floor = gaitFloor else { return 0 }
                return CGFloat(floor - (ink.y + ink.height - cell.sourceY)) * scale
            }()
            let aligned = anchor == .top
                ? bottomLine - CGFloat(standingTop) * scale
                : bottomLine - height - lift
            let top = options.aligns
                ? aligned
                : cellTop + CGFloat(ink.y - cell.sourceY) * scale
            let boundedTop = min(max(top, cellTop), cellTop + CGFloat(side) - height)
            // CGContext 는 아래가 0이라 세로를 뒤집어 넣는다.
            context.draw(piece, in: CGRect(
                x: bounded, y: full.height - boundedTop - height, width: width, height: height
            ))
        }
        guard let made = context.makeImage() else { return nil }
        return NSBitmapImageRep(cgImage: made).representation(using: .png, properties: [:])
    }

    /// 원본에서 잘라낼 칸 하나.
    struct Cell {
        let row: Int
        let column: Int
        let sourceX: Int
        let sourceY: Int
        /// 그 칸 안에서 그림이 실제로 차지한 자리. 통째로 비었으면 nil.
        let ink: Box?
        /// 잉크 위쪽 1/3 의 가로 한가운데. 걷는 자세를 머리로 맞출 때 쓴다.
        let headCenterX: Int?
    }

    struct Box {
        var x: Int
        var y: Int
        var width: Int
        var height: Int
    }
}

/// 준비하는 동안 픽셀을 들고 있는 판. 한 번 풀어 놓고 계속 고친다.
private struct Canvas {
    let width: Int
    let height: Int
    var data: [UInt8]

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

    /// 픽셀을 지운다. **색까지 0으로 만든다.**
    ///
    /// 알파만 0으로 두면 색이 그대로 남는데, 미리 곱해진 알파(premultiplied)에서는
    /// 그게 잘못된 값이다. 나중에 크기를 조절할 때 보간이 그 색을 이웃으로 끌어와서
    /// **캐릭터 가장자리에 배경색 테가 생긴다** — 자홍 배경이면 분홍 테가 남는다.
    private mutating func erase(_ index: Int) {
        data[index * 4] = 0
        data[index * 4 + 1] = 0
        data[index * 4 + 2] = 0
        data[index * 4 + 3] = 0
    }
    private func opaque(_ x: Int, _ y: Int) -> Bool { data[offset(x, y) + 3] > 40 }

    /// 배경을 지우고 지운 픽셀 수를 돌려준다.
    ///
    /// **가장자리에서 번져 들어간다.** 색만 보고 지우면 회색으로 그린 죽음 칸까지
    /// 날아간다. 배경은 테두리에서 이어져 있고 캐릭터는 윤곽선으로 막혀 있어서,
    /// 번지는 방식이면 안쪽을 안 건드린다.
    mutating func clearBackground() -> Int {
        // **알파를 들고 온 그림은 색으로 지우지 않는다.**
        //
        // 색으로 지우는 길은 배경이 진짜 픽셀로 그려져 왔을 때만 쓸 것이다. 이미
        // 투명한 그림에까지 태우면 지울 것이 없는데도 가장자리에서 배경색을 추측하는데,
        // 거기 남아 있는 것은 배경이 아니라 **그림 둘레에 번진 옅은 그림자**다.
        // 그걸 배경으로 읽으면 그와 밝기가 겹치는 것이 통째로 날아간다 — 라쿤이
        // 그랬다. 그림자가 짙은 남색이라 "밝기 0~37 이 배경" 으로 잡혔고,
        // 같은 범위인 **윤곽선과 발이 지워졌다.**
        if isAlreadyTransparent() {
            let hazed = clearHaze()
            print("배경: 그림이 이미 투명하다 — 색으로 지우지 않는다")
            return hazed
        }
        guard let key = borderKey() else {
            print("가장자리가 한 가지 배경이 아니다 — 배경을 안 건드린다")
            return 0
        }
        print("배경: \(key.describe())")

        // **단색 배경은 번질 필요가 없다.** 캐릭터에 안 쓰는 색으로 받았으므로 그 색인
        // 픽셀은 어디에 있든 배경이다. 번지는 방식은 칸 경계선이 벽이 되어 막히는데,
        // 실제로 선을 그어 준 시트에서 26%밖에 못 지웠다.
        //
        // 무채색(흰 배경·체커무늬)은 다르다. 회색으로 그린 죽음 칸이 같은 색이라,
        // 색만 보고 지우면 그 칸이 통째로 날아간다 — 거기서는 번져야 한다.
        if case .solid = key {
            var wiped = 0
            for index in 0..<(width * height) where key.matches(self, index % width, index / width) {
                if data[index * 4 + 3] > 8 { wiped += 1 }
                erase(index)
            }
            // **선은 그다음이다.** 배경이 빠지고 나면 칸 경계선만 쭉 이어진 한 색 줄로
            // 남아서 알아보기 쉬워진다.
            let rules = clearRules(except: key)
            if rules > 0 { print("지운 칸 경계선: \(rules)픽셀") }
            return wiped + rules
        }

        // **선을 먼저 지운다.** 칸 경계선이 그림 테두리까지 둘러 있으면 번짐이 아예
        // 시작을 못 한다 — 가장자리 픽셀이 배경이 아니라 선이기 때문이다.
        let rules = clearRules(except: key)
        if rules > 0 { print("지운 칸 경계선: \(rules)픽셀") }

        var seen = [Bool](repeating: false, count: width * height)
        var queue: [Int] = []
        func push(_ x: Int, _ y: Int) {
            let index = y * width + x
            guard !seen[index], key.matches(self, x, y) else { return }
            seen[index] = true
            queue.append(index)
        }
        for x in 0..<width { push(x, 0); push(x, height - 1) }
        for y in 0..<height { push(0, y); push(width - 1, y) }

        var head = 0
        while head < queue.count {
            let index = queue[head]
            head += 1
            let x = index % width, y = index / width
            if x > 0 { push(x - 1, y) }
            if x < width - 1 { push(x + 1, y) }
            if y > 0 { push(x, y - 1) }
            if y < height - 1 { push(x, y + 1) }
        }
        for index in queue { erase(index) }

        // 가장자리에 남은 테. 지워진 칸에 닿아 있으면서 아직 배경 기미가 남은 것들이다.
        var fringe = 0
        for _ in 0..<2 {
            var marked: [Int] = []
            for y in 0..<height {
                for x in 0..<width {
                    let index = y * width + x
                    guard !seen[index], data[index * 4 + 3] > 8 else { continue }
                    guard key.matches(self, x, y, slack: 2.0) else { continue }
                    let touching = [(1, 0), (-1, 0), (0, 1), (0, -1)].contains { dx, dy in
                        let nx = x + dx, ny = y + dy
                        guard nx >= 0, nx < width, ny >= 0, ny < height else { return false }
                        return seen[ny * width + nx]
                    }
                    if touching { marked.append(index) }
                }
            }
            for index in marked { seen[index] = true; erase(index) }
            fringe += marked.count
        }
        return queue.count + fringe
    }

    /// 배경이 이미 빠져 있는 그림인지. 가장자리 띠가 거의 다 비어 있으면 그렇다.
    ///
    /// **칸 선이 그어져 있어도 상관없다.** 칸 자리는 규격으로 정해져 있어서
    /// (`cells(columns:rows:)` 가 균등 격자로 가르고 경계 안쪽만 훑는다) 선은 어차피
    /// 어느 칸에도 안 들어간다. 지울 이유가 없는 것을 지우려다 그림을 깎는 쪽이 나쁘다.
    private func isAlreadyTransparent() -> Bool {
        let band = max(1, min(8, min(width, height) / 8))
        var total = 0, empty = 0
        for x in stride(from: 0, to: width, by: 2) {
            for y in Array(0..<band) + Array((height - band)..<height) {
                total += 1
                if data[offset(x, y) + 3] <= 8 { empty += 1 }
            }
        }
        guard total > 0 else { return false }
        return empty * 10 >= total * 7
    }

    /// 그림에서 떨어져 나온 옅은 안개를 지운다. 지운 픽셀 수를 돌려준다.
    ///
    /// AI가 준 투명 배경에는 그림 둘레로 넓게 번진 반투명 그림자가 딸려 온다.
    /// 눈에는 뿌연 테로 보이고, 칸 잉크 상자를 부풀려 캐릭터를 작게 만든다.
    ///
    /// **가장자리 계단은 남긴다.** 반투명이라고 다 지우면 윤곽선이 톱니가 된다.
    /// 그림의 일부인 반투명은 **불투명한 픽셀에 붙어 있고**, 그림자는 떨어져 있다 —
    /// 둘을 가르는 것이 이 검사다.
    private mutating func clearHaze() -> Int {
        let reach = 2
        var doomed: [Int] = []
        for y in 0..<height {
            for x in 0..<width {
                let alpha = data[offset(x, y) + 3]
                guard alpha > 0, alpha < 250 else { continue }
                var touchesInk = false
                for dy in -reach...reach where !touchesInk {
                    for dx in -reach...reach {
                        let nx = x + dx, ny = y + dy
                        guard nx >= 0, ny >= 0, nx < width, ny < height else { continue }
                        if data[offset(nx, ny) + 3] >= 250 { touchesInk = true; break }
                    }
                }
                if !touchesInk { doomed.append(y * width + x) }
            }
        }
        for index in doomed { erase(index) }
        return doomed.count
    }

    /// 배경이 무엇인지 가장자리에서 알아낸다.
    private func borderKey() -> BackgroundKey? {
        // **테두리를 한 줄만 보면 안 된다.** 칸 경계선을 그려 준 시트는 바깥에도 선이
        // 둘러 있어서, 맨 바깥 줄만 재면 배경이 아니라 그 선 색을 배경으로 읽는다.
        // 띠로 훑으면 선은 몇 픽셀뿐이라 배경에 묻힌다.
        let band = max(1, min(8, min(width, height) / 8))
        var samples: [(Int, Int, Int)] = []
        for x in stride(from: 0, to: width, by: 2) {
            for y in Array(0..<band) + Array((height - band)..<height) {
                let index = offset(x, y)
                guard data[index + 3] > 40 else { continue }
                samples.append((Int(data[index]), Int(data[index + 1]), Int(data[index + 2])))
            }
        }
        guard samples.count > 20 else { return nil }
        let chromatic = samples.filter { max($0.0, $0.1, $0.2) - min($0.0, $0.1, $0.2) >= 40 }
        // 절반 넘게 색이 있으면 단색 배경이다. 그 색만 정확히 뺀다.
        if chromatic.count * 2 > samples.count {
            let red = chromatic.map(\.0).sorted()[chromatic.count / 2]
            let green = chromatic.map(\.1).sorted()[chromatic.count / 2]
            let blue = chromatic.map(\.2).sorted()[chromatic.count / 2]
            return .solid(red, green, blue)
        }
        // 무채색이면 흰 배경이거나 체커무늬다. 밝기 범위로 잡는다.
        let values = samples.map { ($0.0 + $0.1 + $0.2) / 3 }.sorted()
        return .gray(values[values.count / 20] - 14, values[values.count - 1 - values.count / 20] + 14)
    }

    /// 칸 경계에 그어 준 선을 지운다.
    ///
    /// **배경색으로 안 그어 준다.** 그리는 쪽은 칸을 눈으로 보려고 진한 색으로 긋는데,
    /// 그 선은 배경 빼기에 안 걸려서 남는다. 칸마다 잉크 상자가 칸 전체로 잡혀서
    /// 자리 맞추기가 아무 일도 못 하게 된다.
    ///
    /// 가로로 쭉(또는 세로로 쭉) 이어진 한 색 줄만 지운다. 캐릭터는 칸 사이가 떨어져
    /// 있어서 그림 폭을 가득 채울 수가 없다.
    mutating func clearRules(except key: BackgroundKey) -> Int {
        var marked: [Int] = []
        for y in 0..<height where spans(horizontal: true, at: y, except: key) {
            for x in 0..<width { marked.append(y * width + x) }
        }
        for x in 0..<width where spans(horizontal: false, at: x, except: key) {
            for y in 0..<height { marked.append(y * width + x) }
        }
        for index in marked { erase(index) }
        return marked.count
    }

    /// 그 줄이 처음부터 끝까지 한 색으로 꽉 찼는지.
    ///
    /// **배경색으로 찬 줄은 선이 아니다.** 칸 사이 빈틈이 배경색으로 쭉 이어져 있어서,
    /// 안 걸러내면 그 줄까지 선으로 보고 지운다.
    private func spans(horizontal: Bool, at line: Int, except key: BackgroundKey) -> Bool {
        let count = horizontal ? width : height
        guard count > 0 else { return false }
        // **조금 봐준다.** 선 가장자리가 배경과 섞여 있으면 몇 픽셀은 지워지거나 색이
        // 어긋난다. 끝까지 한 색이기를 요구하면 실제로 그어 준 선을 못 잡는다 —
        // 이 시트가 그랬다. 열에 아홉이 맞으면 선으로 본다.
        var matched = 0
        var low = [255, 255, 255], high = [0, 0, 0]
        for step in 0..<count {
            let index = horizontal ? offset(step, line) : offset(line, step)
            guard data[index + 3] > 120 else { continue }
            let x = horizontal ? step : line, y = horizontal ? line : step
            guard !key.matches(self, x, y) else { return false }
            matched += 1
            for channel in 0..<3 {
                let value = Int(data[index + channel])
                if value < low[channel] { low[channel] = value }
                if value > high[channel] { high[channel] = value }
            }
        }
        guard matched * 10 >= count * 9 else { return false }
        return (0..<3).allSatisfy { high[$0] - low[$0] <= 60 }
    }

    /// 작은 덩어리를 턴다. 워터마크 조각이 여기서 빠진다.
    mutating func clearSpecks(smallerThan limit: Int) -> Int {
        var seen = [Bool](repeating: false, count: width * height)
        var removed = 0
        for start in 0..<(width * height) where !seen[start] && data[start * 4 + 3] > 8 {
            var group = [start]
            seen[start] = true
            var head = 0
            while head < group.count {
                let index = group[head]
                head += 1
                let x = index % width, y = index / width
                for dx in -1...1 {
                    for dy in -1...1 where dx != 0 || dy != 0 {
                        let nx = x + dx, ny = y + dy
                        guard nx >= 0, nx < width, ny >= 0, ny < height else { continue }
                        let next = ny * width + nx
                        guard !seen[next], data[next * 4 + 3] > 8 else { continue }
                        seen[next] = true
                        group.append(next)
                    }
                }
            }
            guard group.count < limit else { continue }
            for index in group { erase(index) }
            removed += group.count
        }
        return removed
    }

    /// 균등 격자로 갈라, 칸마다 그림이 실제로 차지한 자리를 잰다.
    func cells(columns: Int, rows: Int, keep: Int) -> [SheetPrep.Cell] {
        let cellWidth = width / max(columns, 1)
        let cellHeight = height / max(rows, 1)
        var result: [SheetPrep.Cell] = []
        for row in 0..<min(keep, rows) {
            for column in 0..<columns {
                let originX = column * cellWidth, originY = row * cellHeight
                // **칸 안쪽만 훑는다.** 칸 경계에 그어 준 선이 여기서 통째로 빠진다 —
                // 선을 찾아 지우는 길도 해 봤는데, 발끝이 선에 닿아 있거나 선이 배경과
                // 섞여 있으면 놓친다. 격자 자리는 우리가 아니까 안 보면 그만이다.
                let inset = max(2, min(cellWidth, cellHeight) / 32)
                var minX = cellWidth, minY = cellHeight, maxX = -1, maxY = -1
                for y in inset..<(cellHeight - inset) {
                    for x in inset..<(cellWidth - inset) where opaque(originX + x, originY + y) {
                        if x < minX { minX = x }
                        if x > maxX { maxX = x }
                        if y < minY { minY = y }
                        if y > maxY { maxY = y }
                    }
                }
                let ink: SheetPrep.Box? = maxX < minX ? nil : SheetPrep.Box(
                    x: originX + minX, y: originY + minY,
                    width: maxX - minX + 1, height: maxY - minY + 1
                )
                // 머리(잉크 위쪽 1/3)의 가로 한가운데. 발이 옆으로 나가도 안 흔들린다.
                var head: Int?
                if let ink {
                    let limit = max(1, ink.height / 3)
                    var low = cellWidth, high = -1
                    for y in 0..<limit {
                        for x in 0..<cellWidth where opaque(originX + x, ink.y + y) {
                            if x < low { low = x }
                            if x > high { high = x }
                        }
                    }
                    if high >= low { head = originX + (low + high) / 2 }
                }
                result.append(SheetPrep.Cell(
                    row: row, column: column, sourceX: originX, sourceY: originY,
                    ink: ink, headCenterX: head
                ))
            }
        }
        return result
    }

    func crop(_ box: SheetPrep.Box) -> CGImage? {
        var buffer = data
        guard let context = CGContext(
            data: &buffer, width: width, height: height, bitsPerComponent: 8,
            bytesPerRow: width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ), let whole = context.makeImage() else { return nil }
        return whole.cropping(to: CGRect(x: box.x, y: box.y, width: box.width, height: box.height))
    }
}

/// 배경을 무엇으로 볼지.
private enum BackgroundKey {
    /// 단색. 그 색에서 얼마나 벗어나도 배경으로 칠지는 `slack` 이 정한다.
    case solid(Int, Int, Int)
    /// 무채색 밝기 범위. 흰 배경과 체커무늬가 여기 들어온다.
    case gray(Int, Int)

    /// 색상(0~359). 회색이면 nil — 계열을 따질 수 없다.
    static func hue(_ r: Int, _ g: Int, _ b: Int) -> Int? {
        let top = max(r, g, b), bottom = min(r, g, b)
        let delta = top - bottom
        // 너무 칙칙하거나 너무 어두우면 계열이 없다. 회색 부엉이가 여기서 걸러진다.
        guard delta > 40, top > 60 else { return nil }
        let scaled: Double
        switch top {
        case r: scaled = 60 * (Double(g - b) / Double(delta))
        case g: scaled = 60 * (2 + Double(b - r) / Double(delta))
        default: scaled = 60 * (4 + Double(r - g) / Double(delta))
        }
        return (Int(scaled.rounded()) % 360 + 360) % 360
    }

    func describe() -> String {
        switch self {
        case .solid(let r, let g, let b): return String(format: "단색 #%02X%02X%02X", r, g, b)
        case .gray(let low, let high): return "무채색 밝기 \(low)~\(high)"
        }
    }

    func matches(_ canvas: Canvas, _ x: Int, _ y: Int, slack: Double = 1) -> Bool {
        let index = (y * canvas.width + x) * 4
        guard canvas.data[index + 3] > 8 else { return true }
        let red = Int(canvas.data[index]), green = Int(canvas.data[index + 1])
        let blue = Int(canvas.data[index + 2])
        switch self {
        case .solid(let r, let g, let b):
            // **색 거리로 보지 않는다.** 같은 자홍이라도 칸마다 옅게 칠해 오면
            // 거리로는 안 걸린다 — 실제로 몇 칸만 분홍이 남았다.
            // 색상(어느 계열인가)이 같고 칙칙하지 않으면 배경으로 본다.
            // 파란 부엉이(205도)와 자홍(300도)은 색상이 멀어서 안 섞인다.
            guard let key = BackgroundKey.hue(r, g, b), let pixel = BackgroundKey.hue(red, green, blue)
            else {
                let room = Int(60 * slack)
                return abs(red - r) <= room && abs(green - g) <= room && abs(blue - b) <= room
            }
            let gap = min(abs(key - pixel), 360 - abs(key - pixel))
            return gap <= Int(26 * slack)
        case .gray(let low, let high):
            let spread = Int(24 * slack)
            guard max(red, green, blue) - min(red, green, blue) < spread else { return false }
            let value = (red + green + blue) / 3
            return value >= low - Int(6 * (slack - 1)) && value <= high + Int(6 * (slack - 1))
        }
    }
}
