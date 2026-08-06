import Foundation

/// 버전별 변경 내역.
///
/// 설정 창의 "버전" 탭이 이걸 그대로 보여준다. 쓰는 방법은 CLAUDE.md 참고 —
/// 기능 단위로 한 줄씩, "추가 / 수정 / 변경" 처럼 명사형으로 끝맺는다.
struct ChangelogEntry: Codable, Equatable {
    let version: String
    /// 아직 내보내지 않은 항목은 nil. 릴리스할 때 날짜를 채운다.
    let date: String?
    let notes: [String]
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
    static let feedURL = URL(
        string: "https://raw.githubusercontent.com/ldg030201/dong-csu/main/docs/changelog.json"
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
        ChangelogEntry(version: "2.0.0", date: nil, notes: [
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
