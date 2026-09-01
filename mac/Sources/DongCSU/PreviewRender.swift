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

/// `dong-csu --render-owl out.png` — 부엉이 애니메이션을 한 장에 펼친다.
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

/// `dong-csu --render-owl-gif <디렉터리>` — 기분마다 움직이는 GIF를 한 장씩 만든다.
///
/// 문서에 넣을 그림이라 프레임 시간이 실제와 어긋나면 안 된다. 손으로 만들지 않고
/// `OwlMood.frames`를 그대로 읽어서, 자세를 고치면 GIF도 같이 바뀌게 한다.
@MainActor
enum OwlGIFRenderer {
    /// 만들어진 파일 경로들. 하나라도 실패하면 nil.
    static func writeAll(to directory: String, cell: CGFloat, sheet: MascotSpriteSet? = nil) -> [String]? {
        let base = URL(fileURLWithPath: directory, isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        } catch {
            return nil
        }

        var written: [String] = []
        for animation in OwlAnimation.all {
            let url = base.appendingPathComponent("\(animation.name).gif")
            guard write(animation, to: url, cell: cell, sheet: sheet) else { return nil }
            written.append(url.path)
        }
        return written
    }

    private static func write(
        _ animation: OwlAnimation, to url: URL, cell: CGFloat, sheet: MascotSpriteSet? = nil
    ) -> Bool {
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

        for (beat, frame) in frames.enumerated() {
            // 투명 배경으로 두면 프레임이 지워지는 방식에 따라 잔상이 남는다.
            // 배경을 칠해서 프레임마다 화면을 통째로 덮게 한다.
            let body = ZStack {
                if let sheet {
                    // **앱과 같은 판단으로 칸을 고른다.** 여기서 따로 고르면 문서의
                    // 부엉이가 화면의 부엉이와 다른 자세를 한다.
                    MascotSpriteView(
                        set: sheet,
                        sprite: MascotSprite.resolve(
                            mood: animation.mood, pose: frame.pose,
                            gait: animation.gait, beat: beat,
                            // 문서용 그림에는 붙어 있는 상태가 없다. 창이 없다.
                            perch: nil
                        ),
                        flipped: false,
                        size: cell
                    )
                } else {
                    OwlMarkView(pose: frame.pose, palette: .normal)
                }
            }
            let content = body
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

/// `dong-csu --dump-sprites <경로>` — 격자 부엉이를 규격 시트로 굽는다.
///
/// **빌드가 이걸 불러서 번들에 넣는다.** HUD·펫의 마스코트는 예외 없이 파일에서
/// 읽으므로, 기본으로 깔리는 부엉이도 여기서 구운 파일이다. 색이 변형마다 달라서
/// (테스트판은 보라) 소스에 넣어 둘 수가 없고, 자세를 고치면 시트도 같이 바뀌어야
/// 해서 빌드 때 굽는 것이 유일하게 어긋나지 않는 길이다.
///
/// 그리는 사람에게 줄 빈 틀(`rules empty`)과 예시(`rules`)도 여기서 나온다.
@MainActor
enum MascotSpriteExport {
    /// 상태마다 어느 자세·팔레트로 그릴지.
    ///
    /// `tint` 로 몸 색을 강제로 바꿔서 뽑을 수 있다. 통로가 살아 있는지 눈으로 가를 때
    /// 쓴다 — 부엉이를 그대로 뽑으면 격자로 그린 것과 똑같이 나와서, 파일에서 그리는
    /// 중인지 코드로 그리는 중인지 알 수 없다.
    static func pose(for sprite: MascotSprite, tint: NSColor? = nil) -> (OwlPose, OwlPalette) {
        let (pose, palette) = basePose(for: sprite)
        // **죽음은 회색 그대로 둔다.** 거기 색은 장식이 아니라 정보다.
        guard let tint, sprite != .dead else { return (pose, palette) }
        return (pose, .tinted(body: tint))
    }

    private static func basePose(for sprite: MascotSprite) -> (OwlPose, OwlPalette) {
        switch sprite {
        case .idle:       return (OwlMood.idle.frames[0].pose, OwlMood.idle.palette)
        case .sleepy:     return (OwlMood.tired.frames[0].pose, OwlMood.tired.palette)
        case .exhausted:  return (OwlMood.exhausted.frames[0].pose, OwlMood.exhausted.palette)
        case .walkA:      return (walk(phase: 0), OwlMood.idle.palette)
        case .walkB:      return (walk(phase: 2), OwlMood.idle.palette)
        // 격자 부엉이의 달리기는 날개를 펴는 것으로 표현한다.
        case .runA:       return (walk(phase: 0, gait: .run), OwlMood.idle.palette)
        case .runB:       return (walk(phase: 2, gait: .run), OwlMood.idle.palette)
        // 벽에 붙은 자세도 격자에는 없다. 날개를 편 매달림으로 대신 굽는다.
        case .cling:      return (.carried(lean: 1, face: 1, feet: 1, wings: .spread),
                                  OwlMood.dragged.palette)
        case .blinkCling: return (closedEyes(basePose(for: .cling).0), OwlMood.dragged.palette)
        // 날개를 든 채 다리를 모아 늘어뜨린 모습. 목덜미를 잡혀 매달린 것이다.
        case .held:       return (.carried(lean: 0, face: 0, feet: 0, wings: .lift),
                                  OwlMood.dragged.palette)
        case .dizzy:      return (OwlMood.dizzy.frames[0].pose, OwlMood.dizzy.palette)
        // **아직 앱이 안 쓰는 자세다.** 격자 부엉이에는 앉기도 가장자리 매달리기도
        // 없어서, 가장 가까운 것으로 굽는다 — 예시 그림의 그 칸이 비지 않게 하려는 것뿐이다.
        case .sit:        return (OwlMood.exhausted.frames[0].pose, OwlMood.idle.palette)
        case .blinkSit:   return (OwlMood.exhausted.frames[0].pose, OwlMood.idle.palette)
        case .ledge:      return (.carried(lean: 0, face: 0, feet: 0, wings: .spread),
                                  OwlMood.dragged.palette)
        case .blinkHeld:  return (closedEyes(basePose(for: .held).0), OwlMood.dragged.palette)
        case .blinkLedge: return (closedEyes(basePose(for: .ledge).0), OwlMood.dragged.palette)
        case .dead:       return (OwlMood.offline.frames[0].pose, OwlMood.offline.palette)
        case .walkSleepyA:   return (sleepyWalk(phase: 0), OwlMood.tired.palette)
        case .walkSleepyB:   return (sleepyWalk(phase: 2), OwlMood.tired.palette)
        case .blink:
            return (OwlPose(eyes: .closed), OwlMood.idle.palette)
        case .blinkSleepy:
            return (closedEyes(OwlMood.tired.frames[0].pose), OwlMood.tired.palette)
        }
    }

    /// 한 칸을 몇 픽셀로 뽑을지. **정수여야 한다** — 나누어떨어지지 않으면 어떤 행은
    /// 2px, 어떤 행은 3px가 되어 자리마다 다른 얼굴이 된다.
    static let cell: CGFloat = 8

    /// 걷는 자세. **기울기는 빼고 굽는다.**
    ///
    /// 몸을 좌우로 미는 건 화면에서 코드가 넣는다(`OwlAnimator.spriteSway`). 그림에도
    /// 구워 두면 두 번 밀린다. 받은 그림은 규격으로 만들 때 자리를 맞춰서 어차피
    /// 기울기가 없으므로, 여기서도 빼야 두 갈래가 같아진다.
    private static func walk(
        phase: Int,
        base: OwlPose = OwlPose(),
        gait: OwlGait = .walk
    ) -> OwlPose {
        var pose = OwlAnimator.gaitPose(base: base, phase: phase, gait: gait)
        pose.lean = 0
        return pose
    }

    /// 졸린 채로 걷는 자세. 지침의 기본 자세(실눈·처진 날개) 위에 걸음을 얹는다.
    private static func sleepyWalk(phase: Int) -> OwlPose {
        walk(phase: phase, base: OwlMood.tired.frames[0].pose)
    }

    /// 같은 자세에 눈만 감긴 것.
    private static func closedEyes(_ pose: OwlPose) -> OwlPose {
        var pose = pose
        pose.eyes = .closed
        return pose
    }

    /// 한 상태를 그리는 뷰. 어느 칸이든 **자세를 통째로** 그린다.
    static func view(pose: OwlPose, palette: OwlPalette, cell: CGFloat) -> some View {
        OwlMarkView(pose: pose, palette: palette)
            .frame(width: cell * CGFloat(OwlMark.columns), height: cell * CGFloat(OwlMark.lines))
    }

    /// 한 장에 칸을 나눠 담아 뽑는다. 사용자에게 예시로 줄 형식이다.
    static func writeSheet(
        to path: String,
        tint: NSColor? = nil,
        multiple: Int = 1,
        rules: Bool = false,
        empty: Bool = false,
        labels: Bool = false
    ) -> Bool {
        let side = CGFloat(MascotSheet.canonicalCell * multiple)
        let rule = CGFloat(MascotSheet.canonicalRule * multiple)
        // 한 칸 안에서 부엉이를 몇 픽셀짜리 칸으로 그릴지. **정수여야 한다** —
        // 나누어떨어지지 않으면 어떤 행은 2px, 어떤 행은 3px가 되어 자리마다 얼굴이 다르다.
        let owlCell = (side / CGFloat(OwlMark.columns)).rounded(.down)

        // 이름표는 **칸 위에 따로 띠를 두고** 거기 적는다. 그림 위에 얹으면 이미지
        // 모델이 그 글자까지 캐릭터의 일부로 보고 따라 그린다.
        let strip = labels ? side * 0.2 : 0
        let content = VStack(spacing: 0) {
            ruleLine(rules, length: nil, thickness: rule)
            ForEach(MascotSheet.rowIndices, id: \.self) { row in
                if labels {
                    HStack(spacing: 0) {
                        ruleLine(false, length: strip, thickness: rule)
                        ForEach(MascotSheet.columnIndices, id: \.self) { column in
                            label(row: row, column: column, side: side)
                                .frame(width: side, height: strip, alignment: .bottomLeading)
                            ruleLine(false, length: strip, thickness: rule)
                        }
                    }
                }
                HStack(spacing: 0) {
                    ruleLine(rules, length: side, thickness: rule)
                    ForEach(MascotSheet.columnIndices, id: \.self) { column in
                        cellView(
                            empty ? nil : MascotSheet.layout[row][column],
                            tint: tint,
                            cell: owlCell
                        )
                        // 칸은 정사각으로 둔다. 캐릭터 비율과 무관하게 격자가 고르다.
                        .frame(width: side, height: side)
                        ruleLine(rules, length: side, thickness: rule)
                    }
                }
                ruleLine(rules, length: nil, thickness: rule)
            }
        }
        .frame(
            width: CGFloat(MascotSheet.columns) * side + CGFloat(MascotSheet.columns + 1) * rule,
            height: CGFloat(MascotSheet.rows) * (side + strip)
                + CGFloat(MascotSheet.rows + 1) * rule
        )
        .background(labels ? Color.white : Color.clear)
        return ImageRenderer(content: content).writePNG(to: path, scale: 1)
    }

    /// 칸에 붙이는 이름표. **그리는 쪽에 줄 규격 그림에만 넣는다** —
    /// 앱이 읽는 시트에 글자가 들어가면 그게 그림의 일부가 된다.
    ///
    /// 이미지 모델에게는 긴 글보다 이 한 장이 훨씬 잘 먹는다.
    @ViewBuilder
    private static func label(row: Int, column: Int, side: CGFloat) -> some View {
        let sprite = MascotSheet.layout[row][column]
        // **"워터마크" 라고 적지 않는다.** 그렇게 적어 뒀더니 그리는 쪽이 거기에
        // 진짜 워터마크를 그려 넣었다. 비우라고만 하면 비운다.
        let text = sprite?.rawValue ?? "비움"
        HStack(alignment: .firstTextBaseline, spacing: side * 0.03) {
            Text(text)
                .font(.system(size: side * 0.062, weight: .semibold, design: .monospaced))
                .foregroundStyle(sprite == nil ? Color(white: 0.55) : Color(white: 0.15))
            Spacer(minLength: 0)
        }
        .padding(.horizontal, side * 0.03)
        .padding(.bottom, side * 0.035)
    }

    /// 칸을 가르는 선. **어느 칸에도 안 들어간다** — 칸 자리를 이만큼 밀어 놨다.
    /// 그리는 사람이 지우든 남기든 그림에는 영향이 없다.
    @ViewBuilder
    private static func ruleLine(_ shown: Bool, length: CGFloat?, thickness: CGFloat) -> some View {
        if let length {
            Rectangle()
                .fill(shown ? Color(white: 0.45) : Color.clear)
                .frame(width: thickness, height: length)
        } else {
            Rectangle()
                .fill(shown ? Color(white: 0.45) : Color.clear)
                .frame(height: thickness)
        }
    }

    /// 칸 하나. 빈칸은 투명하게 둔다.
    @ViewBuilder
    private static func cellView(
        _ sprite: MascotSprite?,
        tint: NSColor?,
        cell: CGFloat
    ) -> some View {
        if let sprite {
            spriteView(sprite, tint: tint, cell: cell)
        } else {
            Color.clear
        }
    }

    /// **일반 함수여야 한다.** `ViewBuilder` 안에서는 튜플을 풀 수 없다.
    private static func spriteView(
        _ sprite: MascotSprite,
        tint: NSColor?,
        cell: CGFloat
    ) -> some View {
        let (pose, palette) = Self.pose(for: sprite, tint: tint)
        return Self.view(pose: pose, palette: palette, cell: cell)
    }

    /// 낱장으로도 뽑는다. 시트를 못 만드는 도구를 쓰는 사람을 위한 길이다.
    static func writeAll(
        to directory: String,
        tint: NSColor? = nil,
        cell: CGFloat = MascotSpriteExport.cell
    ) -> [String]? {
        let base = URL(fileURLWithPath: directory, isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        } catch {
            return nil
        }

        var written: [String] = []
        for sprite in MascotSprite.allCases {
            let (pose, palette) = pose(for: sprite, tint: tint)
            let url = base.appendingPathComponent("\(sprite.rawValue).png")
            // 배경은 투명하게 둔다. 펫 모드가 그걸 그대로 뚫는다.
            let content = view(pose: pose, palette: palette, cell: cell)
            guard ImageRenderer(content: content).writePNG(to: url.path, scale: 1) else { return nil }
            written.append(url.path)
        }
        return written
    }
}

/// `dong-csu --render out.png` — HUD를 고정값으로 그려 PNG로 저장한다.
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
        showsScopedLimit: Bool = false,
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
            fetchedAt: Date().addingTimeInterval(state == .ok ? 0 : -13 * 3600),
            // 모델별 한도는 서버가 줄 때만 있다. 켠 모습을 그리려면 여기서 흉내낸다.
            limits: showsScopedLimit
                ? [UsageLimit(kind: "weekly_scoped", modelName: "Fable",
                              percent: 18, resetsAt: Date().addingTimeInterval(26 * 3600))]
                : []
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
        // 실제 화면과 같은 규칙으로 색을 뺀다. 안 그러면 미리보기만 멀쩡해 보인다.
        animator.setUnusable(store.isSpent)

        let palette = HUDPalette(isDark: isDark)
        let content = UsageHUDView(
            store: store,
            iconStyle: iconStyle,
            mode: mode,
            isHovered: isHovered,
            showsScopedLimit: showsScopedLimit,
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
    /// 측정 탭을 그릴 고정값. 재는 중이고, 세션 창을 한 번 넘긴 모습이다.
    /// `--probe-layout` 이 쓰는 고정값. 재는 중인지와 기록 개수를 바꿔 가며 잰다.
    static func probeMeterState(running: Bool, records: Int) -> UsageMeter.State {
        var state = meterState()
        if !running {
            state.startedAt = nil
            state.stoppedAt = nil
        }
        let sample = state.history
        state.history = (0..<records).map { index in
            let base = sample[index % sample.count]
            // id 가 시작 시각이라 겹치면 ForEach 가 항목을 합쳐 버린다.
            return UsageMeter.Record(
                startedAt: base.startedAt.addingTimeInterval(-Double(index) * 3600),
                stoppedAt: base.stoppedAt.addingTimeInterval(-Double(index) * 3600),
                tracks: base.tracks, tokens: base.tokens,
                tokensByModel: base.tokensByModel, samples: base.samples
            )
        }
        return state
    }

    private static func meterState() -> UsageMeter.State {
        var state = UsageMeter.State()
        state.startedAt = Date().addingTimeInterval(-(5 * 3600 + 42 * 60))
        state.samples = 34
        state.lastSampledAt = Date().addingTimeInterval(-3 * 60)
        state.order = ["session", "weekly_all", "weekly_scoped/Fable"]
        state.tracks = [
            "session": .init(title: "세션 (5시간)", accumulated: 118, lastPercent: 24, resets: 1),
            "weekly_all": .init(title: "주간 (7일)", accumulated: 17, lastPercent: 90),
            "weekly_scoped/Fable": .init(title: "주간 · Fable", accumulated: 3, lastPercent: 15),
        ]
        state.tokens = TokenTally(
            responses: 536, input: 1_824, output: 1_145_375,
            cacheCreation: 16_885_030, cacheRead: 452_846_994
        )
        state.tokensByModel = [
            "Opus 5": TokenTally(responses: 412, input: 1_500, output: 980_000,
                                 cacheCreation: 14_000_000, cacheRead: 400_000_000),
            "Haiku 4.5": TokenTally(responses: 124, input: 324, output: 165_375,
                                    cacheCreation: 2_885_030, cacheRead: 52_846_994),
        ]
        // 끝난 측정 몇 개. 기록 목록이 제 안에서 넘겨지는지 여기서 눈으로 본다.
        state.history = (1...4).map { index in
            let stoppedAt = Date().addingTimeInterval(-Double(index) * 26 * 3600)
            return UsageMeter.Record(
                startedAt: stoppedAt.addingTimeInterval(-Double(index) * 40 * 60),
                stoppedAt: stoppedAt,
                tracks: [.init(title: "세션 (5시간)", accumulated: Double(index) * 7)],
                tokens: TokenTally(
                    responses: index * 40, input: index * 900, output: index * 120_000,
                    cacheCreation: index * 1_400_000, cacheRead: index * 32_000_000
                ),
                tokensByModel: [:],
                samples: index * 3
            )
        }
        return state
    }

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
            fetchedAt: Date(),
            rateLimitTier: "default_claude_max_5x",
            tokenExpiresAt: Date().addingTimeInterval(6 * 3600 + 41 * 60)
        )
        let view = SettingsView(
            settings: HUDSettings(defaults: UserDefaults(suiteName: "dong-csu.preview") ?? .standard),
            // 상태 탭이 조회 카운트다운을 그리므로 예정 시각까지 넣어야 실제와 같아진다.
            store: UsageStore(preview: snapshot, nextPoll: Date().addingTimeInterval(7 * 60 + 12)),
            updates: UpdateChecker(
                preview: update,
                lastCheckedAt: Date().addingTimeInterval(-40 * 60)
            ),
            meter: UsageMeter(preview: meterState()),
            actions: SettingsActions(refresh: {}, resetPosition: {}, login: {}, quit: {}),
            // **번들 이름을 쓰지 않는다.** 문서 그림은 테스트 바이너리로 뽑기 때문에
            // 창 바닥에 `DongCSU-Test` 가 박힌다.
            version: "DongCSU \(dongCSUVersion)",
            initialTab: tab,
            isPreviewRender: true
        )
        .content
        .preferredColorScheme(isDark ? .dark : .light)
        .background(Color(nsColor: .windowBackgroundColor))

        return ImageRenderer(content: view).writePNG(to: path, scale: 2)
    }
}
