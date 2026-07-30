import SwiftUI

/// Claude 마크(방사형 별표)를 벡터로 그린 도형.
/// 공식 브랜드 애셋이 아니라 형태만 맞춘 근사 버전이다.
/// 실제 로고 파일을 쓰고 싶으면 이 도형 대신 Image로 갈아끼우면 된다.
struct ClaudeMark: Shape {
    var spokeCount: Int = 12

    func path(in rect: CGRect) -> Path {
        let outer = min(rect.width, rect.height) / 2
        let center = CGPoint(x: rect.midX, y: rect.midY)
        let baseHalfWidth = outer * 0.30
        let tipHalfWidth = outer * 0.075

        // 위를 향한 스포크 하나를 만들고, 회전 복사해서 방사형으로 배치한다.
        // 밑단은 두껍게 겹쳐서 가운데가 메워지고, 끝은 살짝 둥근 쐐기 모양이 된다.
        var spoke = Path()
        spoke.move(to: CGPoint(x: -baseHalfWidth, y: 0))
        spoke.addQuadCurve(
            to: CGPoint(x: -tipHalfWidth, y: -outer * 0.94),
            control: CGPoint(x: -baseHalfWidth * 0.80, y: -outer * 0.55)
        )
        spoke.addQuadCurve(
            to: CGPoint(x: tipHalfWidth, y: -outer * 0.94),
            control: CGPoint(x: 0, y: -outer)
        )
        spoke.addQuadCurve(
            to: CGPoint(x: baseHalfWidth, y: 0),
            control: CGPoint(x: baseHalfWidth * 0.80, y: -outer * 0.55)
        )
        spoke.closeSubpath()

        var path = Path()
        for index in 0..<spokeCount {
            let angle = Double(index) * (2 * .pi / Double(spokeCount))
            let transform = CGAffineTransform(translationX: center.x, y: center.y)
                .rotated(by: angle)
            path.addPath(spoke, transform: transform)
        }
        return path
    }
}
