using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using DongCSU.App.Rendering;
using DongCSU.Core.Owl;

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
    private string? lastGridKey;

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? LoginRequested;
    public event Action? QuitRequested;
    public event Action? Activated;

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

        var quit = new ToolStripMenuItem("DongCSU 종료");
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

        icon = new NotifyIcon
        {
            Text = "DongCSU",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = SystemIcons.Application,
        };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Activated?.Invoke(); };
    }

    public void UpdateSummary(string text, bool needsReauth)
    {
        // 트레이 툴팁은 127자를 넘으면 통째로 안 보인다. 잘라서 넣는다.
        icon.Text = text.Length > 63 ? text[..60] + "…" : text;
        summaryItem.Text = text;
        reloginItem.Visible = needsReauth;
    }

    /// <summary>
    /// 부엉이를 트레이 아이콘 크기로 그려서 올린다.
    ///
    /// **그림이 그대로면 아무것도 하지 않는다.** 눈 깜빡임은 0.05초짜리라, 프레임마다
    /// Bitmap 과 Icon 을 새로 만들면 초당 스무 번씩 GDI 핸들을 만들었다 버리게 된다.
    /// </summary>
    public void UpdateOwl(string[] grid, IReadOnlyDictionary<string, string> palette, int size = 32)
    {
        var key = string.Join("\n", grid) + "|" + string.Join(",", palette.Values);
        if (key == lastGridKey) return;
        lastGridKey = key;

        var next = BuildIcon(grid, palette, size);
        icon.Icon = next;

        // NotifyIcon 이 새 아이콘을 잡은 뒤에 옛것을 버린다. 먼저 버리면 잠깐 빈칸이 뜬다.
        currentIcon?.Dispose();
        currentIcon = next;
    }

    private static Icon BuildIcon(string[] grid, IReadOnlyDictionary<string, string> palette, int size)
    {
        var document = OwlDocument.Embedded;
        var cell = Math.Max(1, size / document.Grid.Lines);
        var width = cell * document.Grid.Columns;
        var height = cell * document.Grid.Lines;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            for (var y = 0; y < grid.Length; y++)
            {
                var row = grid[y];
                for (var x = 0; x < row.Length; x++)
                {
                    var key = OwlRenderer.PaletteKey(row[x]);
                    if (key is null || !palette.TryGetValue(key, out var hex)) continue;

                    var media = OwlRenderer.ParseColor(hex);
                    using var brush = new SolidBrush(
                        System.Drawing.Color.FromArgb(media.R, media.G, media.B));
                    graphics.FillRectangle(brush, x * cell, y * cell, cell, cell);
                }
            }
        }

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
    }
}
