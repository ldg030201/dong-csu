import AppKit
import SwiftUI

/// 설정 창 왼쪽의 탭.
///
/// 항목이 한 화면에 다 들어가던 시절에는 세로로 이어 붙였지만, 아이콘 묶음이
/// 늘어나면서 창이 너무 길어졌다. 화면 자체를 나눠 두면 캐릭터를 더 만들어도
/// 아이콘 탭만 길어진다.
enum SettingsTab: String, CaseIterable, Identifiable {
    case status
    case display
    case icon
    case pet
    case account
    case version

    var id: String { rawValue }

    var title: String {
        switch self {
        case .status: return "상태"
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
    /// 창 크기의 유일한 출처. 뷰 프레임과 NSWindow 양쪽이 이걸 쓴다.
    /// 높이는 가장 긴 탭(펫)이 스크롤 없이 들어가는 값이다. 권한 경고가 떠도
    /// 잘리지 않게 그만큼 여유를 둔다.
    /// 탭에 항목을 더했으면 `--render-settings`로 재어 보고 여기를 함께 올린다.
    static let size = CGSize(width: sidebarWidth + 1 + contentWidth, height: 540)

    @ObservedObject var settings: HUDSettings
    @ObservedObject var store: UsageStore
    @ObservedObject var updates: UpdateChecker
    let actions: SettingsActions
    let version: String
    /// 미리보기 렌더는 ScrollView 안을 그리지 못한다. 그럴 때는 스크롤을 벗겨서
    /// 내용이 잘리더라도 보이게 한다.
    var isPreviewRender = false

    /// 어느 탭이 열려 있는지. 메뉴에서 "변경 내역…"을 누르면 창 밖에서 바꾸므로
    /// 뷰 안의 @State가 아니라 설정 객체가 들고 있다.
    private var tab: SettingsTab { settings.settingsTab }

    init(
        settings: HUDSettings,
        store: UsageStore,
        updates: UpdateChecker,
        actions: SettingsActions,
        version: String,
        initialTab: SettingsTab? = nil,
        isPreviewRender: Bool = false
    ) {
        self.settings = settings
        self.store = store
        self.updates = updates
        self.actions = actions
        self.version = version
        self.isPreviewRender = isPreviewRender
        if let initialTab { settings.settingsTab = initialTab }
    }

    var body: some View {
        // 창을 줄이면 가로·세로 스크롤이 생긴다. 내용 폭은 고정해서 레이아웃이 흔들리지 않게 한다.
        ScrollView([.horizontal, .vertical]) {
            content
        }
    }

    /// 스크롤 밖의 알맹이. 미리보기 렌더는 ScrollView를 그리지 못해서 이걸 직접 그린다.
    var content: some View {
        HStack(spacing: 0) {
            sidebar
            Divider()
            VStack(spacing: 0) {
                VStack(alignment: .leading, spacing: 16) {
                    Text(tab.title)
                        .font(.system(size: 15, weight: .semibold))
                    tabBody
                    Spacer(minLength: 0)
                }
                .padding(18)
                .frame(width: Self.contentWidth, alignment: .leading)

                Divider()
                footer
            }
        }
        // 미리보기는 내용이 창보다 길면 잘린다. 그때는 높이를 풀어 전부 그린다.
        .frame(width: Self.size.width, height: isPreviewRender ? nil : Self.size.height)
    }

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
    private var tabBody: some View {
        switch tab {
        case .status: statusSection
        case .display: displaySection
        case .icon: iconSection
        case .pet: petSection
        case .account: accountSection
        case .version: versionSection
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
            .disabled(!settings.isHUDVisible)

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

            Toggle("입력 피하기 (일부 앱만)", isOn: Binding(
                get: { settings.petDodgesTyping },
                // 켜는 순간에만 권한을 묻는다. 꺼 두면 이 앱은 아무것도 요청하지 않는다.
                set: { on in
                    settings.petDodgesTyping = on
                    if on { CaretWatcher.requestTrust() }
                }
            ))
            petNote(typingDodgeNote)
            petNote("메모장·Xcode처럼 글자 위치를 알려주는 앱에서만 동작한다. Claude·Slack 같은 Electron 앱은 그 정보를 주지 않아 아무 일도 일어나지 않는다.")
            typingPermissionNotice
        }
        .disabled(!settings.isHUDVisible)
    }

    /// 권한이 없으면 **아무것도 안 한다.** 그 사실을 그대로 적는다 —
    /// 켜져 있는데 안 도는 이유를 화면에서 알 수 있어야 한다.
    private var typingDodgeNote: String {
        CaretWatcher.isTrusted
            ? "글자가 닿을 참이면 오른쪽으로, 오른쪽이 막히면 아래로 뛰어서 비킨다."
            : "손쉬운 사용 권한이 필요하다."
    }

    private func petNote(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 11))
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }

    /// 권한은 시스템 설정에서 켜므로 앱에 알림이 오지 않는다. 짧은 주기로 다시 확인해서,
    /// 허용하고 돌아왔을 때 경고가 남아 있지 않게 한다.
    @ViewBuilder private var typingPermissionNotice: some View {
        TimelineView(.periodic(from: .now, by: 2)) { _ in
            if settings.petDodgesTyping, !CaretWatcher.isTrusted {
                HStack(spacing: 8) {
                    Image(systemName: "exclamationmark.triangle.fill")
                        .foregroundStyle(.orange)
                    Text("권한이 없어 지금은 동작하지 않는다")
                        .font(.system(size: 11))
                        .fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: 4)
                    Button("허용하기") {
                        CaretWatcher.requestTrust()
                        CaretWatcher.openAccessibilitySettings()
                    }
                    .controlSize(.small)
                }
            }
        }
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

    private var versionSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            updateBox
            Divider()
            if isPreviewRender {
                changelogList
            } else {
                ScrollView { changelogList }
                    .frame(maxHeight: .infinity)
            }
        }
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
            .padding(.horizontal, 5)
            .padding(.vertical, 1)
            .background { Capsule().fill(tint.opacity(0.20)) }
    }

    private var changelogList: some View {
        VStack(alignment: .leading, spacing: 14) {
                ForEach(updates.entries, id: \.version) { entry in
                    VStack(alignment: .leading, spacing: 3) {
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
        .frame(maxWidth: .infinity, alignment: .leading)
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
    private let actions: SettingsActions
    /// 설정 창을 띄울 화면. HUD가 놓인 화면을 따라간다.
    private let preferredScreen: () -> NSScreen?

    init(
        settings: HUDSettings,
        store: UsageStore,
        updates: UpdateChecker,
        actions: SettingsActions,
        preferredScreen: @escaping () -> NSScreen?
    ) {
        self.settings = settings
        self.store = store
        self.updates = updates
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
        if window == nil {
            let version = AppInfo.displayVersion
            let view = SettingsView(
                settings: settings,
                store: store,
                updates: updates,
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
            // 이보다 줄이면 스크롤로 볼 수 있다.
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
