using DongCSU.Core.Pet;

namespace DongCSU.Core.Tests;

/// <summary>
/// 마구 흔들면 어지러워한다.
///
/// **천천히 옮기는 것만으로는 절대 안 쌓여야 한다** — 창을 자리 잡으려고 조금씩 미는
/// 동안 어지러워지면 성가시다.
/// </summary>
public class PetShakeTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>좌우로 <paramref name="times"/> 번 흔든다. 어지러워졌으면 true.</summary>
    private static bool Shake(PetShake shake, MovingTime clock, int times, double distance)
    {
        var dizzy = false;
        var x = 500.0;
        for (var i = 0; i < times; i++)
        {
            x += i % 2 == 0 ? distance : -distance;
            clock.Advance(TimeSpan.FromMilliseconds(16));
            if (shake.Sample(new PetPoint(x, 300))) dizzy = true;
        }
        return dizzy;
    }

    [Fact]
    public void 마구_흔들면_어지러워한다()
    {
        var clock = new MovingTime(Start);
        var shake = new PetShake(clock);
        shake.Begin();

        // 16ms 마다 30 씩 좌우 → 1875 pt/s. 뒤집힘으로 친다.
        Assert.True(Shake(shake, clock, 12, 30));
        Assert.True(shake.IsDizzy);
    }

    [Fact]
    public void 천천히_옮기면_어지러워하지_않는다()
    {
        var clock = new MovingTime(Start);
        var shake = new PetShake(clock);
        shake.Begin();

        // 16ms 마다 2 씩 → 125 pt/s. 문턱(320) 아래라 안 쌓인다.
        Assert.False(Shake(shake, clock, 200, 2));
        Assert.False(shake.IsDizzy);
    }

    /// <summary>한 방향으로 아주 빠르게만 끌어도 조금씩은 쌓인다.</summary>
    [Fact]
    public void 한쪽으로만_아주_빠르면_천천히_쌓인다()
    {
        var clock = new MovingTime(Start);
        var shake = new PetShake(clock);
        shake.Begin();

        var x = 0.0;
        var dizzy = false;
        for (var i = 0; i < 30; i++)
        {
            x += 30;   // 16ms 마다 30 → 1875 pt/s
            clock.Advance(TimeSpan.FromMilliseconds(16));
            if (shake.Sample(new PetPoint(x, 300))) dizzy = true;
        }

        // 뒤집힘이 없으니 spinGain(0.07)만 쌓인다 — 30번으로는 문턱(3.0)에 못 간다.
        Assert.False(dizzy);
    }

    /// <summary>위아래로만 흔들어도 어지러워져야 한다.</summary>
    [Fact]
    public void 위아래로만_흔들어도_어지러워한다()
    {
        var clock = new MovingTime(Start);
        var shake = new PetShake(clock);
        shake.Begin();

        var y = 300.0;
        var dizzy = false;
        for (var i = 0; i < 12; i++)
        {
            y += i % 2 == 0 ? 30 : -30;
            clock.Advance(TimeSpan.FromMilliseconds(16));
            if (shake.Sample(new PetPoint(500, y))) dizzy = true;
        }

        Assert.True(dizzy);
    }

    [Fact]
    public void 어지러움은_한동안_이어지다_풀린다()
    {
        var clock = new MovingTime(Start);
        var shake = new PetShake(clock);
        shake.Begin();
        Shake(shake, clock, 12, 30);

        Assert.True(shake.IsDizzy);
        clock.Advance(PetShake.DizzyDuration);
        Assert.False(shake.IsDizzy);
    }

    /// <summary>
    /// 끌 때마다 점수를 새로 센다. 사이를 두고 조금씩 흔든 것이 쌓여서
    /// 엉뚱한 때 어지러워지면 안 된다.
    /// </summary>
    [Fact]
    public void 다시_끌기_시작하면_점수를_새로_센다()
    {
        var clock = new MovingTime(Start);
        var shake = new PetShake(clock);

        // 네 번이면 2.29 로 문턱(3.0) 아래다. 그대로 이어지면 두 번째 묶음에서 넘는다.
        shake.Begin();
        Assert.False(Shake(shake, clock, 4, 30));

        shake.Begin();                 // 놓았다가 다시 잡았다 — 여기서 0 으로 돌아간다
        Assert.False(Shake(shake, clock, 4, 30));
        Assert.False(shake.IsDizzy);
    }

    /// <summary>
    /// 속도를 새로 재지 못한 표본은 그렇다고 알려야 한다.
    ///
    /// 못 쟀을 때 <c>Velocity</c> 는 **옛 값 그대로**다. 그걸 새 값으로 알리면
    /// 마우스가 선 뒤에도 부엉이가 한 칸 더 기울어져 있는다.
    /// </summary>
    [Fact]
    public void 첫_표본은_속도를_재지_못한다()
    {
        var clock = new MovingTime(Start);
        var shake = new PetShake(clock);
        shake.Begin();

        shake.Sample(new PetPoint(500, 300));

        Assert.False(shake.Measured);
    }

    [Fact]
    public void 같은_눈금에_두_번_오면_속도를_재지_못한다()
    {
        var clock = new MovingTime(Start);
        var shake = new PetShake(clock);
        shake.Begin();

        shake.Sample(new PetPoint(500, 300));
        clock.Advance(TimeSpan.FromMilliseconds(16));
        shake.Sample(new PetPoint(560, 300));
        Assert.True(shake.Measured);
        var measured = shake.Velocity;

        // 시계를 안 돌리고 한 번 더 — 잰 것이 없다.
        shake.Sample(new PetPoint(620, 300));

        Assert.False(shake.Measured);
        Assert.Equal(measured, shake.Velocity);
    }

    private sealed class MovingTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }
}
