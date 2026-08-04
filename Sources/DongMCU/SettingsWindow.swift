import AppKit
import SwiftUI

/// 설정 창의 내용.
struct SettingsView: View {
    /// 창 크기의 유일한 출처. 뷰 프레임과 NSWindow 양쪽이 이걸 쓴다.
    static let size = CGSize(width: 380, height: 660)

    @ObservedObject var settings: HUDSettings
    @ObservedObject var store: UsageStore
    let actions: SettingsActions
    let version: String

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("설정")
                .font(.system(size: 13, weight: .semibold))
                .padding(.horizontal, 18)
                .padding(.top, 10)
                .padding(.bottom, 2)
            statusSection
            Divider()

            // 창 크기가 고정이라 스크롤이 필요 없다.
            VStack(alignment: .leading, spacing: 18) {
                displaySection
                accountSection
                Spacer(minLength: 0)
            }
            .padding(18)

            Divider()
            footer
        }
        .frame(width: Self.size.width, height: Self.size.height)
    }

    // MARK: - 현재 상태

    private var statusSection: some View {
        VStack(alignment: .leading, spacing: 6) {
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

            HStack(spacing: 16) {
                usageColumn(title: "세션", window: store.snapshot?.fiveHour)
                usageColumn(title: "주간", window: store.snapshot?.sevenDay)
                Spacer()
                Button("새로고침", action: actions.refresh)
                    .disabled(store.isRefreshing)
            }
        }
        .padding(18)
    }

    private func usageColumn(title: String, window: UsageWindow?) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
                .font(.system(size: 10, weight: .semibold))
                .foregroundStyle(.secondary)
            Text(window.map { "\(Int($0.utilization.rounded()))%" } ?? "—")
                .font(.system(size: 20, weight: .bold, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(UsageColor.color(for: window?.utilization ?? 0))
        }
    }

    // MARK: - 표시 설정

    private var displaySection: some View {
        VStack(alignment: .leading, spacing: 12) {
            sectionTitle("표시")

            Picker("테마", selection: $settings.appearance) {
                ForEach(HUDAppearance.allCases, id: \.self) { value in
                    Text(value.title).tag(value)
                }
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("가운데 아이콘")
                iconChooser
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

            Toggle("왼쪽 아래에 CPU·메모리 표시", isOn: $settings.showsProcessStats)
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

    /// 세 가지 아이콘을 실제로 그려서 보여주고 고르게 한다.
    private var iconChooser: some View {
        HStack(spacing: 10) {
            ForEach(ClaudeIconStyle.allCases, id: \.self) { style in
                Button {
                    settings.iconStyle = style
                } label: {
                    VStack(spacing: 5) {
                        ZStack {
                            RoundedRectangle(cornerRadius: 10, style: .continuous)
                                .fill(Color(white: 0.16))
                            ClaudeIconView(style: style, size: 28)
                        }
                        .frame(height: 50)
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
                }
                .buttonStyle(.plain)
            }
        }
    }

    // MARK: - 계정

    private var accountSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            sectionTitle("Claude 계정")

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
            Text("dong-mcu \(version)")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
            Spacer()
            Button("종료", action: actions.quit)
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 12)
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
final class SettingsWindowController {
    private var window: NSWindow?
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
    }

    func show() {
        if window == nil {
            let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String
                ?? dongMCUVersion
            let view = SettingsView(
                settings: settings,
                store: store,
                actions: actions,
                version: version
            )

            let window = NSWindow(
                contentRect: NSRect(origin: .zero, size: SettingsView.size),
                styleMask: [.titled, .closable],
                backing: .buffered,
                defer: false
            )
            window.title = "dong-mcu 설정"
            // 제목은 본문 안에 그린다. 타이틀바에 짧게 잘려 보이는 걸 없애기 위해서다.
            window.titlebarAppearsTransparent = true
            window.titleVisibility = .hidden
            window.contentViewController = NSHostingController(rootView: view)
            // NSHostingController는 레이아웃 전에 크기를 모른다. 명시하지 않으면 창이 0으로 찌그러진다.
            window.setContentSize(SettingsView.size)
            window.isReleasedWhenClosed = false
            self.window = window
        }

        guard let window else { return }

        // Dock 아이콘이 없는 앱이라 직접 활성화해야 창이 앞으로 나온다.
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        centerOnFirstShow(window)
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
