namespace DongCSU.Core.Owl;

/// <summary>
/// 그림 한 장으로 그리는 마스코트의 한 칸.
///
/// **맥 <c>MascotSprite.swift</c> 를 옮겨 적은 것이다.** 이름과 배치를 바꾸면 이미
/// 그려진 시트가 통째로 깨진다 — 맥에서 고치면 여기도 같이 고쳐야 한다.
/// </summary>
public enum MascotSprite
{
    Idle, Blink, Sleepy, BlinkSleepy, Exhausted,
    WalkA, WalkB, WalkSleepyA, WalkSleepyB, RunA, RunB,
    Held, BlinkHeld, Ledge, BlinkLedge, Cling, BlinkCling,
    Sit, BlinkSit, Dizzy, Dead,
}

/// <summary>
/// 칸 안에서 그림이 붙어 있는 모서리. 맥 <c>MascotAnchor</c> 와 같다.
/// </summary>
public enum MascotAnchor
{
    Top,
    Bottom,
    /// <summary>왼쪽 끝. 세로 모서리에 옆으로 붙는 자세가 쓴다.</summary>
    Leading,
}

/// <summary>
/// 규격 시트의 배치와 칸 자리.
///
/// **좌표를 파일로 받지 않는다.** 크기를 못 박고 칸을 선으로 갈라 두면 좌표가 계산으로
/// 나온다 — 맥이 <c>canonicalAtlas</c> 로 하는 것과 같은 산식이다.
///
/// 6×4 칸, 한 칸 256, 칸 사이와 바깥에 1픽셀 선. 그래서 시트는 1543×1029 이다.
/// **선은 어느 칸에도 안 들어간다** — 칸 자리를 그만큼 밀어 두었다.
/// </summary>
public static class MascotSheet
{
    /// <summary>한 칸의 한 변(픽셀).</summary>
    public const int Cell = 256;

    /// <summary>칸 사이와 바깥에 두르는 선의 굵기(픽셀).</summary>
    public const int Rule = 1;

    /// <summary>
    /// 어느 칸에 무엇이 앉는지. **순서를 바꾸지 마라.**
    ///
    /// 줄마다 뜻이 있다 — 서 있기 · 움직이기 · 매달리기 · 그 밖.
    /// <c>null</c> 은 안 그려도 되는 빈 칸이고 줄 끝에 몰려 있다.
    /// </summary>
    public static readonly MascotSprite?[][] Layout =
    [
        [MascotSprite.Idle, MascotSprite.Blink, MascotSprite.Sleepy, MascotSprite.BlinkSleepy, MascotSprite.Exhausted, null],
        [MascotSprite.WalkA, MascotSprite.WalkB, MascotSprite.WalkSleepyA, MascotSprite.WalkSleepyB, MascotSprite.RunA, MascotSprite.RunB],
        [MascotSprite.Held, MascotSprite.BlinkHeld, MascotSprite.Ledge, MascotSprite.BlinkLedge, MascotSprite.Cling, MascotSprite.BlinkCling],
        [MascotSprite.Sit, MascotSprite.BlinkSit, MascotSprite.Dizzy, MascotSprite.Dead, null, null],
    ];

    public static int Columns => Layout[0].Length;
    public static int Rows => Layout.Length;

    /// <summary>규격 시트의 크기. 그린 그림이 이 크기(또는 정수배)면 좌표가 바로 맞는다.</summary>
    public static int SheetWidth => Columns * Cell + (Columns + 1) * Rule;
    public static int SheetHeight => Rows * Cell + (Rows + 1) * Rule;

    /// <summary>칸 하나가 시트의 어디에 있는지. 배율은 시트가 규격의 몇 배인지다.</summary>
    public static (int X, int Y, int Side) Box(MascotSprite sprite, int multiple = 1)
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                if (Layout[row][column] != sprite) continue;

                var pitch = (Cell + Rule) * multiple;
                return (Rule * multiple + column * pitch, Rule * multiple + row * pitch, Cell * multiple);
            }
        }
        throw new ArgumentOutOfRangeException(nameof(sprite), sprite, "시트 배치에 없는 칸이다.");
    }

    /// <summary>
    /// 안 그려진 칸을 대신할 칸. 없으면 null 이고 그때는 아무것도 안 그린다.
    ///
    /// **걷기는 걷기로 떨어진다.** 서 있는 자세로 떨어뜨리면 한 박자마다 멈칫한다.
    /// </summary>
    public static MascotSprite? Fallback(MascotSprite sprite) => sprite switch
    {
        MascotSprite.Idle => null,
        MascotSprite.Sleepy or MascotSprite.WalkA or MascotSprite.Held
            or MascotSprite.Dead or MascotSprite.Sit => MascotSprite.Idle,
        MascotSprite.Exhausted => MascotSprite.Sleepy,
        // 정면 대칭이면 B 가 A 를 뒤집은 것과 같아 비워도 된다.
        // **자동으로 뒤집지 않는다** — 옆모습에서 그러면 반대쪽을 보고 걷는다.
        MascotSprite.WalkB => MascotSprite.WalkA,
        MascotSprite.WalkSleepyA => MascotSprite.WalkA,
        MascotSprite.WalkSleepyB => MascotSprite.WalkSleepyA,
        MascotSprite.RunA => MascotSprite.WalkA,
        MascotSprite.RunB => MascotSprite.WalkB,
        MascotSprite.Cling => MascotSprite.Ledge,
        MascotSprite.BlinkCling => MascotSprite.Cling,
        MascotSprite.Dizzy => MascotSprite.Held,
        MascotSprite.BlinkSit => MascotSprite.Sit,
        MascotSprite.BlinkHeld => MascotSprite.Held,
        MascotSprite.BlinkLedge => MascotSprite.Ledge,
        MascotSprite.Ledge => MascotSprite.Held,
        MascotSprite.Blink => MascotSprite.Idle,
        MascotSprite.BlinkSleepy => MascotSprite.Sleepy,
        _ => MascotSprite.Idle,
    };

    /// <summary>
    /// 이 자세에서 눈을 감으면 어느 칸인가. 없으면 그 자세에서는 안 깜빡인다.
    /// </summary>
    public static MascotSprite? Blinking(MascotSprite sprite) => sprite switch
    {
        MascotSprite.Idle => MascotSprite.Blink,
        MascotSprite.Sleepy => MascotSprite.BlinkSleepy,
        MascotSprite.Held => MascotSprite.BlinkHeld,
        MascotSprite.Ledge => MascotSprite.BlinkLedge,
        MascotSprite.Cling => MascotSprite.BlinkCling,
        MascotSprite.Sit => MascotSprite.BlinkSit,
        _ => null,
    };

    /// <summary>
    /// 칸 안에서 어느 모서리에 붙어 있는지. **맥 <c>MascotSprite.anchor</c> 를 옮겼다.**
    ///
    /// **맞닿는 자리가 곧 정보인 자세가 있다.** 가장자리에 매달린 모습은 위 모서리에
    /// 발이 닿아 있어야 "매달렸다"로 읽히고, 조금이라도 떠 있으면 공중에 뜬 것으로
    /// 보인다. 벽에 붙은 모습은 붙잡은 쪽이 왼쪽 끝에 닿아야 한다.
    ///
    /// 나머지는 전부 아래다 — 발이 한 줄에 서야 상태가 바뀔 때 캐릭터가 오르내리지 않는다.
    ///
    /// **이 값은 그릴 때 쓰는 것이 아니라 시트가 그렇게 구워져 있다는 사실이다.**
    /// 맥이 시트를 구울 때(<c>SheetPrep</c>) 이 값으로 자리를 잡아 놓으므로, 그리는
    /// 쪽은 칸을 그대로 옮기기만 하면 된다. <c>--probe-mascot</c> 이 그림에서 잰 것과
    /// 이 표를 맞대 본다 — 어긋나면 표가 틀린 것이다.
    /// </summary>
    public static MascotAnchor Anchor(MascotSprite sprite) => sprite switch
    {
        MascotSprite.Ledge or MascotSprite.BlinkLedge => MascotAnchor.Top,
        MascotSprite.Cling or MascotSprite.BlinkCling => MascotAnchor.Leading,
        _ => MascotAnchor.Bottom,
    };

    /// <summary>
    /// 창 테두리 선이 그림 안쪽 **어디를 지나는지.** 잉크 상자에 대한 비율이다.
    /// **맥 <c>MascotSprite.gripDepth</c> 를 옮겼다.**
    ///
    /// **붙잡는 부위만 창 안으로 넘어간다.** 0 이면 그림이 통째로 테두리 바깥에 딱
    /// 붙는데, 그러면 붙잡는 부위가 테두리에 닿기만 하고 넘어가질 않아서 껴안은 것이
    /// 아니라 벽에 부딪친 것으로 보인다. 반대로 몸까지 겹치면 무엇에 붙어 있는지가
    /// 흐려진다 — 넘어가는 것은 다리 · 발 · 붙잡는 앞다리뿐이다.
    ///
    /// **그리는 쪽에 요구하는 값과 같아야 한다.** <c>docs/characters/prompt.txt</c> 가
    /// "걸터앉기의 아래 15% 는 다리와 발" 이라고 못 박아 두었다.
    ///
    /// 눈감음 짝도 같은 값을 든다 — 붙어 있는 동안 깜빡일 때 자리가 흔들리면 안 된다.
    ///
    /// <b>아직 앱이 안 쓴다.</b> 창에 붙이기(맥 2.5.0)를 옮겨 올 때 쓸 자리라 값만 먼저
    /// 맞춰 둔다 — 그림은 이미 이 값에 맞춰 그려져 있다.
    /// </summary>
    public static double GripDepth(MascotSprite sprite) => sprite switch
    {
        // 다리와 발이 창 안으로. 프롬프트의 "다리는 키의 15%" 와 같은 숫자다.
        MascotSprite.Sit or MascotSprite.BlinkSit => 0.15,
        // 발과 발목만. 몸통이 딸려 들어가면 매달린 것이 아니라 빠진 것으로 보인다.
        MascotSprite.Ledge or MascotSprite.BlinkLedge => 0.15,
        // 뻗은 앞다리 하나가 옆모습 폭에서 차지하는 몫.
        MascotSprite.Cling or MascotSprite.BlinkCling => 0.25,
        // 나머지는 창에 붙지 않는다.
        _ => 0,
    };

    /// <summary>
    /// 바닥에서 **뜬 만큼을 지킬지.**
    ///
    /// 걸음은 다리가 모이는 순간 몸이 가장 높고, 뛸 때는 두 발이 다 뜨는 순간이 있다.
    /// 칸마다 잉크 바닥을 아래줄에 붙이면 그게 통째로 사라진다. 걷기·뛰기끼리 가장
    /// 낮은 것을 땅으로 삼고 나머지는 그만큼 띄운다. 서 있는 자세와 섞지 않는다.
    /// </summary>
    public static bool KeepsLift(MascotSprite sprite) => IsGait(sprite);

    /// <summary>
    /// 가로 자리를 **머리 기준**으로 맞출지.
    ///
    /// 옆모습 걷기는 다리가 앞뒤로 벌어져서 잉크 상자가 그때그때 넓어진다. 상자
    /// 가운데로 맞추면 몸이 앞뒤로 밀리는데, 머리는 걸음 내내 제자리다.
    /// </summary>
    public static bool CentersOnHead(MascotSprite sprite) => IsGait(sprite);

    public static bool IsGait(MascotSprite sprite) => sprite
        is MascotSprite.WalkA or MascotSprite.WalkB
        or MascotSprite.WalkSleepyA or MascotSprite.WalkSleepyB
        or MascotSprite.RunA or MascotSprite.RunB;

    /// <summary>
    /// 기분·눈·걸음에서 칸 하나를 고른다.
    ///
    /// **자세를 만드는 것과 같은 신호에서 나온다** — 격자 부엉이와 그림 마스코트가 서로
    /// 다른 판단을 하면 같은 상황에서 하나는 졸고 하나는 걷는다. 그림 쪽이 훨씬
    /// 성기므로(수백 → 스물) 여기서 뭉뚱그린다.
    /// </summary>
    /// <param name="beat">걸음의 박자. 홀짝으로 두 그림을 번갈아 쓴다.</param>
    /// <param name="restingEyes">
    /// 그 기분이 **평소에** 하고 있는 눈. 평소가 이미 감긴 기분(탈진)은 깜빡일 것이 없다 —
    /// 거기서 실눈을 뜨는 것은 감는 게 아니라 뜨는 것이다.
    /// </param>
    public static MascotSprite Choose(
        OwlMood mood, OwlEyes eyes, PetGaitKind gait, int beat, OwlEyes restingEyes = OwlEyes.Open)
    {
        var and = Base(mood, eyes, gait, beat);

        // **감은 얼굴이 따로 있는 자세만 깜빡인다.** 없으면 뜬 얼굴 그대로다.
        //
        // **완전히 감았을 때만 친다.** 그림에는 중간 단계가 없어서, 반쯤 감긴 칸까지
        // 세면 깜빡임이 두 배 넘게 길어진다 — 잠깐 깜빡이는 게 아니라 질끈 감았다
        // 뜨는 것으로 보인다. 격자 부엉이는 실눈 칸이 따로 있어서 안 그렇다.
        if (eyes != OwlEyes.Closed || restingEyes == OwlEyes.Closed) return and;
        return Blinking(and) ?? and;
    }

    /// <summary>눈을 빼고 본 자세.</summary>
    private static MascotSprite Base(OwlMood mood, OwlEyes eyes, PetGaitKind gait, int beat)
    {
        if (mood == OwlMood.Offline) return MascotSprite.Dead;

        // **기분이 아니라 눈으로 본다.** 끌고 흔드는 동안 기분은 끌림 그대로이고 눈만 풀린다.
        if (eyes == OwlEyes.Dizzy) return MascotSprite.Dizzy;

        // **어느 쪽으로 끄는지 안 본다.** 그림 한 장이라 볼 것이 없다.
        if (gait == PetGaitKind.Dragged) return MascotSprite.Held;

        if (gait is PetGaitKind.Walk or PetGaitKind.Run) return Walk(gait, mood, beat);

        if (mood == OwlMood.Exhausted) return MascotSprite.Exhausted;
        if (mood == OwlMood.Tired) return MascotSprite.Sleepy;
        return MascotSprite.Idle;
    }

    /// <summary>
    /// 걸음의 어느 박자인지.
    ///
    /// **두 박자를 번갈아 쓴다** — A 가 다리를 벌린 순간, B 가 모은 순간이다.
    /// 왼발·오른발 두 장이 아니다. 발만 바꾼 두 장으로는 걷는 것으로 안 읽힌다.
    /// </summary>
    private static MascotSprite Walk(PetGaitKind gait, OwlMood mood, int beat)
    {
        var second = beat % 2 == 1;

        // 쫓겨서 뛸 때는 뛰기 그림. 지친 몸으로 도망치는 것이라 졸림보다 앞선다.
        if (gait == PetGaitKind.Run) return second ? MascotSprite.RunB : MascotSprite.RunA;

        if (mood is OwlMood.Tired or OwlMood.Exhausted)
        {
            return second ? MascotSprite.WalkSleepyB : MascotSprite.WalkSleepyA;
        }
        return second ? MascotSprite.WalkB : MascotSprite.WalkA;
    }
}

/// <summary>
/// 그림을 고를 때 보는 움직임 상태.
///
/// <c>PetGait</c> 에 끌림이 없어서 따로 둔다 — 격자 쪽은 끌림을 자세로 만들지만
/// 그림 쪽은 칸 하나를 고르는 문제라 같은 자리에서 갈린다.
/// </summary>
public enum PetGaitKind { Still, Walk, Run, Dragged }
