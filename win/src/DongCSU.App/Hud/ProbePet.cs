using DongCSU.Core.Pet;

namespace DongCSU.App.Hud;

/// <summary>
/// <c>--probe-pet</c> — 다른 화면으로 넘어가는 계산을 화면 없이 재 본다.
/// 맥 <c>--probe-perch checkScreens</c> 와 같은 자리다.
///
/// <b>눈으로는 못 본다.</b> 배회는 3~11초에 한 번, 26 단위/초로 움직이고 키를 누르는
/// 동안은 멈춘다 — 옆 화면까지 걸어가는 것을 지켜보려면 몇 분이 걸린다. 게다가
/// 배율이 다른 모니터에서만 드러나는 어긋남은 그렇게 봐도 안 잡힌다.
///
/// <b>창을 띄우지 않는다.</b> 실제 모니터 배치를 읽어서 그 자리에 서도 되는지만 묻는다.
///
/// 화면이 하나면 잴 것이 없으므로 그렇다고 적고 0 으로 끝난다 — <b>그래야 CI 에 걸 수
/// 있다.</b> 빌드 기계에는 모니터가 하나뿐이다.
/// </summary>
internal static class ProbePet
{
    public static int Run()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        Console.WriteLine("다른 화면으로 넘어가기");

        // **`PetStage` 와 달리 물리 픽셀 그대로다.** 여기는 창이 없어서 환산 계수를
        // 읽을 데가 없다. 재는 것이 "그 자리에 서도 되나" 하나뿐이라 단위가 한 벌로만
        // 맞으면 되고, 실제 앱은 `PetStage` 가 같은 API 로 환산해 넘긴다.
        var areas = new List<PetRect>(screens.Length);
        foreach (var screen in screens)
        {
            var work = screen.WorkingArea;
            areas.Add(new PetRect(work.X, work.Y, work.Width, work.Height));
            Console.WriteLine(
                $"  화면 — x {work.Left}~{work.Right}   y {work.Top}~{work.Bottom}"
                + $"{(screen.Primary ? "   (주)" : "")}");
        }

        if (areas.Count < 2)
        {
            Console.WriteLine("  화면이 하나뿐이라 잴 것이 없다 (설정을 켜도 아무 일도 안 한다)");
            return 0;
        }

        // 첫 화면의 오른쪽 끝에 세워 둔다.
        var home = areas[0];
        var window = new PetRect(home.Right - 128 - 8, home.Y + 8, 128, 160);

        var stage = new ProbeStage
        {
            Window = window,
            WorkArea = home,
            WorkAreas = areas,
            Cursor = new PetPoint(home.X, home.Y),
            SinceLastKey = TimeSpan.MaxValue,
        };

        var problems = 0;
        // 화면 끝에 세워 두고 그 너머로 밀어 본다.
        problems += Line(
            "꺼짐 — 옆 화면 자리를 요구하면 지금 화면으로 되당긴다",
            !Crosses(stage, window, home));
        stage.CrossesScreens = true;
        problems += Line(
            "켜짐 — 옆 화면 자리를 그대로 받아들인다",
            Crosses(stage, window, home));

        // **이음매 위에서 시작해도 막히지 않는다.** 여기가 막히면 기능이 통째로 안
        // 돈다 — 화면 사이에 창 폭만큼의 죽은 띠가 생겨서 한 걸음(2.6)으로는 영영
        // 못 건넌다. 그래서 **걸친 자리에서 출발해** 더 갈 수 있는지 본다.
        // 세우는 것은 `Crosses` 가 한다. 여기서 또 대입하면 누가 이 값을 쥐는지 흐려진다.
        var seam = new PetRect(home.Right - 64, window.Y, window.Width, window.Height);
        problems += Line(
            "켜짐 — 이음매 위(두 화면에 걸친 자리)에서도 더 간다",
            Crosses(stage, seam, home));

        return problems == 0 ? 0 : 1;
    }

    /// <summary>
    /// 그 자리에서 커서에 밀렸을 때 <b>화면 끝을 넘어가는지.</b>
    ///
    /// <see cref="PetMotion"/> 의 가두기를 그대로 태운다 — 여기서 셈을 다시 적으면
    /// 진단이 실제와 다른 답을 낸다.
    /// </summary>
    private static bool Crosses(ProbeStage stage, PetRect at, PetRect home)
    {
        var pet = new PetMotion { CrossesScreens = stage.CrossesScreens, DodgesCursor = true };
        stage.Window = at;
        // 커서를 왼쪽에 두면 오른쪽으로 물러난다.
        stage.Cursor = new PetPoint(at.X - 200, at.Center.Y);

        if (!pet.RequestDodge(stage)) return false;
        var tick = pet.Tick(stage);
        if (tick.MoveTo is not { } moved) return false;

        // 첫 화면에서 창 왼쪽 위가 갈 수 있는 오른쪽 끝.
        var wall = home.Right - 8 - at.Width;
        return moved.X > wall + 0.001;
    }

    private static int Line(string label, bool ok)
    {
        Console.WriteLine($"  {label,-52}{(ok ? "통과" : "실패")}");
        return ok ? 0 : 1;
    }

    private sealed class ProbeStage : IPetStage
    {
        /// <summary>지금 재는 것이 켠 상태인지. <see cref="Crosses"/> 가 본다.</summary>
        public bool CrossesScreens { get; set; }

        public PetRect Window { get; set; }
        public PetRect? WorkArea { get; set; }
        public IReadOnlyList<PetRect> WorkAreas { get; set; } = [];
        public PetPoint Cursor { get; set; }
        public TimeSpan SinceLastKey { get; set; }
        public double Scale => 1;
    }
}
