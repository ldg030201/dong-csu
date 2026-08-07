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
        AppLog.Write("키 입력 감시를 걸었다 — 마우스 움직임은 '입력'으로 세지 않는다");
    }

    /// <summary>
    /// <c>WM_INPUT</c> 이 왔다. **키보드 것일 때만** 시각을 갱신한다.
    ///
    /// 온 것만으로 갱신하면 안 된다 — 이 창에는 우리 말고도 raw input 을 등록하는 것이
    /// 있다(WPF 의 스타일러스·터치). 그러면 **마우스를 움직이는 내내 시각이 갱신돼서**
    /// 키보드만 듣겠다고 만든 이 클래스가 <c>GetLastInputInfo</c> 와 똑같아진다.
    /// 실제로 그랬고, 그래서 펫이 커서를 영영 안 피했다.
    ///
    /// **머리말만 읽는다**(<c>RID_HEADER</c>). 거기에는 장치 종류만 있고 눌린 키는
    /// 들어 있지도 않다 — 읽지 않는 게 아니라 **가져오지도 않는다.**
    /// </summary>
    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmInput && IsKeyboard(lParam)) since.Restart();
        return IntPtr.Zero;
    }

    private static bool IsKeyboard(IntPtr rawInput)
    {
        var size = (uint)Marshal.SizeOf<RawInputHeader>();
        var header = default(RawInputHeader);

        var read = GetRawInputData(rawInput, RidHeader, ref header, ref size,
            (uint)Marshal.SizeOf<RawInputHeader>());

        // 못 읽었으면 **키보드가 아닌 것으로 본다.** 여기서 실수하면 펫이 안 움직인다.
        return read != unchecked((uint)-1) && header.Type == RimTypeKeyboard;
    }

    private const int WmInput = 0x00FF;
    private const uint RawInputSink = 0x00000100;   // RIDEV_INPUTSINK
    private const uint RidHeader = 0x10000005;      // RID_HEADER
    private const uint RimTypeKeyboard = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr Extra;
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput, uint command, ref RawInputHeader data, ref uint size, uint headerSize);
}
