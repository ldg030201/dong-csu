import AppKit

/// 실행 중인 번들이 정식판인지 테스트판인지 알려준다.
///
/// 두 앱은 번들 ID가 달라서 설정·창 위치·메뉴바 자리를 서로 건드리지 않는다.
/// 화면에 이름을 쓸 때는 하드코딩하지 말고 여기서 가져온다.
enum AppInfo {
    static var name: String {
        Bundle.main.infoDictionary?["CFBundleName"] as? String ?? "DongCSU"
    }

    static var version: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? dongCSUVersion
    }

    /// 테스트판은 메뉴바 아이콘 색을 달리해서 정식판과 구분한다.
    ///
    /// 표시 이름이 아니라 번들 ID로 판별한다. 이름은 바뀔 수 있지만
    /// 번들 ID는 설정을 붙잡고 있어서 함부로 못 바꾼다.
    /// 프로세스가 도는 동안 바뀌지 않는다. 마스코트를 그릴 때마다 읽히는 자리라
    /// 매번 번들 정보를 뒤지지 않게 한 번만 정한다.
    static let isTestBuild: Bool = Bundle.main.bundleIdentifier?.hasSuffix("-test") ?? false

    /// 테스트판 메뉴바 아이콘의 몸 색. 정식판과 나란히 떠도 구분되게 한다.
    /// 렌더 통로도 같은 값을 써야 미리보기가 실제와 어긋나지 않는다.
    static let testBuildTint = NSColor(srgbRed: 0.54, green: 0.34, blue: 0.85, alpha: 1)

    /// "DongCSU 0.2.0" 처럼 이름과 버전을 붙인 표기.
    static var displayVersion: String {
        "\(name) \(version)"
    }

    /// HUD 왼쪽 위에 붙이는 버전 딱지. 테스트판은 뒤에 `test`가 붙는다.
    static var badgeVersion: String {
        isTestBuild ? "\(version) test" : version
    }

    /// 마스코트를 그릴 기본 팔레트.
    ///
    /// 테스트판은 메뉴바 아이콘과 **같은 보라색 몸**으로 그린다. 링도 카드도 없는
    /// 펫 모드에서는 글자를 붙일 자리가 없어서, 정식판과 구분할 방법이 색뿐이다.
    /// 프레임마다 불리는 자리라 팔레트를 새로 만들지 않고 한 벌 만들어 둔 걸 돌려준다.
    static var owlPalette: OwlPalette {
        isTestBuild ? testBuildPalette : .normal
    }

    private static let testBuildPalette = OwlPalette.tinted(body: testBuildTint)
}
