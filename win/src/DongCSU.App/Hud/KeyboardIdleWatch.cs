using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DongCSU.Core;

namespace DongCSU.App.Hud;

/// <summary>
/// 마지막 **키 입력** 이후 얼마나 지났는지.
///
/// 맥의 <c>CGEventSource.secondsSinceLastEventType(.keyDown)</c> 자리다. 윈도우에는
/// 그것에 딱 맞는 것이 없다 — <c>GetLastInputInfo</c> 는 **마우스까지 센다.** 그걸 쓰면
/// 커서를 움직이는 내내 펫이 멈춰서, 맥과 다른 앱이 된다.
///
/// 그래서 RawInput 으로 키보드만 듣는다. <c>RIDEV_INPUTSINK</c> 라 포커스가 없어도 온다.
///
/// **어떤 키인지는 절대 읽지 않는다.** 시각만 적고 그마저 기록 파일에도 안 남긴다 —
/// 남의 키 입력을 엿보는 코드가 되면 안 된다. <c>RIDEV_NOLEGACY</c> 도 쓰지 않는다
/// (다른 앱으로 가는 키를 막아 버린다).
/// </summary>
internal sealed class KeyboardIdleWatch
{
    private readonly Stopwatch since = Stopwatch.StartNew();
    private bool listening;

    /// <summary>
    /// 마지막 키 입력 이후 지난 시간.
    ///
    /// RawInput 을 못 걸었으면 <see cref="GetLastInputInfo"/> 로 물러선다. 그때는
    /// 마우스까지 세므로 맥보다 자주 멈춘다.
    /// </summary>
    public TimeSpan Elapsed
    {
        get
        {
            if (listening) return since.Elapsed;

            var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.MaxValue;

            var idle = (uint)Environment.TickCount - info.Time;
            return TimeSpan.FromMilliseconds(idle);
        }
    }

    public void Attach(IntPtr handle)
    {
        if (handle == IntPtr.Zero || listening) return;

        var source = HwndSource.FromHwnd(handle);
        if (source is null) return;

        var device = new RawInputDevice
        {
            UsagePage = 0x01,   // Generic Desktop
            Usage = 0x06,       // Keyboard
            Flags = RawInputSink,
            Target = handle,
        };

        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            AppLog.Write("키 입력 감시를 걸지 못했다 — 마우스 움직임까지 '입력'으로 센다");
            return;
        }

        source.AddHook(OnMessage);
        listening = true;
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // **내용은 보지 않는다.** 왔다는 사실만으로 충분하다.
        if (message == WmInput) since.Restart();
        return IntPtr.Zero;
    }

    private const int WmInput = 0x00FF;
    private const uint RawInputSink = 0x00000100;   // RIDEV_INPUTSINK

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInput info);
}
