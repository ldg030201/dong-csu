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

    private static let rows = [
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
    private static let eyes = [(2, 2), (8, 2)]

    private static let columns = 11
    private static let lines = 8

    var bodyColor: Color = ClawdMark.bodyColor
    var eyeColor: Color = Color.black.opacity(0.88)

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
