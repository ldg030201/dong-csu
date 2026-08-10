import Foundation

// 버전별 변경 내역.
//
// 설정 창의 "버전" 탭이 이걸 그대로 보여준다. 쓰는 방법은 CLAUDE.md 참고 —
// 기능 단위로 묶고, 한 줄씩 명사형으로 끝맺는다.

/// 변경 한 줄이 어느 갈래인지. 항목 앞에 딱지로 붙는다.
enum ChangeKind: String, Codable, Equatable, CaseIterable {
    case new
    case improve
    case change
    case fix
    case remove

    var title: String {
        switch self {
        case .new: return "신규"
        case .improve: return "개선"
        case .change: return "변경"
        case .fix: return "오류"
        case .remove: return "제거"
        }
    }
}

/// 변경 한 줄.
struct ChangelogNote: Codable, Equatable {
    let kind: ChangeKind
    let text: String

    static func new(_ text: String) -> Self { Self(kind: .new, text: text) }
    static func improve(_ text: String) -> Self { Self(kind: .improve, text: text) }
    static func change(_ text: String) -> Self { Self(kind: .change, text: text) }
    static func fix(_ text: String) -> Self { Self(kind: .fix, text: text) }
    static func remove(_ text: String) -> Self { Self(kind: .remove, text: text) }
}

/// 기능 단위 묶음. 화면·메뉴 이름을 그대로 쓴다("측정", "펫 모드").
///
/// 한 버전에 스무 줄이 쌓이면 평평한 목록으로는 무엇이 달라졌는지 안 잡힌다.
/// 쓰는 사람은 자기가 쓰는 기능만 보면 되므로 그 단위로 묶는다.
struct ChangelogGroup: Codable, Equatable {
    let title: String
    /// 이 묶음이 어느 설정 탭 이야기인지(`SettingsTab` 의 rawValue).
    ///
    /// 제목 앞에 **그 탭에 실제로 붙어 있는 아이콘**이 그대로 나온다. 여기에 아이콘
    /// 이름을 직접 적지 않는 이유가 그거다 — 탭 아이콘을 바꾸면 여기도 같이 바뀌어야 한다.
    ///
    /// **메뉴가 아닌 것은 nil.** 마스코트·HUD·설치처럼 탭에 없는 이야기는 공통 아이콘
    /// 하나로 묶는다.
    var tab: String?
    /// 이 묶음 자체가 이번에 새로 생긴 기능인지. 제목 오른쪽에 "신규"가 붙는다.
    var isNew: Bool = false
    let notes: [ChangelogNote]
}

struct ChangelogEntry: Codable, Equatable {
    let version: String
    /// 아직 내보내지 않은 항목은 nil. 릴리스할 때 날짜를 채운다.
    let date: String?
    /// 평평한 목록.
    ///
    /// **지우면 안 된다.** 2.2.0 이하가 같은 JSON을 받아보는데 그쪽은 이것만 읽는다.
    /// 2.3.0부터는 `groups` 에서 만들어 내므로 손으로 적지 않는다.
    let notes: [String]
    /// 2.3.0부터. 기능별로 묶고 항목마다 갈래를 단다.
    ///
    /// 옛 항목은 nil이고, 그때는 화면이 `notes` 를 그대로 늘어놓는다. 이미 나간 문구를
    /// 뒤늦게 갈래로 나누면 사용자가 봤던 것과 달라지므로 **옛 버전은 손대지 않는다.**
    let groups: [ChangelogGroup]?
}

extension ChangelogEntry {
    /// 2.3.0부터 쓰는 형태. 옛 앱이 읽는 평평한 목록은 여기서 만들어 낸다 —
    /// 두 곳에 손으로 적으면 반드시 어긋난다.
    init(version: String, date: String?, groups: [ChangelogGroup]) {
        self.version = version
        self.date = date
        self.groups = groups
        self.notes = groups.flatMap { group in
            group.notes.map { "[\(group.title)] \($0.text)" }
        }
    }

    /// 2.2.0 이하. 그때 나간 문구를 그대로 둔다.
    init(version: String, date: String?, notes: [String]) {
        self.version = version
        self.date = date
        self.notes = notes
        self.groups = nil
    }
}

/// 원격에서 받아오는 변경 내역 파일의 형태.
///
/// 앱에 박혀 있는 내역은 그 버전까지밖에 모른다. 새 버전에 무엇이 들어갔는지
/// 업데이트하기 전에 보려면 밖에서 받아와야 한다.
struct ChangelogFeed: Codable {
    let entries: [ChangelogEntry]
}

enum Changelog {
    /// 원격 내역 주소. 릴리스 API 대신 이 파일 하나로 최신 버전과 내역을 함께 받는다.
    ///
    /// 저장소를 `mac/` · `win/` 으로 가르면서 옮겼다. 2.0.0 이하는 아직 뿌리의
    /// `docs/changelog.json` 을 보므로 `dump_changelog` 가 그쪽에도 같은 것을 쓴다.
    static let feedURL = URL(
        string: "https://raw.githubusercontent.com/ldg030201/dong-csu/main/mac/docs/changelog.json"
    )!

    /// 이 목록을 JSON으로 뽑는다. `dong-csu --dump-changelog`가 쓴다.
    ///
    /// `.sortedKeys`가 없으면 키 순서가 실행마다 달라져서, 같은 소스로 뽑아도
    /// 파일이 매번 바뀐 것처럼 보인다(CI의 일치 검사가 계속 실패한다).
    static func jsonData() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(ChangelogFeed(entries: entries))
    }

    /// 최신이 위로 온다.
    ///
    /// 맨 위는 아직 내보내지 않은 항목이다. 무언가를 만들거나 고칠 때마다 여기에
    /// 한 줄씩 쌓고, 릴리스할 때 버전과 날짜를 확정한다.
    static let entries: [ChangelogEntry] = [
        ChangelogEntry(version: "2.3.0", date: nil, groups: [
            ChangelogGroup(title: "측정", tab: "measure", isNew: true, notes: [
                .new("시작·중지 사이에 쓴 한도 %p와 토큰 수 측정 (설정 창 측정 탭)"),
                .new("모델별 한도·토큰 표시"),
                .new("토큰 합계와 캐시 제외 합계 표시"),
                .new("일시정지·계속 (세워 둔 동안의 시간과 사용량은 빼고 셈)"),
                .new("측정 기록 목록 (중지하면 남고, 누르면 그때 값을 펼쳐 봄)"),
                .new("HUD·펫 모드의 측정 버튼 (설정 버튼 왼쪽, 누르면 측정 화면이 열림)"),
            ]),
            ChangelogGroup(title: "마스코트", notes: [
                .change("주간 한도를 다 쓰면 완전히 멈추고 주간 링·점까지 회색이 되도록 변경 "
                        + "(걷기·커서 피하기·끌림 반응 정지)"),
            ]),
            ChangelogGroup(title: "펫 모드", tab: "pet", notes: [
                .fix("커서 피하기 판정이 마스코트보다 아래를 보던 문제 수정"),
                .fix("막 들어갔을 때 커서가 마스코트 위에 있어도 링이 바로 뜨지 않던 문제 수정"),
                .fix("설정·새로고침 버튼에 마우스를 올려도 표시가 바뀌지 않던 문제 수정"),
            ]),
            ChangelogGroup(title: "토큰 자동 갱신", tab: "account", isNew: true, notes: [
                .new("만료된 토큰을 앱이 스스로 갱신 (다섯 시간마다 뜨던 재로그인 안내가 없어짐)"),
                .new("갱신한 토큰을 keychain에 되돌려 쓰기 (Claude Code 로그인이 같이 풀리지 않음)"),
            ]),
            ChangelogGroup(title: "변경 내역", tab: "version", notes: [
                .improve("기능별로 묶고 항목마다 갈래를 붙여 보여주도록 개선"),
                .improve("목록이 길어져도 창이 늘어나지 않고 안에서 넘겨보도록 개선"),
            ]),
            ChangelogGroup(title: "아이콘", tab: "icon", notes: [
                .fix("Claude 앱을 나중에 설치하면 다시 띄우기 전까지 못 찾던 문제 수정"),
            ]),
        ]),
        ChangelogEntry(version: "2.2.0", date: "2026-08-07", notes: [
            "펫 모드에 설정·새로고침 버튼 추가 (마스코트 아래, 마우스를 올리면 나타남)",
            "펫 모드에 새 버전 표시 추가 (오른쪽 위)",
            "설정 창을 열 때마다 가로 스크롤이 생기던 문제 수정",
            "업데이트가 끝난 뒤 터미널이 키 입력을 기다리던 문제 수정",
            "업데이트가 끝나도 터미널이 Dock에 남던 문제 수정",
        ]),
        ChangelogEntry(version: "2.1.3", date: "2026-08-07", notes: [
            "주간 한도를 다 쓰면 마스코트·세션 링·세션 숫자를 회색으로 표시하도록 변경",
            "마스코트 움직이기를 캐릭터 애니메이션으로 이름 변경",
            "펫 모드가 꺼져 있으면 사용량 링·스스로 움직이기 설정을 잠그도록 변경",
            "아이콘 탭의 설명 문구 제거",
            "위치 초기화가 주 모니터가 아니라 그때 쓰던 모니터로 가던 문제 수정",
            "배경 불투명도 기본값을 100%로 변경",
        ]),
        ChangelogEntry(version: "2.1.2", date: "2026-08-06", notes: [
            "마스코트 애니메이션 끄기 설정 추가 (아이콘 탭)",
            "모든 설정 초기화 추가 (표시 탭)",
            "설정 창을 키우면 내용이 가운데 멈춰 있고 둘레만 벌어지던 문제 수정",
            "앱 안의 업데이트가 Homebrew 전체를 갱신하던 것을 이 tap 만 갱신하도록 개선",
        ]),
        ChangelogEntry(version: "2.1.1", date: "2026-08-06", notes: [
            "로그인할 때 자동 시작 설정 추가 (표시 탭)",
        ]),
        ChangelogEntry(version: "2.1.0", date: "2026-08-06", notes: [
            "부엉이 그리드·색·프레임표를 shared/owl.json 으로 내보내는 기능 추가",
            "우클릭·메뉴바 메뉴에서 설정 창과 겹치는 항목 제거 (새로고침·설정·종료만 남김)",
        ]),
        ChangelogEntry(version: "2.0.0", date: "2026-08-05", notes: [
            "앱 이름을 DongCSU로 변경 (윈도우판 대비, macOS를 뜻하던 m 제거)",
            "Homebrew tap·명령·번들 ID를 dong-csu로 변경",
            "옛 이름에서 쓰던 설정을 첫 실행 때 옮겨 오는 기능 추가",
        ]),
        ChangelogEntry(version: "1.5.2", date: "2026-08-05", notes: [
            "앱 안의 업데이트가 Homebrew 확인 물음 앞에서 멈추던 문제 수정",
            "혼자 돌아다니기·커서 피하기 기본값을 켜짐으로 변경",
        ]),
        ChangelogEntry(version: "1.5.1", date: "2026-08-05", notes: [
            "HUD 왼쪽 위 버전 글자가 둥근 모서리에 잘리던 문제 수정",
            "조회가 끊겨 회색이 된 펫을 끌면 색이 돌아오던 문제 수정",
            "조회가 끊긴 동안에도 펫이 걸어다니던 문제 수정",
            "커서를 피하는 속도와 반응을 빠르게 개선",
        ]),
        ChangelogEntry(version: "1.5.0", date: "2026-08-05", notes: [
            "펫이 혼자 화면을 걸어다니는 기능 추가 (설정 → 펫, 기본 꺼짐)",
            "커서를 펫 위에 올려두면 비켜주는 기능 추가 (설정 → 펫, 기본 꺼짐)",
            "글을 쓰는 동안 펫이 제자리에 멈추는 기능 추가",
            "부엉이 걷기 동작 추가 (눈 깜빡임 포함)",
            "부엉이 달리기 동작 추가 (날개짓)",
            "커서가 쫓아오면 펫이 뛰어서 달아나는 동작 추가",
            "HUD 왼쪽 위에 버전 표시 추가 (설정에서 끄기 가능)",
            "테스트판 마스코트 색을 보라색으로 변경",
        ]),
        ChangelogEntry(version: "1.4.0.2", date: "2026-08-05", notes: [
            "펫 모드에서 마스코트 한 귀퉁이를 눌러도 끌리지 않던 문제 수정",
            "마스코트를 더블클릭해 펫 모드에 들어가면 링이 바로 뜨지 않던 문제 수정",
            "펫 모드에서 조회가 끊겼을 때 링과 마스코트가 다르게 흐려지던 문제 수정",
        ]),
        ChangelogEntry(version: "1.4.0.1", date: "2026-08-05", notes: [
            "Homebrew formula가 깨져 설치·업데이트가 실패하던 문제 수정",
            "업데이트가 끝난 뒤 키를 누르면 터미널 창이 닫히는 기능 추가",
        ]),
        ChangelogEntry(version: "1.4.0", date: "2026-08-05", notes: [
            "펫 모드 추가 (마스코트만 표시, 설정 창에 펫 탭 추가)",
            "펫 모드 사용량 링 표시 설정 추가 (항상 / 마우스를 올리면 / 표시 안 함)",
            "HUD를 위아래로 끌면 부엉이가 날개를 드는 동작 추가",
            "부엉이 애니메이션 추가 (눈 깜빡임 · 지침 · 탈진 · 연결 끊김)",
            "마구 흔들면 부엉이가 어지러워하는 동작 추가",
            "HUD를 끌 때 마우스 방향·속도를 따르는 부엉이 동작 추가",
        ]),
        ChangelogEntry(version: "1.3.1.1", date: "2026-08-04", notes: [
            "지원 범위를 macOS 14(Sonoma) 이상으로 조정",
        ]),
        ChangelogEntry(version: "1.3.1", date: "2026-08-04", notes: [
            "원격 내역이 뒤처졌을 때 지금 버전 항목이 사라지던 문제 수정",
            "재로그인이 필요한 상태에서 불필요하게 반복 조회하던 문제 수정",
        ]),
        ChangelogEntry(version: "1.3.0", date: "2026-08-04", notes: [
            "아직 설치하지 않은 버전의 변경 내역도 볼 수 있게 함",
        ]),
        ChangelogEntry(version: "1.2.0.1", date: "2026-08-04", notes: [
            "Swift 5.10 환경에서 빌드가 실패하던 문제 수정",
        ]),
        ChangelogEntry(version: "1.2.0", date: "2026-08-04", notes: [
            "설정 창 종료 버튼에 확인 창 추가",
            "Command Line Tools가 오래된 환경에서 설치가 실패하던 문제 수정",
            "지원 범위를 macOS 13(Ventura)까지 넓힘",
        ]),
        ChangelogEntry(version: "1.1.0", date: "2026-08-04", notes: [
            "자동 업데이트 확인 추가 (하루 1회, 끄기 가능)",
            "새 버전 알림 표시 추가 (HUD 왼쪽 위)",
            "설정 창에 버전 탭 추가 (버전 확인·업데이트·변경 내역)",
        ]),
        ChangelogEntry(version: "1.0.0", date: "2026-08-04", notes: [
            "마스코트 부엉이 추가 (가운데 아이콘 기본값)",
            "앱 아이콘 추가",
            "메뉴바 아이콘을 부엉이로 변경",
            "설정 창 탭 분리 (상태·표시·아이콘·계정)",
            "HUD 크기 설정 추가 (작게 / 보통 / 크게 / 매우 크게)",
            "상태 탭에 조회 시각·초기화 시간 표시 추가",
            "가운데 아이콘 묶음 분리 (캐릭터 / Claude)",
            "앱 표시 이름을 DongMCU로 변경",
        ]),
        ChangelogEntry(version: "0.2.0.1", date: "2026-08-04", notes: [
            "CPU·메모리 표시 시 버튼 클릭이 통과되지 않던 문제 수정",
            "접었다 펴면 CPU·메모리 줄이 사라지던 문제 수정",
            "설정 창 크기 조절·스크롤 지원 추가",
        ]),
        ChangelogEntry(version: "0.2.0", date: "2026-08-04", notes: [
            "설정 창 추가 (톱니 버튼 또는 ⌘,)",
            "테마 선택 추가 (라이트·다크·시스템)",
            "HUD 접기와 펼침 방향 설정 추가",
            "배경 불투명도 설정 추가",
            "가운데 아이콘 선택 추가",
            "CPU·메모리 표시 추가",
            "다른 모니터로 옮길 수 없던 문제 수정",
            "드래그가 커서를 따라오지 못하던 문제 수정",
        ]),
        ChangelogEntry(version: "0.1.2", date: "2026-08-03", notes: [
            "Homebrew 설치 경로 정리",
        ]),
        ChangelogEntry(version: "0.1.1", date: "2026-08-03", notes: [
            "Homebrew 샌드박스에서 빌드가 실패하던 문제 수정",
        ]),
        ChangelogEntry(version: "0.1.0", date: "2026-08-03", notes: [
            "첫 공개",
            "이중 링 사용량 표시 추가 (5시간 세션 / 7일 주간)",
            "메뉴바 아이콘 추가",
            "새로고침 버튼과 조회 카운트다운 추가",
            "토큰 만료 시 오래된 값 표시 추가",
        ]),
    ]
}
