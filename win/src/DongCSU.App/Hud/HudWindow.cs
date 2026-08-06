using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DongCSU.Core;

namespace DongCSU.App.Hud;

/// <summary>
/// 화면 위에 항상 떠 있는 창.
///
/// 창틀이 없고 배경이 비치며 작업 표시줄에 안 나온다. 드래그로 옮기고 더블클릭으로
/// 접었다 편다 — 맥판과 같다. 카드 위의 버튼 세 개(접기·설정·새로고침)와 새 버전
/// 표시는 <see cref="HudView.HitTest"/> 로 직접 자리를 재서 나눠 준다.
/// </summary>
public sealed class HudWindow : Window
{
    private readonly HudView view = new();
    private readonly AppSettings settings;

    /// <summary>카운트다운이 초 단위로 움직여야 해서 1초마다 다시 그린다.</summary>
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool isDragging;

    /// <summary>버튼을 누른 채로 있는 중. 뗄 때 같은 자리면 그때 실행한다.</summary>
    private HudHit pressed = HudHit.None;

    public event Action? ModeToggled;
    public event Action? ContextMenuRequested;
    public event Action? SettingsRequested;
    public event Action? RefreshRequested;
    public event Action? UpdatesRequested;
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

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseDoubleClick += OnDoubleClick;
        MouseRightButtonUp += (_, _) => ContextMenuRequested?.Invoke();
        LocationChanged += (_, _) => { if (!isDragging) Moved?.Invoke(); };
        IsVisibleChanged += (_, _) => SyncTicker();

        tick.Tick += (_, _) => view.InvalidateVisual();
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

        SyncTicker();
    }

    /// <summary>
    /// 초 단위로 움직일 것이 있을 때만 타이머를 돌린다.
    ///
    /// 접힌 카드에는 글자가 없고, 숨겨 두면 아무도 안 본다. 그런데도 계속 돌리면
    /// 보이지도 않는 그림을 1초마다 다시 그린다.
    /// </summary>
    private void SyncTicker()
    {
        var needed = IsVisible && view.Mode != HudMode.Collapsed;
        if (needed && !tick.IsEnabled) tick.Start();
        else if (!needed && tick.IsEnabled) tick.Stop();
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
    /// **<c>Forms.Screen</c> 을 쓰면 안 된다.** 그쪽은 물리 픽셀이고 WPF 의 Left·Top 은
    /// DIP 라, 배율이 100%가 아닌 화면에서는 값이 어긋난다. 150% 화면이면 실제로는
    /// 안에 있는 창을 밖에 있다고 판정해서 매번 오른쪽 위로 되돌린다.
    /// <c>SystemParameters</c> 쪽은 DIP 라 그대로 견줄 수 있다.
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

    /// <summary>지금 자리가 화면 밖이면 안으로 끌어온다. 옮겼으면 true.</summary>
    public bool ClampIntoScreen()
    {
        var size = view.DesiredHudSize;
        if (IsOnAnyScreen(Left, Top, size)) return false;

        var area = SystemParameters.WorkArea;
        Left = area.Right - size.Width - 24;
        Top = area.Top + 24;
        SavePosition();
        return true;
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

    // ── 마우스 ──────────────────────────────────────────────────────

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var hit = view.HitTest(e.GetPosition(view));
        if (hit == view.Hover) return;

        view.Hover = hit;
        Cursor = hit == HudHit.None ? Cursors.Arrow : Cursors.Hand;
        ToolTip = view.TooltipFor(hit);
        view.InvalidateVisual();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (view.Hover == HudHit.None) return;

        view.Hover = HudHit.None;
        pressed = HudHit.None;
        Cursor = Cursors.Arrow;
        ToolTip = null;
        view.InvalidateVisual();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) return;

        // 버튼 위에서 시작한 클릭은 창을 끌지 않는다. 누르자마자 실행하지도 않는다 —
        // 밖으로 끌어내면 취소되는 것이 버튼의 상식이다.
        var hit = view.HitTest(e.GetPosition(view));
        if (hit != HudHit.None)
        {
            pressed = hit;
            e.Handled = true;
            return;
        }

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

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var target = pressed;
        pressed = HudHit.None;
        if (target == HudHit.None) return;

        // 누른 자리에서 뗐을 때만 실행한다.
        if (view.HitTest(e.GetPosition(view)) != target) return;

        e.Handled = true;
        switch (target)
        {
            case HudHit.Collapse: ModeToggled?.Invoke(); break;
            case HudHit.Settings: SettingsRequested?.Invoke(); break;
            case HudHit.Refresh: RefreshRequested?.Invoke(); break;
            case HudHit.UpdateBadge: UpdatesRequested?.Invoke(); break;
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        // 버튼을 두 번 누른 것은 접기가 아니다. 이미 버튼이 두 번 실행됐다.
        if (view.HitTest(e.GetPosition(view)) != HudHit.None) return;

        ModeToggled?.Invoke();
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

    protected override void OnClosed(EventArgs e)
    {
        tick.Stop();
        base.OnClosed(e);
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
