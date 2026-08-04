import AppKit
import SwiftUI

/// dong-mcu 마스코트 부엉이.
///
/// 그리드는 15열 × 13행이다. 몸통은 가운데 11열(x2...x12)만 쓰고,
/// 좌우 2열은 **날개를 펼 여백**이라 정지 상태에서는 비어 있다.
/// 그래서 마크는 세로를 기준으로 크기를 맞춰야 한다(`OwlMark.aspectRatio`).
///
/// 파츠를 문자열 그리드로 나눠 두고 겹쳐 그린다. 눈만 갈아끼우면 깜빡이고,
/// 날개 레이어만 바꾸면 펴진다. 애니메이션은 `OwlPose`로 조합한다.
enum OwlMark {
    static let columns = 15
    static let lines = 13
    /// 좌우 여백이 있어 가로가 더 길다. 링 안에 넣을 때는 높이를 기준으로 맞춘다.
    static let aspectRatio = CGFloat(columns) / CGFloat(lines)

    // MARK: - 파츠
    // '#' 몸통  'd' 날개  'l' 배 무늬  'w' 눈 흰자  'k' 눈동자·눈꺼풀  'y' 부리·발

    static let body = [
        "...##.....##...",
        "...###...###...",
        "..###########..",
        "..###########..",
        "..###########..",
        "..###########..",
        "..###########..",
        "..###########..",
        "..###########..",
        "..###########..",
        "...#########...",
        "....#######....",
        "...............",
    ]

    static let wingsFolded = [
        "...............",
        "...............",
        "...............",
        "..dd.......dd..",
        "..dd.......dd..",
        "..dd.......dd..",
        "..dd.......dd..",
        "..dd.......dd..",
        "..dd.......dd..",
        "..dd.......dd..",
        "...d.......d...",
        "...............",
        "...............",
    ]

    /// 펼친 날개는 좌우 여백까지 써서 어깨 위로 뻗는다.
    static let wingsSpread = [
        "dd...........dd",
        "dd...........dd",
        ".dd.........dd.",
        "..dd.......dd..",
        "..dd.......dd..",
        "..dd.......dd..",
        "..dd.......dd..",
        "..dd.......dd..",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
    ]

    static let belly = [
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        ".....l.l.l.....",
        "......l.l......",
        "...............",
        "...............",
        "...............",
    ]

    /// 깜빡임 3단계. 위에서부터 눈꺼풀이 내려온다.
    static let eyesOpen = [
        "...............",
        "...............",
        "...............",
        "....www.www....",
        "....wkw.wkw....",
        "....www.www....",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
    ]

    static let eyesHalf = [
        "...............",
        "...............",
        "...............",
        "....kkk.kkk....",
        "....wkw.wkw....",
        "....www.www....",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
    ]

    static let eyesClosed = [
        "...............",
        "...............",
        "...............",
        "....kkk.kkk....",
        "....kkk.kkk....",
        "....www.www....",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
    ]

    static let beak = [
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "......yyy......",
        ".......y.......",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
    ]

    static let feetStand = [
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "....###.###....",
    ]

    static let feetStepA = [
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...###..###....",
    ]

    static let feetStepB = [
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "...............",
        "....###..###...",
    ]

    // MARK: - 색

    /// 날개는 몸통보다 어둡되, 다크 배경 위에서도 실루엣이 남을 만큼은 밝아야 한다.
    static let bodyColor = Color(red: 0x3A / 255, green: 0x72 / 255, blue: 0xC4 / 255)
    static let wingColor = Color(red: 0x27 / 255, green: 0x54 / 255, blue: 0x8F / 255)
    static let bellyColor = Color(red: 0x9F / 255, green: 0xC4 / 255, blue: 0xEE / 255)
    static let faceColor = Color(red: 0xFF / 255, green: 0xF3 / 255, blue: 0xE0 / 255)
    static let pupilColor = Color(red: 0x0E / 255, green: 0x1B / 255, blue: 0x2E / 255)
    static let beakColor = Color(red: 0xF6 / 255, green: 0xA6 / 255, blue: 0x23 / 255)

    static func color(for character: Character) -> Color? {
        switch character {
        case "#": return bodyColor
        case "d": return wingColor
        case "l": return bellyColor
        case "w": return faceColor
        case "k": return pupilColor
        case "y": return beakColor
        default: return nil
        }
    }

    /// 레이어를 좌우로 민다. 캔버스 밖으로 나간 칸은 버린다.
    static func shifted(_ layer: [String], by dx: Int) -> [String] {
        guard dx != 0 else { return layer }
        return layer.map { row in
            let characters = Array(row)
            var output = [Character](repeating: ".", count: characters.count)
            for (index, character) in characters.enumerated() where character != "." {
                let moved = index + dx
                if moved >= 0 && moved < characters.count { output[moved] = character }
            }
            return String(output)
        }
    }
}

extension OwlMark {
    /// 메뉴바용 그리드. 높이가 16px뿐이라 13행짜리 본체를 그대로 줄이면 한 칸이 1.2px가 되어
    /// 눈도 부리도 뭉갠다. 그래서 8행 × 9열로 다시 그린 별도 그리드를 쓴다.
    ///
    /// 눈과 부리는 칠하지 않고 구멍으로 남긴다. 밝은 메뉴바에서도 어두운 메뉴바에서도
    /// 배경이 그대로 비쳐서 이목구비로 읽힌다.
    static let statusRows = [
        "##.....##",
        "#########",
        "#..###..#",
        "#..###..#",
        "####.####",
        "#########",
        "#########",
        "##.###.##",
    ]

    /// 메뉴바용 비트맵. 높이는 8의 배수로 주는 게 좋다(한 칸이 정수 픽셀이 되어 선명하다).
    ///
    /// `fill`이 nil이면 템플릿 이미지로 둔다. 그러면 시스템이 메뉴바 밝기에 맞춰
    /// 알아서 칠해 준다. 부엉이 몸색(진청)은 어두운 메뉴바에서 묻혀서 그대로 못 쓴다.
    static func statusItemImage(height: CGFloat, fill: NSColor? = nil) -> NSImage {
        let columns = statusRows[0].count
        let lines = statusRows.count
        let width = (height * CGFloat(columns) / CGFloat(lines)).rounded()
        let color = fill ?? .black

        let image = NSImage(size: NSSize(width: width, height: height), flipped: false) { _ in
            guard let context = NSGraphicsContext.current?.cgContext else { return true }
            let cellWidth = width / CGFloat(columns)
            let cellHeight = height / CGFloat(lines)
            context.setFillColor(color.cgColor)

            for (y, row) in statusRows.enumerated() {
                // NSImage 좌표계는 아래가 0이므로 행 순서를 뒤집는다.
                let flipped = lines - 1 - y
                for (x, character) in row.enumerated() where character == "#" {
                    let left = (CGFloat(x) * cellWidth).rounded()
                    let right = (CGFloat(x + 1) * cellWidth).rounded()
                    let bottom = (CGFloat(flipped) * cellHeight).rounded()
                    let top = (CGFloat(flipped + 1) * cellHeight).rounded()
                    context.fill(CGRect(x: left, y: bottom, width: right - left, height: top - bottom))
                }
            }
            return true
        }
        image.isTemplate = fill == nil
        return image
    }
}

/// 부엉이의 한 자세. 레이어 조합만 바꿔 애니메이션 프레임을 만든다.
struct OwlPose: Equatable {
    enum Eyes { case open, half, closed }
    enum Wings { case folded, spread }
    enum Feet { case stand, stepA, stepB }

    var eyes: Eyes = .open
    var wings: Wings = .folded
    var feet: Feet = .stand
    /// 몸통만 좌우로 기울인다(발은 제자리). 걸을 때 뒤뚱거리게 하는 값.
    var lean: Int = 0

    static let idle = OwlPose()

    /// 아래에서 위로 겹쳐 그릴 레이어들.
    var layers: [[String]] {
        let eyeLayer: [String] = {
            switch eyes {
            case .open: return OwlMark.eyesOpen
            case .half: return OwlMark.eyesHalf
            case .closed: return OwlMark.eyesClosed
            }
        }()
        let wingLayer = wings == .folded ? OwlMark.wingsFolded : OwlMark.wingsSpread
        let feetLayer: [String] = {
            switch feet {
            case .stand: return OwlMark.feetStand
            case .stepA: return OwlMark.feetStepA
            case .stepB: return OwlMark.feetStepB
            }
        }()

        // 발은 땅에 붙어 있어야 해서 기울임에서 뺀다.
        let leaning = [OwlMark.body, wingLayer, OwlMark.belly, eyeLayer, OwlMark.beak]
            .map { OwlMark.shifted($0, by: lean) }
        return leaning + [feetLayer]
    }
}

struct OwlMarkView: View {
    var pose: OwlPose = .idle

    var body: some View {
        Canvas { context, size in
            // 한 칸이 정수 크기가 아니면 어떤 행은 2px, 어떤 행은 3px로 그려져
            // 크기마다 형태가 달라진다. 칸 크기를 내림해서 어디서나 같은 그림이 되게 한다.
            // 남는 여백은 가운데로 몬다.
            let cell = max(
                1,
                floor(min(
                    size.width / CGFloat(OwlMark.columns),
                    size.height / CGFloat(OwlMark.lines)
                ))
            )
            let originX = ((size.width - cell * CGFloat(OwlMark.columns)) / 2).rounded()
            let originY = ((size.height - cell * CGFloat(OwlMark.lines)) / 2).rounded()

            func rect(x: Int, y: Int) -> CGRect {
                CGRect(
                    x: originX + CGFloat(x) * cell,
                    y: originY + CGFloat(y) * cell,
                    width: cell,
                    height: cell
                )
            }

            for layer in pose.layers {
                for (y, row) in layer.enumerated() {
                    for (x, character) in row.enumerated() {
                        guard let fill = OwlMark.color(for: character) else { continue }
                        context.fill(Path(rect(x: x, y: y)), with: .color(fill))
                    }
                }
            }
        }
        .aspectRatio(OwlMark.aspectRatio, contentMode: .fit)
    }
}
