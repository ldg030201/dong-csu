import AppKit
import ImageIO
import SwiftUI
import UniformTypeIdentifiers

extension NSImage {
    /// PNG로 저장한다. 렌더 통로 네 곳이 같은 사슬을 각자 쓰고 있었다.
    func writePNG(to path: String) -> Bool {
        guard
            let tiff = tiffRepresentation,
            let bitmap = NSBitmapImageRep(data: tiff),
            let png = bitmap.representation(using: .png, properties: [:])
        else { return false }
        return (try? png.write(to: URL(fileURLWithPath: path))) != nil
    }
}

@MainActor
extension ImageRenderer {
    /// 뷰를 그려 PNG로 저장한다.
    func writePNG(to path: String, scale: CGFloat) -> Bool {
        self.scale = scale
        return nsImage?.writePNG(to: path) ?? false
    }
}

/// `dong-mcu --render-owl out.png` — 부엉이 애니메이션을 한 장에 펼친다.
///
/// 기분마다 프레임이 가로로, 기분끼리는 세로로 늘어선다. 앱을 띄우고 몇 초씩
/// 기다리지 않고도 어느 프레임에서 형태가 깨지는지 한눈에 볼 수 있다.
@MainActor
enum OwlSheetRenderer {
    static func write(to path: String, cell: CGFloat) -> Bool {
        let content = VStack(alignment: .leading, spacing: cell * 0.18) {
            ForEach(Array(OwlAnimation.all.enumerated()), id: \.offset) { _, row in
                HStack(alignment: .top, spacing: cell * 0.12) {
                    Text(row.title)
                        .font(.system(size: cell * 0.2, weight: .semibold))
                        .foregroundStyle(.white)
                        .frame(width: cell * 0.9, alignment: .leading)
                        .padding(.top, cell * 0.35)

                    ForEach(Array(row.frames.enumerated()), id: \.offset) { _, frame in
                        VStack(spacing: cell * 0.06) {
                            OwlMarkView(pose: frame.pose, palette: row.palette)
                                .frame(height: cell)
                            Text(durationText(frame))
                                .font(.system(size: cell * 0.14, design: .rounded))
                                .monospacedDigit()
                                .foregroundStyle(.white.opacity(0.55))
                        }
                    }
                }
            }
        }
        .padding(cell * 0.4)
        .background(Color(white: 0.13))

        return ImageRenderer(content: content).writePNG(to: path, scale: 2)
    }

    /// 프레임이 얼마나 머무는지. 지터가 있으면 범위로 적는다.
    private static func durationText(_ frame: OwlFrame) -> String {
        guard frame.duration > 0 else { return "정지" }
        let start = String(format: "%.2f", frame.duration)
        guard frame.jitter > 0 else { return "\(start)s" }
        return "\(start)~\(String(format: "%.2f", frame.duration + frame.jitter))s"
    }
}

/// `dong-mcu --render-owl-gif <디렉터리>` — 기분마다 움직이는 GIF를 한 장씩 만든다.
///
/// 문서에 넣을 그림이라 프레임 시간이 실제와 어긋나면 안 된다. 손으로 만들지 않고
/// `OwlMood.frames`를 그대로 읽어서, 자세를 고치면 GIF도 같이 바뀌게 한다.
@MainActor
enum OwlGIFRenderer {
    /// 만들어진 파일 경로들. 하나라도 실패하면 nil.
    static func writeAll(to directory: String, cell: CGFloat) -> [String]? {
        let base = URL(fileURLWithPath: directory, isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        } catch {
            return nil
        }

        var written: [String] = []
        for animation in OwlAnimation.all {
            let url = base.appendingPathComponent("\(animation.name).gif")
            guard write(animation, to: url, cell: cell) else { return nil }
            written.append(url.path)
        }
        return written
    }

    private static func write(_ animation: OwlAnimation, to url: URL, cell: CGFloat) -> Bool {
        let frames = animation.frames
        guard let destination = CGImageDestinationCreateWithURL(
            url as CFURL,
            UTType.gif.identifier as CFString,
            frames.count,
            nil
        ) else { return false }

        // 0 = 무한 반복.
        CGImageDestinationSetProperties(destination, [
            kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0],
        ] as CFDictionary)

        for frame in frames {
            // 투명 배경으로 두면 프레임이 지워지는 방식에 따라 잔상이 남는다.
            // 배경을 칠해서 프레임마다 화면을 통째로 덮게 한다.
            let content = OwlMarkView(pose: frame.pose, palette: animation.palette)
                .frame(height: cell)
                .padding(cell * 0.22)
                .background(Color(white: 0.13))
            let renderer = ImageRenderer(content: content)
            renderer.scale = 2
            guard let image = renderer.cgImage else { return false }

            let delay = delaySeconds(for: frame)
            CGImageDestinationAddImage(destination, image, [
                kCGImagePropertyGIFDictionary: [
                    kCGImagePropertyGIFDelayTime: delay,
                    kCGImagePropertyGIFUnclampedDelayTime: delay,
                ],
            ] as CFDictionary)
        }

        return CGImageDestinationFinalize(destination)
    }

    /// 지터가 있는 프레임은 한가운데 값으로 잡는다. GIF는 매번 다르게 기다릴 수 없어서
    /// 실제 앱이 평균적으로 머무는 만큼을 보여주는 게 가장 덜 어긋난다.
    private static func delaySeconds(for frame: OwlFrame) -> Double {
        // 프레임이 하나뿐인 정지 기분은 시간이 0이다. GIF에 0을 넣으면
        // 뷰어가 제멋대로 100ms로 바꿔 잡으므로 눈에 띄는 값을 준다.
        guard frame.duration > 0 else { return 1 }
        return frame.duration + frame.jitter / 2
    }
}

/// `dong-mcu --render out.png` — HUD를 고정값으로 그려 PNG로 저장한다.
/// 앱을 띄우지 않고 레이아웃·색·아이콘을 확인하려고 둔 디버그 통로.
@MainActor
enum HUDPreviewRenderer {
    /// 미리보기에서 재현할 상태.
    enum State: String {
        case ok
        /// 갱신에 실패해 마지막 성공값을 보여주는 중.
        case stale
        /// 토큰 만료 등으로 재로그인이 필요한 상태.
        case reauth
    }

    static func write(
        to path: String,
        utilization: (session: Double, weekly: Double),
        iconStyle: ClaudeIconStyle,
        state: State,
        mode: HUDMode = .expanded,
        isHovered: Bool = false,
        isDark: Bool = true,
        side: HUDExpandSide = .right,
        opacity: Double = 0.92,
        showsStats: Bool = false,
        scale: HUDScale = .normal,
        showsUpdateBadge: Bool = false,
        versionBadge: String? = nil,
        versionBadgeIsTest: Bool = false
    ) -> Bool {
        let snapshot = UsageSnapshot(
            planName: "Max",
            fiveHour: UsageWindow(
                utilization: utilization.session,
                resetsAt: Date().addingTimeInterval(3 * 3600 + 12 * 60)
            ),
            sevenDay: UsageWindow(
                utilization: utilization.weekly,
                resetsAt: Date().addingTimeInterval(26 * 3600)
            ),
            fetchedAt: Date().addingTimeInterval(state == .ok ? 0 : -13 * 3600)
        )

        // 실제 창과 같은 배경(반투명 단색)을 쓰고, 그 뒤에 회색 바탕을 깔아
        // 데스크톱 위에 얹힌 상태를 흉내낸다.
        let store = UsageStore(
            preview: snapshot,
            nextPoll: Date().addingTimeInterval(7 * 60 + 12),
            error: state == .ok ? nil : "토큰 만료 — Claude Code 재로그인 필요",
            needsReauth: state == .reauth
        )
        // 실제 앱과 같은 판정을 태워서, 넘긴 사용률·상태에 맞는 기분으로 그린다.
        // 멈춘 애니메이터는 그 기분의 첫 프레임에 머물러 있어 정지 그림이 된다.
        let animator = OwlAnimator()
        animator.setMood(OwlMood.resolve(store: store, isDragging: false))
        // 테스트판 모습을 그릴 때는 마스코트 색도 함께 바꾼다. 실제 테스트 번들에서는
        // `AppInfo.owlPalette`가 같은 색을 주므로 미리보기가 어긋나지 않는다.
        if versionBadgeIsTest {
            animator.paletteOverride = .tinted(body: AppInfo.testBuildTint)
        }

        let palette = HUDPalette(isDark: isDark)
        let content = UsageHUDView(
            store: store,
            iconStyle: iconStyle,
            mode: mode,
            isHovered: isHovered,
            palette: palette,
            expandSide: side,
            usageMonitor: showsStats ? { let m = ProcessUsageMonitor(); m.start(); return m }() : nil,
            scale: scale.factor,
            showsUpdateBadge: showsUpdateBadge,
            versionBadge: versionBadge,
            versionBadgeIsTest: versionBadgeIsTest,
            owlAnimator: animator
        )
            .background {
                ZStack {
                    Color(white: isDark ? 0.42 : 0.55)
                    // 펫은 카드 배경이 없다. 바탕만 깔아서 데스크톱 위에 얹힌 모습을 흉내낸다.
                    if mode.showsBackdrop {
                        Color(nsColor: palette.backdrop(opacity: opacity))
                    }
                }
            }
            .clipShape(
                RoundedRectangle(
                    cornerRadius: UsageHUDView.cornerRadius(mode: mode, scale: scale.factor),
                    style: .continuous
                )
            )

        return ImageRenderer(content: content).writePNG(to: path, scale: 3)
    }

    /// 설정 창을 PNG로 렌더한다. 탭마다 화면이 달라서 어느 탭을 그릴지 받는다.
    /// `update`를 주면 그 버전이 나와 있는 것처럼 그린다(버전 탭 확인용).
    static func writeSettings(
        to path: String,
        isDark: Bool,
        tab: SettingsTab = .status,
        update: String? = nil
    ) -> Bool {
        let snapshot = UsageSnapshot(
            planName: "Max",
            fiveHour: UsageWindow(utilization: 34, resetsAt: Date().addingTimeInterval(3 * 3600)),
            sevenDay: UsageWindow(utilization: 61, resetsAt: Date().addingTimeInterval(26 * 3600)),
            fetchedAt: Date()
        )
        let view = SettingsView(
            settings: HUDSettings(defaults: UserDefaults(suiteName: "dong-mcu.preview") ?? .standard),
            // 상태 탭이 조회 카운트다운을 그리므로 예정 시각까지 넣어야 실제와 같아진다.
            store: UsageStore(preview: snapshot, nextPoll: Date().addingTimeInterval(7 * 60 + 12)),
            updates: UpdateChecker(
                preview: update,
                lastCheckedAt: Date().addingTimeInterval(-40 * 60)
            ),
            actions: SettingsActions(refresh: {}, resetPosition: {}, login: {}, quit: {}),
            version: AppInfo.displayVersion,
            initialTab: tab,
            isPreviewRender: true
        )
        .content
        .preferredColorScheme(isDark ? .dark : .light)
        .background(Color(nsColor: .windowBackgroundColor))

        return ImageRenderer(content: view).writePNG(to: path, scale: 2)
    }
}
