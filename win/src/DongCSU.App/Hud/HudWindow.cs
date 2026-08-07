using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DongCSU.Core;
using DongCSU.Core.Pet;

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

    /// <summary>마스코트를 두 번 눌렀다. 펫 모드를 드나든다.</summary>
    public event Action? PetToggled;

    /// <summary>잡혔다 놓였다. 그동안 스스로 움직이는 것을 멈춘다.</summary>
    public event Action? HeldChanged;

    /// <summary>마구 흔들려서 어지러워졌다.</summary>
    public event Action? DizzyStarted;

    /// <summary>흔들림 점수. 끌 때마다 새로 센다.</summary>
    public PetShake Shake { get; } = new();

    public event Action? ContextMenuRequested;
    public event Action? SettingsRequested;
    public event Action? RefreshRequested;
    public event Action? UpdatesRequested;
    public event Action? Moved;

    public HudView View => view;

    /// <summary>
    /// 지금 손에 잡혀 있는지(끌거나 버튼을 누르고 있는지).
    ///
    /// 그동안에는 스스로 움직이지 않는다 — 손에 잡힌 채로 걸어나가면 잡은 자리에서
    /// 미끄러진다.
    /// </summary>
    public bool IsHeld => isDragging || pressed != HudHit.None;

    /// <summary>마우스가 마스코트 위에 있는지. 커서 피하기가 이걸 센다.</summary>
    public bool IsMascotHovered => view.Hover == HudHit.Mascot;

    /// <summary>버튼 줄이나 새 버전 표시 위에 있는지. **여기서는 절대 비키지 않는다.**</summary>
    public bool IsControlHovered =>
        view.Hover is HudHit.Settings or HudHit.Refresh or HudHit.UpdateBadge;

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
        LocationChanged += (_, _) =>
        {
            if (isDragging)
            {
                // 끄는 동안 자리를 계속 넣어 준다. DragMove 는 자기 루프를 돌지만
                // LocationChanged 는 그 안에서도 온다.
                if (Shake.Sample(new PetPoint(Left, Top))) DizzyStarted?.Invoke();
                return;
            }
            Moved?.Invoke();
        };
        IsVisibleChanged += (_, _) => SyncTicker();

        tick.Tick += (_, _) => view.InvalidateVisual();
    }

    /// <summary>뷰 상태를 창 크기에 반영하고 다시 그린다.</summary>
    public void Refresh()
    {
        var size = view.DesiredHudSize;

        // **크기가 실제로 바뀐 호출에서만 자리를 잡는다.**
        //
        // Refresh 는 부엉이 프레임을 넘길 때마다도 불린다. 매번 보정하면 나중에 펫이
        // 스스로 걷기 시작했을 때 매 프레임 창을 도로 끌어당긴다.
        var changed = Math.Abs(Width - size.Width) > 0.5 || Math.Abs(Height - size.Height) > 0.5;
        var oldWidth = Width;

        Width = size.Width;
        Height = size.Height;
        view.Width = size.Width;
        view.Height = size.Height;

        if (changed && !double.IsNaN(oldWidth)) AnchorAfterResize(oldWidth, size.Width);

        view.InvalidateVisual();
        SyncTicker();
    }

    /// <summary>
    /// 크기가 바뀔 때 붙잡을 모서리.
    ///
    /// 그냥 두면 창이 **늘 오른쪽·아래로 자란다.** 오른쪽으로 펼치도록 해 뒀으면
    /// 왼쪽 위가 고정이라 그게 맞지만, 왼쪽으로 펼치도록 해 뒀으면 반대로 오른쪽 위가
    /// 고정이어야 한다 — 안 그러면 접었다 펼 때마다 링이 옆으로 미끄러진다.
    /// 펫(128)과 펼침(240)을 오갈 때는 그 차이가 커서 더 티가 난다.
    /// </summary>
    private void AnchorAfterResize(double oldWidth, double newWidth)
    {
        if (ExpandsLeft) Left += oldWidth - newWidth;

        // 커진 쪽이 화면 밖으로 나갈 수 있다.
        ClampIntoScreen();
    }

    /// <summary>왼쪽으로 펼치는 설정인지. 창이 그 방향으로 자란다.</summary>
    public bool ExpandsLeft { get; set; }

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

    /// <summary>기억해 둔 자리로. 처음이면 기본 자리에 붙인다.</summary>
    public void RestorePosition()
    {
        var size = view.DesiredHudSize;
        var left = settings.WindowLeft ?? DefaultPosition(size).X;
        var top = settings.WindowTop ?? DefaultPosition(size).Y;

        // 모니터를 뺐다 꽂으면 기억해 둔 자리가 화면 밖일 수 있다. 그러면 안 보인다.
        if (!IsOnAnyScreen(left, top, size))
        {
            var fallback = DefaultPosition(size);
            left = fallback.X;
            top = fallback.Y;
        }

        Left = left;
        Top = top;
    }

    /// <summary>
    /// 기본 자리 — **주 모니터 오른쪽 위.**
    ///
    /// <c>SystemParameters.WorkArea</c> 는 주 모니터의 작업 영역이고 단위가 DIP 라
    /// <see cref="Window.Left"/> 와 그대로 견줄 수 있다. 작업 표시줄을 피해서 잡히므로
    /// 표시줄을 위나 옆에 두는 사람에게도 맞다.
    /// </summary>
    private static Point DefaultPosition(Size size)
    {
        var area = SystemParameters.WorkArea;
        return new Point(area.Right - size.Width - 24, area.Top + 24);
    }

    /// <summary>
    /// 주 모니터 오른쪽 위로 되돌린다.
    ///
    /// **기억해 둔 자리를 지우는 것만으로는 창이 안 움직인다** — 그 값은 뜰 때 한 번만
    /// 읽히기 때문이다. 지우고, 옮기고, 새 자리를 다시 적어 둔다. 창을 화면 밖으로
    /// 보내 버렸을 때 앱 안에서 되돌릴 유일한 길이라 재시작을 요구해서는 안 된다.
    /// </summary>
    public void ResetPosition()
    {
        var target = DefaultPosition(view.DesiredHudSize);
        Left = target.X;
        Top = target.Y;

        // 숨겨 둔 채로 눌렀어도 다음에 켰을 때 그 자리에 있어야 한다.
        SavePosition();
        AppLog.Write($"HUD 위치를 기본 자리로 되돌렸다 ({target.X:F0}, {target.Y:F0})");
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

        // 펫에서 링과 버튼을 띄우는 조건. **마스코트나 그 아래 버튼 줄 위일 때만** 이다 —
        // 창 전체로 잡으면 투명한 네 귀퉁이에서도 뜬다.
        //
        // 버튼 줄도 포함해야 한다. 링을 스쳐 버튼으로 내려가는 동안 사라져 버리면
        // 누르려던 것이 눈앞에서 없어진다.
        var hovering = view.Mode == HudMode.Pet
            && hit is HudHit.Mascot or HudHit.Settings or HudHit.Refresh or HudHit.UpdateBadge;
        if (hovering != view.IsHovered)
        {
            view.IsHovered = hovering;
            view.InvalidateVisual();
        }

        if (hit == view.Hover) return;

        view.Hover = hit;
        // 마스코트는 끄는 자리다. 손가락 커서를 띄우면 눌러야 할 것처럼 보인다.
        Cursor = hit is HudHit.None or HudHit.Mascot ? Cursors.Arrow : Cursors.Hand;
        ToolTip = view.TooltipFor(hit);
        view.InvalidateVisual();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (view.Hover == HudHit.None && !view.IsHovered) return;

        view.Hover = HudHit.None;
        view.IsHovered = false;
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
        //
        // **마스코트는 예외다.** 펫 모드에서는 그것이 창의 거의 전부라, 여기서 못 끌면
        // 창을 옮길 방법이 아예 없어진다. 마스코트에서는 드래그를 그대로 살린다.
        var hit = view.HitTest(e.GetPosition(view));
        if (hit is not HudHit.None and not HudHit.Mascot)
        {
            pressed = hit;
            HeldChanged?.Invoke();
            e.Handled = true;
            return;
        }

        try
        {
            isDragging = true;
            Shake.Begin();
            HeldChanged?.Invoke();
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
            HeldChanged?.Invoke();
            Moved?.Invoke();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var target = pressed;
        pressed = HudHit.None;
        if (target == HudHit.None) return;

        HeldChanged?.Invoke();

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

        switch (view.HitTest(e.GetPosition(view)))
        {
            // 마스코트를 두 번 누르면 펫으로 드나든다. 맥과 같은 자리다.
            case HudHit.Mascot: PetToggled?.Invoke(); break;
            case HudHit.None: ModeToggled?.Invoke(); break;
            // 버튼을 두 번 누른 것은 접기가 아니다. 이미 버튼이 두 번 실행됐다.
        }
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

        keyboard.Attach(handle);
    }

    private readonly KeyboardIdleWatch keyboard = new();

    /// <summary>마지막 키 입력 이후 지난 시간. 글을 쓰는 동안 펫을 멈추는 데 쓴다.</summary>
    public TimeSpan SinceLastKey => keyboard.Elapsed;

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
