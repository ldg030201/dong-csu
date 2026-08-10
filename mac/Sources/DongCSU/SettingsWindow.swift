import AppKit
import SwiftUI

/// 설정 창 왼쪽의 탭.
///
/// 항목이 한 화면에 다 들어가던 시절에는 세로로 이어 붙였지만, 아이콘 묶음이
/// 늘어나면서 창이 너무 길어졌다. 화면 자체를 나눠 두면 캐릭터를 더 만들어도
/// 아이콘 탭만 길어진다.
enum SettingsTab: String, CaseIterable, Identifiable {
    case status
    case measure
    case display
    case icon
    case pet
    case account
    case version

    var id: String { rawValue }

    var title: String {
        switch self {
        case .status: return "상태"
        case .measure: return "측정"
        case .display: return "표시"
        case .icon: return "아이콘"
        case .pet: return "펫"
        case .account: return "계정"
        case .version: return "버전"
        }
    }

    var symbol: String {
        switch self {
        case .status: return "gauge"
        case .measure: return "stopwatch"
        case .display: return "slider.horizontal.3"
        case .icon: return "face.smiling"
        case .pet: return "pawprint"
        case .account: return "person.crop.circle"
        case .version: return "arrow.down.circle"
        }
    }
}

/// 설정 창의 내용.
struct SettingsView: View {
    static let sidebarWidth: CGFloat = 124
    static let contentWidth: CGFloat = 356
    /// **처음 열릴 때의 크기이자, 내용이 스크롤 없이 다 들어가는 최소 크기.**
    /// 높이는 가장 긴 탭(표시)이 기준이다. 탭에 항목을 더했으면
    /// `--render-settings`로 재어 보고 여기를 함께 올린다.
    ///
    /// 표시 탭은 평소 582이고, 로그인 항목을 시스템 설정에서 꺼 두면 안내 한 줄이
    /// 더 붙는다. **그 상태까지 들어가는 값**이라 평소에는 아래가 조금 빈다 —
    /// 드물게 뜨는 줄이 잘려서 버튼을 못 누르는 것보다 낫다.
    ///
    /// 창은 이보다 **키울 수도 줄일 수도** 있다. 키우면 내용이 따라 늘어나고,
    /// 줄이면 스크롤이 생긴다. 그 규칙은 `body`에 있다.
    static let size = CGSize(width: sidebarWidth + 1 + contentWidth, height: 604)

    @ObservedObject var settings: HUDSettings
    @ObservedObject var store: UsageStore
    @ObservedObject var updates: UpdateChecker
    @ObservedObject var meter: UsageMeter
    let actions: SettingsActions
    let version: String
    /// 미리보기 렌더는 ScrollView 안을 그리지 못한다. 그럴 때는 스크롤을 벗겨서
    /// 내용이 잘리더라도 보이게 한다.
    var isPreviewRender = false

    /// 초기화는 되돌릴 수 없어서 한 번 더 묻는다.
    @State private var isConfirmingReset = false

    /// 팝업으로 펼쳐 볼 지난 측정. nil이면 팝업이 없다.
    @State private var selectedRecord: UsageMeter.Record?

    /// 어느 탭이 열려 있는지. 메뉴에서 "변경 내역…"을 누르면 창 밖에서 바꾸므로
    /// 뷰 안의 @State가 아니라 설정 객체가 들고 있다.
    private var tab: SettingsTab { settings.settingsTab }

    init(
        settings: HUDSettings,
        store: UsageStore,
        updates: UpdateChecker,
        meter: UsageMeter,
        actions: SettingsActions,
        version: String,
        initialTab: SettingsTab? = nil,
        isPreviewRender: Bool = false
    ) {
        self.settings = settings
        self.store = store
        self.updates = updates
        self.meter = meter
        self.actions = actions
        self.version = version
        self.isPreviewRender = isPreviewRender
        if let initialTab { settings.settingsTab = initialTab }
    }

    /// 창 크기를 따라간다.
    ///
    /// **키우면 늘어나고 줄이면 스크롤이 생긴다.** 둘 다 되게 하려면 창에서 받은 크기와
    /// 내용의 최소 크기 중 **큰 쪽**을 내용에 준다 — 창이 크면 그 크기가 이겨서 꽉 차고,
    /// 창이 작으면 최소 크기가 이겨서 넘치는 만큼 스크롤이 생긴다.
    ///
    /// `ScrollView` 만으로는 안 된다. 스크롤 방향으로는 크기를 제안하지 않아서, 안에
    /// `maxWidth: .infinity` 를 줘도 내용은 제 크기에 머문다 — 창만 커지고 알맹이는
    /// 가운데 고정된 채 둘레만 비는 게 그래서 생긴다.
    var body: some View {
        GeometryReader { proxy in
            ScrollView([.horizontal, .vertical]) {
                content(viewportHeight: proxy.size.height)
                    // **폭은 못 박는다.** 가로 스크롤이 열려 있으면 폭 제안이 무한이라,
                    // `minWidth` 만 주면 긴 문장이 줄바꿈하지 않고 한 줄로 뻗는다.
                    // 그러면 창을 열 때마다 가로 스크롤이 생긴다(2.1.3 에서 그랬다).
                    // 창이 커지면 이 값도 같이 커져서 내용이 따라 늘어난다.
                    .frame(width: max(Self.size.width, proxy.size.width), alignment: .topLeading)
                    // 높이는 최소만 준다. 내용이 길면 넘쳐서 세로로 스크롤되어야 한다.
                    .frame(minHeight: max(Self.size.height, proxy.size.height), alignment: .topLeading)
            }
        }
    }

    /// 스크롤 밖의 알맹이. 미리보기 렌더는 ScrollView를 그리지 못해서 이걸 직접 그린다.
    var content: some View { content(viewportHeight: Self.size.height) }

    private func content(viewportHeight: CGFloat) -> some View {
        HStack(spacing: 0) {
            sidebar
            Divider()
            VStack(spacing: 0) {
                VStack(alignment: .leading, spacing: 16) {
                    Text(tab.title)
                        .font(.system(size: 15, weight: .semibold))
                    tabBody(viewportHeight: viewportHeight)
                    Spacer(minLength: 0)
                }
                .padding(18)
                // 남는 폭·높이를 가져간다. 사이드바만 고정이고 본문은 창을 따라 늘어난다.
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)

                Divider()
                footer
            }
        }
        // 미리보기 렌더에는 창이 없어서 폭을 정해 줘야 한다. 높이는 풀어 두고 전부 그린다.
        .frame(width: isPreviewRender ? Self.size.width : nil, alignment: .topLeading)
    }

    // MARK: - 안에서 따로 스크롤하는 목록

    /// 탭 본문이 쓸 수 있는 높이. 창 높이에서 탭 밖의 고정된 부분을 뺀 값이다.
    ///
    /// **목록을 안에서 따로 넘겨보게 하려면 위쪽 어딘가의 높이가 정해져 있어야 한다.**
    /// 바깥 스크롤은 세로로 높이를 무한히 제안해서, 그 아래에서는 `maxHeight: .infinity`
    /// 가 "남는 만큼"이 아니라 "내용만큼"으로 풀린다 — 목록이 제 길이대로 늘어나고
    /// 창이 아래로 길어진다.
    ///
    /// 목록이 시작하는 자리를 재서(GeometryReader + preference) 맞추는 길을 먼저 해 봤고
    /// **버렸다.** 값이 레이아웃이 끝난 뒤에야 돌아와서 그 판에는 반영되지 않는다.
    /// `--probe-layout` 이 그때도 창이 그대로 늘어나는 것을 잡아냈다.
    private func tabBodyHeight(_ viewportHeight: CGFloat) -> CGFloat {
        max(Self.size.height, viewportHeight) - Self.chromeHeight
    }

    /// 탭 본문 밖에 늘 붙어 있는 것 — 제목·여백·구분선·바닥줄.
    ///
    /// **넉넉하게 잡는다.** 모자라게 잡으면 몇 픽셀이 넘쳐서 쓰지도 않을 스크롤 막대가
    /// 뜨는데, 넉넉하면 아래가 조금 빌 뿐이다.
    private static let chromeHeight: CGFloat = 148

    /// 재는 중일 때 기록 목록에 주는 높이.
    ///
    /// 그때는 위쪽 살아 있는 값이 자리를 거의 다 써서, 남는 만큼을 계산하면 음수가 된다.
    /// 정해진 만큼만 주고 넘치는 것은 바깥 스크롤이 받는다 — **기록이 몇 개든 창이
    /// 늘어나는 양은 그대로**이고, 그게 이 변경에서 없애려던 것이다.
    private static let runningHistoryHeight: CGFloat = 168


    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 2) {
            ForEach(SettingsTab.allCases) { item in
                Button {
                    settings.settingsTab = item
                } label: {
                    HStack(spacing: 7) {
                        Image(systemName: item.symbol)
                            .font(.system(size: 12))
                            .frame(width: 16)
                        Text(item.title)
                            .font(.system(size: 12))
                        Spacer(minLength: 0)
                    }
                    .padding(.horizontal, 8)
                    .padding(.vertical, 6)
                    .background {
                        RoundedRectangle(cornerRadius: 6, style: .continuous)
                            .fill(tab == item ? Color.accentColor.opacity(0.20) : .clear)
                    }
                    .foregroundStyle(tab == item ? Color.primary : Color.secondary)
                    // 배경이 없는 부분도 눌리게 한다.
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            Spacer(minLength: 0)
        }
        .padding(8)
        .frame(width: Self.sidebarWidth, alignment: .top)
        .background(Color(nsColor: .underPageBackgroundColor).opacity(0.5))
    }

    @ViewBuilder
    private func tabBody(viewportHeight: CGFloat) -> some View {
        switch tab {
        case .status: statusSection
        case .measure: measureSection(viewportHeight: viewportHeight)
        case .display: displaySection
        case .icon: iconSection
        case .pet: petSection
        case .account: accountSection
        case .version: versionSection(viewportHeight: viewportHeight)
        }
    }

    // MARK: - 현재 상태

    private var statusSection: some View {
        // 초기화까지 남은 시간과 조회 카운트다운이 있어서 초 단위로 다시 그린다.
        TimelineView(.periodic(from: .now, by: 1)) { context in
            VStack(alignment: .leading, spacing: 14) {
                HStack(spacing: 8) {
                    Text(store.snapshot?.planName ?? "Claude")
                        .font(.system(size: 15, weight: .semibold))
                    Spacer()
                    if store.needsReauth {
                        Label("재로그인 필요", systemImage: "exclamationmark.triangle.fill")
                            .font(.system(size: 11, weight: .medium))
                            .foregroundStyle(.orange)
                    } else if store.isStale {
                        Label("오래된 값", systemImage: "clock.arrow.circlepath")
                            .font(.system(size: 11, weight: .medium))
                            .foregroundStyle(.orange)
                    }
                }

                usageColumn(title: "세션 (5시간)", window: store.snapshot?.fiveHour, now: context.date)
                usageColumn(title: "주간 (7일)", window: store.snapshot?.sevenDay, now: context.date)

                Divider()

                VStack(alignment: .leading, spacing: 4) {
                    statusRow(title: "마지막 조회", value: fetchedText(now: context.date))
                    statusRow(title: "다음 조회", value: nextPollText(now: context.date))
                    statusRow(title: "조회 주기", value: settings.pollInterval.title)
                }

                if let error = store.errorText {
                    Text(error)
                        .font(.system(size: 11))
                        .foregroundStyle(.orange)
                        .fixedSize(horizontal: false, vertical: true)
                }

                Button(store.isRefreshing ? "조회 중…" : "새로고침", action: actions.refresh)
                    .disabled(store.isRefreshing)
            }
        }
    }

    private func usageColumn(title: String, window: UsageWindow?, now: Date) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(title)
                .font(.system(size: 10, weight: .semibold))
                .foregroundStyle(.secondary)
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(window.map { "\(Int($0.utilization.rounded()))%" } ?? "—")
                    .font(.system(size: 20, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(UsageColor.color(for: window?.utilization ?? 0))
                Text(RemainingTime.text(until: window?.resetsAt, now: now))
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
        }
    }

    private func statusRow(title: String, value: String) -> some View {
        HStack(spacing: 8) {
            Text(title)
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
            Spacer(minLength: 12)
            Text(value)
                .font(.system(size: 11).monospacedDigit())
        }
    }

    private func fetchedText(now: Date) -> String {
        guard let fetchedAt = store.snapshot?.fetchedAt else { return "아직 없음" }
        return RemainingTime.ageText(since: fetchedAt, now: now)
    }

    private func nextPollText(now: Date) -> String {
        guard let next = store.nextPollDate else { return "멈춤" }
        guard next.timeIntervalSince(now) > 0 else { return "곧" }
        return "\(RemainingTime.clockText(until: next, now: now)) 뒤"
    }

    // MARK: - 측정

    /// 시작~중지 사이에 얼마나 썼는지.
    ///
    /// **두 숫자가 재는 범위가 다르다.** 한도 %p는 클로드 앱·웹까지 포함한 계정 전체이고,
    /// 토큰은 Claude Code 것뿐이다. 어느 쪽이 무엇을 재는지 밝혀 두지 않으면 두 값이
    /// 안 맞는 것을 버그로 읽는다.
    private func measureSection(viewportHeight: CGFloat) -> some View {
        // 재는 중에는 경과 시간이 초 단위로 움직인다.
        TimelineView(.periodic(from: .now, by: 1)) { context in
            VStack(alignment: .leading, spacing: 14) {
                // **중지하면 그 자리에서 끝난다.** 멈춘 측정을 위에 계속 펼쳐 두면
                // 다음에 열었을 때 재고 있는 것처럼 보이고, 같은 값이 아래 기록에도
                // 있어서 두 번 잰 것으로 읽힌다. 끝난 것은 기록에서 본다.
                if meter.isRunning {
                    measureHeader(now: context.date)
                    Divider()
                    measureLimits
                    Divider()
                    measureTokens
                    Divider()
                }

                measureControls(now: context.date)

                if !meter.state.history.isEmpty {
                    Divider()
                    measureHistory(viewportHeight: viewportHeight)
                }
            }
        }
        // 탭을 열자마자, 그리고 열어 둔 동안에는 자주 다시 센다. 앱이 평소에 도는
        // 주기(1분)는 창을 안 볼 때 기준이라, 보고 있는 동안에는 숫자가 멈춘 것처럼 보인다.
        // 덧붙은 부분만 읽어서 값이 싸다.
        .onAppear { meter.scanTokens() }
        .onReceive(Timer.publish(every: 5, on: .main, in: .common).autoconnect()) { _ in
            meter.scanTokens()
        }
        .sheet(item: $selectedRecord) { record in
            measureRecordSheet(record)
        }
    }

    /// 재는 중에만 그린다.
    private func measureHeader(now: Date) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(RemainingTime.elapsedText(meter.elapsed(now: now) ?? 0))
                .font(.system(size: 21, weight: .bold, design: .rounded))
                .monospacedDigit()

            if meter.isPaused {
                Label("일시정지", systemImage: "pause.circle")
                    .font(.system(size: 11, weight: .medium))
                    .foregroundStyle(.orange)
            } else {
                Label("재는 중", systemImage: "record.circle")
                    .font(.system(size: 11, weight: .medium))
                    .foregroundStyle(.red)
            }
            Spacer(minLength: 0)
        }
    }

    private var measureLimits: some View {
        VStack(alignment: .leading, spacing: 5) {
            measureLabel("한도 소모", note: "클로드 앱·웹 포함")

            if meter.tracksInOrder.isEmpty {
                // 시작하면 곧바로 조회가 나간다. 여기 오래 머물면 조회가 실패한 것이다.
                Text(store.errorText.map { "조회 실패: \($0)" } ?? "기준점을 잡는 중…")
                    .font(.system(size: 11))
                    .foregroundStyle(store.errorText == nil ? Color.secondary : Color.orange)
            } else {
                limitRows(meter.tracksInOrder)
            }
        }
    }

    /// 한도 줄들. 지금 재는 것과 지난 기록이 같은 모양으로 보이게 함수로 뺐다.
    @ViewBuilder
    private func limitRows(_ tracks: [UsageMeter.LimitTrack]) -> some View {
        ForEach(Array(tracks.enumerated()), id: \.offset) { _, track in
            HStack(spacing: 8) {
                Text(track.title)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                Spacer(minLength: 12)
                if track.resets > 0 {
                    // 리셋을 넘겨서도 계속 쌓았다는 표시. 이게 없으면 창이 새로
                    // 열린 뒤의 값이 왜 이렇게 큰지 알 수 없다.
                    Text("리셋 \(track.resets)회 넘김")
                        .font(.system(size: 10))
                        .foregroundStyle(.tertiary)
                }
                Text(String(format: "%.0f%%p", track.accumulated))
                    .font(.system(size: 12, weight: .semibold).monospacedDigit())
            }
        }
    }

    private var measureTokens: some View {
        VStack(alignment: .leading, spacing: 5) {
            measureLabel("토큰", note: "Claude Code만")

            if !ClaudeCodeUsage.isAvailable {
                Text("Claude Code 기록을 찾지 못했다")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            } else if meter.state.tokens.isEmpty {
                Text("아직 없음")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            } else {
                tokenRows(meter.state.tokens, byModel: meter.state.tokensByModel)
            }
        }
    }

    /// 토큰 줄들. 한도 쪽과 같은 이유로 함수로 뺐다.
    @ViewBuilder
    private func tokenRows(_ tokens: TokenTally, byModel: [String: TokenTally]) -> some View {
        // **단위를 적는다.** `입력 4` 만 있으면 네 번 물었다는 뜻으로 읽힌다.
        // 횟수인 것은 응답 하나뿐이다.
        measureRow("응답", "\(TokenFormat.exact(tokens.responses))건")
        measureRow("입력", "\(TokenFormat.short(tokens.input)) 토큰")
        measureRow("출력", "\(TokenFormat.short(tokens.output)) 토큰")
        measureRow("캐시 생성", "\(TokenFormat.short(tokens.cacheCreation)) 토큰")
        measureRow("캐시 읽기", "\(TokenFormat.short(tokens.cacheRead)) 토큰")

        Divider().padding(.vertical, 2)
        // **네 값을 그냥 더한 것이다.** 단가가 서로 달라서 이 숫자가 곧 요금이나
        // 한도 소모량은 아니다 — 그건 위의 %p 가 답한다.
        measureRow("합계", "\(TokenFormat.short(tokens.total)) 토큰")
        // **캐시가 합계를 가린다.** 캐시 읽기가 보통 전체의 90% 넘게 차지해서, 합계만
        // 보면 실제로 주고받은 양을 짐작할 수 없다. 오간 글에 해당하는 쪽을 따로 둔다.
        measureRow("캐시 제외", "\(TokenFormat.short(tokens.withoutCache)) 토큰")

        if byModel.count > 1 {
            Divider().padding(.vertical, 2)
            measureLabel("모델별", note: "합계")
            ForEach(byModel.sorted { $0.value.total > $1.value.total }, id: \.key) { model, tally in
                measureRow(model, "\(TokenFormat.short(tally.total)) 토큰", emphasised: false)
            }
        }
    }

    private func measureControls(now: Date) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                if meter.isPaused {
                    Button("계속", action: meter.resume)
                    Button("중지", action: meter.stop)
                } else if meter.isRunning {
                    Button("일시정지", action: meter.pause)
                    Button("중지", action: meter.stop)
                } else {
                    // 중지한 것은 기록으로 넘어갔다. 여기는 늘 새로 시작하는 자리다.
                    Button("시작", action: meter.start)
                }
                Spacer(minLength: 0)
                if meter.isRunning {
                    Text(measureSampleText(now: now))
                        .font(.system(size: 10))
                        .foregroundStyle(.tertiary)
                }
            }

            Text("한도는 조회할 때(\(settings.pollInterval.title)) 갱신된다. "
                 + "서버가 정수 %로 줘서 1%p 아래는 안 잡힌다. "
                 + "중지하면 아래 기록에 남는다.")
                .font(.system(size: 10))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: - 측정 기록

    /// 끝난 측정 목록. 누르면 그때 값을 그대로 펼쳐 본다.
    ///
    /// 50개까지 쌓이므로 **목록 안에서 따로 넘겨본다.** 그대로 늘어놓으면 창이 그만큼
    /// 길어져서 시작 버튼이 화면 밖으로 밀려난다.
    private func measureHistory(viewportHeight: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                measureLabel("측정 기록", note: "최신 순")
                Spacer(minLength: 8)
                Button("지우기") { meter.clearHistory() }
                    .buttonStyle(.plain)
                    .font(.system(size: 10))
                    .foregroundStyle(.secondary)
            }

            if isPreviewRender {
                historyRows
            } else {
                ScrollView { historyRows }
                    .frame(height: historyHeight(viewportHeight))
            }
        }
    }

    private var historyRows: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(meter.state.history) { record in
                Button { selectedRecord = record } label: { measureHistoryRow(record) }
                    .buttonStyle(.plain)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// 기록 목록에 줄 높이. 재고 있지 않으면 화면에 기록밖에 없으니 남는 자리를 다 가진다.
    private func historyHeight(_ viewportHeight: CGFloat) -> CGFloat {
        guard !meter.isRunning else { return Self.runningHistoryHeight }
        // 위에 시작 버튼 줄과 안내 문구가 있다.
        return max(Self.runningHistoryHeight, tabBodyHeight(viewportHeight) - 110)
    }

    private func measureHistoryRow(_ record: UsageMeter.Record) -> some View {
        HStack(spacing: 8) {
            VStack(alignment: .leading, spacing: 1) {
                Text(Self.recordFormatter.string(from: record.stoppedAt))
                    .font(.system(size: 11))
                Text(RemainingTime.elapsedText(record.duration))
                    .font(.system(size: 10))
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 12)
            VStack(alignment: .trailing, spacing: 1) {
                Text(measureHeadline(record))
                    .font(.system(size: 11, weight: .medium).monospacedDigit())
                Text("\(TokenFormat.short(record.tokens.total)) 토큰")
                    .font(.system(size: 10).monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            Image(systemName: "chevron.right")
                .font(.system(size: 9, weight: .semibold))
                .foregroundStyle(.tertiary)
        }
        .padding(.vertical, 3)
        // 글자가 없는 자리도 눌리게 한다.
        .contentShape(Rectangle())
    }

    /// 목록에 한 줄로 요약할 값. 세션이 있으면 그것, 없으면 첫 한도.
    private func measureHeadline(_ record: UsageMeter.Record) -> String {
        guard let track = record.tracks.first else { return "—" }
        return String(format: "%@ %.0f%%p", track.title, track.accumulated)
    }

    private func measureRecordSheet(_ record: UsageMeter.Record) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 2) {
                Text(Self.recordFormatter.string(from: record.stoppedAt))
                    .font(.system(size: 15, weight: .semibold))
                Text("\(RemainingTime.elapsedText(record.duration)) 동안 · 표본 \(record.samples)회")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Divider()
            VStack(alignment: .leading, spacing: 5) {
                measureLabel("한도 소모", note: "클로드 앱·웹 포함")
                if record.tracks.isEmpty {
                    Text("잡힌 표본이 없다")
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                } else {
                    limitRows(record.tracks)
                }
            }

            Divider()
            VStack(alignment: .leading, spacing: 5) {
                measureLabel("토큰", note: "Claude Code만")
                if record.tokens.isEmpty {
                    Text("없음")
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                } else {
                    tokenRows(record.tokens, byModel: record.tokensByModel)
                }
            }

            HStack {
                Spacer()
                Button("닫기") { selectedRecord = nil }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(18)
        .frame(width: 320)
    }

    private static let recordFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "ko_KR")
        formatter.dateFormat = "M월 d일 (E) HH:mm"
        return formatter
    }()

    private func measureSampleText(now: Date) -> String {
        guard let last = meter.state.lastSampledAt else { return "표본 없음" }
        return "표본 \(meter.state.samples)회 · \(RemainingTime.ageText(since: last, now: now))"
    }

    private func measureLabel(_ title: String, note: String) -> some View {
        HStack(spacing: 6) {
            Text(title)
                .font(.system(size: 10, weight: .semibold))
                .foregroundStyle(.secondary)
            Text(note)
                .font(.system(size: 10))
                .foregroundStyle(.tertiary)
        }
    }

    private func measureRow(_ title: String, _ value: String, emphasised: Bool = true) -> some View {
        HStack(spacing: 8) {
            Text(title)
                .font(.system(size: 11))
                .foregroundStyle(emphasised ? Color.secondary : Color.secondary.opacity(0.75))
            Spacer(minLength: 12)
            Text(value)
                .font(.system(size: 11.5, weight: emphasised ? .medium : .regular).monospacedDigit())
        }
    }

    // MARK: - 표시 설정

    private var displaySection: some View {
        VStack(alignment: .leading, spacing: 12) {
            Picker("테마", selection: $settings.appearance) {
                ForEach(HUDAppearance.allCases, id: \.self) { value in
                    Text(value.title).tag(value)
                }
            }

            Picker("크기", selection: $settings.scale) {
                ForEach(HUDScale.allCases, id: \.self) { value in
                    Text(value.title).tag(value)
                }
            }

            Picker("조회 주기", selection: $settings.pollInterval) {
                ForEach(PollInterval.allCases, id: \.self) { value in
                    Text(value.title).tag(value)
                }
            }

            Picker("펼침 방향", selection: $settings.expandSide) {
                ForEach(HUDExpandSide.allCases, id: \.self) { value in
                    Text(value.title).tag(value)
                }
            }

            VStack(alignment: .leading, spacing: 2) {
                HStack {
                    Text("배경 불투명도")
                    Spacer()
                    Text("\(Int((settings.backdropOpacity * 100).rounded()))%")
                        .font(.system(size: 11).monospacedDigit())
                        .foregroundStyle(.secondary)
                }
                Slider(
                    value: $settings.backdropOpacity,
                    in: HUDSettings.minOpacity...HUDSettings.maxOpacity
                )
            }

            Toggle("아래 줄에 CPU·메모리 표시", isOn: $settings.showsProcessStats)
                .disabled(!settings.isHUDVisible || settings.mode != .expanded)

            Toggle(versionBadgeTitle, isOn: $settings.showsVersionBadge)
                .disabled(!settings.isHUDVisible)

            Toggle("HUD 표시", isOn: $settings.isHUDVisible)
            // 펫은 여기 넣지 않는다. 펫 탭이 따로 있고, 접기와 한 줄에 묶어 두면
            // 접으려다 펫으로 넘어가는 것과 같은 혼란이 설정 창에도 생긴다.
            Toggle("접어서 링만 보기", isOn: Binding(
                get: { settings.mode == .collapsed },
                set: { settings.mode = $0 ? .collapsed : .expanded }
            ))
            .disabled(!settings.isHUDVisible)

            HStack {
                Button("위치 초기화", action: actions.resetPosition)
                    .disabled(!settings.isHUDVisible)
                Text("HUD는 드래그로 옮길 수 있고, 더블클릭하면 보기가 넘어간다.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Divider().padding(.vertical, 2)

            Toggle("로그인할 때 자동 시작", isOn: $settings.startsAtLogin)

            // 시스템 설정에서 꺼 버린 경우. 여기서 다시 켜지지 않으니 그쪽으로 보낸다.
            if LoginItem.needsSystemSettings {
                HStack(spacing: 6) {
                    Text("시스템 설정에서 꺼 두셨습니다.")
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                    Button("로그인 항목 열기", action: LoginItem.openSystemSettings)
                        .controlSize(.small)
                }
            }

            Divider().padding(.vertical, 2)

            HStack(spacing: 8) {
                Button("모든 설정 초기화") { isConfirmingReset = true }
                Text("창 위치·크기·아이콘·펫 설정을 전부 처음 상태로 되돌린다.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .confirmationDialog(
                "모든 설정을 초기화할까요?",
                isPresented: $isConfirmingReset,
                titleVisibility: .visible
            ) {
                Button("초기화", role: .destructive) {
                    settings.resetAll()
                    // 창 위치는 설정 객체가 아니라 패널이 들고 있다. 함께 되돌린다.
                    actions.resetPosition()
                }
                Button("취소", role: .cancel) {}
            } message: {
                Text("되돌릴 수 없습니다. 로그인할 때 자동 시작도 함께 꺼집니다.")
            }
        }
    }

    /// 버전 딱지는 펼친 보기의 왼쪽 위에만 붙는다. 접힌 카드는 자리가 없고,
    /// 펫에는 붙일 배경이 없다 — 거기서는 마스코트 색이 테스트판인지 알려준다.
    private var versionBadgeTitle: String {
        AppInfo.isTestBuild
            ? "왼쪽 위에 버전 표시 (테스트판은 test)"
            : "왼쪽 위에 버전 표시"
    }

    // MARK: - 아이콘

    /// 아이콘을 실제로 그려서 보여주고 고르게 한다.
    /// 출처가 다른 그림이 섞이지 않도록 묶음별로 나눠 놓는다.
    // MARK: - 펫

    private var petSection: some View {
        VStack(alignment: .leading, spacing: 14) {
            Toggle("펫 모드", isOn: Binding(
                get: { settings.mode == .pet },
                set: { on in
                    // 펫에서 나올 때는 들어가기 직전의 보기로 돌아간다.
                    if on {
                        settings.modeBeforePet = settings.mode
                        settings.mode = .pet
                    } else if settings.mode == .pet {
                        settings.mode = settings.modeBeforePet
                    }
                }
            ))
            .disabled(!settings.isHUDVisible)

            Text("배경과 숫자를 걷어내고 마스코트만 띄운다. HUD의 마스코트를 더블클릭해도 들어간다.")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Divider()

            Picker("사용량 링", selection: $settings.petRingDisplay) {
                ForEach(PetRingDisplay.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            // 펫 뒤에만 두르는 링이라 펫 모드가 아니면 고를 것이 없다.
            .disabled(!settings.isHUDVisible || settings.mode != .pet)

            Text("펫 뒤에 두르는 이중 링이다. 바깥이 5시간 세션, 안쪽이 7일 주간.")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Divider()
            motionSection
        }
    }

    /// 펫이 스스로 움직이는 것들. 전부 펫 모드에서만 돈다 —
    /// 숫자가 붙은 카드가 혼자 걸어다니면 읽으려던 값이 도망간다.
    private var motionSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            sectionTitle("스스로 움직이기 (펫 모드에서만)")

            Toggle("혼자 돌아다니기", isOn: $settings.petWanders)
            petNote("가만히 두면 화면을 천천히 걸어다닌다. 글을 쓰는 동안에는 멈춘다.")

            Toggle("커서 피하기", isOn: $settings.petDodgesCursor)
            petNote("커서를 올려둔 채 1초 가까이 잡지 않으면 반대쪽으로 비켜준다.")

        }
        // 제목에 "펫 모드에서만"이라고 적어 두고 켤 수 있게 두면 앞뒤가 안 맞는다.
        // 펫 모드가 아니면 실제로 아무 일도 일어나지 않으므로 함께 잠근다.
        .disabled(!settings.isHUDVisible || settings.mode != .pet)
    }

    private func petNote(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 11))
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }

    private var iconSection: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("HUD 링 가운데에 그릴 그림이다.")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)

            ForEach(IconStyleGroup.allCases, id: \.self) { group in
                VStack(alignment: .leading, spacing: 5) {
                    sectionTitle(group.title)
                    HStack(spacing: 10) {
                        ForEach(group.styles, id: \.self) { style in
                            iconTile(style)
                        }
                        // 묶음마다 개수가 달라도 타일 크기가 흔들리지 않게 남는 폭을 밀어낸다.
                        Spacer(minLength: 0)
                    }
                }
            }

            Divider().padding(.vertical, 2)

            // 정지 그림(Claude 쪽)을 골라 두면 켤 것이 없어서 잠긴다.
            Toggle("캐릭터 애니메이션", isOn: $settings.animatesIcon)
                .disabled(!settings.iconStyle.isAnimated)
        }
    }

    private func iconTile(_ style: ClaudeIconStyle) -> some View {
        Button {
            settings.iconStyle = style
        } label: {
            VStack(spacing: 5) {
                ZStack {
                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .fill(Color(white: 0.16))
                    ClaudeIconView(style: style, size: 28)
                }
                .frame(width: 76, height: 50)
                .overlay {
                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .strokeBorder(
                            settings.iconStyle == style ? Color.accentColor : Color.clear,
                            lineWidth: 2
                        )
                }
                Text(style.shortTitle)
                    .font(.system(size: 10))
                    .foregroundStyle(settings.iconStyle == style ? .primary : .secondary)
            }
            .frame(width: 76)
        }
        .buttonStyle(.plain)
    }

    // MARK: - 버전

    private func versionSection(viewportHeight: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            updateBox
            Divider()
            if isPreviewRender {
                changelogList
            } else {
                // 위 두 개는 제 크기를 가져가고, 남는 자리를 이게 다 차지한다.
                ScrollView { changelogList }
                    .frame(maxHeight: .infinity)
            }
        }
        // 높이를 못 박아야 바로 위 `maxHeight: .infinity` 가 "남는 만큼"으로 풀린다.
        .frame(height: isPreviewRender ? nil : tabBodyHeight(viewportHeight), alignment: .topLeading)
    }

    /// 지금 버전과 업데이트 상태.
    private var updateBox: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(AppInfo.version)
                    .font(.system(size: 22, weight: .bold, design: .rounded))
                    .monospacedDigit()
                Text("지금 버전")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                Spacer()
            }

            HStack(spacing: 8) {
                if AppInfo.isTestBuild {
                    Text("테스트판은 새 버전을 확인하지 않습니다")
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                } else if updates.hasUpdate, let latest = updates.latest {
                    Image(systemName: "arrow.down.circle.fill")
                        .foregroundStyle(.white, Color.accentColor)
                        .symbolRenderingMode(.palette)
                    Text("새 버전 \(latest.description)")
                        .font(.system(size: 12, weight: .medium))
                } else if let error = updates.errorText {
                    Text(error)
                        .font(.system(size: 11))
                        .foregroundStyle(.orange)
                        .fixedSize(horizontal: false, vertical: true)
                } else if updates.latest != nil {
                    Text("최신 버전입니다")
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                } else {
                    Text("아직 확인하지 않았습니다")
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 8)
            }

            HStack(spacing: 8) {
                if updates.hasUpdate {
                    Button("업데이트") { _ = UpdateChecker.openUpgrade() }
                        .buttonStyle(.borderedProminent)
                }
                Button(updates.isChecking ? "확인 중…" : "업데이트 확인") { updates.check() }
                    .disabled(updates.isChecking || AppInfo.isTestBuild)
                Spacer(minLength: 0)
                if let checked = updates.lastCheckedAt {
                    Text(RemainingTime.ageText(since: checked, now: Date()))
                        .font(.system(size: 10))
                        .foregroundStyle(.tertiary)
                }
            }

            Toggle("하루에 한 번 새 버전 확인", isOn: $settings.checksForUpdates)
                .font(.system(size: 11))
                .disabled(AppInfo.isTestBuild)

            if updates.hasUpdate {
                Text("업데이트는 터미널에서 brew로 진행된다. 끝나면 앱이 다시 뜬다.")
                    .font(.system(size: 10))
                    .foregroundStyle(.tertiary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private func changelogBadge(_ text: String, tint: Color) -> some View {
        Text(text)
            .font(.system(size: 9, weight: .medium))
            .foregroundStyle(tint)
            .padding(.horizontal, 5)
            .padding(.vertical, 1)
            .background { Capsule().fill(tint.opacity(0.18)) }
    }

    private var changelogList: some View {
        VStack(alignment: .leading, spacing: 16) {
            ForEach(updates.entries, id: \.version) { entry in
                VStack(alignment: .leading, spacing: 6) {
                    changelogVersionRow(entry)
                    if let groups = entry.groups {
                        ForEach(Array(groups.enumerated()), id: \.offset) { _, group in
                            changelogGroup(group)
                        }
                    } else {
                        // 2.2.0 이하는 갈래가 없다. **뒤늦게 나누지 않는다** —
                        // 이미 나간 문구라, 사용자가 그때 본 것과 달라지면 안 된다.
                        ForEach(entry.notes, id: \.self) { note in
                            HStack(alignment: .top, spacing: 5) {
                                Text("·")
                                Text(note)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            .font(.system(size: 11))
                            .foregroundStyle(.secondary)
                        }
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func changelogVersionRow(_ entry: ChangelogEntry) -> some View {
        HStack(spacing: 6) {
            Text(entry.version)
                .font(.system(size: 12, weight: .semibold))
            if let date = entry.date {
                Text(date)
                    .font(.system(size: 10))
                    .foregroundStyle(.tertiary)
            }
            if entry.date == nil {
                changelogBadge("준비 중", tint: .orange)
            } else if entry.version == AppInfo.version {
                changelogBadge("지금 버전", tint: .accentColor)
            }
        }
    }

    /// 묶음 앞에 붙는 아이콘.
    ///
    /// 설정 탭 이야기면 **그 탭에 실제로 붙어 있는 아이콘**을 그대로 쓴다. 여기 따로
    /// 적어 두면 탭 아이콘을 바꿨을 때 변경 내역만 옛 그림으로 남는다.
    private static func groupSymbol(_ group: ChangelogGroup) -> String {
        group.tab.flatMap(SettingsTab.init(rawValue:))?.symbol ?? otherGroupSymbol
    }

    /// 탭에 없는 묶음(마스코트·HUD·설치 같은 것)이 함께 쓰는 아이콘.
    private static let otherGroupSymbol = "wrench.and.screwdriver"

    /// 기능 묶음 하나. 대분류를 달고, 딸린 줄들을 세로줄로 묶어 준다.
    ///
    /// **들여쓰기만으로는 어디까지가 그 묶음인지 안 보인다.** 딱지가 줄마다 붙어 있어서
    /// 왼쪽 끝이 들쭉날쭉해 보이는데, 세로줄이 그 경계를 대신 그어 준다.
    private func changelogGroup(_ group: ChangelogGroup) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 5) {
                Image(systemName: Self.groupSymbol(group))
                    .font(.system(size: 10))
                    .foregroundStyle(.secondary)
                    // 아이콘마다 폭이 달라서, 맞춰 두지 않으면 제목 시작점이 줄마다 어긋난다.
                    .frame(width: 15)
                Text(group.title)
                    .font(.system(size: 11.5, weight: .semibold))
                // 묶음 자체가 이번에 생긴 기능이면 제목 옆에 붙는다. 항목마다 신규가
                // 줄줄이 달리는 것보다 "이 기능이 새로 생겼다"가 한눈에 들어온다.
                if group.isNew {
                    changelogBadge(ChangeKind.new.title, tint: ChangeKind.new.tint)
                }
                Spacer(minLength: 0)
            }

            HStack(alignment: .top, spacing: 8) {
                // 제목 아래로 흐르는 세로줄. 여기까지가 이 묶음이라는 표시다.
                Capsule()
                    .fill(Color.secondary.opacity(0.28))
                    .frame(width: 2)

                VStack(alignment: .leading, spacing: 3) {
                    ForEach(Array(group.notes.enumerated()), id: \.offset) { _, note in
                        HStack(alignment: .firstTextBaseline, spacing: 6) {
                            // **새로 생긴 기능에는 갈래를 안 붙인다.** 전부 새 것이라
                            // 가를 것이 없고, 제목 옆 "신규" 가 이미 그 말을 한다.
                            if !group.isNew {
                                changelogBadge(note.kind.title, tint: note.kind.tint)
                                    // 딱지 폭을 맞춰야 뒤따르는 글이 한 줄로 정렬된다.
                                    .frame(width: 30, alignment: .leading)
                            }
                            Text(note.text)
                                .font(.system(size: 11))
                                .foregroundStyle(.secondary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            // 세로줄이 제목 글자 아래 오게 살짝 들여 둔다.
            .padding(.leading, 3)
        }
    }

    // MARK: - 계정

    private var accountSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(accountDescription)
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Button("Claude Code 재로그인…", action: actions.login)
        }
    }

    private var accountDescription: String {
        if store.needsReauth {
            return "토큰이 만료됐다. 재로그인하면 다시 조회한다. 사용량은 Claude Code가 keychain에 저장한 토큰으로 읽는다."
        }
        return "사용량은 Claude Code가 keychain에 저장한 토큰으로 읽는다. 토큰 수명이 8시간이라 종종 재로그인이 필요하다."
    }

    private var footer: some View {
        HStack {
            Text(version)
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
            Spacer(minLength: 12)
            Button("종료", action: actions.quit)
        }
        .padding(18)
    }

    private func sectionTitle(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.secondary)
            .textCase(.uppercase)
    }
}

/// 갈래마다 딱지 색. 데이터(`ChangeKind`)는 화면을 모르므로 여기서 붙인다.
extension ChangeKind {
    var tint: Color {
        switch self {
        case .new: return .green
        case .improve: return .blue
        case .change: return .purple
        case .fix: return .orange
        case .remove: return .gray
        }
    }
}

/// 설정 창을 하나만 띄우고 재사용한다.
@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    /// 닫을 때 창을 버리므로 자리와 크기만 따로 기억해 둔다.
    private var lastFrame: NSRect?
    private var didCenter = false
    private let settings: HUDSettings
    private let store: UsageStore
    private let updates: UpdateChecker
    private let meter: UsageMeter
    private let actions: SettingsActions
    /// 설정 창을 띄울 화면. HUD가 놓인 화면을 따라간다.
    private let preferredScreen: () -> NSScreen?

    init(
        settings: HUDSettings,
        store: UsageStore,
        updates: UpdateChecker,
        meter: UsageMeter,
        actions: SettingsActions,
        preferredScreen: @escaping () -> NSScreen?
    ) {
        self.settings = settings
        self.store = store
        self.updates = updates
        self.meter = meter
        self.actions = actions
        self.preferredScreen = preferredScreen
        super.init()
    }

    /// 설정 창은 SwiftUI 트리까지 들고 있어서 열어두면 10MB 넘게 쓴다.
    /// 닫을 때 참조를 놓아 메모리를 돌려준다. 다음에 열면 다시 만든다.
    func windowWillClose(_ notification: Notification) {
        lastFrame = window?.frame
        // 알림 처리 중에 창을 없애지 않도록 한 턴 미룬다.
        DispatchQueue.main.async { [weak self] in
            self?.window?.delegate = nil
            self?.window = nil
        }
    }

    func show() {
        // 시스템 설정에서 로그인 항목을 껐다 켰을 수 있다. 열 때마다 실제 상태로 맞춘다.
        settings.refreshLoginItem()

        if window == nil {
            let version = AppInfo.displayVersion
            let view = SettingsView(
                settings: settings,
                store: store,
                updates: updates,
                meter: meter,
                actions: actions,
                version: version
            )

            let window = NSWindow(
                contentRect: NSRect(origin: .zero, size: SettingsView.size),
                styleMask: [.titled, .closable, .resizable],
                backing: .buffered,
                defer: false
            )
            window.title = "\(AppInfo.name) 설정"
            // 제목은 본문 안에 그린다. 타이틀바에 짧게 잘려 보이는 걸 없애기 위해서다.
            window.titlebarAppearsTransparent = true
            window.titleVisibility = .hidden
            window.contentViewController = NSHostingController(rootView: view)
            // NSHostingController는 레이아웃 전에 크기를 모른다. 명시하지 않으면 창이 0으로 찌그러진다.
            window.setContentSize(SettingsView.size)
            // 이보다 줄이면 스크롤로 볼 수 있다. 키우면 내용이 따라 늘어난다.
            window.contentMinSize = NSSize(width: 320, height: 220)
            window.isReleasedWhenClosed = false
            window.delegate = self
            // 닫았다 다시 열면 이전 자리·크기를 그대로 쓴다.
            if let lastFrame { window.setFrame(lastFrame, display: false) }
            self.window = window
        }

        guard let window else { return }

        // Dock 아이콘이 없는 앱이라 직접 활성화해야 창이 앞으로 나온다.
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        if lastFrame == nil { centerOnFirstShow(window) }
    }

    /// NSWindow.center()는 NSScreen.main 기준이라 모니터가 여러 대면
    /// HUD와 다른 화면에 열릴 수 있다. HUD가 있는 화면 가운데로 직접 놓는다.
    /// 두 번째부터는 사용자가 옮긴 자리를 지킨다.
    private func centerOnFirstShow(_ window: NSWindow) {
        guard !didCenter, let screen = preferredScreen() ?? NSScreen.main else { return }
        didCenter = true

        let area = screen.visibleFrame
        let size = window.frame.size
        window.setFrameOrigin(
            NSPoint(x: area.midX - size.width / 2, y: area.midY - size.height / 2)
        )
    }
}
