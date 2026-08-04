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

    /// 지쳤을 때 축 늘어뜨린 날개. 접은 날개를 한 칸 내린 것이다.
    /// 리터럴로 한 벌 더 적어 두면 접은 날개를 손볼 때 이쪽만 옛 모양으로 남는다.
    static let wingsDroop = shifted(wingsFolded, dy: 1)


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

    /// 매달렸을 때 모아 늘어뜨린 다리.
    /// 서 있을 때처럼 벌리고 있으면 들려 있어도 딛고 선 것처럼 보인다.
    static let feetDangle = [
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
        ".....##.##.....",
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

    /// 레이어를 밀어서 옮긴다. `dy`는 양수가 아래쪽이고, 캔버스 밖으로 나간 칸은 버린다.
    static func shifted(_ layer: [String], dx: Int = 0, dy: Int = 0) -> [String] {
        guard dx != 0 || dy != 0 else { return layer }

        let empty = [Character](repeating: ".", count: columns)
        var output = [[Character]](repeating: empty, count: lines)
        for (y, row) in layer.enumerated() {
            let movedY = y + dy
            guard movedY >= 0, movedY < lines else { continue }
            for (x, character) in row.enumerated() where character != "." {
                let movedX = x + dx
                guard movedX >= 0, movedX < columns else { continue }
                output[movedY][movedX] = character
            }
        }
        return output.map { String($0) }
    }
}

// MARK: - 색

/// 부엉이 한 마리의 색 한 벌.
///
/// 색을 상수로 박아 두면 회색으로 물러앉히거나 테스트판을 구분할 방법이 없다.
/// 그림(그리드)과 색을 갈라 두어서, 같은 자세를 팔레트만 바꿔 다시 그린다.
struct OwlPalette: Equatable {
    var body: Color
    var wing: Color
    var belly: Color
    var face: Color
    var pupil: Color
    var beak: Color

    /// 날개는 몸통보다 어둡되, 다크 배경 위에서도 실루엣이 남을 만큼은 밝아야 한다.
    static let normal = OwlPalette(
        body: Color(red: 0x3A / 255, green: 0x72 / 255, blue: 0xC4 / 255),
        wing: Color(red: 0x27 / 255, green: 0x54 / 255, blue: 0x8F / 255),
        belly: Color(red: 0x9F / 255, green: 0xC4 / 255, blue: 0xEE / 255),
        face: Color(red: 0xFF / 255, green: 0xF3 / 255, blue: 0xE0 / 255),
        pupil: Color(red: 0x0E / 255, green: 0x1B / 255, blue: 0x2E / 255),
        beak: Color(red: 0xF6 / 255, green: 0xA6 / 255, blue: 0x23 / 255)
    )

    /// 조회가 안 되는 동안. 채도를 빼서 화면 뒤로 물러나 보이게 한다.
    /// 얼굴과 부리는 형태가 남을 만큼의 밝기 차이를 유지한다 — 아예 한 덩어리로
    /// 뭉개면 무슨 그림인지 알 수 없어서, 앱이 고장난 것처럼 읽힌다.
    static let offline = OwlPalette(
        body: Color(white: 0.40),
        wing: Color(white: 0.26),
        belly: Color(white: 0.60),
        face: Color(white: 0.84),
        pupil: Color(white: 0.12),
        beak: Color(white: 0.68)
    )

    /// 몸통과 날개만 다른 색으로. 눈·부리는 그대로 두어 얼굴이 남는다.
    /// 단색으로 칠해 버리면 눈까지 사라져서 무슨 그림인지 알 수 없다.
    static func tinted(body tint: NSColor) -> OwlPalette {
        var palette = normal
        palette.body = Color(nsColor: tint)
        palette.wing = Color(nsColor: tint.shadow(withLevel: 0.3) ?? tint)
        return palette
    }

    func color(for character: Character) -> Color? {
        switch character {
        case "#": return body
        case "d": return wing
        case "l": return belly
        case "w": return face
        case "k": return pupil
        case "y": return beak
        default: return nil
        }
    }

    /// AppKit으로 그릴 때(메뉴바 등) 쓰는 같은 색.
    func nsColor(for character: Character) -> NSColor? {
        color(for: character).map(NSColor.init)
    }
}

extension OwlMark {
    /// 메뉴바용 비트맵.
    ///
    /// 앱 아이콘·HUD와 같은 그림이어야 하므로 본체 그리드를 그대로 쓴다. 한때는
    /// 메뉴바용으로 8행짜리 그리드를 따로 뒀는데, 그러면 같은 마스코트인데도
    /// 자리마다 다른 얼굴이 되어서 버렸다.
    ///
    /// 메뉴바 높이(16pt)에 13행을 넣으면 한 칸이 1pt다. 레티나에서는 2px이라
    /// 눈과 부리까지 살아남는다. 한 칸을 정수로 맞춰야 형태가 유지되므로
    /// 실제 그림 높이는 요청한 높이보다 작을 수 있다.
    ///
    /// 테스트판을 색으로 구분할 때는 `palette`에 `OwlPalette.tinted(body:)`를 넘긴다.
    static func statusItemImage(height: CGFloat, palette: OwlPalette = .normal) -> NSImage {
        let cell = max(1, floor(height / CGFloat(lines)))
        let size = NSSize(width: cell * CGFloat(columns), height: cell * CGFloat(lines))

        let image = NSImage(size: size, flipped: false) { _ in
            guard let context = NSGraphicsContext.current?.cgContext else { return true }

            for layer in OwlPose.idle.layers {
                for (y, row) in layer.enumerated() {
                    // NSImage 좌표계는 아래가 0이므로 행 순서를 뒤집는다.
                    let flipped = lines - 1 - y
                    for (x, character) in row.enumerated() {
                        guard let fill = palette.nsColor(for: character) else { continue }
                        context.setFillColor(fill.cgColor)
                        context.fill(CGRect(
                            x: CGFloat(x) * cell,
                            y: CGFloat(flipped) * cell,
                            width: cell,
                            height: cell
                        ))
                    }
                }
            }
            return true
        }
        // 템플릿으로 두면 단색으로 칠해져 부엉이 색이 사라진다.
        image.isTemplate = false
        return image
    }
}

/// 부엉이의 한 자세. 레이어 조합만 바꿔 애니메이션 프레임을 만든다.
struct OwlPose: Equatable {
    enum Eyes { case open, half, closed }
    enum Wings { case folded, spread, droop }
    enum Feet { case stand, stepA, stepB, dangle }

    var eyes: Eyes = .open
    var wings: Wings = .folded
    var feet: Feet = .stand
    /// 몸을 통째로 좌우로 기울인다(발은 제자리). 걸을 때 뒤뚱거리게 하는 값.
    var lean: Int = 0
    /// 몸을 통째로 위아래로 움직인다(발은 제자리). 양수면 발 위로 주저앉는다.
    /// 숨을 쉬는 것처럼 보이게 하거나 지쳐서 내려앉을 때 쓴다.
    var bob: Int = 0
    /// 눈과 부리만 좌우로 **더** 민다. `lean`에 더해진다.
    ///
    /// 몸이 기운 만큼 반대로 주면 얼굴이 공간에 붙박인 채 머리 윤곽만 흔들린다.
    /// **부엉이는 몸이 어떻게 흔들리든 시선을 한 곳에 붙잡아 둔다.** 머리 윤곽까지
    /// 따로 떼어 봤지만, 폭이 같은 덩어리가 한 칸 어긋나면 고개를 든 게 아니라
    /// 그림이 깨진 것처럼 보였다. 얼굴만 움직이는 쪽이 고개를 돌린 것으로 읽힌다.
    var faceLean: Int = 0
    /// 다리의 좌우 자리. 발은 `lean`을 받지 않으므로 이 값이 그대로 위치다.
    /// 몸보다 한 박자 늦게 따라가게 두면 매달려 흔들리는 것처럼 보인다.
    var feetLean: Int = 0

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
        let wingLayer: [String] = {
            switch wings {
            case .folded: return OwlMark.wingsFolded
            case .spread: return OwlMark.wingsSpread
            case .droop: return OwlMark.wingsDroop
            }
        }()
        let feetLayer: [String] = {
            switch feet {
            case .stand: return OwlMark.feetStand
            case .stepA: return OwlMark.feetStepA
            case .stepB: return OwlMark.feetStepB
            case .dangle: return OwlMark.feetDangle
            }
        }()

        // 발은 땅(또는 허공)에 매달린 채라 기울임·오르내림에서 빼고 제 값만 쓴다.
        let faceShift = lean + faceLean
        return [
            OwlMark.shifted(OwlMark.body, dx: lean, dy: bob),
            OwlMark.shifted(wingLayer, dx: lean, dy: bob),
            OwlMark.shifted(OwlMark.belly, dx: lean, dy: bob),
            OwlMark.shifted(eyeLayer, dx: faceShift, dy: bob),
            OwlMark.shifted(OwlMark.beak, dx: faceShift, dy: bob),
            OwlMark.shifted(feetLayer, dx: feetLean),
        ]
    }

    /// 몸이 기울어도 시선은 한 곳에 남기고, 다리는 따로 흔든다.
    ///
    /// `faceLean`에 매번 손으로 반대 부호를 적으면, 한 프레임만 놓쳐도 거기서만
    /// 얼굴이 같이 흔들려서 부엉이가 아니라 인형처럼 보인다.
    func swaying(lean: Int = 0, feetLean: Int = 0) -> OwlPose {
        var pose = self
        pose.lean = lean
        pose.faceLean = -lean
        pose.feetLean = feetLean
        return pose
    }
}

struct OwlMarkView: View {
    var pose: OwlPose = .idle
    var palette: OwlPalette = .normal

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
                        guard let fill = palette.color(for: character) else { continue }
                        context.fill(Path(rect(x: x, y: y)), with: .color(fill))
                    }
                }
            }
        }
        .aspectRatio(OwlMark.aspectRatio, contentMode: .fit)
    }
}
