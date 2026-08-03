import AppKit
import SwiftUI

/// Claude Code 마스코트 Clawd.
///
/// 그리드는 Claude Code가 터미널에 그리는 블록 아트를 그대로 옮긴 것이다.
/// 원본은 4행이고, 각 행의 `█`은 셀 전체 / `▄`는 셀 아래 절반만 칠한다.
/// 터미널 셀은 가로:세로가 1:2라서, 아래 절반만 칠한 칸의 윗절반이 눈이 된다.
/// 그래서 4행 × 11열 아트를 8행 × 11열 정사각 픽셀 그리드로 펼쳤다.
///
///     ` █████████ `      →  y0, y1
///     `██▄█████▄██`      →  y2(눈 두 칸), y3
///     ` █████████ `      →  y4, y5
///     `█ █   █ █`        →  y6, y7  (다리 4개)
struct ClawdMark: View {
    static let bodyColor = Color(red: 215 / 255, green: 119 / 255, blue: 87 / 255)

    static let rows = [
        ".#########.",
        ".#########.",
        "##.#####.##",
        "###########",
        ".#########.",
        ".#########.",
        ".#.#...#.#.",
        ".#.#...#.#.",
    ]

    /// 눈 위치(x, y). 몸통에 둘러싸인 빈 칸이라 명시적으로 어둡게 칠한다.
    static let eyes = [(2, 2), (8, 2)]

    static let columns = 11
    static let lines = 8

    static let defaultEyeColor = Color.black.opacity(0.88)

    var bodyColor: Color = ClawdMark.bodyColor
    var eyeColor: Color = ClawdMark.defaultEyeColor

    var body: some View {
        Canvas { context, size in
            let cellWidth = size.width / CGFloat(Self.columns)
            let cellHeight = size.height / CGFloat(Self.lines)

            // 경계를 정수로 반올림해서 픽셀 사이에 실틈이 생기지 않게 한다.
            func rect(x: Int, y: Int) -> CGRect {
                let left = (CGFloat(x) * cellWidth).rounded()
                let right = (CGFloat(x + 1) * cellWidth).rounded()
                let top = (CGFloat(y) * cellHeight).rounded()
                let bottom = (CGFloat(y + 1) * cellHeight).rounded()
                return CGRect(x: left, y: top, width: right - left, height: bottom - top)
            }

            for (y, row) in Self.rows.enumerated() {
                for (x, character) in row.enumerated() where character == "#" {
                    context.fill(Path(rect(x: x, y: y)), with: .color(bodyColor))
                }
            }
            for (x, y) in Self.eyes {
                context.fill(Path(rect(x: x, y: y)), with: .color(eyeColor))
            }
        }
        .aspectRatio(CGFloat(Self.columns) / CGFloat(Self.lines), contentMode: .fit)
    }
}

extension ClawdMark {
    /// 메뉴바용 비트맵.
    ///
    /// 눈은 칠하지 않고 구멍으로 남긴다. 그러면 밝은 메뉴바에서도 어두운 메뉴바에서도
    /// 배경이 그대로 비쳐서 눈처럼 읽힌다.
    /// 높이는 8의 배수로 주는 게 좋다(한 칸이 정수 픽셀이 되어 선명하다).
    static func statusItemImage(height: CGFloat) -> NSImage {
        let width = (height * CGFloat(columns) / CGFloat(lines)).rounded()
        let size = NSSize(width: width, height: height)
        let fill = NSColor(srgbRed: 215 / 255, green: 119 / 255, blue: 87 / 255, alpha: 1)

        let image = NSImage(size: size, flipped: false) { _ in
            guard let context = NSGraphicsContext.current?.cgContext else { return true }
            let cellWidth = width / CGFloat(columns)
            let cellHeight = height / CGFloat(lines)
            context.setFillColor(fill.cgColor)

            for (y, row) in rows.enumerated() {
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
        // 템플릿으로 두면 단색으로 칠해져 Clawd 색이 사라진다.
        image.isTemplate = false
        return image
    }
}
