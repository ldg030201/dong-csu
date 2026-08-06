namespace DongCSU.Core.Owl;

/// <summary>
/// 자세 하나를 한 장의 그리드로 눌러 담는다.
///
/// 레이어를 고르고 미는 규칙은 **알고리즘이라 파일로 넘어오지 않는다.** 맥의
/// <c>OwlPose.layers</c> 를 여기에 옮겨 적은 것이고, 옮겨 적은 것은 언젠가 어긋난다.
/// 그래서 <c>owl.json</c> 은 프레임마다 맥이 합성해 둔 결과를 함께 싣고, 테스트가
/// 전 프레임을 그것과 대조한다. 여기가 틀리면 테스트가 먼저 깨진다.
/// </summary>
public static class OwlComposer
{
    public const char Empty = '.';

    /// <summary>자세를 합성한다. 결과는 <see cref="OwlGrid.Lines"/> 줄이다.</summary>
    public static string[] Compose(OwlDocument document, OwlPose pose)
    {
        var columns = document.Grid.Columns;
        var lines = document.Grid.Lines;

        var output = new char[lines][];
        for (var y = 0; y < lines; y++)
        {
            output[y] = new char[columns];
            Array.Fill(output[y], Empty);
        }

        foreach (var (layer, dx, dy) in LayersOf(document, pose))
        {
            Draw(output, layer, dx, dy, columns, lines);
        }

        return [.. output.Select(row => new string(row))];
    }

    /// <summary>
    /// 그릴 차례대로 (레이어, 가로 밀기, 세로 밀기).
    ///
    /// 뒤에 오는 것이 앞을 덮는다 — 몸 위에 날개, 그 위에 배, 그 위에 얼굴.
    /// </summary>
    private static IEnumerable<(string[] Layer, int Dx, int Dy)> LayersOf(OwlDocument document, OwlPose pose)
    {
        // 매달린 다리는 두 줄이라 몸통 아랫단과 겹친다. 그 줄을 비운 몸을 쓴다.
        var body = pose.Feet == OwlFeet.Dangle ? "bodyHanging" : "body";

        // 얼굴은 몸을 따라가고, 거기서 faceLean 만큼 더 움직인다.
        var faceShift = pose.Lean + pose.FaceLean;

        yield return (Layer(document, body), pose.Lean, pose.Bob);
        yield return (Layer(document, $"wings{pose.Wings}"), pose.Lean, pose.Bob);
        yield return (Layer(document, "belly"), pose.Lean, pose.Bob);
        yield return (Layer(document, $"eyes{pose.Eyes}"), faceShift, pose.Bob);
        yield return (Layer(document, "beak"), faceShift, pose.Bob);
        // 발은 땅(또는 허공)에 매달린 채라 기울임·오르내림에서 빼고 제 값만 쓴다.
        yield return (Layer(document, $"feet{pose.Feet}"), pose.FeetLean, 0);
    }

    private static string[] Layer(OwlDocument document, string name) =>
        document.Layers.TryGetValue(name, out var layer)
            ? layer
            : throw new InvalidDataException($"owl.json 에 '{name}' 레이어가 없다.");

    /// <summary>빈 칸이 아닌 글자만 덮어쓴다. 밖으로 나간 칸은 버린다.</summary>
    private static void Draw(char[][] output, string[] layer, int dx, int dy, int columns, int lines)
    {
        for (var y = 0; y < layer.Length; y++)
        {
            var movedY = y + dy;
            if (movedY < 0 || movedY >= lines) continue;

            var row = layer[y];
            for (var x = 0; x < row.Length; x++)
            {
                if (row[x] == Empty) continue;

                var movedX = x + dx;
                if (movedX < 0 || movedX >= columns) continue;

                output[movedY][movedX] = row[x];
            }
        }
    }
}
