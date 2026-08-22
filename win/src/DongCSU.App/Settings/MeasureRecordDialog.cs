using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DongCSU.Core.Usage;

namespace DongCSU.App.Settings;

/// <summary>
/// 끝난 측정 하나를 펼쳐 보는 창.
///
/// <b>목록 안에서 펼치지 않고 창을 띄우는 이유가 있다.</b> 측정 탭은 재는 동안 1초마다
/// 통째로 다시 만들어진다 — 펼침 상태와 스크롤 자리를 매초 되돌려야 하고, 펼친 줄이
/// 목록 높이를 흔들어 그 자리가 계속 어긋난다. 창으로 빼면 그 싸움이 없어진다.
///
/// 내용은 측정 탭과 <b>같은 함수</b>(<see cref="SettingsWindow.MeasureLimits"/> ·
/// <see cref="SettingsWindow.MeasureTokens"/>)를 부른다. 두 화면이 다르게 보이면
/// 어느 쪽이 맞는지 알 수 없다.
///
/// 기록은 중지 순간의 값을 통째로 얼려 둔 것이라 <b>여기서 다시 계산하지 않는다.</b>
///
/// 뼈대는 <see cref="ConfirmDialog"/> 를 그대로 따른다. XAML 을 쓰지 않는 이유는
/// <c>CLAUDE.md</c> 에 있다.
/// </summary>
internal sealed partial class MeasureRecordDialog : Window
{
    /// <summary>
    /// 기록 하나를 펼쳐 보인다.
    /// </summary>
    /// <param name="includesCache">
    /// 열 때의 캐시 포함 여부. 측정 탭 설정을 물려받되 <b>여기서 바꾼 것은 저장하지
    /// 않는다</b> — 기록 하나를 자세히 보려고 잠깐 켠 것이 탭 설정까지 바꾸면,
    /// 창을 닫은 뒤 목록의 숫자가 통째로 달라져 있다.
    /// </param>
    /// <returns>지웠으면 true.</returns>
    public static bool Show(
        Window owner, SettingsPalette palette, UsageMeter meter, MeterRecord record, bool includesCache) =>
        new MeasureRecordDialog(owner, palette, meter, record, includesCache).ShowDialog() == true;

    private readonly SettingsPalette palette;
    private readonly UsageMeter meter;
    private readonly MeterRecord record;
    private readonly Border root = new();

    /// <summary>이 창 안에서만 산다. 설정에 안 적는다.</summary>
    private bool includesCache;

    private MeasureRecordDialog(
        Window owner, SettingsPalette palette, UsageMeter meter, MeterRecord record, bool includesCache)
    {
        this.palette = palette;
        this.meter = meter;
        this.record = record;
        this.includesCache = includesCache;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;

        // **맥의 320 은 우리 글꼴에서 좁다.** `캐시 읽기 … 4.5억 토큰` 이 한 줄에
        // 들어가야 한다.
        Width = 360;

        // 모델이 여럿인 긴 측정은 내용이 화면보다 길어질 수 있다. 넘치는 만큼은
        // 안쪽 스크롤이 받고, 버튼 줄은 늘 아래에 붙어 있는다.
        MaxHeight = 720;

        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // **주인을 꼭 걸어 둔다.** HUD 가 `WS_EX_NOACTIVATE` 라 안 걸면 이 창이
        // 다른 창 뒤로 깔린다.
        Owner = owner;
        Background = palette.Brush(palette.Window);
        Content = root;

        // Esc 로 닫는다. 창틀이 없어 닫기 단추가 없으니 이것마저 없으면 키보드로는
        // 빠져나갈 길이 없다.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };

        Paint();
    }

    /// <summary>
    /// 내용을 다시 만든다. 캐시 포함을 켜고 끌 때 줄 수·합계·모델별 표가 함께 바뀌므로
    /// **통째로 다시 만든다** — 측정 탭이 <c>ShowTab()</c> 으로 하는 것과 같다.
    /// </summary>
    private void Paint()
    {
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = SettingsWindow.RecordDate(record),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(palette.Primary),
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{RemainingTime.ElapsedText(record.Duration)} 동안 · 표본 {record.Samples}회",
            FontSize = 11.5,
            Foreground = palette.Brush(palette.Secondary),
            Margin = new Thickness(0, 2, 0, 0),
        });

        // 조회 실패는 지금 상태이지 지난 기록의 성질이 아니다. 그래서 null 을 준다.
        stack.Children.Add(SettingsWindow.MeasureLimits(
            palette, record.Tracks, errorText: null, emptyText: "잡힌 표본이 없습니다"));

        // 기록은 얼려 둔 값이라 지금 기록 폴더가 있든 없든 그대로 보여준다.
        stack.Children.Add(SettingsWindow.MeasureTokens(
            palette,
            record.Tokens,
            record.TokensByModel,
            includesCache,
            available: true,
            value => { includesCache = value; Paint(); },
            emptyText: "없음"));

        var (scrollHost, _) = Ui.Scroller(palette, stack);
        // 오른쪽은 스크롤 막대 자리다.
        stack.Margin = new Thickness(0, 0, 12, 0);

        // **삭제가 왼쪽, 닫기가 오른쪽이다.** 확인 창(할 일 왼쪽 · 취소 오른쪽)과 같은
        // 차례라, 두 창을 오가며 손이 헷갈리지 않는다.
        var delete = Ui.Button(palette, "삭제", Delete, Ui.ButtonKind.Danger, focusable: true);
        var close = Ui.Button(palette, "닫기", Close, focusable: true);

        // **처음 초점은 닫기 쪽이다.** 곧바로 Enter 를 치면 창이 닫히지, 기록이 지워지지
        // 않는다 — 맥이 닫기에 `defaultAction` 을 건 것과 같다. 내용을 다시 만들 때마다
        // 걸리므로(캐시 포함을 켜고 끌 때) 새로 만든 단추로 초점이 따라온다.
        close.Loaded += (_, _) => Keyboard.Focus(close);

        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(delete, Dock.Left);
        buttons.Children.Add(delete);
        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(close);

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(scrollHost);

        root.Padding = new Thickness(20, 18, 20, 16);
        root.Child = layout;

        // Tab 이 두 버튼 사이를 돈다. 기본값이면 마지막 버튼에서 초점이 창 밖으로
        // 빠져나가 어디로 갔는지 안 보인다.
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
    }

    private void Delete()
    {
        // **확인 창의 주인은 설정 창이 아니라 이 창이다.** 아니면 확인 창이 이 창
        // 뒤로 깔려 답을 기다리는 줄도 모르게 된다.
        if (!ConfirmDialog.Ask(this, palette, "이 측정 기록을 지울까요?",
                $"{SettingsWindow.RecordDate(record)} 기록이 지워집니다. 되돌릴 수 없습니다.", "지우기"))
        {
            return;
        }

        meter.DeleteRecord(record);
        // 지운 기록의 창을 띄워 둘 수 없다. 값을 넣는 순간 창이 닫힌다.
        DialogResult = true;
    }

    // ── 모서리 ──────────────────────────────────────────────────────

    /// <summary>모서리를 둥글게. 자세한 사정은 <see cref="ConfirmDialog"/> 에 적혀 있다.</summary>
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int RoundCorner = 2;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// <c>AllowsTransparency</c> 는 켜지 않는다 — 켜는 순간 레이어드 창이 되어 DWM 이
    /// 모서리를 안 깎고 글자까지 흐려진다.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var window = new WindowInteropHelper(this).Handle;
        if (window == IntPtr.Zero) return;

        var corner = RoundCorner;
        DwmSetWindowAttribute(window, WindowCornerPreference, ref corner, sizeof(int));

        // COLORREF 는 0x00BBGGRR 이다. 어두운 테마에서 밝은 테두리를 쓰면 떠 보인다.
        var border = palette.IsDark ? 0x003A3A42 : 0x00E0E0E4;
        DwmSetWindowAttribute(window, BorderColor, ref border, sizeof(int));
    }
}
