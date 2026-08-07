namespace DongCSU.Core.Pet;

/// <summary>
/// 커서가 마스코트 위에 **얼마나 머물렀는지** 센다.
///
/// 스치기만 해도 비키면 지나가는 마우스마다 도망친다. 잠깐 머물러야 "치우려는
/// 뜻"으로 본다. 맥과 같은 0.5초다.
///
/// 시각을 직접 재지 않고 받는다 — 그래야 테스트가 시계 없이 돈다.
/// </summary>
public sealed class PetHoverTracker
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(0.5);

    private DateTimeOffset? since;
    private bool fired;

    /// <summary>
    /// 지금 상태를 넣는다. **비켜야 할 순간에 딱 한 번** true 를 돌려준다.
    ///
    /// 계속 안에 있으면 비킨 뒤 다시 0.5초를 센다 — 따라오는 커서에서는 계속 물러난다.
    /// </summary>
    public bool Update(DateTimeOffset now, bool isInside)
    {
        if (!isInside)
        {
            since = null;
            fired = false;
            return false;
        }

        since ??= now;
        if (fired || now - since.Value < Delay) return false;

        fired = true;
        return true;
    }

    /// <summary>비키고 난 뒤. 계속 안에 있으면 처음부터 다시 센다.</summary>
    public void Restart(DateTimeOffset now)
    {
        since = now;
        fired = false;
    }

    public void Reset()
    {
        since = null;
        fired = false;
    }
}
