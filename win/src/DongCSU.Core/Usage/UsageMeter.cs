namespace DongCSU.Core.Usage;

/// <summary>
/// 시작·중지를 눌러 그 사이에 얼마나 썼는지 재는 저장소.
///
/// **두 가지를 나란히 잰다.** 서로 메우는 구멍이 다르다.
///
/// | | 잡는 범위 | 눈금 |
/// | --- | --- | --- |
/// | 한도 %p | 계정 전부 — Claude Code·클로드 앱·웹 | 1%p (서버가 정수로 준다) |
/// | 토큰 수 | **Claude Code만** | 토큰 단위 |
///
/// 한도 쪽은 어디서 쓰든 같은 창을 깎아서 전부 잡히는 대신 눈금이 굵고, 토큰 쪽은
/// 촘촘한 대신 클로드 앱에서 쓴 것을 못 본다. 그래서 하나만 두면 답이 안 된다.
///
/// **앱을 껐다 켜도 이어진다** — 상태가 통째로 <see cref="MeterStore"/> 에 오간다.
///
/// <para>
/// <b>타이머를 여기 두지 않는다.</b> <see cref="UsageStore"/> 와 같은 규약이다 — 언제
/// 부를지는 화면 쪽이 정하고 여기는 <see cref="ScanInterval"/> 과
/// <see cref="WantsScanning"/> 만 알려 준다. 그래야 시계 없이 검사할 수 있고, <c>Core</c>
/// 에 WPF 가 안 들어온다.
/// </para>
/// <para>
/// <b>맥의 <c>@MainActor</c> 에 해당하는 것이 없다.</b> 훑기는 스레드풀에서 돌아오고
/// 버튼은 화면 스레드에서 오므로 상태를 만지는 자리는 전부 <c>gate</c> 로 묶는다.
/// 대신 <see cref="Changed"/>·<see cref="SampleWanted"/> 는 **락 밖에서** 쏜다 —
/// 구독자가 되돌아와 같은 락을 잡으면 상태가 반쯤 고쳐진 채로 도는 순서 문제가 생긴다.
/// 두 이벤트 모두 **UI 스레드라고 믿으면 안 된다**(<see cref="UsageStore.Changed"/> 와
/// 같은 규약이라 화면 쪽이 스스로 디스패치한다).
/// </para>
/// </summary>
public sealed class UsageMeter
{
    /// <summary>남겨 두는 기록 수. 넘치면 오래된 것부터 버린다.</summary>
    public const int HistoryLimit = 50;

    /// <summary>
    /// 토큰을 다시 세는 주기. 사용량 조회(기본 5분)에 묶어두면 화면 숫자가 너무 오래
    /// 멈춰 있는다 — 덧붙은 부분만 읽어서 값이 싸므로 따로 짧게 돈다.
    ///
    /// 맥은 여기에 주기의 1/4(15초) 만큼 tolerance 를 줘서 다른 깨우기와 뭉치게 하는데,
    /// <c>DispatcherTimer</c> 에는 대응이 없어서 옮기지 않았다.
    /// </summary>
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 조회를 부탁하는 사이의 바닥.
    ///
    /// 시작·계속·중지를 연달아 누르면 그때마다 조회가 나가는데 사용량 API 는 창이 좁아
    /// 금방 429 가 된다. 어차피 폴링이 곧 가져오므로 건너뛴다.
    /// <see cref="UsageStore.MinFetchInterval"/> 과는 **다른 물건이다** — 저쪽은 조회
    /// 자체의 바닥이고 이건 측정이 조르지 않게 하는 바닥이다.
    /// </summary>
    public static readonly TimeSpan MinSampleInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 이만큼 넘게 움직여야 창이 새로 열린 것으로 본다.
    ///
    /// **초 단위로 견주지 않는다.** <c>resets_at</c> 은 마이크로초까지 오고 서버가 매번
    /// 조금씩 다르게 줄 수 있는데, 그 지터를 리셋으로 세면 표본마다 소모량이 통째로
    /// 더해져 값이 터진다.
    /// </summary>
    public static readonly TimeSpan WindowJitterTolerance = TimeSpan.FromSeconds(60);

    private readonly Lock gate = new();
    private readonly MeterStore? store;
    private readonly TimeProvider time;
    private readonly string? root;
    private readonly bool scansTranscripts;
    private readonly Func<TokenScan, CancellationToken, Task<TokenScanResult>>? scanRunner;

    private MeterState state;
    private bool isScanning;

    /// <summary>다음 표본은 더하지 말고 기준만 옮긴다. 일시정지에서 돌아올 때 선다.</summary>
    /// <remarks>**저장하지 않는다** — 계속을 누른 그 순간에만 유효하다.</remarks>
    private bool needsRebaseline;

    /// <summary>멈춘 뒤 딱 한 번, 늦게 도착하는 마지막 표본을 받아 주는 일회용 티켓.</summary>
    private bool acceptsFinalSample;

    private DateTimeOffset? lastSampleRequestAt;

    /// <param name="store">null 이면 아무것도 읽지도 쓰지도 않는다(맥의 <c>MeterStore(url: nil)</c>).</param>
    /// <param name="time">검사가 시간을 밀 수 있어야 해서 밖에서 받는다.</param>
    /// <param name="transcriptRoot">
    /// 기록 폴더. 안 주면 <see cref="ClaudeCodeUsage.ProjectsDirectory"/> 다 — 검사가 임시
    /// 폴더를 꽂는 자리이고, 안 꽂으면 검사가 사용자의 진짜 기록을 훑는다.
    /// </param>
    /// <param name="scanRunner">
    /// 훑기를 실제로 돌리는 것. **검사 전용 구멍이다** — 안 꽂으면 스레드풀에서 돈다.
    ///
    /// 훑는 도중에 다시 시작·계속을 누르는 상황은 시간을 잡아 세우지 않으면 재현할 수
    /// 없어서, 표식 대조를 검사하려면 이 자리가 열려 있어야 한다. **생성자로만 받는다** —
    /// 프로퍼티로 열어 두면 살아 있는 미터의 훑기를, 그것도 훑는 도중에 락 밖에서 갈아
    /// 끼울 수 있다. <c>store</c>·<c>time</c>·<c>transcriptRoot</c> 와 같은 깊이의 구멍이다.
    /// </param>
    public UsageMeter(
        MeterStore? store = null,
        TimeProvider? time = null,
        string? transcriptRoot = null,
        Func<TokenScan, CancellationToken, Task<TokenScanResult>>? scanRunner = null)
        : this(store, time, transcriptRoot, scanRunner, scansTranscripts: true)
    {
    }

    private UsageMeter(
        MeterStore? store,
        TimeProvider? time,
        string? transcriptRoot,
        Func<TokenScan, CancellationToken, Task<TokenScanResult>>? scanRunner,
        bool scansTranscripts)
    {
        this.store = store;
        this.time = time ?? TimeProvider.System;
        root = transcriptRoot;
        this.scanRunner = scanRunner;
        this.scansTranscripts = scansTranscripts;
        state = store?.Read() ?? new MeterState();
        // **생성자에서 훑지 않는다.** 앱이 뜬 뒤 화면 쪽이 `if (meter.IsRunning)
        // _ = meter.ScanTokensAsync();` 를 부른다 — 꺼져 있는 동안 쌓인 것을 바로 얹지
        // 않으면 1분 동안 화면이 빈다.
    }

    /// <summary>
    /// 렌더·검사용. 파일을 읽지도 쓰지도 않고 값만 꽂는다(맥의 <c>init(preview:)</c>).
    ///
    /// **저장소를 물리지 않는 것이 핵심이다** — 물리면 문서 그림을 한 장 뽑을 때마다
    /// 사용자의 진짜 <c>meter.json</c> 이 고정값으로 덮인다.
    /// </summary>
    public static UsageMeter Preview(MeterState preview, TimeProvider? time = null)
    {
        // **기록도 안 훑는다.** 훑게 두면 `root` 가 null 이라 사용자의 진짜
        // `~/.claude/projects` 를 보는데, 측정 탭은 열릴 때 한 번 훑으므로
        // `--render-settings measure` 나 `--probe-layout` 이 남의 기록 200MB 를 훑고
        // 그 숫자가 고정값 화면에 섞여 든다. **있을 수 없는 경로를 꽂아 두지 않는다** —
        // 그러면 `root` 가 "기본값 · 진짜 경로 · 훑지 말라는 신호" 셋을 겸하게 되고,
        // 그 가짜 경로가 진단 출력이나 기록에 그대로 새어 나간다.
        var meter = new UsageMeter(
            store: null, time: time, transcriptRoot: null, scanRunner: null, scansTranscripts: false);
        meter.state = preview;
        return meter;
    }

    /// <summary>
    /// 지금 상태. **밖으로 나간 것은 다시 안 움직인다** — 고치는 쪽이 복사본을 고쳐
    /// 통째로 갈아 끼우므로, 화면이 이걸 들고 있어도 배경 훑기와 부딪치지 않는다.
    /// </summary>
    public MeterState State
    {
        get { lock (gate) return state; }
    }

    public bool IsRunning => State.IsRunning;
    public bool IsPaused => State.IsPaused;
    public bool IsCounting => State.IsCounting;
    public IReadOnlyList<LimitTrack> TracksInOrder => State.TracksInOrder;

    /// <summary>
    /// 훑기 타이머를 켜 둘 때인지. 화면 쪽이 이걸 보고 <c>DispatcherTimer</c> 를 켰다 껐다 한다.
    ///
    /// <see cref="IsCounting"/> 과 같은 값이지만 이름을 따로 둔다 — 부르는 쪽이 묻는 것은
    /// "지금 세고 있나" 가 아니라 "타이머를 켜 둘까" 이고, 언젠가 답이 갈릴 수 있다.
    /// </summary>
    public bool WantsScanning => IsCounting;

    /// <summary>
    /// 토큰 세는 중. **훑기가 겹쳐 돌지 않게 하는 표시**이고, 지금 이걸 밖에서 읽는 것은
    /// 검사뿐이다 — 화면에는 아직 훑는 중 표시가 없다. 붙이게 되면
    /// <see cref="ScanTokensAsync"/> 가 시작·끝에서 <see cref="Changed"/> 를 쏘게 해야 한다.
    /// </summary>
    public bool IsScanning
    {
        get { lock (gate) return isScanning; }
    }

    public TimeSpan? Elapsed(DateTimeOffset now) => State.Elapsed(now);

    /// <summary>값이 바뀔 때마다 부른다. **UI 스레드가 아닐 수 있다.**</summary>
    public event Action? Changed;

    /// <summary>
    /// 지금 바로 한 번 조회해 달라는 부탁(맥의 <c>onNeedsSample</c>).
    ///
    /// **시작을 누른 순간 기준점을 잡아야 한다.** 다음 폴링까지 기다리면 그동안 실제로
    /// 쓴 것도 기준이 없어서 못 센다. 저장소를 직접 알지 않으려고 이벤트로 끊어 뒀다.
    ///
    /// 받는 쪽은 <c>force</c> 로 쏘지 마라 — force 는 429 백오프를 무시해서 요청 제한을
    /// 더 부른다(실제로 걸렸다).
    /// </summary>
    public event Action? SampleWanted;

    // MARK: - 시작 · 중지

    public void Start()
    {
        var now = time.GetUtcNow();
        // **지금 파일 끝을 기준으로 잡는다.** 0부터 읽으면 며칠 치 옛 기록을 훑게 된다.
        // 파일을 훑는 일이라 락 밖에서 미리 구한다.
        var offsets = EndOffsets();

        MeterState next;
        lock (gate)
        {
            // 앞 측정에서 남은 티켓이 새 측정의 첫 표본을 삼키면 안 된다.
            acceptsFinalSample = false;
            needsRebaseline = false;

            next = new MeterState
            {
                StartedAt = now,
                Offsets = offsets,
                // **지난 기록은 그대로 가져간다.** 버리면 다시 시작 한 번에 여태 쌓은
                // 것이 통째로 날아간다.
                History = [.. state.History],
            };
            state = next;
        }

        Save(next);
        Changed?.Invoke();
        // **여기서 훑지 않는다.** 방금 모든 오프셋을 파일 끝으로 못 박았으니
        // 지금 훑으면 반드시 0을 더한다 — 파일 200개를 열어 보고 빈손으로 돌아올 뿐이다.
        // 진짜로 덧붙는 것은 5초·60초 타이머가 곧 가져온다. (앱이 뜰 때의 한 번은 다른
        // 경우다. 그때는 꺼져 있는 동안 쌓인 것이 오프셋 뒤에 실제로 있다.)

        // **기준점은 지금 값이어야 한다.** 마지막 조회는 몇 분 전 것일 수 있고, 그걸
        // 기준으로 삼으면 시작을 누르기 전에 쓴 몫이 이번 측정에 들어간다.
        RequestSample();
    }

    /// <summary>잠깐 세운다. 세워 둔 동안 쓴 것은 이번 측정에 안 들어간다.</summary>
    public void Pause()
    {
        lock (gate)
        {
            if (!state.IsCounting) return;
        }

        // **세우기 직전까지 쓴 토큰은 담는다.** 기다리지 않고 던져도, 표식에 PausedAt 이
        // 없어서 이 훑기는 돌아왔을 때 버려지지 않는다.
        _ = ScanTokensAsync();

        MeterState next;
        lock (gate)
        {
            if (!state.IsCounting) return;
            next = state.Copy();
            next.PausedAt = time.GetUtcNow();
            state = next;
        }

        Save(next);
        Changed?.Invoke();
    }

    /// <summary>다시 센다. **기준을 지금으로 새로 잡는다** — 세워 둔 동안의 소모는 빼야 한다.</summary>
    public void Resume()
    {
        lock (gate)
        {
            if (state.PausedAt is null) return;
        }

        var offsets = EndOffsets();

        MeterState next;
        lock (gate)
        {
            if (state.PausedAt is not { } pausedAt) return;
            next = state.Copy();
            next.PausedTotal += time.GetUtcNow() - pausedAt;
            next.PausedAt = null;
            // 토큰 오프셋과 한도 기준을 **함께** 옮겨야 세워 둔 동안 다른 데서 쓴 것이
            // 이번 측정에 안 들어간다.
            next.Offsets = offsets;
            needsRebaseline = true;
            state = next;
        }

        Save(next);
        Changed?.Invoke();
        RequestSample();
    }

    public void Stop()
    {
        lock (gate)
        {
            if (!state.IsRunning) return;
            // **멈출 때도 한 번 더 잰다.** 조회 주기가 몇 분인데 그보다 짧게 재고 멈추면
            // 표본이 시작 때 하나뿐이라 소모량이 늘 0%p 가 된다. 시작과 중지에서 각각
            // 한 번씩 재면 아무리 짧게 재도 두 점 사이의 차이가 남는다.
            acceptsFinalSample = true;
        }

        RequestSample();

        MeterState next;
        lock (gate)
        {
            if (!state.IsRunning) return;

            var now = time.GetUtcNow();
            next = state.Copy();
            if (next.PausedAt is { } pausedAt)
            {
                next.PausedTotal += now - pausedAt;
                next.PausedAt = null;
            }
            next.StoppedAt = now;
            // **도착을 기다리지 않고 그 자리에서 남긴다.** 기다렸다 남기면 조회가
            // 실패했을 때 기록이 영영 안 생긴다 — 늦게 온 값은 SyncArchived 가 덮는다.
            ArchiveCurrent(next);
            state = next;
        }

        Save(next);
        Changed?.Invoke();
        // 멈추기 직전에 쓴 것도 들어가야 한다.
        _ = ScanTokensAsync();
    }

    // MARK: - 기록

    /// <summary>목록만 비운다. 재고 있던 것은 건드리지 않는다.</summary>
    public void ClearHistory()
    {
        MeterState next;
        lock (gate)
        {
            next = state.Copy();
            next.History.Clear();
            state = next;
        }

        Save(next);
        Changed?.Invoke();
    }

    /// <summary>기록 하나만 지운다. 시작 시각이 구분자다.</summary>
    public void DeleteRecord(DateTimeOffset startedAt)
    {
        MeterState next;
        lock (gate)
        {
            next = state.Copy();
            if (next.History.RemoveAll(record => record.StartedAt == startedAt) == 0) return;
            state = next;
        }

        Save(next);
        Changed?.Invoke();
    }

    public void DeleteRecord(MeterRecord record) => DeleteRecord(record.StartedAt);

    /// <summary>끝난 측정을 목록 맨 위에 남긴다. 같은 시작 시각이 있으면 갈아 끼운다.</summary>
    private static void ArchiveCurrent(MeterState state)
    {
        if (CurrentRecord(state) is not { } record) return;

        state.History.RemoveAll(old => old.StartedAt == record.StartedAt);
        state.History.Insert(0, record);
        if (state.History.Count > HistoryLimit)
        {
            state.History.RemoveRange(HistoryLimit, state.History.Count - HistoryLimit);
        }
    }

    /// <summary>
    /// 중지 뒤 늦게 도착한 값으로 목록의 그 기록을 갱신한다.
    /// **재는 중에는 아무것도 안 한다** — 아직 남긴 것이 없다.
    /// </summary>
    private static void SyncArchived(MeterState state)
    {
        if (state.IsRunning || CurrentRecord(state) is not { } record) return;

        var index = state.History.FindIndex(old => old.StartedAt == record.StartedAt);
        if (index < 0) return;
        state.History[index] = record;
    }

    /// <summary>
    /// 지금 값을 그대로 얼린 기록.
    ///
    /// **반드시 복사본을 담는다.** <see cref="MeterState.TracksInOrder"/> 가 돌려주는 것은
    /// 살아 있는 <see cref="LimitTrack"/> 참조라, 그대로 담으면 중지 뒤 늦게 온 표본이
    /// 목록 안의 값까지 함께 바꾼다(맥은 struct 라 담는 순간 복사됐다).
    /// </summary>
    private static MeterRecord? CurrentRecord(MeterState state)
    {
        if (state.StartedAt is not { } startedAt || state.StoppedAt is not { } stoppedAt) return null;

        return new MeterRecord
        {
            StartedAt = startedAt,
            StoppedAt = stoppedAt,
            Tracks = [.. state.TracksInOrder.Select(track => track.Clone())],
            Tokens = state.Tokens,
            TokensByModel = new Dictionary<string, TokenTally>(state.TokensByModel, StringComparer.Ordinal),
            Samples = state.Samples,
        };
    }

    // MARK: - 한도

    /// <summary>
    /// 조회가 성공할 때마다 부른다(<see cref="UsageStore.SnapshotReceived"/> 에 붙는다).
    ///
    /// **첫 표본은 기준점일 뿐이다.** 거기서 더하면 재기 시작한 순간 여태 쓴 것이 전부
    /// 이번 측정치로 들어간다.
    /// </summary>
    public void Record(UsageSnapshot snapshot)
    {
        MeterState next;
        lock (gate)
        {
            // 일시정지 중에는 안 받는다. 중지 뒤 늦게 오는 마지막 표본만 티켓으로 통과한다.
            if (!state.IsCounting && !acceptsFinalSample) return;
            // 티켓은 한 장뿐이라 쓰는 자리에서 곧바로 내린다.
            if (!state.IsCounting) acceptsFinalSample = false;

            next = state.Copy();
            foreach (var limit in LimitsOf(snapshot))
            {
                if (!next.Tracks.TryGetValue(limit.Id, out var track))
                {
                    // 처음 보는 한도. 견줄 앞 값이 없으니 빈 트랙에 기준만 얹는다.
                    next.Tracks[limit.Id] = Baseline(new LimitTrack(), limit);
                    next.Order.Add(limit.Id);
                    continue;
                }

                // 계속을 누른 뒤 첫 표본도 **처음 보는 한도와 똑같은 일**이다 — 세워 둔
                // 동안 늘어난 몫은 이번 측정이 쓴 것이 아니라 누적하지 않는다.
                next.Tracks[limit.Id] = needsRebaseline
                    ? Baseline(track, limit)
                    : Advance(track, limit);
            }

            // **루프 밖에서 내린다.** 한도가 여럿인데 안에서 내리면 첫 한도만 재기준된다.
            needsRebaseline = false;

            next.Samples++;
            next.LastSampledAt = snapshot.FetchedAt;
            SyncArchived(next);
            state = next;
        }

        Save(next);
        Changed?.Invoke();
    }

    /// <summary>
    /// 표본 하나를 트랙에 반영한다. 갈래가 셋이다.
    ///
    /// <list type="bullet">
    /// <item>창이 새로 열렸으면 **새 값을 통째로 더하고** 리셋을 하나 센다 — 5시간 창은
    /// 재는 도중에 반드시 한 번은 열리고 그때 값이 0으로 떨어져서, 그냥 빼면 그때까지의
    /// 기록이 날아간다. 더하는 값이 새 percent 그대로인 것은 그것이 **새 창에서 이미 쓴
    /// 몫**이기 때문이다.</item>
    /// <item>창이 그대로인데 값이 올랐으면 차이만 더한다.</item>
    /// <item>창이 그대로인데 값이 내려갔으면 서버 쪽 보정이라 **아무것도 더하지 않고**
    /// 기준만 옮긴다.</item>
    /// </list>
    ///
    /// 파일도 시계도 안 타는 **순수 계산**이고 인자를 고치지 않는다 — 진단 통로와 검사가
    /// 인스턴스 없이 이걸 그대로 부른다.
    /// </summary>
    public static LimitTrack Advance(LimitTrack track, UsageLimit limit)
    {
        var next = track.Clone();

        if (WindowMoved(track.LastResetsAt, limit.ResetsAt))
        {
            next.Accumulated += limit.Percent;
            next.Resets++;
        }
        else if (limit.Percent > track.LastPercent)
        {
            next.Accumulated += limit.Percent - track.LastPercent;
        }

        next.LastPercent = limit.Percent;
        next.LastResetsAt = limit.ResetsAt;
        next.Title = limit.Title;
        return next;
    }

    /// <summary>
    /// **누적하지 않고 기준만 옮긴다.** <see cref="Advance"/> 와 짝이고, 두 자리에서 쓴다 —
    /// 처음 보는 한도(견줄 앞 값이 없다)와 계속을 누른 뒤 첫 표본(세워 둔 동안 늘어난 몫은
    /// 이번 측정 것이 아니다). 사용자가 겪는 상황은 다르지만 하는 일이 글자 그대로 같아서
    /// 한 함수다.
    ///
    /// <see cref="Advance"/> 와 마찬가지로 **인자를 고치지 않고 새 트랙을 돌려준다.**
    /// 제자리에서 고치면 밖으로 나간 상태나 목록에 남긴 기록이 함께 움직일 수 있고,
    /// 안 그런 이유를 매번 주석으로 해명해야 한다.
    /// </summary>
    public static LimitTrack Baseline(LimitTrack track, UsageLimit limit)
    {
        var next = track.Clone();

        next.LastPercent = limit.Percent;
        next.LastResetsAt = limit.ResetsAt;
        // 이름도 함께 옮긴다 — 서버가 이름을 바꾸면 따라가야 하고, 여기만 빼놓으면
        // 계속을 누른 측정에서 이름 하나가 옛것으로 남는다.
        next.Title = limit.Title;
        return next;
    }

    /// <summary>
    /// 창이 새로 열렸는지. 한쪽이라도 없으면 **리셋으로 안 센다** — 서버가 시각을 빠뜨린
    /// 표본 하나 때문에 소모량이 통째로 더해지면 안 된다.
    /// </summary>
    private static bool WindowMoved(DateTimeOffset? old, DateTimeOffset? now)
        => old is { } before && now is { } after
            && (after - before).Duration() > WindowJitterTolerance;

    /// <summary>
    /// 이 표본에서 셀 한도들.
    ///
    /// **옛 서버 응답에는 <c>limits</c> 가 없다.** 그때는 HUD 가 쓰는 두 창으로
    /// <c>session</c>·<c>weekly_all</c> 을 지어낸다 — 이 폴백이 없으면 옛 서버에서 측정이
    /// 통째로 빈다.
    /// </summary>
    public static IReadOnlyList<UsageLimit> LimitsOf(UsageSnapshot snapshot)
    {
        if (snapshot.Limits.Count > 0) return snapshot.Limits;

        var fallback = new List<UsageLimit>(2);
        if (snapshot.FiveHour is { } fiveHour)
        {
            fallback.Add(new UsageLimit
            {
                Kind = "session",
                Percent = fiveHour.Utilization,
                ResetsAt = fiveHour.ResetsAt,
            });
        }
        if (snapshot.SevenDay is { } sevenDay)
        {
            fallback.Add(new UsageLimit
            {
                Kind = "weekly_all",
                Percent = sevenDay.Utilization,
                ResetsAt = sevenDay.ResetsAt,
            });
        }
        return fallback;
    }

    // MARK: - 토큰

    /// <summary>
    /// 기록 파일에서 덧붙은 부분만 읽어 토큰을 더한다.
    ///
    /// **겹쳐 돌지 않는다**(<see cref="IsScanning"/>). 잠들었다 깨면 타이머 Tick 이 몰릴
    /// 수 있는데 그 가드가 막아 준다. 예외는 전부 여기서 삼키고 기록에만 남긴다 —
    /// 화면 쪽이 <c>_ = ScanTokensAsync()</c> 로 던져 두면 아무도 안 보기 때문이다.
    /// </summary>
    public async Task ScanTokensAsync(CancellationToken cancellationToken = default)
    {
        TokenScan scan;
        SessionStamp stamp;

        lock (gate)
        {
            if (state.StartedAt is not { } since) return;
            if (isScanning) return;
            // 세워 둔 동안 쓴 것은 세지 않는다. 다만 중지 직후의 마지막 훑기는 통과시킨다.
            if (!state.IsCounting && state.IsRunning) return;
            if (!RecordsAvailable) return;

            isScanning = true;
            // **락 안에서 만든다.** 오프셋·본 id·시작 시각·표식을 **같은 상태 하나**에서
            // 떠 와야 서로 어긋나지 않는다.
            scan = new TokenScan(since, state.Offsets, state.SeenIds, root);
            // 훑기를 시작한 시점의 측정이 무엇이었는지 적어 둔다. 돌아왔을 때 대조한다.
            stamp = state.Stamp;
        }

        // **여기서 `Changed` 를 쏘지 않는다.** 알릴 값이 `IsScanning` 뿐인데 그걸 읽는
        // 화면이 아직 없어서, 이 한 줄이 측정 탭을 통째로 다시 만드는 값만 낸다
        // (5초 훑기 × 12회/분). 나중에 훑는 중 스피너를 붙이게 되면 그때 되살린다.

        MeterState? next = null;
        try
        {
            var runner = scanRunner ?? DefaultScanRunner;
            // 파일 읽기는 화면 밖으로 내보낸다. Core 는 SynchronizationContext 가 있는지
            // 가정하면 안 되므로 `false` 로 받고 상태는 락으로 지킨다.
            var result = await runner(scan, cancellationToken).ConfigureAwait(false);

            lock (gate)
            {
                // **훑는 사이에 딴 측정이 됐으면 통째로 버린다.** 안 버리면 옛 토큰이 새
                // 측정에 더해질 뿐 아니라 **새로 잡아 둔 오프셋이 옛 자리로 되감기고**,
                // 한 번 되감기면 그 뒤 모든 훑기가 같은 구간을 다시 읽어 값이 계속 커진다.
                if (stamp != state.Stamp) return;

                // 아무것도 안 움직였으면 얹을 것이 없다. 재는 동안 대부분의 훑기가 여기서
                // 돌아선다 — 그때마다 새 상태를 만들어 저장하면 **바이트까지 같은 파일**을
                // 1분에 열두 번 다시 쓴다.
                if (!result.Moved) return;

                next = Applying(result, state);
                SyncArchived(next);
                state = next;
            }
        }
        catch (Exception error)
        {
            AppLog.Write($"측정: 토큰 훑기 실패 — {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            lock (gate) isScanning = false;
            // **바뀐 것이 있을 때만 알린다.** 버렸거나 던졌으면 상태가 그대로라 알릴 것이 없다.
            if (next is not null)
            {
                Save(next);
                Changed?.Invoke();
            }
        }
    }

    /// <summary>
    /// 훑은 결과를 여태 쌓은 것 위에 얹는다.
    ///
    /// **인자를 고치지 않고 새 상태를 돌려준다.** 얹기를 여기 한 곳에 모아 두는 것이
    /// 핵심이다 — 진단 통로(<c>--probe-meter scan</c>)도 <see cref="ScanTokensAsync"/> 를
    /// 거쳐 결국 이 함수를 타므로 확인 통로와 실제 동작이 갈라질 수 없다.
    ///
    /// 오프셋과 본 id 는 결과의 것으로 **통째로 갈아 끼운다**(<see cref="TokenScanResult"/>
    /// 가 델타가 아니라 전체를 담는다). 그래서 **베끼지 않고 그대로 들인다** — 결과는 이
    /// 훑기 한 번의 것이라 얹고 나면 아무도 안 보고, 베껴 봐야 그 자리에서 버려진다.
    /// </summary>
    public static MeterState Applying(TokenScanResult result, MeterState state)
    {
        var next = state.CopyAdopting(result.Offsets, result.SeenIds);

        var (tokens, byModel) = TokenScanApply.Applying(result, state.Tokens, state.TokensByModel);
        next.Tokens = tokens;
        next.TokensByModel = byModel;
        return next;
    }

    private static Task<TokenScanResult> DefaultScanRunner(TokenScan scan, CancellationToken cancellationToken)
        => Task.Run(scan.Run, cancellationToken);

    /// <summary>
    /// 기록 폴더가 있는지. 없으면 아예 안 훑는다(WSL 만 쓰는 사람이 여기 걸린다).
    /// 고정값 미터(<see cref="Preview"/>)는 폴더가 있든 없든 안 훑는다.
    /// </summary>
    private bool RecordsAvailable
        => scansTranscripts && (root is null ? ClaudeCodeUsage.IsAvailable : Directory.Exists(root));

    /// <summary>
    /// 지금 파일 끝. 시작·계속이 기준을 잡는 자리다.
    ///
    /// 고정값 미터는 빈 사전을 받는다 — <see cref="RecordsAvailable"/> 만 막으면
    /// 여기서 사용자의 진짜 기록 폴더를 걸어 다닌다.
    /// </summary>
    private Dictionary<string, long> EndOffsets()
        => scansTranscripts
            ? ClaudeCodeUsage.EndOffsets(root)
            : new Dictionary<string, long>(ClaudeCodeUsage.PathComparer);

    // MARK: - 표본 부탁 · 저장

    private void RequestSample()
    {
        bool wanted;
        lock (gate)
        {
            var now = time.GetUtcNow();
            wanted = lastSampleRequestAt is not { } last || now - last >= MinSampleInterval;
            if (wanted) lastSampleRequestAt = now;
        }

        if (wanted) SampleWanted?.Invoke();
    }

    /// <summary>
    /// 바뀔 때마다 곧바로 쓴다. 상태는 이미 밖으로 내보낸 뒤라 여기서 다시 안 움직이므로
    /// **락 밖에서** 써도 안전하다 — 직렬화가 훑기를 붙잡고 있을 이유가 없다.
    /// </summary>
    private void Save(MeterState snapshot) => store?.Write(snapshot);
}
