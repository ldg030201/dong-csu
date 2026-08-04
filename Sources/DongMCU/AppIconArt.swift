import AppKit
import SwiftUI

/// 앱 아이콘 그림. `dong-mcu --render-icon` 으로 PNG를 뽑아 .icns를 만든다.
///
/// macOS 아이콘 규격대로 캔버스의 82.4%만 본체로 쓰고 나머지는 여백으로 둔다.
/// 여백이 없으면 Dock·Finder에서 다른 앱보다 커 보인다.
///
/// 링을 두른 안도 만들어 봤지만 16·32px에서 가운데 부엉이가 뭉개져서 버렸다.
/// 작은 크기에서 알아볼 수 있는 쪽이 아이콘으로는 낫다.
struct AppIconArt: View {
    /// 캔버스 한 변.
    let side: CGFloat

    private var plateSide: CGFloat { side * 0.824 }

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: plateSide * 0.2237, style: .continuous)
                .fill(
                    LinearGradient(
                        colors: [
                            Color(red: 0.17, green: 0.20, blue: 0.27),
                            Color(red: 0.07, green: 0.08, blue: 0.11),
                        ],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )
                .frame(width: plateSide, height: plateSide)

            OwlMarkView().frame(height: plateSide * 0.72)
        }
        .frame(width: side, height: side)
    }
}

@MainActor
enum AppIconRenderer {
    /// 아이콘 한 장을 PNG로 저장한다.
    static func write(to path: String, side: CGFloat) -> Bool {
        let renderer = ImageRenderer(content: AppIconArt(side: side))
        // 픽셀 크기를 side와 같게 맞춘다. 확대·축소는 여기서 끝내고 iconutil에 맡기지 않는다.
        renderer.scale = 1

        guard let image = renderer.nsImage,
              let tiff = image.tiffRepresentation,
              let bitmap = NSBitmapImageRep(data: tiff),
              let png = bitmap.representation(using: .png, properties: [:])
        else { return false }
        return (try? png.write(to: URL(fileURLWithPath: path))) != nil
    }
}
