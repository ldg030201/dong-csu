import Foundation

/// 부엉이를 파일 하나로 내보낸다 — `dong-csu --dump-owl shared/owl.json`.
///
/// 윈도우판이 같은 부엉이를 그리려면 그리드·색·프레임표가 필요하다. 그걸 손으로
/// 옮겨 적으면 **맥에서 자세를 고칠 때마다 어긋난다.** 그래서 맥 소스를 그대로 뽑아
/// 둘이 같은 파일을 본다. `Changelog.swift` → `docs/changelog.json` 과 같은 방식이고,
/// CI가 소스와 파일이 다르면 실패시킨다.
///
/// **프레임마다 합성이 끝난 15×13 그리드를 같이 넣는다.** 레이어 겹치기(`OwlPose.layers`)
/// 는 알고리즘이라 윈도우가 새로 구현해야 하는데, 거기서 틀려도 이 그리드와 비교하면
/// 바로 드러난다. 그림 하나 그려보지 않고 텍스트만으로 검증할 수 있다.
enum OwlExport {
    /// 이 파일의 형식이 바뀌면 올린다. 읽는 쪽이 모르는 형식을 조용히 잘못 읽는 걸 막는다.
    static let formatVersion = 1

    /// `OwlAnimation.all` 이 메인 액터라 여기도 그렇다. 부르는 쪽은 진단 통로뿐이다.
    @MainActor
    static func jsonData() throws -> Data {
        let encoder = JSONEncoder()
        // 키 순서가 실행마다 달라지면 같은 소스로 뽑아도 파일이 바뀐 것처럼 보인다.
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(payload())
    }

    // MARK: - 형태

    private struct Payload: Encodable {
        let formatVersion: Int
        let grid: Grid
        let layers: [String: [String]]
        let palettes: [String: [String: String]]
        let moodThresholds: [String: Double]
        let usageColors: [UsageStop]
        let animations: [Animation]
    }

    private struct Grid: Encodable {
        let columns: Int
        let lines: Int
        /// 몸통이 실제로 쓰는 열 수. 좌우 여백은 날개를 펼 자리다.
        let bodyColumns: Int
    }

    private struct UsageStop: Encodable {
        let at: Double
        let hex: String
    }

    private struct Animation: Encodable {
        let name: String
        let title: String
        let palette: String
        let frames: [Frame]
    }

    private struct Frame: Encodable {
        let duration: TimeInterval
        let jitter: TimeInterval
        let pose: Pose
        /// 합성이 끝난 그림. 한 줄이 한 행이고, `.`은 빈 칸이다.
        let grid: [String]
    }

    private struct Pose: Encodable {
        let eyes: String
        let wings: String
        let feet: String
        let lean: Int
        let bob: Int
        let faceLean: Int
        let feetLean: Int
    }

    // MARK: - 뽑기

    @MainActor
    private static func payload() -> Payload {
        Payload(
            formatVersion: formatVersion,
            grid: Grid(
                columns: OwlMark.columns,
                lines: OwlMark.lines,
                bodyColumns: OwlMark.bodyColumns
            ),
            layers: OwlMark.layers,
            palettes: [
                "normal": hexes(of: .normal),
                "offline": hexes(of: .offline),
                "test": hexes(of: .tinted(body: AppInfo.testBuildTint)),
            ],
            moodThresholds: [
                "tired": OwlMood.tiredThreshold,
                "exhausted": OwlMood.exhaustedThreshold,
            ],
            usageColors: UsageColor.stops.map {
                UsageStop(at: $0.threshold, hex: OwlColor($0.rgb.0, $0.rgb.1, $0.rgb.2).hex)
            },
            animations: OwlAnimation.all.map(animation)
        )
    }

    private static func hexes(of palette: OwlPalette) -> [String: String] {
        [
            "body": palette.body.hex,
            "wing": palette.wing.hex,
            "belly": palette.belly.hex,
            "face": palette.face.hex,
            "pupil": palette.pupil.hex,
            "beak": palette.beak.hex,
        ]
    }

    private static func animation(_ source: OwlAnimation) -> Animation {
        Animation(
            name: source.name,
            title: source.title,
            // 끊김만 회색이고 나머지는 평소 색이다. 이름으로 가리켜서 색을 두 번 적지 않는다.
            palette: source.palette == .offline ? "offline" : "normal",
            frames: source.frames.map(frame)
        )
    }

    private static func frame(_ source: OwlFrame) -> Frame {
        Frame(
            duration: source.duration,
            jitter: source.jitter,
            pose: pose(source.pose),
            grid: source.pose.grid.map { String($0) }
        )
    }

    private static func pose(_ source: OwlPose) -> Pose {
        Pose(
            eyes: String(describing: source.eyes),
            wings: String(describing: source.wings),
            feet: String(describing: source.feet),
            lean: source.lean,
            bob: source.bob,
            faceLean: source.faceLean,
            feetLean: source.feetLean
        )
    }
}
