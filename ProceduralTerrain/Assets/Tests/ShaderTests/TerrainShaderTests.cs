using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class TerrainShaderTests
{
    // ── 테스트 1: 평탄한 지형 → 오목도 전부 0 ─────────────────────────
    [Test]
    public void ComputeConcavityColors_FlatMap_AllZero()
    {
        // 2x2 셀 = 3x3 정점
        int w = 2, d = 2;
        float[] heights = new float[(w + 1) * (d + 1)];
        for (int i = 0; i < heights.Length; i++) heights[i] = 5f;

        Color[] colors = TerrainGenerator.ComputeConcavityColors(heights, w, d);

        foreach (var c in colors)
            Assert.AreEqual(0f, c.r, 0.001f, "평탄 지형의 오목도는 0이어야 합니다.");
    }

    // ── 테스트 2: 계곡(낮은 중심 정점) → 중심 오목도 > 0 ───────────────
    [Test]
    public void ComputeConcavityColors_Valley_CenterHasPositiveConcavity()
    {
        int w = 2, d = 2;
        float[] heights = new float[(w + 1) * (d + 1)];
        for (int i = 0; i < heights.Length; i++) heights[i] = 5f;
        // 중심 정점 index = z*(w+1)+x = 1*3+1 = 4
        heights[4] = 0f; // 주변보다 훨씬 낮음 = 계곡

        Color[] colors = TerrainGenerator.ComputeConcavityColors(heights, w, d);

        Assert.Greater(colors[4].r, 0f, "계곡 정점의 오목도는 양수여야 합니다.");
    }

    // ── 테스트 3: 봉우리(높은 중심 정점) → 중심 오목도 = 0 (클램프) ────
    [Test]
    public void ComputeConcavityColors_Peak_ZeroConcavity()
    {
        int w = 2, d = 2;
        float[] heights = new float[(w + 1) * (d + 1)];
        for (int i = 0; i < heights.Length; i++) heights[i] = 5f;
        heights[4] = 10f; // 주변보다 훨씬 높음 = 봉우리

        Color[] colors = TerrainGenerator.ComputeConcavityColors(heights, w, d);

        Assert.AreEqual(0f, colors[4].r, 0.001f, "봉우리 정점의 오목도는 0으로 클램프되어야 합니다.");
    }

    // ── 테스트 4: 반환 배열 크기가 정점 수와 일치 ──────────────────────
    [Test]
    public void ComputeConcavityColors_ReturnsSizeMatchesVertexCount()
    {
        int w = 5, d = 7;
        float[] heights = new float[(w + 1) * (d + 1)];

        Color[] colors = TerrainGenerator.ComputeConcavityColors(heights, w, d);

        Assert.AreEqual((w + 1) * (d + 1), colors.Length);
    }
}
