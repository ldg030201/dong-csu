using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DongCSU.Core.Pet;

namespace DongCSU.App.Hud;

/// <summary>
/// 다른 앱 창이 화면 어디에 있는지 읽는다. 펫을 창 테두리에 붙일 때만 쓴다.
///
/// <b>권한이 하나도 들지 않는다.</b> <c>EnumWindows</c> · <c>GetWindowRect</c> ·
/// <c>DwmGetWindowAttribute</c> 는 같은 데스크톱의 창이면 아무것도 묻지 않는다.
/// 맥판이 <c>CGWindowListCopyWindowInfo</c> 로 같은 자리를 채운다.
///
/// <b>창 제목을 안 본다.</b> 맥은 못 보고(권한이 걸린다) 여기서는 볼 수 있지만, 두 판이
/// 같은 판단을 하게 두려고 안 본다 — 제목으로 거르기 시작하면 어느 창에 붙느냐가
/// 판마다 달라진다.
///
/// <b>고르는 셈은 여기 없다.</b> 그건 <c>Core</c> 의 <see cref="PerchFinder"/> 가 하고
/// 가짜 창 목록으로 테스트된다. 여기는 <b>목록을 뜨는 일</b>만 한다.
/// </summary>
internal static partial class WindowSurvey
{
    /// <summary>
    /// 지금 화면에 떠 있는 <b>보통 앱 창들.</b> 앞에 있는 것이 먼저 온다.
    ///
    /// <c>EnumWindows</c> 는 z 순서를 위에서 아래로 훑는다 — 맥의
    /// <c>.optionOnScreenOnly</c> 가 보장하는 것과 같은 순서라, 가림 판정
    /// (<see cref="PerchFinder"/> 가 앞선 것만 본다)이 그대로 옮겨진다.
    /// </summary>
    /// <param name="stage">
    /// 좌표 계수를 여기서 받는다. <b>커서·작업 영역과 같은 계수를 봐야 한다</b> —
    /// 인자로 흘려보내면 언젠가 한쪽만 다른 값을 받는다.
    /// </param>
    /// <param name="self">우리 HUD 창 핸들. 목록에서 뺀다.</param>
    public static IReadOnlyList<PerchWindow> OnScreenWindows(PetStage stage, IntPtr self)
    {
        var (scaleX, scaleY) = stage.DeviceToDip();
        return Collect(self, scaleX, scaleY);
    }

    /// <summary>
    /// 창 없이 부르는 판. <b>진단 통로 전용</b>(<c>--probe-perch windows</c>)이다 —
    /// 거기서는 앱을 안 띄우므로 환산 계수를 읽을 창이 없다.
    ///
    /// <b>1:1 로 본다.</b> 재려는 것이 "어떤 창이 목록에 남는가" 라 단위가 한 벌로만
    /// 맞으면 되고, 실제 앱은 위쪽을 탄다.
    /// </summary>
    public static IReadOnlyList<PerchWindow> OnScreenWindowsRaw() =>
        Collect(IntPtr.Zero, 1, 1);

    private static IReadOnlyList<PerchWindow> Collect(IntPtr self, double scaleX, double scaleY)
    {
        var found = new List<PerchWindow>(16);
        var ownPid = (uint)Environment.ProcessId;
        var scale = (scaleX, scaleY);

        Native.EnumWindows((hwnd, _) =>
        {
            // **이름은 걸러내기가 이미 알아낸 것을 받는다.** 예전에는 창 하나마다
            // pid 를 세 번 물었다 — 걸러내기가 한 번, 그 안의 이름 조회가 또 한 번,
            // 목록에 담을 때 다시 한 번. 끄는 동안 30Hz 로 창 수만큼 도는 자리다.
            if (!Keep(hwnd, self, ownPid, out var owner)) return true;
            if (VisibleFrame(hwnd) is not { } rect) return true;

            var frame = PetStage.ToDip(rect, scale);

            // 띠·조각 창은 뺀다. 한 앱이 진짜 창과 띠를 둘 다 올리는 일이 흔하고,
            // 띠에 붙으면 붙은 게 아니라 걸쳐진 것으로 보인다.
            // **DIP 로 바꾼 뒤에 잰다** — 물리 픽셀로 재면 배율 150% 화면에서 문턱이
            // 1.5배로 커진다.
            if (frame.Width < PerchFinder.MinimumWindow.Width) return true;
            if (frame.Height < PerchFinder.MinimumWindow.Height) return true;

            found.Add(new PerchWindow(hwnd, frame, owner));
            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// 목록에 남길 창인지. <b>맥의 걸러내기와 한 줄씩 짝지어 뒀다.</b>
    /// </summary>
    /// <param name="owner">
    /// 남길 창이면 그 창을 띄운 프로세스 이름. <b>여기서 같이 낸다</b> — 이름을 알아내려면
    /// pid 가 필요한데 걸러내기가 이미 물어봤다.
    /// </param>
    private static bool Keep(IntPtr hwnd, IntPtr self, uint ownPid, out string owner)
    {
        owner = "";
        if (hwnd == self) return false;
        if (!Native.IsWindowVisible(hwnd)) return false;

        // **윈도우에만 있는 함정.** 최소화된 창도 `IsWindowVisible` 이 참이고
        // `GetWindowRect` 가 (-32000, -32000) 을 준다 — 안 거르면 화면 밖 유령이
        // 목록을 채우고, 그중 하나가 제일 가까운 것으로 뽑힐 수도 있다.
        if (Native.IsIconic(hwnd)) return false;

        // 도구 팔레트 · 트레이 딸림 창. 맥의 `layer != 0` 자리다.
        if ((Native.GetWindowLong(hwnd, Native.GwlExStyle) & Native.WsExToolWindow) != 0)
        {
            return false;
        }

        // **가장 중요하다.** 다른 가상 데스크톱의 창과 잠들어 있는 UWP 앱
        // (`ApplicationFrameHost`)이 여기 걸린다. 맥의 "다른 스페이스로 넘어간 창이
        // 목록에서 사라진다" 와 정확히 같은 자리다 — 안 거르면 **아무것도 없는 자리에
        // 매달린 것으로 보인다.**
        if (Native.DwmGetWindowAttribute(
                hwnd, Native.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        // 배경화면 · 작업 표시줄. 맥의 `.excludeDesktopElements` 자리다.
        if (IsShell(hwnd)) return false;

        // **진짜 방어선.** HUD 창은 `WS_EX_TOOLWINDOW` 라 위에서 저절로 빠지지만
        // **설정 창은 보통 창이다.** 빼먹으면 설정 창을 여는 순간 펫이 제 설정 창에
        // 매달린다 — 맥이 같은 이유로 pid 를 본다.
        _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == ownPid) return false;

        // **이름으로도 한 번 더 뺀다.** pid 만 보면 **우리 앱의 다른 판**이 남는다 —
        // 정식판과 테스트판을 같이 띄워 비교하는 것이 이 저장소의 개발 방식이라
        // (`win/CLAUDE.md`), 안 빼면 테스트판 펫이 정식판 설정 창에 매달린다.
        // 맥이 `kCGWindowOwnerName != AppInfo.name` 으로 같은 일을 한다.
        owner = ProcessName(pid);
        if (owner.StartsWith("DongCSU", StringComparison.OrdinalIgnoreCase)) return false;

        // 거의 투명한 창. 못 읽으면(레이어드가 아니면) 불투명으로 본다.
        if (Native.GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags)
            && (flags & Native.LwaAlpha) != 0 && alpha <= 2)
        {
            return false;
        }

        return true;
    }

    /// <summary>배경화면·작업 표시줄·셸 조각인지. 클래스 이름으로 가른다.</summary>
    private static bool IsShell(IntPtr hwnd)
    {
        var name = ClassName(hwnd);
        return name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "Windows.UI.Core.CoreWindow" or "MultitaskingViewFrame";
    }

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(64);
        var length = Native.GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : "";
    }

    /// <summary>
    /// 창이 <b>눈에 보이는</b> 사각형(물리 픽셀).
    ///
    /// <b><c>GetWindowRect</c> 를 그대로 쓰면 안 된다.</b> 윈도우 10 부터 DWM 이 창
    /// 바깥에 <b>보이지 않는 잡는 테두리</b>를 두는데, <c>GetWindowRect</c> 는 그것까지
    /// 센다 — 96DPI 기준 좌·우·아래로 각각 7~8px 이고 배율을 따라 커진다. 위쪽에는
    /// 없어서 <b>네 변이 서로 다르게 어긋난다.</b>
    ///
    /// 그대로 붙이면 왼쪽·오른쪽·아래에서 펫이 창에서 8px 떠 있고 위에서만 맞는다 —
    /// "왜 옆에 붙으면 뜨지" 로 보이는데 원인이 우리 계산이 아니라서 찾기 어렵다.
    ///
    /// <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> 가 <b>보이는</b> 사각형을 준다. DWM 이 꺼져
    /// 있거나 창이 아직 합성되기 전이면 실패하므로 그때만 <c>GetWindowRect</c> 로 떨어진다.
    /// </summary>
    private static NativeStage.NativeRect? VisibleFrame(IntPtr hwnd)
    {
        if (Native.DwmGetWindowAttributeRect(
                hwnd, Native.DwmwaExtendedFrameBounds,
                out var bounds, Marshal.SizeOf<NativeStage.NativeRect>()) == 0)
        {
            return bounds;
        }
        return Native.GetWindowRect(hwnd, out var raw) ? raw : null;
    }

    /// <summary>
    /// 그 창을 띄운 프로세스 이름. <b>창 제목이 아니다</b> — 붙는 데 안 쓰고 진단이
    /// 사람에게 보여줄 뿐이라, 맥과 같은 것(앱 이름)을 보여준다.
    ///
    /// 캐시한다. 붙어 있는 내내 0.25초마다 목록을 뜨는데 그때마다
    /// <c>Process.GetProcessById</c> 를 부르면 창 수만큼 핸들을 연다.
    /// </summary>
    private static readonly Dictionary<uint, string> Names = [];

    private static string ProcessName(uint pid)
    {
        if (Names.TryGetValue(pid, out var cached)) return cached;

        string name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            // 그새 끝난 프로세스. 이름이 없다고 붙기를 막을 이유는 없다.
            name = "?";
        }

        // **캐시가 무한히 자라지 않게 한다.** pid 는 재사용되므로 오래 두면 엉뚱한
        // 이름이 남는데, 어차피 진단 표시용이라 가끔 비워도 탈이 없다.
        if (Names.Count > 256) Names.Clear();
        Names[pid] = name;
        return name;
    }

    /// <summary>
    /// 우리 창이 그 창보다 <b>앞에 있는지.</b> 둘 중 하나라도 못 찾으면 null.
    ///
    /// <b><see cref="OnScreenWindows"/> 로는 못 본다</b> — 거기는 우리 프로세스를
    /// 통째로 걸러낸다. 그래서 여기서만 걸러내지 않은 순회를 돈다.
    ///
    /// 이게 필요한 이유: 붙어 있는 동안 펫은 <c>Topmost</c> 를 내려놓는데, 사용자가
    /// <b>이미 맨 앞인 창을 한 번 더 누르면</b> 그 창이 우리 위로 올라온다. 그때는
    /// "뒤 → 앞 전이" 가 없어서 올리는 조건에 안 걸리고, 창 안으로 넘어간 다리·날개가
    /// 그대로 창 뒤에 묻힌다 — <b>잡고 있는 것으로 안 보인다.</b>
    /// </summary>
    public static bool? IsAhead(IntPtr mine, IntPtr other)
    {
        var mineRank = -1;
        var otherRank = -1;
        var rank = 0;

        Native.EnumWindows((hwnd, _) =>
        {
            if (hwnd == mine && mineRank < 0) mineRank = rank;
            if (hwnd == other && otherRank < 0) otherRank = rank;
            rank++;
            return mineRank < 0 || otherRank < 0;
        }, IntPtr.Zero);

        if (mineRank < 0 || otherRank < 0) return null;
        return mineRank < otherRank;
    }

    /// <summary>
    /// 지금 누군가 창을 끌고 있는지. 그러면 촘촘히 따라간다.
    ///
    /// 맥은 <c>secondsSinceLastEventType(.leftMouseDragged) &lt; 0.3</c> 을 보지만
    /// 윈도우에는 그 통로가 없다. <b>왼쪽 버튼이 눌려 있는지</b>가 같은 뜻이고 더
    /// 곧다 — 끌고 있으면 눌려 있다.
    /// </summary>
    public static bool IsDraggingSomething =>
        (Native.GetAsyncKeyState(Native.VkLButton) & 0x8000) != 0;

    internal static partial class Native
    {
        public const int GwlExStyle = -20;
        public const int WsExToolWindow = 0x00000080;
        public const int DwmwaExtendedFrameBounds = 9;
        public const int DwmwaCloaked = 14;
        public const int VkLButton = 0x01;
        public const uint LwaAlpha = 0x02;

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

        // **여기만 `DllImport` 다.** `LibraryImport` 의 소스 생성기는 델리게이트와
        // `StringBuilder` 를 마샬링하지 못한다.
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int max);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(IntPtr hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsIconic(IntPtr hwnd);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static partial int GetWindowLong(IntPtr hwnd, int index);

        [LibraryImport("user32.dll")]
        public static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowRect(IntPtr hwnd, out NativeStage.NativeRect rect);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetLayeredWindowAttributes(
            IntPtr hwnd, out uint key, out byte alpha, out uint flags);

        [LibraryImport("user32.dll")]
        public static partial short GetAsyncKeyState(int key);

        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        public static partial int DwmGetWindowAttribute(
            IntPtr hwnd, int attribute, out int value, int size);

        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        public static partial int DwmGetWindowAttributeRect(
            IntPtr hwnd, int attribute, out NativeStage.NativeRect value, int size);
    }
}
