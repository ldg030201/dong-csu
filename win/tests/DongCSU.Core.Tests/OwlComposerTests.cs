using DongCSU.Core.Owl;

namespace DongCSU.Core.Tests;

/// <summary>
/// **이 저장소에서 가장 중요한 테스트다.**
///
/// 부엉이 그림은 맥 소스가 원본이고 윈도우는 그걸 읽어 다시 그린다. 레이어를 고르고
/// 미는 규칙만은 파일로 넘어오지 않아서 <see cref="OwlComposer"/> 에 옮겨 적었는데,
/// 옮겨 적은 것은 언젠가 어긋난다.
///
/// 그래서 <c>owl.json</c> 은 프레임마다 **맥이 합성해 둔 결과**를 함께 싣는다.
/// 여기서 전 프레임을 대조하면, 그림 한 장 그려 보지 않고 글자만으로 어긋남이 잡힌다.
/// </summary>
public class OwlComposerTests
{
    private static readonly OwlDocument Document = OwlDocument.Embedded;

    public static TheoryData<string, int> AllFrames()
    {
        var data = new TheoryData<string, int>();
        foreach (var animation in Document.Animations)
        {
            for (var i = 0; i < animation.Frames.Count; i++)
            {
                data.Add(animation.Name, i);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllFrames))]
    public void 합성_결과가_맥이_넘긴_그리드와_같다(string animationName, int frameIndex)
    {
        var animation = Document.Animations.Single(a => a.Name == animationName);
        var frame = animation.Frames[frameIndex];

        var composed = OwlComposer.Compose(Document, frame.Pose);

        Assert.Equal(frame.Grid, composed);
    }

    [Fact]
    public void 형식_번호를_안다()
    {
        Assert.Equal(OwlDocument.SupportedFormatVersion, Document.FormatVersion);
    }

    [Fact]
    public void 그리드_크기가_모든_레이어와_맞는다()
    {
        foreach (var (name, layer) in Document.Layers)
        {
            Assert.Equal(Document.Grid.Lines, layer.Length);
            Assert.All(layer, row => Assert.Equal(Document.Grid.Columns, row.Length));
            Assert.False(string.IsNullOrEmpty(name));
        }
    }

    [Fact]
    public void 애니메이션마다_프레임이_있고_팔레트가_실재한다()
    {
        Assert.NotEmpty(Document.Animations);
        foreach (var animation in Document.Animations)
        {
            Assert.NotEmpty(animation.Frames);
            Assert.True(
                Document.Palettes.ContainsKey(animation.Palette),
                $"'{animation.Name}' 이 없는 팔레트 '{animation.Palette}' 를 가리킨다.");
            Assert.All(animation.Frames, frame => Assert.True(frame.Duration >= 0));
        }
    }

    /// <summary>
    /// **duration 0 은 "멈춤"이다.** 프레임이 하나뿐인 애니메이션(끊김)이 그렇고,
    /// 맥은 그럴 때 타이머를 아예 걸지 않는다. 윈도우도 그래야 한다 — 0초짜리
    /// 프레임을 그대로 타이머에 넣으면 쉬지 않고 도는 루프가 된다.
    /// </summary>
    [Fact]
    public void 멈춘_프레임은_한_장짜리_애니메이션에만_있다()
    {
        foreach (var animation in Document.Animations)
        {
            var still = animation.Frames.Where(f => f.Duration == 0).ToList();
            if (still.Count == 0) continue;

            Assert.True(
                animation.Frames.Count == 1,
                $"'{animation.Name}' 에 0초짜리 프레임이 있는데 프레임이 {animation.Frames.Count} 장이다 — 돌릴 수 없다.");
        }
    }

    [Fact]
    public void 팔레트마다_필요한_색이_다_있다()
    {
        string[] required = ["body", "wing", "belly", "face", "pupil", "beak"];
        foreach (var (name, palette) in Document.Palettes)
        {
            foreach (var key in required)
            {
                Assert.True(palette.ContainsKey(key), $"팔레트 '{name}' 에 '{key}' 가 없다.");
            }
        }
    }

    [Fact]
    public void 링_색_구간이_사용률_순서대로다()
    {
        var stops = Document.UsageColors;
        Assert.NotEmpty(stops);
        for (var i = 1; i < stops.Count; i++)
        {
            Assert.True(stops[i].At > stops[i - 1].At, "구간이 오름차순이 아니다.");
        }
    }

    [Fact]
    public void 기분_임계값이_있다()
    {
        Assert.True(Document.MoodThresholds["tired"] < Document.MoodThresholds["exhausted"]);
    }
}
