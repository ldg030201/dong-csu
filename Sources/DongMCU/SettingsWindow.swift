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
    case account

    var id: String { rawValue }

    var title: String {
        switch self {
        case .status: return "상태"
        case .display: return "표시"
        case .icon: return "아이콘"
        case .account: return "계정"
        }
    }

    var symbol: String {
        switch self {
        case .status: return "gauge"
        case .display: return "slider.horizontal.3"
        case .icon: return "face.smiling"
        case .account: return "person.crop.circle"
        }
    }
}

/// 설정 창의 내용.
struct SettingsView: View {
    static let sidebarWidth: CGFloat = 124
    static let contentWidth: CGFloat = 356
    /// 창 크기의 유일한 출처. 뷰 프레임과 NSWindow 양쪽이 이걸 쓴다.
    /// 높이는 가장 긴 탭(표시)이 스크롤 없이 들어가는 값이다.
    static let size = CGSize(width: sidebarWidth + 1 + contentWidth, height: 460)

    @ObservedObject var settings: HUDSettings
    @ObservedObject var store: UsageStore
    let actions: SettingsActions
    let version: String

    @State private var tab: SettingsTab

    init(
        settings: HUDSettings,
        store: UsageStore,
        actions: SettingsActions,
        version: String,
        initialTab: SettingsTab = .status
    ) {
        self.settings = settings
        self.store = store
        self.actions = actions
        self.version = version
        _tab = State(initialValue: initialTab)
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
        .frame(width: Self.size.width, height: Self.size.height)
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 2) {
            ForEach(SettingsTab.allCases) { item in
                Button {
                    tab = item
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
        case .account: accountSection
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
                .disabled(!settings.isHUDVisible || settings.isCollapsed)

            Toggle("HUD 표시", isOn: $settings.isHUDVisible)
            Toggle("접어서 링만 보기", isOn: $settings.isCollapsed)
                .disabled(!settings.isHUDVisible)

            HStack {
                Button("위치 초기화", action: actions.resetPosition)
                    .disabled(!settings.isHUDVisible)
                Text("HUD는 드래그로 옮길 수 있고, 더블클릭하면 접힌다.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
        }
    }

    // MARK: - 아이콘

    /// 아이콘을 실제로 그려서 보여주고 고르게 한다.
    /// 출처가 다른 그림이 섞이지 않도록 묶음별로 나눠 놓는다.
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
    private let actions: SettingsActions
    /// 설정 창을 띄울 화면. HUD가 놓인 화면을 따라간다.
    private let preferredScreen: () -> NSScreen?

    init(
        settings: HUDSettings,
        store: UsageStore,
        actions: SettingsActions,
        preferredScreen: @escaping () -> NSScreen?
    ) {
        self.settings = settings
        self.store = store
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
