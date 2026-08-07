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
            if (monitor == IntPtr.Zero) return null;

            var info = new NativeStage.MonitorInfo { Size = Marshal.SizeOf<NativeStage.MonitorInfo>() };
            if (!NativeStage.GetMonitorInfo(monitor, ref info)) return null;

            var (scaleX, scaleY) = DeviceToDip();
            var work = info.Work;
            return new PetRect(
                work.Left * scaleX,
                work.Top * scaleY,
                (work.Right - work.Left) * scaleX,
                (work.Bottom - work.Top) * scaleY);
        }
    }

    /// <summary>물리 픽셀 → DIP 계수. 창이 아직 안 떴으면 1:1 로 본다.</summary>
    private (double X, double Y) DeviceToDip()
    {
        var source = PresentationSource.FromVisual(hud);
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
}
