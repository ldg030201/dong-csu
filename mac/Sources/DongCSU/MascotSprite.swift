import AppKit
import SwiftUI

/// 그림(PNG) 으로 그리는 마스코트의 상태.
///
/// **HUD·펫의 마스코트는 예외 없이 이 통로를 탄다.** 기본으로 깔린 부엉이도 빌드할 때
/// 규격 시트로 구워져 번들에 들어가고, 사용자 그림과 똑같이 읽힌다 — 파일 하나를
/// 바꾸면 캐릭터가 바뀐다. 부엉이만 코드로 그리면 같은 규격을 두 번 구현하는 셈이라
/// 둘이 반드시 어긋난다.
///
/// 격자로 그리는 코드(`OwlMark`)는 **그림을 만드는 도구**로 남는다 — 메뉴바·앱 아이콘과
/// `shared/owl.json` 이 계속 그걸 쓴다.
///
/// 사람이 스무 칸 남짓 그려서 넣을 수 있는 규모여야 해서, 격자 쪽이 계산으로 만들어
/// 내는 수백 가지를 다 담지 않는다.
///
/// **좌우는 반전으로 얻는다.** 오른쪽 걷기·오른쪽 끌림을 따로 그리지 않는다.
/// 부엉이는 정면 대칭이라 반전이 아무 일도 하지 않지만, 옆으로 처진 그림을 넣는
/// 사용자에게는 그대로 먹는다.
enum MascotSprite: String, CaseIterable {
    case idle
    /// 졸림 두 단계. 탈진은 이미 눈을 감고 있어서 감은 얼굴을 따로 두지 않는다.
    case sleepy
    case exhausted
    /// 걷기 두 박자. **옆모습이고 둘 다 왼쪽을 본다.**
    ///
    /// 한동안 두 박자 사이에 "두 발 모음" 한 장을 끼웠는데 **뺐다.** 그 칸은 사실상
    /// 서 있는 자세라, 걸을 때마다 한 박자씩 멈춰 서는 것으로 보였다 — 얼굴이 조금만
    /// 달라도 한 박자마다 표정이 바뀌어서 쉬지 않고 눈을 깜빡이는 것처럼 읽혔다.
    ///
    /// **옆모습이라 반전으로 만들 수 없다.** 뒤집으면 반대쪽을 보게 되므로 두 박자를
    /// 다 그려야 한다. 반전은 이제 **어느 쪽으로 걷나**에만 쓴다.
    case walkA
    case walkB
    /// 졸린 채로 걷기. **눈만 다른 게 아니라 몸도 처져 있다.**
    ///
    /// 평소 걷기를 그대로 쓰면 지쳐 있다는 게 서 있을 때만 보인다 — 걷기 시작하는
    /// 순간 말짱해 보여서 사용량이 줄어든 것처럼 읽힌다.
    /// 탈진에는 이게 없다. 거기서는 아예 걸어다니지 않는다.
    case walkSleepyA
    case walkSleepyB
    /// 뛰기 두 박자. 글자에 쫓겨 비킬 때 돈다.
    ///
    /// **걷기를 빨리 돌리는 것으로는 부족하다.** 자세가 그대로라 종종거리는 것으로만
    /// 보인다 — 몸을 앞으로 기울이고 다리를 크게 벌려야 뛰는 것으로 읽힌다.
    case runA
    case runB
    /// 집어 들렸을 때. **어느 쪽으로 끄는지는 안 본다.**
    ///
    /// 방향마다 그림을 두는 길을 해 봤고 버렸다(`dragSide`·`dragUp`·`dragDown`).
    /// 원래 끌림이 좋았던 건 몸·얼굴·다리가 한 틱씩 늦게 따라오는 시차인데 그건 계산으로만
    /// 나오고, 그림 세 장으로 남는 건 "셋 중 하나 고르기"뿐이다. 그마저도 마우스가
    /// 흔들리면 셋 사이를 왔다 갔다 해서 **매달려 있는 한 장보다 덜 읽혔다.**
    ///
    /// 그 판정을 없애면서 속도 문턱·가로세로 비교·낡은 속도 판정이 통째로 빠졌다.
    case held
    /// 마구 흔들었을 때.
    case dizzy
    /// 앉아 있을 때. **아직 앱이 안 쓴다** — 그림만 미리 받아 둔다.
    ///
    /// 나중에 만들 것이라고 나중에 받으면 **같은 캐릭터가 안 나온다.** AI는 세션이
    /// 바뀌면 화풍도 비율도 재현하지 못한다. 한 번에 받아 두는 편이 싸다.
    case sit
    case blinkSit
    /// **가로** 모서리에 거꾸로 매달렸을 때. 창 아래쪽 테두리에 붙는 자리다.
    case ledge
    /// **세로** 모서리에 옆으로 붙었을 때. 창 좌우 테두리에 붙는 자리다.
    ///
    /// `ledge` 로는 못 쓴다 — 거꾸로 매달린 그림을 90도 돌리면 누워 버린다.
    /// 창은 대개 옆면이 더 길어서 붙을 자리가 오히려 여기에 많다.
    ///
    /// 왼쪽 벽에 붙은 것을 그리고 오른쪽은 반전으로 쓴다. 옆모습이라 반전하면
    /// 반대쪽을 보는데, **벽에 붙을 때는 그게 맞다.**
    case cling
    case blinkCling
    /// 조회가 끊겼거나 주간 한도를 다 썼다.
    case dead

    // 눈을 감은 얼굴. **자세마다 한 장씩 둔다.**
    //
    // 눈만 그린 그림을 위에 겹치는 길을 먼저 해 봤고 **버렸다.** 자세 그림에는 뜬 눈이
    // 이미 그려져 있어서, 감은 눈을 덧그려도 밑에 있는 뜬 눈이 지워지지 않는다 —
    // 두 눈이 겹쳐서 눈꺼풀 아래로 흰자가 삐져나온다.
    //
    // 안 그려 넣으면 그 자세에서 깜빡임만 없어진다. 자세는 그대로다.
    case blink
    case blinkSleepy
    case blinkHeld
    case blinkLedge

    /// 그 그림이 없으면 대신 쓸 것. **한 칸만 그려도 앱이 돌아야 한다.**
    var fallback: MascotSprite? {
        switch self {
        case .idle: return nil
        case .sleepy, .walkA, .held, .dead, .sit:
            return .idle
        case .exhausted: return .sleepy
        // **걷기는 걷기로 떨어진다.** `idle` 로 떨어뜨리면 한 박자마다 선 자세가
        // 끼어들어서, 걷다 말고 멈칫하는 것으로 보인다.
        //
        // 정면 대칭으로 그린 캐릭터는 `walkB` 가 `walkA` 를 뒤집은 것과 같아서
        // 비워도 된다. **자동으로 뒤집어 주지는 않는다** — 옆모습 캐릭터에서
        // 그렇게 하면 반대쪽을 보고 걷는다.
        case .walkB: return .walkA
        // 졸린 걷기를 안 그렸으면 같은 박자의 평소 걷기로. 눈만 말짱해질 뿐 자세는 맞다.
        case .walkSleepyA: return .walkA
        case .walkSleepyB: return .walkSleepyA
        // 뛰기를 안 그렸으면 걷기를 빨리 돌린다. 예전에 하던 그대로다.
        case .runA: return .walkA
        case .runB: return .walkB
        // 매달린 것끼리는 통한다.
        case .cling: return .ledge
        case .blinkCling: return .cling
        case .dizzy: return .held
        case .blinkSit: return .sit
        case .blinkHeld: return .held
        case .blinkLedge: return .ledge
        // 매달린 것끼리는 통한다. 가장자리를 안 그렸으면 들린 모습으로.
        case .ledge: return .held
        // 감은 얼굴이 없으면 뜬 얼굴로 — 그 틱에 눈만 안 감긴다.
        case .blink: return .idle
        case .blinkSleepy: return .sleepy
        }
    }

    /// 칸 안에서 어느 모서리에 붙는지.
    ///
    /// **맞닿는 자리가 곧 정보인 자세가 있다.** 가장자리에 매달린 모습은 위 모서리에
    /// 손이 닿아 있어야 "매달렸다"로 읽히고, 조금이라도 떠 있으면 공중에 뜬 것으로
    /// 보인다. 앉은 모습은 반대로 아래 모서리에 닿아야 한다.
    ///
    /// 나머지는 전부 아래다 — 발이 한 줄에 서야 상태가 바뀔 때 캐릭터가 오르내리지 않는다.
    var anchor: MascotAnchor {
        switch self {
        case .ledge: return .top
        // 붙잡은 쪽이 상자 왼쪽 끝에 닿아야 창 테두리에 붙는다.
        case .cling, .blinkCling: return .leading
        default: return .bottom
        }
    }

    /// 가로 자리를 **머리 기준**으로 맞출지.
    ///
    /// 옆모습 걷기는 다리가 앞뒤로 벌어져서 잉크 상자가 그때그때 넓어진다.
    /// 상자 가운데로 맞추면 몸이 앞뒤로 밀리는데, 머리는 걸음 내내 제자리다.
    ///
    /// 어지러움처럼 **몸이 기운 것이 그림의 뜻인** 자세에는 쓰지 않는다.
    var centersOnHead: Bool {
        switch self {
        case .walkA, .walkB, .walkSleepyA, .walkSleepyB, .runA, .runB:
            return true
        default:
            return false
        }
    }

    /// 기분·자세·걸음에서 그림 한 칸을 고른다.
    ///
    /// **앱과 문서용 GIF가 같은 것을 본다.** 두 곳에 따로 적으면 문서의 부엉이가
    /// 화면의 부엉이와 다른 자세를 하게 되고, 그걸 알아채는 방법이 없다.
    ///
    /// 자세를 만드는 것과 같은 신호에서 나온다 — 격자 부엉이와 그림 마스코트가 서로
    /// 다른 판단을 하면 같은 상황에서 하나는 졸고 하나는 걷는다. 그림 쪽이 훨씬
    /// 성기므로(수백 → 스물) 여기서 뭉뚱그린다.
    @MainActor
    static func resolve(
        mood: OwlMood, pose: OwlPose, gait: OwlGait?, beat: Int, perch: MascotPerch?
    ) -> MascotSprite {
        let base = self.base(mood: mood, pose: pose, gait: gait, beat: beat, perch: perch)
        // **감은 얼굴이 따로 있는 자세만 깜빡인다.** 없으면 뜬 얼굴 그대로다.
        //
        // 완전히 감았을 때만 친다. 그림에는 중간 단계가 없어서, 반쯤 감긴 프레임까지
        // 세면 깜빡임이 두 배 넘게 길어진다 — 잠깐 깜빡이는 게 아니라 질끈 감았다
        // 뜨는 것으로 보인다. 격자 부엉이는 실눈 프레임이 따로 있어서 안 그렇다.
        //
        // 평소가 이미 감긴 얼굴인 기분(탈진)은 깜빡일 것이 없다. 거기서 실눈을 뜨는
        // 것은 감는 게 아니라 뜨는 것이다.
        guard pose.eyes == .closed, mood.frames[0].pose.eyes != .closed,
              let closed = base.blinking else { return base }
        return closed
    }

    /// 눈을 빼고 본 자세.
    @MainActor
    private static func base(
        mood: OwlMood, pose: OwlPose, gait: OwlGait?, beat: Int, perch: MascotPerch?
    ) -> MascotSprite {
        if mood == .offline { return .dead }
        // **붙어 있으면 그 자세가 이긴다.** 붙는 자리는 이 칸의 알맹이로 계산해 둔 것이라
        // (`UsageHUDView.petPerchOrigin`) 다른 칸이 그려지면 자리가 그만큼 어긋난다 —
        // 흔든 뒤 몇 초 동안 옆면에 붙은 부엉이가 26pt 삐져나오는 식이다.
        //
        // 끊김(`offline`)만 이 앞이다. 회색으로 굳어야 할 때 살아 있는 그림이 나오면 안 된다.
        if let perch { return perch.sprite }
        // **기분이 아니라 눈으로 본다.** 끌고 흔드는 동안에는 기분이 `.dragged` 그대로이고
        // (`effectiveMood` 가 끌림을 이기게 해 뒀다) **눈만 풀린다.**
        if pose.eyes == .dizzy { return .dizzy }
        // **어느 쪽으로 끄는지 안 본다.** 그림 한 장이라 볼 것이 없다.
        // (붙어 있으면 위에서 이미 갈렸다 — 끌고 가다 붙을 자리에 닿으면 놓기 전에
        // 그 자세를 미리 잡는다.)
        if mood == .dragged { return .held }
        if let gait { return walk(gait: gait, mood: mood, beat: beat) }
        if mood == .exhausted { return .exhausted }
        if mood == .tired { return .sleepy }
        return .idle
    }

    /// 걸음의 어느 박자인지.
    ///
    /// **두 박자를 번갈아 쓴다** — A 가 다리를 벌린 순간, B 가 다리를 모은 순간이다.
    /// 왼발·오른발 두 장이 아니다. 벌림과 모음을 번갈아 보여주면 보는 사람이 네
    /// 박자로 읽는다. 발만 바꾼 두 장으로는 그 착각이 안 걸려서, 실제로 "발 색만
    /// 바뀐다" 로 보였다.
    ///
    /// 탈진은 여기 오지 않는다 — 그때는 `PetMotion` 이 배회를 끊는다. 쫓겨서 뛸 때만
    /// 걸음이 도는데, 지친 몸으로 도망치는 것이라 졸린 걷기를 그대로 쓴다.
    private static func walk(gait: OwlGait, mood: OwlMood, beat: Int) -> MascotSprite {
        let second = beat % 2 == 1
        // 쫓겨서 뛸 때는 뛰기 그림. 지친 몸으로 도망치는 것이라 졸림보다 앞선다.
        if gait == .run { return second ? .runB : .runA }
        if mood == .tired || mood == .exhausted {
            return second ? .walkSleepyB : .walkSleepyA
        }
        return second ? .walkB : .walkA
    }

    /// 바닥에서 **뜬 만큼을 지킬지.**
    ///
    /// 걸음은 다리가 모이는 순간 몸이 가장 높고, 뛸 때는 두 발이 다 뜨는 순간이 있다.
    /// 그건 그림에 그려 넣는 것이라 칸마다 잉크 바닥을 칸 아래줄에 붙이면 **통째로
    /// 사라진다.** 실제로 뛰기B가 13px 떠 있게 그려져 왔는데 우리가 땅에 눌러 놨다.
    ///
    /// 걷기·뛰기 칸끼리 가장 낮은 것을 땅으로 삼고, 나머지는 그만큼 띄워 놓는다.
    /// 서 있는 자세와 섞지 않는다 — 저쪽은 발이 한 줄에 서야 한다.
    var keepsLift: Bool {
        switch self {
        case .walkA, .walkB, .walkSleepyA, .walkSleepyB, .runA, .runB:
            return true
        default:
            return false
        }
    }

    /// 이 자세에서 눈을 감으면 어느 그림인가. 없으면 깜빡이지 않는다.
    var blinking: MascotSprite? {
        switch self {
        case .idle: return .blink
        case .sleepy: return .blinkSleepy
        case .sit: return .blinkSit
        // **오래 붙잡고 있을 수 있다.** 끌림은 짧다고 빼 뒀었는데, 쥔 채로 한참 두거나
        // 창틀에 매달아 두면 몇 분씩 이어진다 — 그동안 안 깜빡이면 죽은 것으로 보인다.
        case .held: return .blinkHeld
        case .ledge: return .blinkLedge
        case .cling: return .blinkCling
        // 탈진은 이미 감고 있고, 어지러움·죽음은 눈 자체가 정보다.
        //
        // **걸을 때도 안 깜빡인다.** 옆모습이라 눈이 점 하나 크기고, 화면에서는
        // 40pt 남짓으로 줄어서 감았는지 떴는지 보이지 않는다. 그 넉 장을 빼면
        // 그리는 쪽이 맞춰야 할 칸이 넷 줄어든다 — 어긋남이 제일 잦던 자리다.
        default: return nil
        }
    }
}

/// 칸 안에서 캐릭터가 붙는 모서리.
enum MascotAnchor {
    case top
    case bottom
    /// 왼쪽 끝. 세로 모서리에 옆으로 붙는 자세가 쓴다.
    case leading
}

/// 다른 앱 창의 **어느 테두리에** 붙어 있는지.
///
/// **면 하나에서 자세와 반전이 둘 다 나온다.** 붙일 자리를 고르는 쪽(`WindowSurvey`)이
/// 어느 칸을 쓸지까지 정하게 두면, 그림 사정을 창 계산이 알아야 한다. 면만 알려주면
/// 그림에 대한 판단은 여기 한 곳에 남는다.
enum MascotPerch {
    /// 창 **위** 테두리에 앉았다.
    case top
    /// 창 **아래** 테두리에 거꾸로 매달렸다.
    case bottom
    case left
    case right

    var sprite: MascotSprite {
        switch self {
        case .top: return .sit
        case .bottom: return .ledge
        case .left, .right: return .cling
        }
    }

    /// **`cling` 원본은 왼쪽이 벽인 옆모습이다**(`MascotSprite.cling` 주석).
    /// 그래서 창 **오른쪽** 테두리가 원본이고, 왼쪽 테두리일 때 뒤집는다.
    /// 헷갈리기 쉬운 자리라 여기 한 줄로 못 박는다.
    var flipsSprite: Bool { self == .left }
}

/// 한 장에 칸을 나눠 담는 배치.
///
/// **낱장으로 받으면 자리가 어긋난다.** 상태마다 파일을 따로 그리면 캐릭터가 앉은
/// 자리가 조금씩 달라져서 상태가 바뀔 때 마스코트가 튄다. 한 격자 안에 나란히
/// 그리면 그 문제가 구조적으로 사라지고, 그림을 만드는 쪽도 한 번에 그린다.
enum MascotSheet {
    /// 어느 칸에 무엇이 앉는지. **순서를 바꾸지 마라** —
    /// 이 배치로 이미 만들어진 그림이 전부 깨진다.
    ///
    /// **안 그려도 되는 칸은 줄 끝에 몬다.** 그려야 하는 칸이 왼쪽부터 이어져 있어야
    /// 어디까지 그렸는지 눈으로 잡히고, 가운데가 뚫려 있으면 빠뜨린 것처럼 보인다.
    ///
    /// 줄마다 뜻이 있다 — 서 있기 · 움직이기 · 매달리기 · 그 밖.
    /// 남는 세 칸은 상태를 더할 자리다. 나중에 늘리려고 새로 받으면
    /// **같은 캐릭터가 안 나온다.**
    static let layout: [[MascotSprite?]] = [
        [.idle, .blink, .sleepy, .blinkSleepy, .exhausted, nil],
        [.walkA, .walkB, .walkSleepyA, .walkSleepyB, .runA, .runB],
        [.held, .blinkHeld, .ledge, .blinkLedge, .cling, .blinkCling],
        [.sit, .blinkSit, .dizzy, .dead, nil, nil],
    ]

    static var columns: Int { layout[0].count }
    static var rows: Int { layout.count }
    /// `ForEach` 에 그대로 넘길 수 있는 형태. 범위를 직접 넘기면 타입 추론이 엉킨다.
    static var rowIndices: [Int] { Array(0..<rows) }
    static var columnIndices: [Int] { Array(0..<columns) }

    /// 시트 파일 이름. 폴더에 이게 있으면 낱장보다 먼저 본다.
    static let fileName = "mascot.png"

    // MARK: - 정해 놓은 규격

    /// 한 칸의 한 변(픽셀).
    static let canonicalCell = 256
    /// 칸 사이와 바깥에 두르는 선의 굵기(픽셀).
    ///
    /// **선은 어느 칸에도 안 들어간다.** 칸 자리를 이 굵기만큼 밀어 놓았기 때문에,
    /// 그리는 사람이 선을 지우든 남기든 그림에는 아무 영향이 없다. 눈으로 칸 경계를
    /// 볼 수 있으면서 그림은 안 더럽히는 유일한 방법이다.
    static let canonicalRule = 1

    /// 규격 크기. **이 크기(또는 그 정수배)로 그리면 좌표를 따로 안 적어도 된다.**
    ///
    /// 처음에는 그린 그림에서 칸을 찾아내는 쪽을 기본으로 뒀는데 **거꾸로였다.**
    /// 찾아내야 하는 상황은 규격을 안 지킨 그림뿐이고, 규격을 지키게 하려면 크기를 못
    /// 박고 칸을 선으로 갈라 주면 된다. 찾아내는 쪽(`--fit-sheet`)은 구제용으로 남긴다.
    static var canonicalSize: CGSize {
        CGSize(
            width: columns * canonicalCell + (columns + 1) * canonicalRule,
            height: rows * canonicalCell + (rows + 1) * canonicalRule
        )
    }

    /// 규격 좌표. 배율 1이면 `canonicalSize` 기준이다.
    static func canonicalAtlas(multiple: Int = 1) -> MascotAtlas {
        let pitch = (canonicalCell + canonicalRule) * multiple
        let side = canonicalCell * multiple
        let inset = canonicalRule * multiple
        var frames: [String: MascotAtlas.Box] = [:]
        for (row, line) in layout.enumerated() {
            for (column, sprite) in line.enumerated() {
                guard let sprite else { continue }
                frames[sprite.rawValue] = MascotAtlas.Box(
                    x: inset + column * pitch,
                    y: inset + row * pitch,
                    w: side,
                    h: side
                )
            }
        }
        return MascotAtlas(frames: frames)
    }

    /// 이 크기가 규격(또는 그 정수배)이면 몇 배인지. 아니면 nil.
    static func canonicalMultiple(for size: CGSize) -> Int? {
        let base = canonicalSize
        guard base.width > 0, base.height > 0 else { return nil }
        let multiple = Int((size.width / base.width).rounded())
        guard multiple >= 1,
              size.width == base.width * CGFloat(multiple),
              size.height == base.height * CGFloat(multiple)
        else { return nil }
        return multiple
    }

    /// 배치 순서대로 늘어놓은 이름. 좌표를 뽑을 때 읽은 순서와 맞춘다.
    static var readingOrder: [MascotSprite] { layout.flatMap { $0 }.compactMap { $0 } }

    /// 시트를 칸으로 잘라 상태별 그림으로 만든다.
    ///
    /// 읽는 순서는 **적어 둔 좌표 → 규격 좌표 → 균등 분할** 이다.
    /// 규격 크기(`canonicalSize` 또는 그 정수배)로 그렸으면 좌표가 이미 정해져 있어서
    /// 아무것도 찾아내지 않는다.
    static func slice(_ sheet: NSImage, atlas: MascotAtlas? = nil) -> [MascotSprite: NSImage] {
        guard let cg = sheet.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return [:] }
        let size = CGSize(width: cg.width, height: cg.height)
        // 손으로 적어 둔 좌표가 가장 세다. 규격에서 벗어난 그림을 구제하려고 적는 것이라,
        // 우리가 정해 둔 것으로 덮으면 적어 둔 뜻이 없어진다.
        if let atlas {
            let cut = sliceByAtlas(cg, atlas: atlas)
            // 안 맞는 좌표표는 버리고 아래로 내려간다.
            if !cut.isEmpty { return cut }
        }
        // 규격 크기면 정해 둔 좌표를 그대로 쓴다. **찾아낼 것이 없다.**
        if let multiple = canonicalMultiple(for: size) {
            return sliceByAtlas(cg, atlas: canonicalAtlas(multiple: multiple))
        }
        return sliceByGrid(cg)
    }

    /// 이 크기의 그림을 어떻게 읽는지. 화면에 알려줄 때 쓴다.
    static func readingMethod(for size: CGSize, hasAtlas: Bool) -> String {
        if hasAtlas { return "적어 둔 좌표" }
        if canonicalMultiple(for: size) != nil { return "규격 좌표" }
        return "균등 분할"
    }

    /// 적어 둔 좌표대로 자른다.
    private static func sliceByAtlas(_ cg: CGImage, atlas: MascotAtlas) -> [MascotSprite: NSImage] {
        let bounds = CGRect(x: 0, y: 0, width: cg.width, height: cg.height)
        var cuts: [(MascotSprite, CGImage)] = []
        for sprite in MascotSprite.allCases {
            guard let box = atlas.frames[sprite.rawValue] else { continue }
            // CGImage 는 위가 0이라 적어 둔 좌표와 그대로 맞는다.
            let rect = CGRect(x: box.x, y: box.y, width: box.w, height: box.h)
            // **하나라도 그림 밖으로 나가면 이 좌표표는 이 그림 것이 아니다.**
            // 잘라서 쓰면 시트를 바꿔 놓고 옛 좌표를 남겨 뒀을 때 엉뚱한 조각이
            // 들어가는데, 그림이 나오기는 하므로 왜 이상한지 알 길이 없다.
            guard bounds.contains(rect) else { return [:] }
            guard let cut = cg.cropping(to: rect) else { continue }
            cuts.append((sprite, cut))
        }
        return trimTogether(cuts)
    }

    /// 좌표가 없으면 균등 격자로 자른다.
    private static func sliceByGrid(_ cg: CGImage) -> [MascotSprite: NSImage] {
        let cellWidth = cg.width / columns
        let cellHeight = cg.height / rows
        guard cellWidth > 0, cellHeight > 0 else { return [:] }

        var cuts: [(MascotSprite, CGImage)] = []
        for (row, line) in layout.enumerated() {
            for (column, sprite) in line.enumerated() {
                guard let sprite else { continue }
                let rect = CGRect(
                    x: column * cellWidth, y: row * cellHeight,
                    width: cellWidth, height: cellHeight
                )
                guard let cut = cg.cropping(to: rect) else { continue }
                cuts.append((sprite, cut))
            }
        }
        return trimTogether(cuts)
    }

    /// 잘라 낸 칸들에서 **다 같이** 여백을 걷어낸다.
    ///
    /// 모든 칸의 알맹이를 감싸는 상자 하나를 찾아 전부 같은 자리로 다시 자른다.
    /// 그린 사람이 칸 둘레에 남긴 여백이 통째로 빠져서, 여백을 얼마나 뒀든 화면에서
    /// 같은 크기로 나온다.
    ///
    /// **칸마다 따로 가운데를 맞추면 안 된다.** 걸을 때 몸이 좌우로 기우는 것과 들어
    /// 올릴 때 몸이 올라가는 것은 그린 사람이 칸 안에서 자리를 달리해 만든 것이라,
    /// 칸마다 새로 맞추면 그 움직임이 통째로 사라진다.
    ///
    /// 칸 크기가 서로 다르면 "칸 안에서 본 자리"라는 것이 성립하지 않아서 그냥 둔다.
    private static func trimTogether(_ cuts: [(MascotSprite, CGImage)]) -> [MascotSprite: NSImage] {
        // **투명한 칸은 버린다.** 안 그린 자리를 넣으면 그 상태에서 마스코트가 사라진다 —
        // 대체로 떨어지게 두는 편이 낫다.
        let drawn = cuts.compactMap { sprite, cut in
            opaqueBounds(cut).map { (sprite, cut, $0) }
        }
        guard let first = drawn.first else { return [:] }

        let uniform = drawn.allSatisfy {
            $0.1.width == first.1.width && $0.1.height == first.1.height
        }
        let common = uniform ? drawn.dropFirst().reduce(first.2) { $0.union($1.2) } : nil

        var found: [MascotSprite: NSImage] = [:]
        for (sprite, cut, _) in drawn {
            let piece = common.flatMap { cut.cropping(to: $0) } ?? cut
            found[sprite] = NSImage(
                cgImage: piece,
                size: NSSize(width: piece.width, height: piece.height)
            )
        }
        return found
    }

    /// 알파가 있는 픽셀을 감싸는 가장 작은 상자. 통째로 투명하면 nil.
    static func opaqueBounds(_ image: CGImage) -> CGRect? {
        let width = image.width, height = image.height
        guard width > 0, height > 0 else { return nil }
        var pixels = [UInt8](repeating: 0, count: width * height * 4)
        guard let context = CGContext(
            data: &pixels, width: width, height: height, bitsPerComponent: 8,
            bytesPerRow: width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return nil }
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

        var minX = width, minY = height, maxX = -1, maxY = -1
        for y in 0..<height {
            let row = y * width * 4
            for x in 0..<width where pixels[row + x * 4 + 3] > 8 {
                if x < minX { minX = x }
                if x > maxX { maxX = x }
                if y < minY { minY = y }
                if y > maxY { maxY = y }
            }
        }
        guard maxX >= minX, maxY >= minY else { return nil }
        return CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1)
    }
}

/// 칸마다 어느 자리를 읽을지 적어 둔 것. 시트 옆에 `mascot.json` 으로 둔다.
///
/// **그림을 먼저 그리고 좌표를 뒤에 적는다.** 좌표를 먼저 정해 놓고 "여기에 그려라"
/// 하면 그리는 쪽이 그 자리를 못 맞춘다 — 균등 격자를 못 맞추는 것과 같은 이유다.
/// 나온 그림을 보고 자리를 적으면 어긋남이라는 것이 아예 없어진다.
///
/// 좌표는 **그림 픽셀 기준이고 왼쪽 위가 (0, 0)** 이다.
/// 없는 칸은 적지 않는다 — `fallback` 을 타고 내려간다.
struct MascotAtlas: Codable {
    struct Box: Codable {
        var x: Int
        var y: Int
        var w: Int
        var h: Int
    }

    var frames: [String: Box]

    /// 시트 옆에 두는 파일 이름.
    static let fileName = "mascot.json"

    static func load(from url: URL) -> MascotAtlas? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        guard let atlas = try? JSONDecoder().decode(MascotAtlas.self, from: data) else { return nil }
        return atlas.frames.isEmpty ? nil : atlas
    }

    func write(to url: URL) -> Bool {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(self) else { return false }
        return (try? data.write(to: url)) != nil
    }
}

/// 한 캐릭터의 그림 묶음.
///
/// **시트(`mascot.png`)를 먼저 본다.** 없으면 낱장 `<상태>.png` 를 찾는다.
/// 없는 상태는 `fallback` 을 타고 내려가 결국 `idle` 로 떨어지므로,
/// **한 칸만 그려도 동작한다.**
struct MascotSpriteSet {
    private let images: [MascotSprite: NSImage]
    /// 시트에서 읽었는지. 설정 화면에서 무엇을 넣었는지 알려줄 때 쓴다.
    let fromSheet: Bool
    /// 칸 자리를 어떻게 정했는지. 사람에게 그대로 보여준다.
    ///
    /// **제 그림이 이상하게 나올 때 제일 먼저 볼 값이다.** "규격 좌표"가 아니면
    /// 크기가 규격에서 벗어났다는 뜻이고, 그때부터는 칸이 밀렸을 수 있다.
    let readingMethod: String

    /// 가장 큰 칸의 가로·세로(픽셀).
    ///
    /// 그림을 화면 크기에 맞출 때 **묶음 전체에 같은 배율**을 쓰려고 들고 있는다.
    /// 칸마다 제 크기에 맞춰 늘리면, 작게 그린 칸이 큰 칸만큼 부풀어서 상태가 바뀔
    /// 때마다 마스코트가 커졌다 작아졌다 한다.
    let extent: CGSize

    /// 자세마다 그림이 **묶음 상자 어디를 덮는지**(0~1, 위가 0).
    ///
    /// 창 테두리에 붙일 때 쓴다. 매달린 칸은 손이 상자 위쪽에, 앉은 칸은 발이 아래쪽에
    /// 그려져 있어서, 상자를 그대로 테두리에 대면 자세마다 몇십 pt씩 뜬다.
    ///
    /// **상수로 못 박지 않는다.** 사용자가 넣은 그림은 칸 안에서 자리가 또 달라서,
    /// 우리 부엉이에 맞춘 숫자를 박아 두면 남의 그림에서는 반드시 틀린다. 그림에서 잰다.
    let ink: [MascotSprite: CGRect]

    var isEmpty: Bool { images.isEmpty }

    /// 시트 한 장만으로. 번들에 든 기본 마스코트가 이 길로 온다.
    init?(sheetAt url: URL) {
        guard let sheet = NSImage(contentsOf: url) else { return nil }
        let sliced = MascotSheet.slice(sheet)
        guard !sliced.isEmpty else { return nil }
        self.images = sliced
        self.fromSheet = true
        self.readingMethod = MascotSheet.readingMethod(for: Self.pixelSize(of: sheet), hasAtlas: false)
        let extent = Self.extent(of: sliced)
        self.extent = extent
        self.ink = Self.inkFractions(of: sliced, extent: extent)
    }

    /// 칸마다 알맹이가 묶음 상자의 어디에 놓이는지 잰다.
    ///
    /// **`MascotSpriteView` 가 그리는 자리와 같은 셈이어야 한다** — 거기서 칸은 상자
    /// 안에 가로 가운데·세로 바닥으로 놓인다(`frame(alignment: .bottom)`). 칸이 전부
    /// 같은 크기면(여백을 다 같이 걷어낸 보통 경우) 그 보정은 0 이지만, 칸 크기가
    /// 제각각인 좌표표에서는 이걸 빼먹으면 붙는 자리가 칸마다 어긋난다.
    private static func inkFractions(
        of images: [MascotSprite: NSImage], extent: CGSize
    ) -> [MascotSprite: CGRect] {
        guard extent.width > 0, extent.height > 0 else { return [:] }
        var found: [MascotSprite: CGRect] = [:]
        for (sprite, image) in images {
            guard let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil),
                  let box = MascotSheet.opaqueBounds(cg)
            else { continue }
            let offsetX = (extent.width - CGFloat(cg.width)) / 2
            let offsetY = extent.height - CGFloat(cg.height)
            found[sprite] = CGRect(
                x: (offsetX + box.minX) / extent.width,
                y: (offsetY + box.minY) / extent.height,
                width: box.width / extent.width,
                height: box.height / extent.height
            )
        }
        return found
    }

    /// 그 자세가 묶음 상자 안에서 덮는 자리. 없으면 `fallback` 을 타고 내려간다.
    ///
    /// **그림과 같은 칸을 봐야 한다** — `image(_:)` 가 대신 내놓은 칸과 다른 칸을 재면
    /// 실제로 그려진 것과 다른 자리에 붙는다.
    func inkFraction(_ sprite: MascotSprite) -> CGRect? {
        if let box = ink[sprite] { return box }
        var current = sprite.fallback
        while let step = current {
            if let box = ink[step] { return box }
            current = step.fallback
        }
        return nil
    }

    /// 그림의 진짜 픽셀 크기. `NSImage.size` 는 포인트라 레티나 PNG 에서 어긋난다.
    private static func pixelSize(of image: NSImage) -> CGSize {
        guard let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
            return image.size
        }
        return CGSize(width: cg.width, height: cg.height)
    }

    private static func extent(of images: [MascotSprite: NSImage]) -> CGSize {
        CGSize(
            width: images.values.map(\.size.width).max() ?? 1,
            height: images.values.map(\.size.height).max() ?? 1
        )
    }

    /// 그 상태의 그림과, 좌우로 뒤집어 써야 하는지.
    ///
    /// 뒤집기는 **어느 쪽으로 걷나**에만 쓴다. 걸음의 두 박자는 옆모습이라 뒤집어서
    /// 만들 수 없다 — 뒤집으면 반대쪽을 보게 된다.
    func image(_ sprite: MascotSprite) -> (image: NSImage, mirrored: Bool)? {
        if let image = images[sprite] { return (image, false) }
        var current = sprite.fallback
        while let step = current {
            if let image = images[step] { return (image, false) }
            current = step.fallback
        }
        return nil
    }

    /// 넣어 둔 상태들. 어떤 그림이 있는지 보여줄 때 쓴다.
    var available: [MascotSprite] {
        MascotSprite.allCases.filter { images[$0] != nil }
    }
}

/// 앱에 딸려 오는 기본 마스코트를 들고 있는다.
///
/// **뷰의 body에서 불리므로 캐시한다.** 캐시가 없으면 다시 그릴 때마다 디스크를 읽고
/// `NSImage` 를 새로 만든다 — `ClaudeIcon.resolveImage()` 가 같은 이유로 캐시한다.
@MainActor
enum MascotSpriteStore {
    private static var cached: MascotSpriteSet?
    private static var loaded = false

    /// 번들에 구워 둔 규격 시트.
    ///
    /// **부엉이도 파일이다.** 코드로 그리는 갈래를 따로 두면 같은 규격을 두 번
    /// 구현하는 셈이라 둘이 반드시 어긋난다. `build.sh` 가 빌드할 때
    /// `Resources/mascot.png` 를 넣거나, 없으면 격자 부엉이에서 구워 넣는다.
    static var bundled: MascotSpriteSet? {
        if loaded { return cached }
        loaded = true
        cached = Bundle.main
            .url(forResource: "mascot", withExtension: "png")
            .flatMap { MascotSpriteSet(sheetAt: $0) }
        return cached
    }
}

/// 애니메이터를 **지켜보면서** 그림을 갈아끼운다.
///
/// **`@ObservedObject` 가 있어야 한다.** 없이 `animator.spriteState` 를 읽기만 하면
/// 상태는 바뀌는데 뷰가 다시 그려지지 않아서 그림이 처음 것에 멈춰 있는다 —
/// 격자로 그리는 쪽(`AnimatedOwlView`)도 같은 이유로 이 꼴을 하고 있다.
struct AnimatedMascotSpriteView: View {
    @ObservedObject var animator: OwlAnimator
    let set: MascotSpriteSet
    let size: CGFloat
    var widthLimit: CGFloat?

    var body: some View {
        MascotSpriteView(
            set: set,
            sprite: animator.spriteState,
            flipped: animator.spriteFlipped,
            sway: animator.spriteSway,
            testLook: animator.usesTestLook,
            size: size,
            widthLimit: widthLimit
        )
    }
}

/// 그림 한 장을 마스코트 자리에 그린다.
///
/// 링 안에 들어가야 하므로 **정사각 그림이 원을 뚫지 않게** 지름 기준으로 줄인다.
/// 부엉이는 네 귀퉁이가 비어서 그냥 들어갔지만, 사용자 그림은 모서리까지 차 있다.
struct MascotSpriteView: View {
    let set: MascotSpriteSet
    let sprite: MascotSprite
    let flipped: Bool
    /// 몸을 좌우로 미는 양(칸). 걸을 때 뒤뚱거리는 것.
    ///
    /// **그림 전체를 민다.** 파츠를 따로 움직이면 동물마다 자리가 애매해지는데,
    /// "걸을 때 몸이 좌우로 흔들린다"는 어느 동물에나 맞는다.
    var sway: Int = 0
    /// 테스트판 색으로 그릴지. **기본은 번들을 본다** — 렌더 통로만 손으로 꽂는다.
    var testLook: Bool = AppInfo.isTestBuild
    /// 마스코트 자리의 **높이**.
    let size: CGFloat
    /// 옆으로 퍼져도 되는 한계. nil 이면 안 막는다.
    var widthLimit: CGFloat?

    var body: some View {
        if let found = set.image(sprite) {
            let image = found.image
            // 묶음 전체에 배율을 하나만 쓴다. 칸마다 제 크기에 맞춰 늘리면
            // 작게 그린 칸이 큰 칸만큼 부풀어서 상태가 바뀔 때마다 크기가 요동친다.
            let scale = Self.scale(extent: set.extent, height: size, widthLimit: widthLimit)
            let box = CGSize(width: set.extent.width * scale, height: set.extent.height * scale)
            layer(image)
                .frame(width: image.size.width * scale, height: image.size.height * scale)
                // **칸끼리는 바닥을 맞춘다.** 칸마다 높이가 다를 때 가운데로 맞추면
                // 주저앉은 자세에서 발이 공중에 뜬다.
                .frame(width: box.width, height: box.height, alignment: .bottom)
                // 그 묶음을 자리 한가운데에 놓는다. 링 안에서 한쪽으로 쏠리지 않게.
                .frame(width: max(box.width, size), height: size)
                // 거울상으로 얻은 칸은 한 번 더 뒤집는다. 두 번 뒤집히면 제자리다 —
                // 정면 대칭 캐릭터에서만 비워 두므로 어느 쪽이든 맞는다.
                .scaleEffect(x: (flipped != found.mirrored) ? -1 : 1)
                // **뒤집기 바깥에서 민다.** 안쪽에 두면 뒤집힐 때 흔들리는 방향도 뒤집힌다.
                //
                // **위아래로는 안 흔든다.** 걸음의 오르내림은 그림이 담는다 —
                // 걷기B 칸이 걷기A 보다 위에 그려져 있어서, 코드가 또 흔들면 두 번 겹친다.
                .offset(x: CGFloat(sway) * Self.swayUnit(size: size))
                .shadow(color: .black.opacity(0.45), radius: 2, y: 1)
        }
    }

    private func layer(_ image: NSImage) -> some View {
        Image(nsImage: image)
            .resizable()
            .interpolation(Self.interpolation(for: image))
            .scaledToFit()
            // **테스트판은 색을 돌려 놓는다.** 링도 카드도 없는 펫 모드에서는 글자를
            // 붙일 자리가 없어서 정식판과 구분할 방법이 색뿐이다 — 격자 부엉이가
            // 보라색 팔레트로 그 일을 하던 것을, 그림 마스코트가 기본이 되면서
            // 여기가 이어받는다. 색을 정해 칠하지 않고 돌리는 이유는 **어떤 그림이
            // 들어올지 모르기 때문이다.** 사용자 그림에도 그대로 먹는다.
            .hueRotation(.degrees(testLook ? 42 : 0))
    }

    /// 원본이 픽셀 그림인지 아닌지에 따라 확대 방식을 바꾼다.
    ///
    /// **한쪽으로 못 박으면 반드시 한쪽이 망가진다.**
    /// 작은 픽셀 그림을 부드럽게 늘리면 칸 경계가 번져서 픽셀 아트가 아니게 되고,
    /// 큰 그림(사진·정교한 일러스트)을 최근접으로 늘이면 계단이 그대로 드러난다.
    ///
    /// 가르는 기준은 **원본 크기**다. 화면에서 가장 크게 쓰는 곳이 펫 모드 126pt(2x로
    /// 252px)라, 그보다 훨씬 작게 그렸다면 확대해서 쓰라고 만든 픽셀 그림으로 본다.
    static func interpolation(for image: NSImage) -> Image.Interpolation {
        let side = max(image.size.width, image.size.height)
        return side <= pixelArtLimit ? .none : .high
    }

    /// 이 아래는 픽셀 그림으로 친다. 부엉이를 뽑은 것이 120×104 라 넉넉히 위에 둔다.
    static let pixelArtLimit: CGFloat = 160

    /// 한 칸만큼 미는 거리.
    ///
    /// 부엉이 격자가 세로 13칸이라 한 칸이 높이의 1/13이다. 그림 마스코트에는 격자가
    /// 없으므로 **높이에 대한 비율**로 옮겨 놓는다 — 어느 크기에서도 같은 만큼 흔들린다.
    static func swayUnit(size: CGFloat) -> CGFloat { size / 13 }

    /// 그림을 화면에 올릴 배율.
    ///
    /// **높이를 먼저 맞춘다.** 격자 부엉이가 같은 값을 높이로 받으므로, 이래야 부엉이를
    /// 그림으로 뽑아 넣었을 때 크기가 그대로다. 예전에는 가장 긴 변을 맞췄는데,
    /// 부엉이처럼 가로가 더 긴 그림은 그만큼 작아졌다(84 → 72.8).
    ///
    /// 폭 제한이 있으면 거기에 맞춰 한 번 더 줄인다.
    static func scale(extent: CGSize, height: CGFloat, widthLimit: CGFloat?) -> CGFloat {
        let byHeight = height / max(extent.height, 1)
        guard let widthLimit else { return byHeight }
        return min(byHeight, widthLimit / max(extent.width, 1))
    }
}
