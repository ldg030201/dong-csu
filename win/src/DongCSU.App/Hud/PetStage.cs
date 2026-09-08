using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DongCSU.Core.Pet;

namespace DongCSU.App.Hud;

/// <summary>
/// 펫이 보는 무대. **좌표 변환이 사는 유일한 곳이다.**
///
/// 윈도우 API 는 전부 **물리 픽셀**로 말하고(<c>GetCursorPos</c>·<c>GetMonitorInfo</c>),
/// WPF 의 <c>Left/Top/Width/Height</c> 는 **DIP** 다. 배율 100%가 아닌 화면에서 이 둘을
/// 섞으면 커서가 엉뚱한 자리에 있는 것으로 계산되고, 펫이 화면 밖으로 걸어 나간다.
///
/// 계수는 그 창에 WPF 가 **실제로 쓰는 값**에서 읽는다. DPI 인식 모드가 무엇이든 맞고,
/// 창을 다른 배율 모니터로 옮기면 값이 바뀌므로 매 틱 다시 읽는다.
///
/// <c>SystemParameters.WorkArea</c> 를 쓰지 않는 이유는 그것이 **주 모니터 것뿐**이기
/// 때문이다. 펫은 자기가 있는 모니터 안에서 돌아다녀야 한다.
/// </summary>
internal sealed class PetStage(HudWindow hud) : IPetStage
{
    /// <summary>마지막 키 입력 시각. 창이 넣어 준다. 없으면 늘 조용한 것으로 본다.</summary>
    public TimeSpan SinceLastKey { get; set; } = TimeSpan.MaxValue;

    public PetRect Window => new(hud.Left, hud.Top, hud.Width, hud.Height);

    public PetPoint Cursor
    {
        get
        {
            if (!NativeStage.GetCursorPos(out var point)) return new PetPoint(double.MinValue, double.MinValue);

            var (scaleX, scaleY) = DeviceToDip();
            return new PetPoint(point.X * scaleX, point.Y * scaleY);
        }
    }

    public PetRect? WorkArea
    {
        get
        {
            var handle = new WindowInteropHelper(hud).Handle;
            if (handle == IntPtr.Zero) return null;

            var monitor = NativeStage.MonitorFromWindow(handle, NativeStage.MonitorDefaultToNearest);
            return monitor == IntPtr.Zero ? null : WorkAreaOf(monitor, DeviceToDip());
        }
    }

    /// <summary>
    /// 붙어 있는 모든 화면의 작업 영역.
    ///
    /// **<see cref="WorkArea"/> 와 같은 계수로 환산한다.** 모니터마다 배율이 다를 수
    /// 있는데 화면마다 제 배율로 나누면 붙어 있는 화면이 겹치거나 벌어져서, 펫이
    /// 이음매에서 허공을 밟거나 아예 못 건넌다. 물리 픽셀에 **지금 창의 계수 하나**를
    /// 곱한 한 벌의 좌표계여야 한다.
    ///
    /// <c>System.Windows.Forms.Screen.AllScreens</c> 도 같은 것을 주지만 그쪽은 물리
    /// 픽셀이라 환산을 잊기 쉽다. 여기서는 위와 **같은 API** 로 재서 단위가 갈릴
    /// 자리를 안 만든다.
    /// </summary>
    public IReadOnlyList<PetRect> WorkAreas
    {
        get
        {
            // **계수는 한 번만 묻는다.** 화면마다 물어도 답은 어차피 하나인데
            // (`DeviceToDip` 은 지금 창의 계수다), 이건 걷는 동안 초당 열 번 도는
            // 자리라 화면 수만큼 시각 트리를 헤집을 이유가 없다.
            var scale = DeviceToDip();
            var found = new List<PetRect>(2);
            NativeStage.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                if (WorkAreaOf(monitor, scale) is { } work) found.Add(work);
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    /// <summary>
    /// 지금 눈금. 창이 아직 안 떴으면 1:1.
    ///
    /// **가로만 본다.** 세로도 같은 값이고(윈도우는 축마다 다른 배율을 안 준다),
    /// 목적지를 옮길 때 쓰는 비율은 하나면 된다.
    /// </summary>
    public double Scale => DeviceToDip().X;

    private PetRect? WorkAreaOf(IntPtr monitor, (double X, double Y) scale)
    {
        var info = new NativeStage.MonitorInfo { Size = Marshal.SizeOf<NativeStage.MonitorInfo>() };
        if (!NativeStage.GetMonitorInfo(monitor, ref info)) return null;

        return ToDip(info.Work, scale);
    }

    /// <summary>
    /// 물리 픽셀 사각형 → DIP 사각형.
    ///
    /// <b>창 목록도 이걸 쓴다.</b> 창 테두리와 작업 영역이 한 좌표계에 있어야 붙는
    /// 자리가 맞는데, 곱하는 자리가 둘이면 "테두리에서 DPI 테두리를 빼자" 같은 손질이
    /// 한쪽에만 들어간다.
    /// </summary>
    internal static PetRect ToDip(NativeStage.NativeRect rect, (double X, double Y) scale) =>
        new(rect.Left * scale.X,
            rect.Top * scale.Y,
            (rect.Right - rect.Left) * scale.X,
            (rect.Bottom - rect.Top) * scale.Y);

    /// <summary>
    /// 물리 픽셀 → DIP 계수. 창이 아직 안 떴으면 1:1 로 본다.
    ///
    /// **창 목록을 뜨는 쪽(<see cref="WindowSurvey"/>)도 이걸 쓴다.** 커서·작업 영역과
    /// **같은 계수**를 봐야 해서 열어 뒀다 — 거기서 같은 셈을 새로 적으면 이 클래스가
    /// "좌표 변환이 사는 유일한 곳" 이라는 것이 깨진다.
    /// </summary>
    internal (double X, double Y) DeviceToDip() => DeviceToDip(hud);

    /// <summary>
    /// <inheritdoc cref="DeviceToDip()" path="/summary/node()"/>
    ///
    /// <b>무대 없이도 부를 수 있어야 한다.</b> 붙어 있는 동안 마우스를 흘려보내는
    /// 판정(<c>HudWindow.OnHitTest</c>)은 창 안에서 도는데, 거기서 같은 셈을 새로
    /// 적으면 이 클래스가 "좌표 변환이 사는 유일한 곳" 이라는 것이 깨진다.
    /// </summary>
    internal static (double X, double Y) DeviceToDip(System.Windows.Media.Visual visual)
    {
        var source = PresentationSource.FromVisual(visual);
        var transform = source?.CompositionTarget?.TransformFromDevice;
        return transform is { } matrix ? (matrix.M11, matrix.M22) : (1, 1);
    }
}

internal static partial class NativeStage
{
    public const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public NativeRect Full;
        public NativeRect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    // **여기만 `DllImport` 다.** `LibraryImport` 의 소스 생성기는 델리게이트를 마샬링하지
    // 못한다. 다른 셋은 그대로 `LibraryImport` 로 둔다.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(
        IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
}
