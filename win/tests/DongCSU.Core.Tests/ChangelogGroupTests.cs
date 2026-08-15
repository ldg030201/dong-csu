using System.Text.Json;
using DongCSU.Core;

namespace DongCSU.Core.Tests;

/// <summary>
/// 기능 묶음과 갈래. 맥 2.3.0 의 <c>ChangelogGroup</c> 을 옮겨 온 것이다.
/// </summary>
public class ChangelogGroupTests
{
    private static ChangelogEntry Grouped() => new()
    {
        Version = "9.9.9",
        Groups =
        [
            new ChangelogGroup
            {
                Title = "펫 모드",
                Tab = "pet",
                Notes = [ChangelogNote.Fix("걷다 멈추던 문제 수정"), ChangelogNote.Change("기본값 변경")],
            },
            new ChangelogGroup
            {
                Title = "측정",
                Tab = "measure",
                IsNew = true,
                Notes = [ChangelogNote.New("측정 기록 목록")],
            },
        ],
    };

    /// <summary>
    /// **평평한 목록은 묶음에서 만들어 낸다.** 두 곳에 손으로 적으면 반드시 어긋나고,
    /// 2.2.0 이하는 이것만 읽으므로 비어 있으면 안 된다.
    /// </summary>
    [Fact]
    public void 평평한_목록을_묶음에서_만들어_낸다()
    {
        Assert.Equal(
            [
                "[펫 모드] 걷다 멈추던 문제 수정",
                "[펫 모드] 기본값 변경",
                "[측정] 측정 기록 목록",
            ],
            Grouped().Notes);
    }

    /// <summary>이미 나간 항목은 묶음이 없다. 그때 적은 문구를 그대로 둔다.</summary>
    [Fact]
    public void 묶음이_없으면_적어_둔_목록을_그대로_쓴다()
    {
        var entry = new ChangelogEntry { Version = "1.0.0", Notes = ["옛 문구"] };

        Assert.Null(entry.Groups);
        Assert.Equal(["옛 문구"], entry.Notes);
    }

    /// <summary>
    /// 원격 파일을 받아 읽는 통로가 묶음까지 되살려야 한다. 여기가 깨지면 새 버전의
    /// 변경 내역이 화면에서 통째로 평평해진다.
    /// </summary>
    [Fact]
    public void JSON_으로_나갔다_들어와도_그대로다()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        var json = JsonSerializer.Serialize(Grouped(), options);
        var back = JsonSerializer.Deserialize<ChangelogEntry>(json, options);

        Assert.NotNull(back);
        Assert.Equal(2, back.Groups?.Count);
        Assert.Equal("펫 모드", back.Groups![0].Title);
        Assert.Equal("pet", back.Groups[0].Tab);
        Assert.Equal(ChangeKind.Fix, back.Groups[0].Notes[0].Kind);
        Assert.True(back.Groups[1].IsNew);
        Assert.Equal(Grouped().Notes, back.Notes);
    }

    /// <summary>갈래는 소문자로 싣는다. JSON 의 나머지가 camelCase 라 여기만 튀면 안 된다.</summary>
    [Fact]
    public void 갈래를_소문자로_싣는다()
    {
        var json = JsonSerializer.Serialize(Grouped());

        Assert.Contains("\"fix\"", json);
        Assert.DoesNotContain("\"Fix\"", json);
    }

    [Theory]
    [InlineData(ChangeKind.New, "신규")]
    [InlineData(ChangeKind.Improve, "개선")]
    [InlineData(ChangeKind.Change, "변경")]
    [InlineData(ChangeKind.Fix, "오류")]
    [InlineData(ChangeKind.Remove, "제거")]
    public void 갈래_이름(ChangeKind kind, string expected)
    {
        Assert.Equal(expected, kind.Title());
    }

    /// <summary>
    /// **묶음이 생기기 전에 나간 것은 뒤늦게 나누지 않는다.** 사용자가 그때 본 것과
    /// 달라진다. 2.2.0 이하는 평평한 목록 그대로 둔다.
    /// </summary>
    [Fact]
    public void 묶음보다_먼저_나간_판은_평평한_그대로다()
    {
        var groupsArrived = new Version(2, 3, 0);

        foreach (var entry in Changelog.Entries)
        {
            if (Version.Parse(entry.Version) >= groupsArrived) continue;
            Assert.Null(entry.Groups);
        }
    }

    /// <summary>거꾸로, 묶음이 생긴 뒤의 판은 평평한 목록을 손으로 적지 않는다.</summary>
    [Fact]
    public void 묶음이_생긴_뒤의_판은_묶음을_쓴다()
    {
        var newest = Changelog.Entries[0];

        Assert.NotNull(newest.Groups);
        Assert.NotEmpty(newest.Notes);
        // 평평한 목록은 묶음에서 나온 것이라 앞에 묶음 이름이 붙어 있다.
        Assert.All(newest.Notes, note => Assert.StartsWith("[", note));
    }
}
