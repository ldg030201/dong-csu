using DongCSU.Core;

namespace DongCSU.App.Services;

/// <summary>
/// 한 번에 하나만 뜨게 한다.
///
/// <b>이게 없어서 여러 개가 떴다.</b> 이 앱은 트레이에만 뜨고 작업 표시줄에 안 나와서,
/// 이미 떠 있는데도 사람 눈에는 안 켜진 것처럼 보인다 — 그래서 바로가기를 또 누르고,
/// 로그인할 때 자동 시작으로 하나 · 손으로 하나가 겹치기도 한다. 그러면 HUD 가 겹쳐 뜨고,
/// 조회가 두 배로 나가 429 를 부르고, 두 프로세스가 같은 <c>settings.json</c> ·
/// <c>meter.json</c> · <c>token.json</c> 을 번갈아 덮어쓴다(토큰은 회전하므로 한쪽이
/// 갱신하는 순간 다른 쪽 것이 죽는다).
///
/// <b>두 번째로 띄운 것은 먼저 뜬 것을 불러내고 물러난다.</b> 그냥 조용히 꺼지면 눌렀는데
/// 아무 일도 없어서 또 누르게 된다 — 먼저 뜬 쪽이 HUD 나 설정 창을 앞으로 내 준다.
///
/// <b>이름은 어셈블리 이름에서 온다</b>(<see cref="AppInfo.Name"/>). 정식판과 테스트판은
/// 설정·기록이 갈려 있어서 나란히 띄워 비교하는 것이 쓰는 방법이라, 판마다 하나씩은 뜬다.
/// <c>Local\</c> 이라 로그인 세션마다 따로다 — 사용자를 바꿔 들어온 사람의 것까지 막지 않는다.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// 이미 떠 있는 것이 놓아 주기를 기다리는 시간.
    ///
    /// <b>업데이트 직후를 위한 것이다.</b> Velopack 이 새 판을 띄울 때 옛 프로세스가 아직
    /// 끝나는 중일 수 있는데, 거기서 곧바로 물러나면 업데이트하고 나서 앱이 통째로
    /// 사라진다. 진짜로 하나가 떠 있으면 그쪽은 영영 안 놓으므로 이만큼 기다렸다 물러난다
    /// — 그 사이 화면에는 먼저 뜬 쪽이 이미 앞으로 나와 있다.
    /// </summary>
    public static readonly TimeSpan HandoffWait = TimeSpan.FromSeconds(5);

    private readonly Mutex mutex;
    private readonly EventWaitHandle wake;
    private RegisteredWaitHandle? listening;

    private SingleInstance(Mutex mutex, EventWaitHandle wake)
    {
        this.mutex = mutex;
        this.wake = wake;
    }

    /// <summary>
    /// 이 판의 자리를 잡는다. 이미 하나가 떠 있으면 그쪽을 불러내고 null.
    ///
    /// <b>돌려받은 것을 <c>Main</c> 이 끝날 때까지 쥐고 있어야 한다.</b> 놓치면 GC 가
    /// 거둬 가면서 핸들이 닫히고, 그 순간 자리가 비어 다음에 띄운 것이 또 뜬다.
    /// </summary>
    public static SingleInstance? Acquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, $@"Local\{name}.Instance");
        var wake = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Wake");

        if (TryTake(mutex, TimeSpan.Zero)) return new SingleInstance(mutex, wake);

        // **먼저 부르고 그다음에 기다린다.** 진짜로 떠 있는 쪽이면 누른 그 순간에 앞으로
        // 나와야 한다 — 기다린 뒤에 부르면 5초 동안 아무 일도 없어 보인다.
        AppLog.Write("이미 떠 있다 — 그쪽을 앞으로 부르고 물러난다");
        wake.Set();

        if (TryTake(mutex, HandoffWait))
        {
            // 옛 프로세스가 끝나는 중이었다(업데이트 직후). **방금 보낸 부름을 거둔다** —
            // 안 거두면 아무도 안 받은 신호가 남아서, 우리가 뜨자마자 제 부름을 받는다.
            wake.Reset();
            AppLog.Write("앞서 떠 있던 것이 끝나서 자리를 넘겨받았다");
            return new SingleInstance(mutex, wake);
        }

        wake.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// 두 번째로 띄운 것이 부르면 <paramref name="reveal"/> 을 부른다.
    ///
    /// <b>UI 스레드가 아니다</b> — 스레드풀에서 온다. 받는 쪽이 디스패치해야 한다.
    /// </summary>
    public void Listen(Action reveal)
    {
        listening?.Unregister(null);
        listening = ThreadPool.RegisterWaitForSingleObject(
            wake, (_, _) => reveal(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// 자리를 잡는다. <b>앞 프로세스가 놓지 못하고 죽었으면</b>(강제 종료 · 업데이트 중
    /// <c>Environment.Exit</c>) 버려진 자리로 넘어오는데, 그것도 우리 것이다.
    /// </summary>
    private static bool TryTake(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        listening?.Unregister(null);
        listening = null;
        try
        {
            // 잡은 스레드에서만 놓을 수 있다. `Main` 이 잡고 `Main` 이 놓으므로 맞지만,
            // 어긋나도 프로세스가 끝나면 OS 가 놓는다 — 그때는 다음 것이 버려진 자리로 받는다.
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        mutex.Dispose();
        wake.Dispose();
    }
}
