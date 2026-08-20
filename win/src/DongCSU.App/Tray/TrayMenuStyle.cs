using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// **`Color` 는 전역에서 WPF 것으로 묶여 있다**(GlobalUsings.cs). 트레이 메뉴는 WinForms 라
// GDI+ 색을 써야 해서 이 파일에서만 이름을 붙여 쓴다.
using Gdi = System.Drawing.Color;
using MenuFonts = System.Drawing.SystemFonts;
using MenuPen = System.Drawing.Pen;

namespace DongCSU.App.Tray;

/// <summary>
/// 트레이·우클릭 메뉴의 모양.
///
/// **WinForms 기본 메뉴는 각지고 회색이다.** 윈도우 11 의 다른 메뉴들과 나란히 놓으면
/// 혼자 옛 앱처럼 보인다. 색과 모서리를 직접 잡는다.
///
/// WPF <c>ContextMenu</c> 로 바꾸지 않는 이유는 <c>TrayIcon.ShowMenuAtCursor</c> 에 있다 —
/// HUD 가 <c>WS_EX_NOACTIVATE</c> 라 바깥을 눌러도 안 닫힌다.
/// </summary>
internal static class TrayMenuStyle
{
    public static void Apply(ContextMenuStrip menu, bool dark)
    {
        menu.Renderer = new Renderer(dark);
        menu.BackColor = dark ? Ink(0x24, 0x24, 0x2A) : Gdi.White;
        menu.ForeColor = dark ? Gdi.FromArgb(0xEA, 0xEA, 0xEE) : Gdi.FromArgb(0x1A, 0x1A, 0x1A);
        menu.Font = MenuFonts.MenuFont ?? menu.Font;
        // 글자와 테두리 사이. 기본값은 빽빽해서 항목이 서로 붙어 보인다.
        menu.Padding = new Padding(4, 6, 4, 6);
        menu.DropShadowEnabled = true;

        // **띄울 때마다 건다.** 팝업 창은 닫힐 때 핸들이 사라져서, 한 번만 걸어 두면
        // 두 번째부터 각진 채로 뜬다. 모서리를 깎는 일 자체는 확인 창과 나눠 쓰므로
        // RoundedWindow 로 빼 두었다.
        menu.HandleCreated += (_, _) => RoundedWindow.Round(menu.Handle, dark);
        if (menu.IsHandleCreated) RoundedWindow.Round(menu.Handle, dark);
    }

    private static Gdi Ink(int r, int g, int b) => Gdi.FromArgb(r, g, b);

    /// <summary>
    /// 색과 강조 모양.
    ///
    /// <c>ToolStripProfessionalRenderer</c> 의 색표만 갈아서는 **고른 항목이 각진
    /// 파란 띠로 남는다.** 그 부분만 직접 그린다.
    /// </summary>
    private sealed class Renderer(bool dark) : ToolStripProfessionalRenderer(new Palette(dark))
    {
        private readonly bool dark = dark;

        /// <summary>강조도 둥글게. 메뉴 모서리만 깎고 안은 각지면 따로 논다.</summary>
        private const int HighlightRadius = 6;

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripMenuItem item || !item.Selected || !item.Enabled)
            {
                return;
            }

            // 좌우로 조금 들여서 그린다. 메뉴 테두리에 딱 붙으면 둥근 모서리를 뚫는다.
            var box = new Rectangle(3, 1, e.Item.Width - 7, e.Item.Height - 2);
            using var brush = new SolidBrush(dark
                ? Gdi.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)
                : Gdi.FromArgb(0x18, 0x00, 0x00, 0x00));

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(box, HighlightRadius);
            e.Graphics.FillPath(brush, path);
            e.Graphics.SmoothingMode = SmoothingMode.Default;
        }

        /// <summary>구분선은 옅은 한 줄이면 된다. 기본은 두 줄(어두운 선 + 밝은 선)이라 도드라진다.</summary>
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new MenuPen(dark
                ? Gdi.FromArgb(0x22, 0xFF, 0xFF, 0xFF)
                : Gdi.FromArgb(0x1A, 0x00, 0x00, 0x00));
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // 못 누르는 항목(사용량 요약)은 흐리게. 기본 회색은 어두운 테마에서 안 읽힌다.
            e.TextColor = e.Item.Enabled
                ? (dark ? Gdi.FromArgb(0xEA, 0xEA, 0xEE) : Gdi.FromArgb(0x1A, 0x1A, 0x1A))
                : (dark ? Gdi.FromArgb(0x8A, 0x8A, 0x92) : Gdi.FromArgb(0x70, 0x70, 0x76));
            base.OnRenderItemText(e);
        }

        private static GraphicsPath Rounded(Rectangle box, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(box.X, box.Y, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>바탕과 테두리 색. 나머지는 위 <see cref="Renderer"/> 가 직접 그린다.</summary>
    private sealed class Palette(bool dark) : ProfessionalColorTable
    {
        private Gdi Surface => dark ? Gdi.FromArgb(0x24, 0x24, 0x2A) : Gdi.White;
        private Gdi Edge => dark ? Gdi.FromArgb(0x3A, 0x3A, 0x42) : Gdi.FromArgb(0xE0, 0xE0, 0xE4);

        public override Gdi ToolStripDropDownBackground => Surface;
        public override Gdi MenuBorder => Edge;
        public override Gdi MenuItemBorder => Gdi.Transparent;
        public override Gdi MenuItemSelected => Gdi.Transparent;
        public override Gdi MenuItemSelectedGradientBegin => Gdi.Transparent;
        public override Gdi MenuItemSelectedGradientEnd => Gdi.Transparent;
        public override Gdi ImageMarginGradientBegin => Surface;
        public override Gdi ImageMarginGradientMiddle => Surface;
        public override Gdi ImageMarginGradientEnd => Surface;
        public override Gdi SeparatorDark => Edge;
        public override Gdi SeparatorLight => Surface;
    }
}
