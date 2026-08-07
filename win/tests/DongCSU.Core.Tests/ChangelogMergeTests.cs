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
}
