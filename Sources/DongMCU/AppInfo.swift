import AppKit

/// 실행 중인 번들이 정식판인지 테스트판인지 알려준다.
///
/// 두 앱은 번들 ID가 달라서 설정·창 위치·메뉴바 자리를 서로 건드리지 않는다.
/// 화면에 이름을 쓸 때는 하드코딩하지 말고 여기서 가져온다.
enum AppInfo {
    static var name: String {
        Bundle.main.infoDictionary?["CFBundleName"] as? String ?? "dong-mcu"
    }

    static var version: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? dongMCUVersion
    }

    /// 테스트판은 메뉴바 아이콘 색을 달리해서 정식판과 구분한다.
    static var isTestBuild: Bool {
        name.hasSuffix("-test")
    }

    /// "dong-mcu 0.2.0" 처럼 이름과 버전을 붙인 표기.
    static var displayVersion: String {
        "\(name) \(version)"
    }
}
