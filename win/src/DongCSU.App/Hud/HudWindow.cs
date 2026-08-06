using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DongCSU.Core;

namespace DongCSU.App.Hud;

/// <summary>
/// 화면 위에 항상 떠 있는 창.
///
/// 창틀이 없고 배경이 비치며 작업 표시줄에 안 나온다. 드래그로 옮기고 더블클릭으로
/// 접었다 편다 — 맥판과 같다.
/// </summary>
public sealed class HudWindow : Window
{
    private readonly HudView view = new();
    private readonly AppSettings settings;
    private bool isDragging;

    public event Action? ModeToggled;
    public event Action? ContextMenuRequested;
    public event Action? Moved;

    public HudView View => view;

    public HudWindow(AppSettings settings)
    {
        this.settings = settings;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Manual;
        Title = "DongCSU";
        Content = view;

        MouseLeftButtonDown += OnMouseDown;
        MouseDoubleClick += OnDoubleClick;
        MouseRightButtonUp += (_, _) => ContextMenuRequested?.Invoke();
        LocationChanged += (_, _) => { if (!isDragging) Moved?.Invoke(); };
    }

    /// <summary>뷰 상태를 창 크기에 반영하고 다시 그린다.</summary>
    public void Refresh()
    {
        var size = view.DesiredHudSize;
        Width = size.Width;
        Height = size.Height;
        view.Width = size.Width;
        view.Height = size.Height;
        view.InvalidateVisual();
    }

    /// <summary>기억해 둔 자리로. 처음이면 오른쪽 위에 붙인다.</summary>
    public void RestorePosition()
    {
        var area = SystemParameters.WorkArea;
        var size = view.DesiredHudSize;

        var left = settings.WindowLeft ?? area.Right - size.Width - 24;
        var top = settings.WindowTop ?? area.Top + 24;

        // 모니터를 뺐다 꽂으면 기억해 둔 자리가 화면 밖일 수 있다. 그러면 안 보인다.
        if (!IsOnAnyScreen(left, top, size))
        {
            left = area.Right - size.Width - 24;
            top = area.Top + 24;
        }

        Left = left;
        Top = top;
    }

    /// <summary>
    /// 기억해 둔 자리가 아직 화면 안인지.
    ///
    /// **`Forms.Screen` 을 쓰면 안 된다.** 그쪽은 물리 픽셀이고 WPF 의 Left·Top 은
    /// DIP 라, 배율이 100%가 아닌 화면에서는 값이 어긋난다. 150% 화면이면 실제로는
    /// 안에 있는 창을 밖에 있다고 판정해서 매번 오른쪽 위로 되돌린다.
    /// `SystemParameters` 쪽은 DIP 라 그대로 견줄 수 있다.
    /// </summary>
    private static bool IsOnAnyScreen(double left, double top, Size size)
    {
        var all = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        // 조금이라도 걸쳐 있으면 잡아서 옮길 수 있다.
        return all.IntersectsWith(new Rect(left, top, size.Width, size.Height));
    }

    /// <summary>
    /// 지금 자리를 기억한다.
    ///
    /// **파일까지 쓴다.** 종료할 때만 쓰면, 앱이 그냥 죽거나 로그아웃으로 끝났을 때
    /// 옮겨 둔 자리가 사라진다. 드래그를 놓는 순간에만 불리므로 자주 쓰지도 않는다.
    /// </summary>
    public void SavePosition()
    {
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
        settings.Save();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) return;
        try
        {
            isDragging = true;
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 버튼이 이미 떼어진 뒤면 던진다. 드래그가 안 됐을 뿐이라 넘어간다.
        }
        finally
        {
            isDragging = false;
            SavePosition();
            Moved?.Invoke();
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) ModeToggled?.Invoke();
    }

    /// <summary>
    /// 전체화면 위에도 뜨게 하고, Alt+Tab 목록에서 뺀다.
    ///
    /// <c>ShowInTaskbar=false</c> 만으로는 Alt+Tab 에 남는다. 도구 창으로 표시해야
    /// 목록에서 빠진다.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(handle, NativeMethods.GwlExStyle,
            style | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate);
    }
}

internal static partial class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
