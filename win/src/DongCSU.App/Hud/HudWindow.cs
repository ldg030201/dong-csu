using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DongCSU.Core;
using DongCSU.Core.Owl;
using DongCSU.Core.Pet;

namespace DongCSU.App.Hud;

/// <summary>
/// 화면 위에 항상 떠 있는 창.
///
/// 창틀이 없고 배경이 비치며 작업 표시줄에 안 나온다. 드래그로 옮기고 더블클릭으로
/// 접었다 편다 — 맥판과 같다. 카드 위의 버튼 네 개(접기·측정·설정·새로고침)와 새 버전
/// 표시는 <see cref="HudView.HitTest"/> 로 직접 자리를 재서 나눠 준다.
/// </summary>
public sealed class HudWindow : Window
{
    private readonly HudView view = new();
    private readonly AppSettings settings;

    /// <summary>카운트다운이 초 단위로 움직여야 해서 1초마다 다시 그린다.</summary>
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };


    /// <summary>
    /// 끌어서 창이 **실제로 움직였다.** 누르고만 있는 것(<see cref="pressing"/>)과 가른다 —
    /// 매달린 자세는 이쪽에서만 나온다.
    /// </summary>
    private bool isDragging;

    /// <summary>마스코트를 누르고 있는 중. 아직 한 칸도 안 움직였을 수 있다.</summary>
    private bool pressing;

    /// <summary>버튼을 누른 채로 있는 중. 뗄 때 같은 자리면 그때 실행한다.</summary>
    private HudHit pressed = HudHit.None;

    /// <summary>우클릭 메뉴가 떠 있는 중. 제 메뉴를 두고 걸어나가지 않게 붙잡는다.</summary>
    private bool isMenuOpen;

    public event Action? ModeToggled;

    /// <summary>마스코트를 두 번 눌렀다. 펫 모드를 드나든다.</summary>
    public event Action? PetToggled;

    /// <summary>잡혔다 놓였다. 그동안 스스로 움직이는 것을 멈춘다.</summary>
    public event Action? HeldChanged;

    /// <summary>마구 흔들려서 어지러워졌다.</summary>
    public event Action? DizzyStarted;

    /// <summary>흔들림 점수. 끌 때마다 새로 센다.</summary>
    public PetShake Shake { get; } = new();

    /// <summary>
    /// 새로고침 버튼 설명에 넣을 남은 초를 채워 달라. **띄우기 직전에 부른다** —
    /// 시시각각 줄어드는 값이라 미리 넣어 두면 옛 숫자가 뜬다.
    /// </summary>
    public event Action? FetchCooldownWanted;

    public event Action? ContextMenuRequested;

    /// <summary>
    /// 측정 버튼을 눌렀다. **재기를 시작하라는 뜻이 아니라 측정 화면을 열라는 뜻이다** —
    /// 까닭은 받는 쪽(<c>AppController</c>)에 적어 뒀다.
    /// </summary>
    public event Action? MeasureRequested;

    public event Action? SettingsRequested;
    public event Action? RefreshRequested;
    public event Action? UpdatesRequested;

    public HudView View => view;

    /// <summary>
    /// 지금 **멈춰 있어야 하는지.** 끄는 중이거나, 어디든 누르고 있거나, 우클릭 메뉴가
    /// 떠 있으면 그렇다.
    ///
    /// 그동안에는 스스로 움직이지 않는다 — 손에 잡힌 채로 걸어나가면 잡은 자리에서
    /// 미끄러지고, 제 메뉴를 두고 걸어나가면 메뉴만 허공에 남는다.
    ///
    /// **매달린 자세와는 다른 값이다.** 그쪽은 <see cref="IsCarried"/> 를 본다.
    /// </summary>
    public bool IsHeld => isDragging || pressing || pressed != HudHit.None || isMenuOpen;

    /// <summary>
    /// 지금 들려 있는지. **실제로 창이 움직인 뒤에만 true.**
    ///
    /// 누르기만 하고 만 클릭에 부엉이가 요동치지 않게 <see cref="IsHeld"/> 와 갈라 뒀다 —
    /// 펫의 설정·새로고침 버튼을 누르고 있는 동안에도, 마스코트를 눌렀다 그 자리에서
    /// 떼기만 해도 매달린 자세가 되면 새로고침 한 번에 부엉이가 버둥거린다.
    /// </summary>
    public bool IsCarried => isDragging;

    /// <summary>
    /// 우클릭 메뉴가 열렸다 닫혔다. 그동안 <see cref="IsHeld"/> 로 걸음을 멈춘다.
    ///
    /// **값이 그대로면 아무것도 하지 않는다** — 알릴 때마다 걸음이 <c>Reset</c> 되어
    /// 배회가 계속 처음의 뜸들이기로 되돌아간다.
    /// </summary>
    public void SetMenuOpen(bool open)
    {
        if (isMenuOpen == open) return;
        isMenuOpen = open;
        HeldChanged?.Invoke();
    }

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

        tipTimer.Tick += (_, _) => OnTipTick();

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseDoubleClick += OnDoubleClick;
        MouseRightButtonUp += (_, _) => ContextMenuRequested?.Invoke();
        // **`WM_NCHITTEST` 를 직접 받는다.** 붙어 있는 동안 창 안으로 넘어간 쪽을
        // 마우스에서 빼려면 이 길밖에 없다(<see cref="OnHitTest"/>).
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource hwnd) hwnd.AddHook(OnHitTest);
        };

        LocationChanged += (_, _) =>
        {
            if (pressing)
            {
                // **여기서 처음으로 "끌렸다"가 된다.** 창이 실제로 움직이기 시작한 뒤에야
                // 매달린 자세로 간다 — 누르기만 하고 만 클릭까지 버둥거리면 새로고침
                // 한 번에 부엉이가 요동친다. 들고 있는 동안 링을 감추는 것도 같이 걸린다.
                if (!isDragging)
                {
                    isDragging = true;
                    view.IsDraggingPet = true;
                    SyncPetRingFade();
                    HeldChanged?.Invoke();
                }

                // 끄는 동안 자리를 계속 넣어 준다. DragMove 는 자기 루프를 돌지만
                // LocationChanged 는 그 안에서도 온다.
                var shaken = Shake.Sample(new PetPoint(Left, Top));
                if (shaken) DizzyStarted?.Invoke();

                // **속도를 새로 재지 못했으면 알리지 않는다.** 첫 표본이거나 같은 눈금에
                // 두 번 왔으면 옛 속도가 그대로 남아 있는데, 그걸 지금 시각으로 다시
                // 알리면 마우스가 선 뒤에도 한 칸 더 기울어져 있는다.
                if (Shake.Measured) DragMoved?.Invoke(new PetPoint(Shake.Velocity.X, -Shake.Velocity.Y));

                // 끄는 내내 "놓으면 여기 걸린다" 를 다시 잰다. **창 목록을 뜨는 일이라
                // 공짜가 아니지만**, 끄는 동안에만 돌고 한 번이 1ms 도 안 걸린다.
                Dragging?.Invoke();
            }
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

    /// <summary>
    /// 끌어다 놓았다. <b>붙을 자리를 찾는 신호다.</b>
    ///
    /// <see cref="Settled"/> 와 따로 두는 이유: 저쪽은 <b>크기</b>를 옮기고 난 뒤라
    /// 자리가 안 바뀐다. 여기는 사용자가 창을 옮겨 놓은 순간이다.
    /// </summary>
    public event Action? Dropped;

    /// <summary>끌고 가는 중. 놓으면 걸릴 자리를 미리 보여주는 자리다.</summary>
    public event Action? Dragging;

    /// <summary>이 창의 핸들. 창 목록에서 우리를 빼는 데 쓴다.</summary>
    public IntPtr Handle => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    /// <summary>
    /// 창에 붙어 있는 동안인지. <b>층이 달라진다.</b>
    ///
    /// 붙어 있는 펫은 <b>그 창과 같은 층</b>에 있어야 한다 — <c>Topmost</c> 로 남겨 두면
    /// 붙은 창이 다른 창에 가려져도 펫만 앞에 떠서, 아무것도 없는 자리에 매달린 것으로
    /// 보인다. 전체화면 창 위에 펫이 떠 있는 것이 맥에서 그렇게 생겼다.
    /// </summary>
    public void SetPerched(bool perched, MascotPerch? edge = null, double sink = 0)
    {
        // **붙은 면과 깊이는 늘 갈아 끼운다.** 창을 따라가다 깊이가 달라질 수 있는데
        // (화면 끝에서 더 깊이 앉는다) `isPerched` 만 보고 돌아 나가면 마우스가
        // 넘어가는 자리가 옛 값에 남는다.
        perchedEdge = perched ? edge : null;
        perchedSink = perched ? sink : 0;

        if (isPerched == perched) return;
        isPerched = perched;
        Topmost = !perched;
        // 놓을 때는 늘 맨 앞으로 돌아온다. 붙어 있는 동안의 층은 `SetPerchFront` 가 잡는다.
        if (!perched) perchedAbove = 0;
    }

    private bool isPerched;
    private MascotPerch? perchedEdge;
    private double perchedSink;

    /// <summary>
    /// 붙어 있는 동안 <b>창 안으로 넘어간 쪽은 마우스를 받지 않는다.</b>
    ///
    /// 넘어간 자리는 대개 <b>남의 창의 제목 표시줄</b>이라, 안 빼면 그 창을 끌려다
    /// 펫이 잡힌다 — 붙여 놓고 나면 그 창을 못 옮기는 셈이다. 맥
    /// <c>HUDPanel.petPointerRect</c> 와 같은 자리다.
    ///
    /// <b>WPF 로는 못 한다.</b> <c>AllowsTransparency</c> 창은 투명한 픽셀에서도 클릭을
    /// 먹으므로, 창 밖으로 흘려보내려면 <c>WM_NCHITTEST</c> 에 <c>HTTRANSPARENT</c> 를
    /// 돌려주는 수밖에 없다.
    /// </summary>
    private IntPtr OnHitTest(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmNcHitTest) return IntPtr.Zero;
        if (!isPerched || perchedEdge is not { } edge) return IntPtr.Zero;
        if (view.PetMascotInkRect(edge) is not { } ink) return IntPtr.Zero;

        // lParam 은 **화면 물리 픽셀**이다. 창 좌표(DIP)로 옮긴다.
        //
        // **`ToInt32()` 를 쓰지 마라.** 주 화면 위나 왼쪽에 놓인 모니터에서는 좌표가
        // 음수라 값이 `Int32` 범위를 벗어날 수 있고, 그러면 조용히 던져서 마우스가
        // 통째로 안 먹는다. 아래 32비트만 잘라 쓰면 부호까지 그대로 남는다.
        var raw = unchecked((int)(lParam.ToInt64() & 0xFFFFFFFF));
        // **계수는 `PetStage` 가 안다.** 커서·작업 영역·창 목록이 다 거기서 오는데
        // 여기만 따로 적으면, 배율을 다루는 방식이 바뀔 때 이 판정만 옛 셈으로 남는다.
        var (scaleX, scaleY) = PetStage.DeviceToDip(this);
        var x = (short)(raw & 0xFFFF) * scaleX - Left;
        var y = (short)((raw >> 16) & 0xFFFF) * scaleY - Top;

        // 창 테두리 선을 넘었는지. **막대를 놓는 것과 같은 셈이다**(`PerchLayout`) —
        // 사용자가 눈으로 맞대 보는 짝이라 여기서 따로 적으면 안 된다.
        if (!PerchLayout.CrossesBorder(
            edge,
            new PetRect(ink.X, ink.Y, ink.Width, ink.Height),
            perchedSink,
            new PetPoint(x, y)))
        {
            return IntPtr.Zero;
        }

        // **버튼 줄과 새 버전 표시는 남긴다.** 창 위 테두리에 앉으면 넘어간 띠가
        // 버튼 줄까지 덮는데, 그것까지 흘려보내면 붙어 있는 동안 설정·새로고침을 못
        // 누른다 — 맥도 `liveRects` 에 버튼 줄을 따로 남긴다.
        //
        // 마스코트와 빈 자리만 흘려보낸다. 잃는 것은 넘어간 부위(다리·발·앞다리)를
        // 잡아서 끌 수 없다는 것뿐이고, 몸통은 그대로 잡힌다.
        var hit = view.HitTest(new Point(x, y));
        if (hit is not (HudHit.None or HudHit.Mascot)) return IntPtr.Zero;

        handled = true;
        return NativeMethods.HtTransparent;
    }

    /// <summary>붙은 창보다 우리가 앞이어야 한다고 마지막으로 판단한 창.</summary>
    private long perchedAbove;

    /// <summary>
    /// 붙어 있는 동안 층을 맞춘다.
    ///
    /// <b>앞뒤 전이만 봐서는 모자라다.</b> 사용자가 <b>이미 맨 앞인 창을 한 번 더 누르면</b>
    /// OS 가 그 창을 우리 위로 올리는데, 그때는 전이가 없어서 조건에 안 걸리고 창 안으로
    /// 넘어간 다리·날개가 그대로 창 뒤에 묻힌다 — <b>잡고 있는 것으로 안 보인다.</b>
    /// 그래서 <b>우리가 그 창보다 앞인지</b>를 직접 확인한다.
    /// </summary>
    public void SetPerchFront(bool front, long window)
    {
        if (!isPerched) return;

        if (!front)
        {
            perchedAbove = 0;
            return;
        }

        // 이미 앞이면 아무것도 안 한다. 매 틱 올리면 다른 창을 쓰는 동안 깜빡인다.
        if (perchedAbove == window
            && WindowSurvey.IsAhead(Handle, (IntPtr)window) == true) return;

        perchedAbove = window;
        NativeMethods.SetWindowPos(
            Handle, NativeMethods.HwndTop, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

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
        // **펼침에만 초 단위로 변하는 글자가 있다.** 접힘에는 글자가 없고, 펫은 링과
        // 마스코트뿐이라 초가 지나도 달라질 것이 없다 — 그런데도 돌리면 하루 8만 번
        // 링·부엉이·버튼을 통째로 다시 그린다. 펫은 하루 종일 켜 두는 보기라 제일 아프다.
        var needed = IsVisible && view.Mode == HudMode.Expanded;
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
            && hit is HudHit.Mascot or HudHit.Measure or HudHit.Settings or HudHit.Refresh
                or HudHit.UpdateBadge or HudHit.PetRow;
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
        // 남은 초는 시시각각 달라진다. **띄우기 직전에** 값을 넣는다.
        FetchCooldownWanted?.Invoke();
        ShowTip(view.TooltipFor(hit));
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
        ShowTip(null);
        SyncPetRingFade();
        view.InvalidateVisual();
    }

    // ── 버튼 설명 ───────────────────────────────────────────────────
    //
    // **WPF 의 `ToolTip` 속성으로는 한 번도 안 뜬다.** 이 창은 `WS_EX_NOACTIVATE` 라
    // 눌러도 앱이 활성화되지 않는데, WPF 는 비활성 앱의 툴팁을 알아서 막는다. 실제로
    // 재 봤다 — 2.5초를 올려 둬도 툴팁 창이 하나도 안 생긴다.
    //
    // 그림뿐인 화면이라 설명이 없으면 눌러 보는 수밖에 없다. 직접 띄운다.

    /// <summary>뜨기까지 기다리는 시간. WPF 기본값과 같다.</summary>
    private static readonly TimeSpan TipDelay = TimeSpan.FromSeconds(0.4);

    private readonly System.Windows.Controls.ToolTip tip = new() { StaysOpen = true, Placement = PlacementMode.Relative };
    private readonly DispatcherTimer tipTimer = new() { Interval = TipDelay };
    private string? tipText;

    /// <summary>
    /// 설명을 예약하거나 지운다.
    ///
    /// **곧바로 띄우지 않는다.** 지나가는 커서마다 뜨면 화면이 어지럽다. 자리를 옮기면
    /// 기다리는 시간이 처음부터 다시 간다.
    /// </summary>
    private void ShowTip(string? text)
    {
        tipTimer.Stop();
        tip.IsOpen = false;
        tipText = text;
        if (text is not null) tipTimer.Start();
    }

    private void OnTipTick()
    {
        tipTimer.Stop();
        if (tipText is null || !IsVisible) return;

        // 커서 오른쪽 아래. 커서가 글을 가리지 않는 자리다.
        var at = Mouse.GetPosition(this);
        tip.HorizontalOffset = at.X + 14;
        tip.VerticalOffset = at.Y + 18;
        tip.PlacementTarget = this;
        tip.Content = tipText;
        tip.IsOpen = true;
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

        // 누르면 설명을 지운다. 눌러서 무슨 일이 일어나는 자리인데 설명이 남아 있으면 가린다.
        ShowTip(null);

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
            // **누르고 있을 뿐이다.** 매달린 자세와 링 감추기는 창이 실제로 움직인
            // 뒤에 LocationChanged 에서 걸린다.
            pressing = true;
            // 그림이 안 넘어가는데 기분만 어지러워지면 안 된다. 맥은 애니메이터가
            // 멎어 있으면 세는 코드 자체가 안 돌지만, 우리는 창 이동에서 따로 세므로
            // 여기서 끊는다. **끌기를 시작할 때마다 읽어서** 설정을 바꾸면 곧 따라간다.
            Shake.Counts = settings.AnimatesMascot && settings.IconStyle.IsAnimated();
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
            // **실제로 끌었는지 먼저 챙긴다.** 아래에서 지워 버리므로 순서가 중요하다.
            var moved = isDragging;

            pressing = false;
            isDragging = false;
            view.IsDraggingPet = false;
            SyncPetRingFade();
            SavePosition();
            HeldChanged?.Invoke();

            // **끌었을 때만 알린다.** `DragMove()` 는 버튼을 뗄 때까지 잡고 있어서
            // 창이 한 픽셀도 안 움직인 그냥 클릭에서도 여기까지 온다 — 그때도 알리면
            // 마스코트를 한 번 누르기만 해도 옆 창에 툭 붙는다(더블클릭의 첫 클릭까지
            // 그렇다). 붙는 쪽이 창을 옮기므로 `SavePosition` 보다는 뒤여야 한다.
            if (moved) Dropped?.Invoke();
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
            case HudHit.Measure: MeasureRequested?.Invoke(); break;
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

    /// <summary>클릭이 통째로 뒤 창으로 넘어간다. 끌 때 보여주는 막대가 쓴다.</summary>
    public const int WsExTransparent = 0x00000020;

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

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoSendChanging = 0x0400;

    /// <summary>맨 앞으로. 붙어 있는 동안 붙은 창을 따라 올라갈 때 쓴다.</summary>
    public static readonly IntPtr HwndTop = IntPtr.Zero;

    public const int WmNcHitTest = 0x0084;

    /// <summary>"여기는 내 자리가 아니다" — 아래 창이 그 클릭을 받는다.</summary>
    public static readonly IntPtr HtTransparent = new(-1);
}
