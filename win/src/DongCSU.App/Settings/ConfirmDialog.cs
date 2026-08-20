using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DongCSU.App.Settings;

/// <summary>
/// 되돌릴 수 없는 일을 한 번 묻는 창.
///
/// **파란 버튼이 곧 할 일이다.** 버튼 글자가 "확인"이면 눌러 보기 전에는 무엇이
/// 일어나는지 모른다 — 종료인지 초기화인지는 제목을 다시 읽어야 알 수 있다.
/// 그래서 파란 버튼에 할 일을 그대로 적는다(<c>종료</c> · <c>초기화</c> · <c>강제 종료</c>).
/// 맥판도 같은 규칙이다(<c>AppDelegate.confirmQuit</c>).
///
/// <c>MessageBox</c> 를 쓰지 않는 이유가 이것이다. 버튼 글자를 못 바꾸고, 게다가
/// 시스템 테마를 따라서 **설정 창이 어두운데 확인 창만 하얗게 뜬다.** 색은 전부
/// <see cref="SettingsPalette"/> 에서만 가져온다.
///
/// XAML 을 쓰지 않는 이유는 <c>CLAUDE.md</c> 에 있다.
/// </summary>
internal sealed partial class ConfirmDialog : Window
{
    /// <summary>물어보고 답을 받는다. 밖으로 나가는 것은 이것 하나뿐이다.</summary>
    /// <param name="confirm">파란 버튼에 적을 **할 일**. "확인"이 아니라 "종료"·"초기화"다.</param>
    public static bool Ask(Window owner, SettingsPalette palette, string title, string body, string confirm) =>
        new ConfirmDialog(owner, palette, title, body, confirm).ShowDialog() == true;

    private readonly bool isDark;

    private ConfirmDialog(Window owner, SettingsPalette palette, string title, string body, string confirm)
    {
        isDark = palette.IsDark;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        // 글이 길어지면 세로로만 늘어난다. 폭까지 내용에 맡기면 한 줄짜리 확인 창이
        // 가로로 길쭉해져서 매번 다른 크기로 뜬다.
        SizeToContent = SizeToContent.Height;
        Width = 380;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // **주인을 꼭 걸어 둔다.** HUD 가 `WS_EX_NOACTIVATE` 라 안 걸면 확인 창이
        // 다른 창 뒤로 깔려 버린다 — 답을 기다리는 창이 안 보이면 앱이 멈춘 것으로 보인다.
        Owner = owner;
        Background = palette.Brush(palette.Window);

        var confirmButton = FocusableButton(palette, confirm, () => Finish(true), Ui.ButtonKind.Accent);
        var cancelButton = FocusableButton(palette, "취소", () => Finish(false));

        // **할 일이 왼쪽, 취소가 오른쪽이다.** 맥과 같은 차례다 — 눈이 파란 버튼에
        // 먼저 닿고, 거기 할 일이 적혀 있다.
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        confirmButton.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(confirmButton);
        buttons.Children.Add(cancelButton);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(palette.Primary),
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 12.5,
            Foreground = palette.Brush(palette.Secondary),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(buttons);

        var root = new Border
        {
            Padding = new Thickness(20, 18, 20, 16),
            Child = stack,
        };

        // Tab 이 두 버튼 사이를 돈다. 기본값으로 두면 마지막 버튼에서 초점이 창 밖으로
        // 빠져나가 어디로 갔는지 안 보인다.
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        Content = root;

        // Esc 는 취소다. 창틀이 없어 닫기 단추가 없으니 이것마저 없으면 키보드로는
        // 빠져나갈 길이 없다.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Finish(false);
            e.Handled = true;
        };

        // **처음 초점은 할 일 쪽이다.** 예전 `MessageBoxResult.OK` 로 걸어 두던 것과
        // 같은 뜻이라, 곧바로 Enter 를 치면 하려던 일이 일어난다.
        Loaded += (_, _) => Keyboard.Focus(confirmButton);
    }

    private void Finish(bool confirmed)
    {
        DialogResult = confirmed;
    }

    // ── 초점을 받는 단추 ────────────────────────────────────────────

    /// <summary>
    /// <see cref="Ui.Button"/> 이 돌려주는 <c>Border</c> 는 초점을 못 받는다. 마우스로만
    /// 누를 수 있는 확인 창은 키보드로 답할 방법이 없으므로, 초점을 받는 테두리를
    /// 한 겹 덧씌워 Tab 으로 오가고 Enter·Space 로 누를 수 있게 한다.
    /// </summary>
    private static Border FocusableButton(
        SettingsPalette palette, string text, Action onClick, Ui.ButtonKind kind = Ui.ButtonKind.Normal)
    {
        // **파란 버튼 위에서는 강조색 고리가 안 보인다.** 바탕이 같은 파랑이라 고리가
        // 녹아 없어져서, 초점이 어디 있는지 알 수 없게 된다. 그때만 글자색을 쓴다.
        var ring = kind == Ui.ButtonKind.Accent ? palette.Primary : palette.Accent;

        var ringed = new Border
        {
            Focusable = true,
            BorderThickness = new Thickness(1),
            BorderBrush = palette.Brush(Colors.Transparent),
            CornerRadius = new CornerRadius(Ui.Radius),
            Padding = new Thickness(2),
            Child = Ui.Button(palette, text, onClick, kind),
        };

        ringed.GotKeyboardFocus += (_, _) => ringed.BorderBrush = palette.Brush(ring);
        ringed.LostKeyboardFocus += (_, _) => ringed.BorderBrush = palette.Brush(Colors.Transparent);
        ringed.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            onClick();
            e.Handled = true;
        };
        return ringed;
    }

    // ── 모서리 ──────────────────────────────────────────────────────

    /// <summary>모서리를 둥글게. 윈도우 11 부터 창 관리자가 직접 깎아 준다.</summary>
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int RoundCorner = 2;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// 창틀을 없앤 창은 각지게 뜬다. 깎는 일은 우리가 하지 않고 창 관리자에게 맡긴다 —
    /// 트레이 메뉴(<c>TrayMenuStyle</c>)와 같은 방식이다.
    ///
    /// **<c>AllowsTransparency</c> 는 켜지 않는다.** 켜는 순간 레이어드 창이 되어 DWM 이
    /// 모서리를 안 깎고, 덤으로 글자 렌더링까지 흐려진다.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var window = new WindowInteropHelper(this).Handle;
        if (window == IntPtr.Zero) return;

        var corner = RoundCorner;
        DwmSetWindowAttribute(window, WindowCornerPreference, ref corner, sizeof(int));

        // COLORREF 는 0x00BBGGRR 이다. 어두운 테마에서 밝은 테두리를 쓰면 떠 보인다.
        var border = isDark ? 0x003A3A42 : 0x00E0E0E4;
        DwmSetWindowAttribute(window, BorderColor, ref border, sizeof(int));
    }
}
