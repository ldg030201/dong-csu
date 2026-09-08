using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DongCSU.Core.Owl;
using DongCSU.Core.Pet;

namespace DongCSU.App.Hud;

/// <summary>
/// 끌고 있는 동안 <b>"놓으면 이 줄에 걸린다"</b> 를 보여주는 얇은 막대.
///
/// <b>이게 없으면 어디에 걸리는지 놓아 봐야 안다.</b> 붙는 문턱은 그림에서 재는데
/// 펫의 창은 그보다 훨씬 커서(링이 128) 눈으로는 얼마나 가까운지 가늠이 안 된다.
///
/// <b>막대 하나뿐이다.</b> 맥은 예전에 그림이 놓일 자리를 옅은 사각형으로 같이 덮어
/// 보여줬는데, 어두운 창 위에서는 그게 흰 판때기로 보였다 — 뒤 창을 안 가리려고 옅게
/// 둔 것이 어두운 배경에서 정반대로 나왔다(2.5.2 에서 뺐다). 어디에 걸리는지는 이
/// 막대가 말해 주고, 어떤 자세로 붙을지는 마스코트가 그때 그 자세로 바뀌면서 말해 준다.
///
/// <b>창을 따로 두는 이유:</b> 펫의 창은 128×160 이라 남의 창 테두리를 담을 수 없다.
/// 표시를 펫 창 안에 그리면 붙을 자리가 창 밖에 있어서 잘린다.
/// </summary>
internal sealed class PerchHint : Window
{
    /// <summary>그림자가 번질 여백. 표시가 놓일 자리보다 이만큼 크다.</summary>
    private const double Bleed = 10;

    /// <summary>막대 굵기. 맥과 같은 5 다.</summary>
    private const double BarThickness = 5;

    private readonly Border bar = new()
    {
        CornerRadius = new CornerRadius(BarThickness / 2),
        // 링 색(초록·노랑·빨강)과 겹치지 않는 파랑. **HUD 의 새 버전 표시에서 그대로
        // 가져온다** — 이 앱이 무언가를 가리킬 때 늘 같은 색이어야 하는데, 값을 여기
        // 다시 적어 두면 저쪽 파랑을 손볼 때 이 막대만 옛 색으로 남는다.
        Background = new SolidColorBrush(HudPalette.Dark.UpdateBadge),
        Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.5,
            BlurRadius = 6,
            ShadowDepth = 1,
            Direction = 270,
        },
    };

    private MascotPerch? shownEdge;
    private double shownSink = double.NaN;

    public PerchHint()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Opacity = 0;
        // **뜰 때 포커스를 뺏으면 끌던 것이 놓아진다.** 아래 `WS_EX_NOACTIVATE` 는
        // 창이 만들어진 뒤에 걸리므로, 처음 뜨는 그 순간은 이 값이 막아 준다.
        ShowActivated = false;
        Focusable = false;

        var canvas = new Canvas();
        canvas.Children.Add(bar);
        Content = canvas;

        // **절대 마우스를 받지 않는다.** 끌고 있는 중에 뜨는 것이라, 하나라도 받으면
        // 끌던 것이 놓아진다.
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
            NativeMethods.SetWindowLong(
                handle, NativeMethods.GwlExStyle,
                style | NativeMethods.WsExTransparent
                | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
        };
    }

    /// <summary>
    /// 걸릴 줄을 표시한다. <paramref name="landing"/> 은 <b>그림이 덮을 화면 사각형</b>이다.
    /// </summary>
    /// <param name="sink">
    /// 붙잡는 부위가 창 안으로 넘어가는 깊이. 막대는 사각형 끝이 아니라 <b>거기서
    /// 안쪽으로 그만큼 들어온 자리</b>에 온다 — 막대가 곧 창 테두리 선이라, 안 맞추면
    /// 끄는 동안 보이는 자리와 손 떼고 앉는 자리가 달라진다.
    /// </param>
    /// <param name="below">이 창 바로 아래 층에 둔다. 안 주면 층을 안 건드린다.</param>
    public void Show(PetRect landing, MascotPerch edge, double sink, IntPtr below = default)
    {
        var frame = landing.Inflate(Bleed);
        Left = frame.X;
        Top = frame.Y;
        Width = frame.Width;
        Height = frame.Height;

        if (shownEdge != edge || shownSink != sink)
        {
            shownEdge = edge;
            shownSink = sink;
            Layout(landing, edge, sink);
        }

        if (IsVisible) return;

        base.Show();

        // **펫보다 한 단계 아래에 둔다.** 같은 `Topmost` 안에서는 나중에 뜬 쪽이 위라,
        // 그냥 두면 막대가 마스코트를 덮는다. 맥은 `floating - 1` 이라는 진짜 아래
        // 레벨을 쓰지만 WPF 에는 그런 것이 없어서 z 순서를 직접 끼워 넣는다.
        if (below != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                new WindowInteropHelper(this).Handle, below, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }

        // 자리를 옮길 때는 그대로 따라가고 **처음 뜰 때만** 부드럽게 나타난다.
        // 옮길 때마다 페이드를 걸면 끄는 내내 깜빡인다.
        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromSeconds(0.12),
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    public new void Hide()
    {
        if (!IsVisible) return;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        base.Hide();
        shownEdge = null;
        shownSink = double.NaN;
    }

    /// <summary>
    /// 막대를 놓는다. 중심이 <b>창 테두리 선이 지나는 자리</b>다 — 그림 끝이 아니라
    /// 거기서 <paramref name="sink"/> 만큼 안쪽이다. 붙잡는 부위가 그 선을 넘어가
    /// 창 면 위에 얹히기 때문이다.
    /// </summary>
    private void Layout(PetRect landing, MascotPerch edge, double sink)
    {
        var horizontal = edge.IsHorizontal();
        bar.Width = horizontal ? landing.Width : BarThickness;
        bar.Height = horizontal ? BarThickness : landing.Height;

        // 창 좌표는 `Bleed` 만큼 바깥에서 시작한다. **선이 어디인지는 Core 가 안다** —
        // 붙어 있는 동안 마우스를 흘려보내는 판정도 같은 것을 보므로, 막대와 그 판정이
        // 갈릴 수가 없다.
        var line = PerchLayout.BorderLine(
            edge, new PetRect(Bleed, Bleed, landing.Width, landing.Height), sink)
            - BarThickness / 2;

        Canvas.SetLeft(bar, horizontal ? Bleed : line);
        Canvas.SetTop(bar, horizontal ? line : Bleed);
    }

}
