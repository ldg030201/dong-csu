using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    /// <summary>마우스가 마스코트 위에 있는지. 링을 띄울지 정할 때 쓴다.</summary>
    public bool IsMascotHovered => view.Hover == HudHit.Mascot;

    /// <summary>
    /// 지금 커서가 **비켜야 할 자리**에 있는지. 창 좌표를 뷰 좌표로 옮겨서 직접 본다.
    ///
    /// 호버 이벤트에 기대지 않는 이유는 <see cref="HudView.PetDodgeZoneContains"/> 에 있다.
    /// </summary>
    public bool CursorWantsDodge(PetPoint cursor) =>
        view.PetDodgeZoneContains(new Point(cursor.X - Left, cursor.Y - Top));

    /// <summary>
    /// 커서가 창 근처에 있는지. **다가오는 것만 알아채면 되는 거친 판정이다.**
    ///
    /// 멀리 있을 때까지 촘촘히 볼 이유가 없어서, 이걸로 먼저 걸러 낸다.
    /// 여유를 두는 이유는 다음 검사까지의 사이에 커서가 창 안으로 들어올 수 있어서다.
    /// </summary>
    public bool CursorIsNear(PetPoint cursor)
    {
        const double margin = 160;
        return cursor.X >= Left - margin && cursor.X <= Left + Width + margin
            && cursor.Y >= Top - margin && cursor.Y <= Top + Height + margin;
    }

    /// <summary>펫 링이 향하는 값. 같은 목표로 애니메이션을 다시 걸지 않으려고 들고 있는다.</summary>
    private double petRingFadeTarget;

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
                var shaken = Shake.Sample(new PetPoint(Left, Top));
                if (shaken) DizzyStarted?.Invoke();

                // **속도를 새로 재지 못했으면 알리지 않는다.** 첫 표본이거나 같은 눈금에
                // 두 번 왔으면 옛 속도가 그대로 남아 있는데, 그걸 지금 시각으로 다시
                // 알리면 마우스가 선 뒤에도 한 칸 더 기울어져 있는다.
                if (Shake.Measured) DragMoved?.Invoke(new PetPoint(Shake.Velocity.X, -Shake.Velocity.Y));
                return;
            }
            Moved?.Invoke();
        };
        IsVisibleChanged += (_, _) => SyncTicker();

        tick.Tick += (_, _) => view.InvalidateVisual();
    }

    /// <summary>
    /// 보기를 바꾼다. **펼침 ↔ 접힘만 0.22초에 걸쳐 옮긴다.**
    ///
    /// 그 둘은 높이가 같고(88) 폭만 달라서, 옛 내용을 그대로 둔 채 창을 줄이면 서랍이
    /// 밀려 들어가는 것처럼 보인다. 커질 때는 새 내용을 먼저 깔고 창을 키워 드러나게 한다.
    ///
    /// **펫은 곧바로 바꾼다.** 펫은 128×160 이고 카드는 240×88 이라 가로는 늘고 세로는
    /// 주는데, 그 사이 프레임마다 어느 쪽에도 안 맞는 크기로 잘린 그림이 뜬다. 내용도
    /// 통째로 다른 것이라 이어지는 느낌이 아니라 찌그러지는 느낌이 된다.
    ///
    /// <c>BeginAnimation</c> 을 쓰지 않는다 — 끝난 뒤에도 속성을 붙들고 있어서
    /// **펫이 스스로 걸을 때 <c>Left</c> 를 옮기지 못하게 된다.** 직접 한 칸씩 민다.
    /// </summary>
    public void SetMode(HudMode next)
    {
        if (view.Mode == next && pendingMode is null) return;

        var to = view.SizeFor(next);
        var glides = view.Mode != HudMode.Pet && next != HudMode.Pet && !double.IsNaN(Width);

        if (!glides)
        {
            view.Mode = next;
            pendingMode = null;
            StopResize();
            var wide = double.IsNaN(Width) ? to.Width : Width;
            ApplyFrame(to, ExpandsLeft && !double.IsNaN(Left) ? Left + wide - to.Width : Left);
            FinishResize();
            return;
        }

        // 작아질 때는 다 줄어든 뒤에 갈아탄다 — 옛 내용이 서랍처럼 밀려 들어간다.
        if (to.Width < Width) pendingMode = next;
        else { view.Mode = next; pendingMode = null; }

        StartResize(to);
    }

    private void StopResize()
    {
        if (!resizing) return;
        CompositionTarget.Rendering -= OnResizeFrame;
        resizeStartedAt = null;
        resizing = false;
    }

    /// <summary>줄어드는 동안 미뤄 둔 보기. 다 줄어들면 이걸로 갈아탄다.</summary>
    private HudMode? pendingMode;

    private Size resizeFrom;
    private Size resizeTo;
    private double resizeLeftFrom;
    private double resizeLeftTo;

    /// <summary>이번 애니메이션이 시작된 렌더 시각. 프레임을 세지 않고 **실제 시간**으로 센다.</summary>
    private TimeSpan? resizeStartedAt;

    /// <summary>맥과 같은 시간. 더 길면 굼떠 보이고 짧으면 곧바로 바꾸는 것과 다름없다.</summary>
    private static readonly TimeSpan ResizeDuration = TimeSpan.FromSeconds(0.22);

    private void StartResize(Size to)
    {
        resizeFrom = new Size(Width, Height);
        resizeTo = to;
        resizeLeftFrom = Left;
        // 왼쪽으로 펼치는 설정이면 오른쪽 위가 고정이라 왼쪽 변이 같이 움직인다.
        resizeLeftTo = ExpandsLeft ? Left + resizeFrom.Width - to.Width : Left;

        if (!resizing) CompositionTarget.Rendering += OnResizeFrame;
        resizeStartedAt = null;   // 첫 프레임에서 시각을 잡는다
        resizing = true;
    }

    /// <summary>
    /// 한 프레임 민다.
    ///
    /// <c>DispatcherTimer</c> 를 쓰지 않는다. 그건 화면 주사에 맞춰 돌지 않아서 어떤
    /// 프레임은 건너뛰고 어떤 프레임은 두 번 그려져 **눈에 띄게 덜컹거린다.**
    /// <c>CompositionTarget.Rendering</c> 은 합성 직전에 정확히 한 번씩 온다.
    ///
    /// 진행도는 프레임 수가 아니라 <see cref="RenderingEventArgs.RenderingTime"/> 으로
    /// 잰다 — 프레임을 떨어뜨려도 걸리는 시간은 늘 0.22초다.
    /// </summary>
    private void OnResizeFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame) return;

        resizeStartedAt ??= frame.RenderingTime;
        var t = Math.Clamp((frame.RenderingTime - resizeStartedAt.Value) / ResizeDuration, 0, 1);
        // 맥의 easeInEaseOut. 양 끝에서 느려져서 미끄러지듯 멈춘다.
        var eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

        ApplyFrame(
            new Size(
                resizeFrom.Width + (resizeTo.Width - resizeFrom.Width) * eased,
                resizeFrom.Height + (resizeTo.Height - resizeFrom.Height) * eased),
            resizeLeftFrom + (resizeLeftTo - resizeLeftFrom) * eased);

        if (t < 1) return;

        StopResize();
        FinishResize();
    }

    /// <summary>
    /// 옮기는 중인지. <see cref="Refresh"/> 가 크기를 도로 끌어당기지 않게 막고,
    /// **그동안 펫이 스스로 걷지 않게** 막는다 — 둘 다 <c>Left</c> 를 쓰면 서로 밀어낸다.
    /// </summary>
    public bool IsResizing => resizing;

    private bool resizing;

    private void FinishResize()
    {
        resizing = false;

        // 줄이는 동안 붙들고 있던 보기를 이제 갈아 끼운다.
        if (pendingMode is { } mode)
        {
            view.Mode = mode;
            pendingMode = null;
        }

        var size = view.DesiredHudSize;
        view.RenderOffsetX = 0;
        ApplyFrame(size, Left);
        ClampIntoScreen();
        SyncPetRingFade();
        SyncTicker();
        view.InvalidateVisual();

        // 옮기는 동안 멈춰 뒀던 걸음을 다시 켠다.
        Settled?.Invoke();
    }

    /// <summary>크기 옮기기가 끝났다. 걸음을 다시 켜라는 신호다.</summary>
    public event Action? Settled;

    /// <summary>끄는 동안의 속도(pt/s). 위가 양수다. 끌리는 자세가 이걸 보고 정해진다.</summary>
    public event Action<PetPoint>? DragMoved;

    /// <summary>
    /// 그리는 자리를 창의 어느 모서리에 붙일지 정한다.
    ///
    /// 뷰는 늘 창의 **왼쪽 위**에 그린다. 오른쪽으로 펼치는 설정에서는 그게 맞다 —
    /// 왼쪽 변이 고정이니 내용도 왼쪽에 붙어 있어야 한다.
    ///
    /// **왼쪽으로 펼치는 설정에서는 반대다.** 오른쪽 변이 고정이라, 그대로 두면 옮기는
    /// 0.22초 동안 카드가 옆으로 미끄러진다. 그만큼 밀어서 오른쪽 변에 붙여 둔다.
    /// </summary>
    private void SyncRenderAnchor(double windowWidth)
    {
        view.RenderOffsetX = ExpandsLeft ? windowWidth - view.DesiredHudSize.Width : 0;
    }

    /// <summary>
    /// 창의 자리와 크기를 한 번에 맞춘다.
    ///
    /// **<c>Width</c>·<c>Height</c>·<c>Left</c> 를 따로 대입하면 창이 그때마다 움직인다.**
    /// 펫(128×160)에서 카드(240×88)로 갈 때는 그 사이에 240×160 이라는 아무 데도 없는
    /// 크기가 한 프레임 뜬다 — 넓고 텅 빈 창이 번쩍인다. 실제로 그랬다.
    /// <c>SetWindowPos</c> 로 한 번에 옮기면 그 프레임이 없다.
    /// </summary>
    private void ApplyFrame(Size size, double left)
    {
        SyncRenderAnchor(size.Width);
        view.Width = size.Width;
        view.Height = size.Height;

        var handle = new WindowInteropHelper(this).Handle;
        var target = double.IsNaN(left) ? Left : left;

        if (handle != IntPtr.Zero && !double.IsNaN(target) && !double.IsNaN(Top))
        {
            var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
            var x = scale is { } m ? m.M11 : 1;
            var y = scale is { } n ? n.M22 : 1;

            NativeMethods.SetWindowPos(
                handle, IntPtr.Zero,
                (int)Math.Round(target * x), (int)Math.Round(Top * y),
                (int)Math.Round(size.Width * x), (int)Math.Round(size.Height * y),
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate
                    | NativeMethods.SwpNoSendChanging);

            // WPF 쪽 값도 맞춰 둔다. 펫 무대가 이걸 읽어서 걸어 다닌다.
            //
            // **이미 같은 값이면 대입하지 않는다.** WPF 가 WM_WINDOWPOSCHANGED 를 아직
            // 처리하지 않았으면 옛 값이 남아 있는데, 그때 대입하면 방금 옮긴 자리로
            // 한 번 더 옮기는 SetWindowPos 가 나간다 — 한 번에 옮기려고 만든 길이 무색해진다.
            if (Different(Width, size.Width)) Width = size.Width;
            if (Different(Height, size.Height)) Height = size.Height;
            if (Different(Left, target)) Left = target;
            return;
        }

        Width = size.Width;
        Height = size.Height;
        if (!double.IsNaN(left)) Left = left;
    }

    /// <summary>화면 한 칸도 안 되는 차이는 같은 것으로 본다.</summary>
    private static bool Different(double a, double b) => double.IsNaN(a) || Math.Abs(a - b) > 0.5;

    /// <summary>뷰 상태를 창 크기에 반영하고 다시 그린다.</summary>
    public void Refresh()
    {
        // 보기를 옮기는 중에는 크기를 건드리지 않는다 — 매 프레임 도로 끌어당긴다.
        if (resizing)
        {
            view.InvalidateVisual();
            return;
        }

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

        // 보기나 "사용량 링" 설정이 바뀌었을 수 있다. 링이 향할 곳을 다시 잡는다.
        SyncPetRingFade();
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
            && hit is HudHit.Mascot or HudHit.Settings or HudHit.Refresh or HudHit.UpdateBadge
                or HudHit.PetRow;
        if (hovering != view.IsHovered)
        {
            view.IsHovered = hovering;
            SyncPetRingFade();
            view.InvalidateVisual();
        }

        if (hit == view.Hover) return;

        view.Hover = hit;
        // 마스코트는 끄는 자리다. 손가락 커서를 띄우면 눌러야 할 것처럼 보인다.
        // 카운트다운·자원 줄·버전 딱지는 **읽는 자리**라 마찬가지다.
        // 누르는 자리에서만 손가락 커서를 띄운다. 마스코트는 끄는 자리고,
        // 카운트다운·자원 줄·버전 딱지는 **읽는 자리**라 화살표 그대로 둔다.
        Cursor = hit.IsButton() ? Cursors.Hand : Cursors.Arrow;
        ToolTip = view.TooltipFor(hit);
        view.InvalidateVisual();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (view.Hover == HudHit.None && !view.IsHovered) return;

        view.Hover = HudHit.None;
        view.IsHovered = false;

        // 누르고 있는 중이면 그 상태는 건드리지 않는다 — 마우스를 잡아 뒀으므로
        // 밖으로 나갔다 돌아와서 떼도 MouseUp 이 온다. 여기서 지우면 그 클릭이 사라진다.
        if (pressed == HudHit.None) Cursor = Cursors.Arrow;
        ToolTip = null;
        SyncPetRingFade();
        view.InvalidateVisual();
    }

    /// <summary>
    /// 펫 링을 0.18초에 걸쳐 띄우거나 내린다.
    ///
    /// **곧바로 켜고 끄면 마우스가 스칠 때마다 번쩍인다.** 애니메이션은 WPF 에 맡긴다 —
    /// <c>AffectsRender</c> 라 값이 바뀌는 프레임마다 알아서 다시 그린다.
    /// </summary>
    private void SyncPetRingFade()
    {
        var target = view.Mode == HudMode.Pet && view.ShowsPetRing ? 1.0 : 0.0;
        // 목표가 그대로면 손대지 않는다. 다시 걸면 진행 중인 것이 처음부터 다시 돈다.
        if (target == petRingFadeTarget) return;
        petRingFadeTarget = target;

        view.BeginAnimation(HudView.PetRingFadeProperty, new DoubleAnimation
        {
            To = target,
            Duration = HudView.PetRingFadeDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) return;

        // 버튼 위에서 시작한 클릭은 창을 끌지 않는다. 누르자마자 실행하지도 않는다 —
        // 밖으로 끌어내면 취소되는 것이 버튼의 상식이다.
        //
        // **마스코트와 설명만 붙은 자리는 예외다.** 펫 모드에서 마스코트는 창의 거의
        // 전부라 여기서 못 끌면 창을 옮길 방법이 없고, 카운트다운·자원 줄·버전 딱지는
        // 누를 것이 없는데도 막으면 카드 아래쪽을 통째로 못 잡게 된다.
        var hit = view.HitTest(e.GetPosition(view));
        if (hit.IsButton())
        {
            pressed = hit;
            // **마우스를 잡아 둔다.** 안 잡으면 창 밖에서 뗐을 때 MouseUp 이 이 창으로
            // 오지 않는다 — 누른 상태가 그대로 남아 펫이 멈춘 채로 굳는다.
            CaptureMouse();
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
        if (IsMouseCaptured) ReleaseMouseCapture();
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

        var hit = view.HitTest(e.GetPosition(view));

        // 마스코트를 두 번 누르면 펫으로 드나든다. 맥과 같은 자리다.
        if (hit == HudHit.Mascot) { PetToggled?.Invoke(); return; }

        // 버튼을 두 번 누른 것은 접기가 아니다. 이미 버튼이 두 번 실행됐다.
        if (hit.IsButton()) return;

        // 나머지는 빈 자리와 같다 — 설명만 붙은 곳에서도 접기가 먹어야 한다.
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

    /// <summary>자리와 크기를 **한 번에** 옮긴다. 따로 대입하면 창이 두 번 움직여 한 프레임 튄다.</summary>
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static partial bool SetWindowPos(
        IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoSendChanging = 0x0400;
}
