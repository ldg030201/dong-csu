using System.Drawing;
using System.Windows.Forms;

namespace DongCSU.App.Tray;

/// <summary>
/// 트레이 아이콘과 메뉴.
///
/// **메뉴에 설정 항목을 늘리지 않는다.** 테마·크기·조회 주기는 전부 설정 창에 있고,
/// 메뉴에 한 벌 더 두면 두 곳을 함께 고쳐야 하는 데다 자주 누르는 항목이 파묻힌다.
/// 메뉴에는 바로 누르는 것만 남긴다 — 맥판과 같은 규칙이다.
/// </summary>
public sealed partial class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly ToolStripMenuItem summaryItem;
    private readonly ToolStripMenuItem reloginItem;
    private Icon? currentIcon;
    private string[]? lastGrid;
    private IReadOnlyDictionary<string, string>? lastPalette;

    /// <summary>
    /// 트레이 아이콘을 다시 그리는 가장 짧은 간격.
    ///
    /// **32×32 로 줄여 놓으면 걷는 다리가 보이지 않는다.** 그런데 걷는 동안에는 그림이
    /// 0.14초마다 바뀌어서, 바뀔 때마다 다시 그리면 Bitmap·Icon 을 초당 일곱 번 만들었다
    /// 버린다. 눈에 보이지도 않는 것에 GDI 를 그만큼 쓸 이유가 없다.
    /// </summary>
    private static readonly TimeSpan MinimumIconGap = TimeSpan.FromMilliseconds(400);

    private DateTimeOffset lastIconAt = DateTimeOffset.MinValue;

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? LoginRequested;
    public event Action? QuitRequested;
    public event Action? Activated;

    /// <summary>
    /// 메뉴가 떠 있는 동안 true. **펫이 제 메뉴를 두고 걸어나가지 않게 하려는 것이다** —
    /// 맥은 <c>popUpContextMenu</c> 가 메뉴가 닫힐 때까지 안 돌아와서 앞뒤로 감싸면
    /// 됐지만, WinForms 의 <c>Show</c> 는 곧바로 돌아오므로 열고 닫히는 것을 받아서 안다.
    ///
    /// 트레이 아이콘 우클릭과 **같은 메뉴 한 벌**이라 트레이에서 연 메뉴에서도 펫이
    /// 멈춘다. 맥과 다른 점이지만 해로울 것이 없고, 메뉴를 두 벌로 갈라 두는 쪽이
    /// 훨씬 나쁘다 — 한쪽에만 항목을 더하게 된다.
    /// </summary>
    public event Action<bool>? MenuOpenChanged;

    public TrayIcon()
    {
        summaryItem = new ToolStripMenuItem("사용량 불러오는 중…") { Enabled = false };

        reloginItem = new ToolStripMenuItem("Claude Code 재로그인…")
        {
            Font = new System.Drawing.Font(
                System.Drawing.SystemFonts.MenuFont!, System.Drawing.FontStyle.Bold),
            Visible = false,
        };
        reloginItem.Click += (_, _) => LoginRequested?.Invoke();

        var refresh = new ToolStripMenuItem("새로고침");
        refresh.Click += (_, _) => RefreshRequested?.Invoke();

        var settings = new ToolStripMenuItem("설정…");
        settings.Click += (_, _) => SettingsRequested?.Invoke();

        // 두 판을 같이 띄우면 트레이에 아이콘이 둘이 된다. 어느 쪽을 끄는지 이름으로 갈린다.
        var quit = new ToolStripMenuItem($"{AppInfo.Name} 종료");
        quit.Click += (_, _) => QuitRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            summaryItem,
            new ToolStripSeparator(),
            reloginItem,
            refresh,
            settings,
            new ToolStripSeparator(),
            quit,
        ]);

        // **여는 쪽(`ShowMenuAtCursor`)이 아니라 메뉴에 건다.** 아래에서 알림 아이콘에
        // 메뉴가 안 붙어 있으면 못 띄우고 되돌아가는 길이 있는데, 여는 쪽에서 켜 두면
        // 뜨지도 않은 메뉴 때문에 펫이 영영 굳는다. `Closed` 도 반드시 함께 건다.
        menu.Opened += (_, _) => MenuOpenChanged?.Invoke(true);
        menu.Closed += (_, _) => MenuOpenChanged?.Invoke(false);

        TrayMenuStyle.Apply(menu, dark: true);

        icon = new NotifyIcon
        {
            Text = AppInfo.Name,
            Visible = true,
            ContextMenuStrip = menu,
            Icon = SystemIcons.Application,
        };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Activated?.Invoke(); };
    }

    /// <summary>
    /// 메뉴 색을 지금 테마에 맞춘다. 설정에서 테마를 바꾸면 창이 부른다.
    ///
    /// **HUD·설정 창과 따로 놀면 안 된다** — 셋이 같이 떠 있는데 메뉴만 흰색이면
    /// 다른 앱 것처럼 보인다.
    /// </summary>
    public void ApplyTheme(bool dark)
    {
        if (icon.ContextMenuStrip is { } strip) TrayMenuStyle.Apply(strip, dark);
    }

    /// <summary>
    /// 트레이 메뉴를 마우스 자리에 띄운다. **HUD 우클릭이 이걸 부른다.**
    ///
    /// 메뉴를 한 벌 더 만들지 않고 트레이 것을 그대로 쓴다 — 맥판이 두 메뉴를 같은
    /// 함수로 만드는 것과 같은 이유다. 두 곳에 따로 두면 한쪽에만 항목을 더하게 된다.
    /// (WPF <c>ContextMenu</c> 를 쓰지 않는 이유는 이 창이 <c>WS_EX_NOACTIVATE</c> 라
    /// 포커스를 못 받아서, 바깥을 눌러도 메뉴가 안 닫히기 때문이다.)
    /// </summary>
    public void ShowMenuAtCursor()
    {
        if (icon.ContextMenuStrip is not { } strip) return;

        strip.Show(Control.MousePosition);

        // **띄운 뒤 앞으로 끌어와야 바깥을 눌렀을 때 닫힌다.** 이 메뉴의 주인 창은
        // HUD 인데 그건 `WS_EX_NOACTIVATE` 라 절대 앞에 서지 못한다. 그대로 두면
        // 다른 앱을 눌러도 메뉴가 그 위에 떠 있는 채로 남는다.
        if (strip.Handle != IntPtr.Zero) NativeMethods.SetForegroundWindow(strip.Handle);
    }

    public void UpdateSummary(string text, bool needsReauth)
    {
        // 트레이 툴팁은 63자를 넘으면 WinForms 가 거부한다. 잘라서 넣는다.
        icon.Text = text.Length > 63 ? text[..60] + "…" : text;
        summaryItem.Text = text;
        reloginItem.Visible = needsReauth;
    }

    /// <summary>
    /// 부엉이를 트레이 아이콘 크기로 그려서 올린다.
    ///
    /// **그림이 그대로면 아무것도 하지 않는다.** 눈 깜빡임은 0.05초짜리라, 프레임마다
    /// Bitmap 과 Icon 을 새로 만들면 초당 스무 번씩 GDI 핸들을 만들었다 버리게 된다.
    ///
    /// 같은지 볼 때 **글자로 이어 붙이지 않는다.** 그것부터가 매 프레임 200자짜리
    /// 문자열을 만드는 일이라, 아끼려고 둔 검사가 아끼는 것보다 더 쓴다. 줄 단위로 견준다.
    ///
    /// 달라졌더라도 <see cref="MinimumIconGap"/> 안에는 다시 그리지 않는다 — 걷는 동안
    /// 그림이 0.14초마다 바뀌는데, 32×32 로 줄이면 그 차이가 눈에 보이지도 않는다.
    /// </summary>
    public void UpdateOwl(string[] grid, IReadOnlyDictionary<string, string> palette, int size = 32)
    {
        if (Same(grid, palette)) return;

        var now = DateTimeOffset.UtcNow;
        if (now - lastIconAt < MinimumIconGap) return;
        lastIconAt = now;

        lastGrid = grid;
        lastPalette = palette;

        var next = BuildIcon(grid, palette, size);
        icon.Icon = next;

        // NotifyIcon 이 새 아이콘을 잡은 뒤에 옛것을 버린다. 먼저 버리면 잠깐 빈칸이 뜬다.
        currentIcon?.Dispose();
        currentIcon = next;
    }

    /// <summary>지난번에 올린 것과 같은 그림인지. 새 문자열을 만들지 않고 견준다.</summary>
    private bool Same(string[] grid, IReadOnlyDictionary<string, string> palette)
    {
        if (!ReferenceEquals(lastPalette, palette)) return false;
        if (lastGrid is not { } previous || previous.Length != grid.Length) return false;

        for (var i = 0; i < grid.Length; i++)
        {
            if (!string.Equals(previous[i], grid[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>
    /// 그림은 <see cref="TrayIconArt"/> 가 그리고 여기서는 아이콘으로만 바꾼다.
    /// **진단 통로가 같은 그림을 봐야 해서 갈라 뒀다.**
    /// </summary>
    private static Icon BuildIcon(string[] grid, IReadOnlyDictionary<string, string> palette, int size)
    {
        using var bitmap = TrayIconArt.Render(grid, palette, size);

        var handle = bitmap.GetHicon();
        try
        {
            // FromHandle 은 핸들을 빌려 쓰기만 한다. 복제해서 우리 것으로 만들고 원본은 놓아준다.
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        currentIcon?.Dispose();
    }

    private static partial class NativeMethods
    {
        [System.Runtime.InteropServices.LibraryImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool DestroyIcon(IntPtr handle);

        [System.Runtime.InteropServices.LibraryImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(IntPtr handle);
    }
}
