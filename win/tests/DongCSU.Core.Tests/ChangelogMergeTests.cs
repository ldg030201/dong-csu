using DongCSU.Core;

namespace DongCSU.Core.Tests;

/// <summary>
/// 앱에 박힌 내역과 원격 내역을 합치는 규칙.
///
/// **원격 것으로 갈아치우면 안 된다.** 방금 올린 버전을 쓰는 앱은 자기보다 뒤처진 목록을
/// 받을 수 있고, 그러면 화면에서 자기 버전 항목이 사라진다.
/// </summary>
public class ChangelogMergeTests
{
    private static ChangelogEntry Entry(string version, string? date = "2026-01-01", string note = "무언가 수정") =>
        new() { Version = version, Date = date, Notes = [note] };

    [Fact]
    public void 원격이_비어_있으면_앱에_박힌_것을_그대로_쓴다()
    {
        Assert.Same(Changelog.Entries, Changelog.Merge([]));
        Assert.Same(Changelog.Entries, Changelog.Merge(null));
    }

    /// <summary>원격이 모르는 최신 버전이 사라지면 안 된다.</summary>
    [Fact]
    public void 원격에_없는_최신_버전도_남는다()
    {
        var newest = Changelog.Entries[0].Version;

        var merged = Changelog.Merge([Entry("0.9.0")]);

        Assert.Contains(merged, e => e.Version == newest);
        Assert.Contains(merged, e => e.Version == "0.9.0");
    }

    /// <summary>같은 버전은 원격 쪽을 택한다 — 나중에 문구를 고쳐 적었을 수 있다.</summary>
    [Fact]
    public void 같은_버전은_원격_쪽을_쓴다()
    {
        var version = Changelog.Entries[0].Version;

        var merged = Changelog.Merge([Entry(version, note: "원격에서 고쳐 적은 문구")]);

        Assert.Equal("원격에서 고쳐 적은 문구", merged.Single(e => e.Version == version).Notes[0]);
    }

    [Fact]
    public void 버전_내림차순으로_세운다()
    {
        var merged = Changelog.Merge([Entry("0.9.0"), Entry("3.0.0"), Entry("1.10.0"), Entry("1.9.0")]);

        var order = merged.Select(e => e.Version).ToList();
        Assert.Equal("3.0.0", order[0]);
        // 두 번째 자리가 두 자리가 돼도 글자 순이 아니라 숫자 순이다.
        Assert.True(order.IndexOf("1.10.0") < order.IndexOf("1.9.0"));
        Assert.Equal("0.9.0", order[^1]);
    }

    // ── Changelog.Read — 못 받았으면 왜 못 받았는지 ────────────────────────────
    //
    // 예전에는 실패를 통째로 삼켜서 404 든 깨진 피드든 화면에도 기록에도 흔적이 없었다.
    // 갈래를 나누는 판단은 화면과 무관하므로 여기서 굳힌다.

    [Fact]
    public void 제대로_된_피드는_사유_없이_항목이_나온다()
    {
        var fetch = Changelog.Read(200, Changelog.Dump());

        Assert.Null(fetch.Failure);
        Assert.NotNull(fetch.Entries);
        Assert.NotEmpty(fetch.Entries);
    }

    /// <summary>404 와 회선 끊김은 다른 일이다. 상태 코드를 그대로 보여줘야 구별된다.</summary>
    [Fact]
    public void 상태_코드가_200이_아니면_그_번호가_사유에_들어간다()
    {
        var fetch = Changelog.Read(404, "");

        Assert.Null(fetch.Entries);
        Assert.NotNull(fetch.Failure);
        Assert.Contains("404", fetch.Failure);
    }

    [Fact]
    public void 형식이_깨졌으면_형식_이야기가_나온다()
    {
        var fetch = Changelog.Read(200, "{{{");

        Assert.Null(fetch.Entries);
        Assert.NotNull(fetch.Failure);
        Assert.Contains("형식", fetch.Failure);
    }

    [Fact]
    public void 항목이_하나도_없으면_비어_있다고_말한다()
    {
        var fetch = Changelog.Read(200, """{"entries":[]}""");

        Assert.Null(fetch.Entries);
        Assert.NotNull(fetch.Failure);
        Assert.Contains("비어 있", fetch.Failure);
    }

    /// <summary>
    /// 실패한 결과를 그대로 <see cref="Changelog.Merge"/> 에 넘겨도 화면이 비지 않는다.
    /// <see cref="원격이_비어_있으면_앱에_박힌_것을_그대로_쓴다"/> 와 짝이다.
    /// </summary>
    [Fact]
    public void 실패한_결과를_그대로_합쳐도_앱에_박힌_것이_나온다()
    {
        var fetch = Changelog.Read(500, "");

        Assert.Same(Changelog.Entries, Changelog.Merge(fetch.Entries));
    }

    /// <summary>
    /// **사유에 응답 본문이 섞이면 안 된다.** 이 문장은 기록 파일에 그대로 남는데,
    /// 엉뚱한 주소에서 받아 온 응답이면 그 안에 무엇이 실려 있을지 모른다.
    /// </summary>
    [Fact]
    public void 사유에_응답_본문이_섞이지_않는다()
    {
        foreach (var status in new[] { 401, 200 })
        {
            var fetch = Changelog.Read(status, """{"secret":"secret-token"}""");

            Assert.NotNull(fetch.Failure);
            Assert.DoesNotContain("secret", fetch.Failure);
        }
    }

    // ── Changelog.Read — 받아온 응답을 성공·실패로 가르는 자리 ──────────────────
    //
    // 갈래를 나누는 것은 화면과 무관한 순수 계산이라 Core 에 있다. 화면 쪽(버전 탭의
    // 주황 한 줄)은 여기서 나온 사유를 그대로 띄우기만 한다.

    [Fact]
    public void 제대로_된_피드는_그대로_읽힌다()
    {
        var fetch = Changelog.Read(200, Changelog.Dump());

        Assert.Null(fetch.Failure);
        Assert.NotNull(fetch.Entries);
        Assert.NotEmpty(fetch.Entries);
    }

    /// <summary>404 를 회선 끊김과 뭉개면 안 된다 — 상태 코드가 사유에 남아야 한다.</summary>
    [Fact]
    public void 상태_코드가_이백이_아니면_그_번호가_사유에_남는다()
    {
        var fetch = Changelog.Read(404, "");

        Assert.Null(fetch.Entries);
        Assert.NotNull(fetch.Failure);
        Assert.Contains("404", fetch.Failure);
    }

    [Fact]
    public void 형식이_깨졌으면_형식_사유가_나온다()
    {
        var fetch = Changelog.Read(200, "{{{");

        Assert.Null(fetch.Entries);
        Assert.Contains("형식", fetch.Failure);
    }

    [Fact]
    public void 내역이_비어_있으면_비었다고_말한다()
    {
        var fetch = Changelog.Read(200, """{"entries":[]}""");

        Assert.Null(fetch.Entries);
        Assert.Contains("비어", fetch.Failure);
    }

    /// <summary>
    /// 실패한 결과를 그대로 합쳐도 앱에 박힌 것이 그대로 나온다.
    ///
    /// 받기가 실패했다고 화면의 변경 내역이 비면 안 된다 — 위의
    /// <see cref="원격이_비어_있으면_앱에_박힌_것을_그대로_쓴다"/> 와 짝이다.
    /// </summary>
    [Fact]
    public void 못_받았으면_앱에_박힌_내역이_그대로_나온다()
    {
        var fetch = Changelog.Read(500, "");

        Assert.Same(Changelog.Entries, Changelog.Merge(fetch.Entries));
    }

    /// <summary>
    /// **사유에 응답 본문을 끼우지 않는다.** 기록 파일로 흘러 들어가는 글이라
    /// 상태 코드와 우리가 쓴 문장만 남긴다.
    /// </summary>
    [Fact]
    public void 사유에_응답_본문이_섞여_나오지_않는다()
    {
        foreach (var fetch in new[]
        {
            Changelog.Read(403, "secret"),
            Changelog.Read(200, "{{{ secret"),
            Changelog.Read(200, """{"entries":[],"note":"secret"}"""),
        })
        {
            Assert.NotNull(fetch.Failure);
            Assert.DoesNotContain("secret", fetch.Failure);
        }
    }
}
