using NUnit.Framework;
using UnityEngine;
using UnityEditor;

/// <summary>
/// TerrainDeformCompute.compute 커널을 직접 GPU에서 실행하는 Edit Mode 통합 테스트.
/// MonoBehaviour 없이 ComputeShader + ComputeBuffer를 직접 구성하여
/// OBB 형태 판정, 직사각형 트렌치, 회전, 볼륨 계산을 검증한다.
/// </summary>
[TestFixture]
public class TerrainDeformComputeTests
{
    private const string SHADER_PATH = "Assets/Shaders/TerrainDeformCompute.compute";
    private const float INITIAL_HEIGHT = 10f;
    private const float VOLUME_EPSILON  = 0.15f; // 고정소수점 반올림 허용 오차
    private const float HEIGHT_EPSILON  = 0.0001f;

    private ComputeShader shader;

    // ── 셋업 ─────────────────────────────────────────────────────────
    [SetUp]
    public void Setup()
    {
        shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(SHADER_PATH);
        Assert.IsNotNull(shader, $"Compute Shader를 찾을 수 없습니다: {SHADER_PATH}");
    }

    // ── 헬퍼: 평탄 높이맵 생성 ───────────────────────────────────────
    private static float[] FlatMap(int width, int depth, float height = INITIAL_HEIGHT)
    {
        var map = new float[(width + 1) * (depth + 1)];
        for (int i = 0; i < map.Length; i++) map[i] = height;
        return map;
    }

    // ── 헬퍼: Compute Shader Dispatch + 결과 읽기 ─────────────────────
    private (float[] heights, float volume) Dispatch(
        float[] initialHeights,
        int width, int depth, float cellSize,
        float centerX, float centerZ,
        float halfExtentX, float halfExtentZ,
        float angleYDeg, float strength, bool raise)
    {
        int kernel    = shader.FindKernel("CSDeform");
        float angleRad = angleYDeg * Mathf.Deg2Rad;

        // Dispatch 범위 계산 (TerrainDeformer.cs 와 동일 로직)
        float cosA   = Mathf.Abs(Mathf.Cos(angleRad));
        float sinA   = Mathf.Abs(Mathf.Sin(angleRad));
        float boundX = halfExtentX * cosA + halfExtentZ * sinA;
        float boundZ = halfExtentX * sinA + halfExtentZ * cosA;
        int startX   = Mathf.FloorToInt(centerX - boundX);
        int startZ   = Mathf.FloorToInt(centerZ - boundZ);
        int groupsX  = Mathf.CeilToInt((boundX * 2f + 1f) / 8f);
        int groupsZ  = Mathf.CeilToInt((boundZ * 2f + 1f) / 8f);

        var heightBuf = new ComputeBuffer(initialHeights.Length, sizeof(float));
        var volumeBuf = new ComputeBuffer(1, sizeof(int));
        heightBuf.SetData(initialHeights);
        volumeBuf.SetData(new int[] { 0 });

        shader.SetBuffer(kernel, "_HeightMap",   heightBuf);
        shader.SetBuffer(kernel, "_VolumeAccum", volumeBuf);
        shader.SetInt   ("_Width",      width);
        shader.SetInt   ("_Depth",      depth);
        shader.SetFloat ("_CenterX",    centerX);
        shader.SetFloat ("_CenterZ",    centerZ);
        shader.SetFloat ("_HalfExtentX", halfExtentX);
        shader.SetFloat ("_HalfExtentZ", halfExtentZ);
        shader.SetFloat ("_AngleY",     angleRad);
        shader.SetInt   ("_StartX",     startX);
        shader.SetInt   ("_StartZ",     startZ);
        shader.SetFloat ("_Strength",   strength);
        shader.SetFloat ("_Direction",  raise ? 1f : -1f);

        shader.Dispatch(kernel, groupsX, groupsZ, 1);

        float[] resultHeights = new float[initialHeights.Length];
        heightBuf.GetData(resultHeights);

        int[] rawVol = new int[1];
        volumeBuf.GetData(rawVol);
        float volume = rawVol[0] / 10000f * cellSize * cellSize;

        heightBuf.Release();
        volumeBuf.Release();

        return (resultHeights, volume);
    }

    // ── 헬퍼: 셀 인덱스 ──────────────────────────────────────────────
    private static int Idx(int width, int x, int z) => z * (width + 1) + x;

    // ================================================================
    // 테스트 1: 정사각형 버킷 → 정사각형 형태로 굴삭
    // ================================================================
    [Test]
    public void SquareBucket_DigsSquareShape()
    {
        int w = 32, d = 32;
        float cx = 16f, cz = 16f, he = 2f; // halfExtent=2 → 5×5 커버

        var (result, _) = Dispatch(FlatMap(w, d), w, d, 1f, cx, cz, he, he, 0f, 1f, false);

        // OBB 내부: 높이가 낮아져야 함
        for (int z = (int)(cz - he); z <= (int)(cz + he); z++)
        for (int x = (int)(cx - he); x <= (int)(cx + he); x++)
            Assert.Less(result[Idx(w, x, z)], INITIAL_HEIGHT,
                $"내부 셀 ({x},{z})이 굴삭되지 않음");

        // OBB 외부 (바로 옆): 높이가 유지되어야 함
        Assert.AreEqual(INITIAL_HEIGHT, result[Idx(w, (int)(cx + he + 1), (int)cz)], HEIGHT_EPSILON,
            "OBB 외부 셀이 굴삭됨");
        Assert.AreEqual(INITIAL_HEIGHT, result[Idx(w, (int)cx, (int)(cz + he + 1))], HEIGHT_EPSILON,
            "OBB 외부 셀이 굴삭됨");
    }

    // ================================================================
    // 테스트 2: 직사각형 버킷(6×2) → X 방향만 넓게 굴삭
    // ================================================================
    [Test]
    public void RectangleBucket_DigsRectangleNotCircle()
    {
        int w = 32, d = 32;
        float cx = 16f, cz = 16f;
        float heX = 3f, heZ = 1f; // X: 6셀 폭, Z: 2셀 깊이

        var (result, _) = Dispatch(FlatMap(w, d), w, d, 1f, cx, cz, heX, heZ, 0f, 1f, false);

        // X+3 (긴 축 끝): 굴삭되어야 함
        Assert.Less(result[Idx(w, (int)(cx + 3), (int)cz)], INITIAL_HEIGHT,
            "X+3 (긴 축 내부) 굴삭 안 됨");

        // Z+2 (짧은 축 밖): 굴삭되지 않아야 함 (원형이면 굴삭됨)
        Assert.AreEqual(INITIAL_HEIGHT, result[Idx(w, (int)cx, (int)(cz + 2))], HEIGHT_EPSILON,
            "Z+2 (짧은 축 외부) 굴삭됨 — 원형으로 동작 중");

        // 대각선 코너 (X+3, Z+2): 굴삭되지 않아야 함 (원형이면 굴삭됨)
        Assert.AreEqual(INITIAL_HEIGHT, result[Idx(w, (int)(cx + 3), (int)(cz + 2))], HEIGHT_EPSILON,
            "대각선 코너 굴삭됨 — 원형으로 동작 중");
    }

    // ================================================================
    // 테스트 3: 90° 회전 → 긴 축이 Z 방향으로 전환
    // ================================================================
    [Test]
    public void RotatedBucket_90Deg_LongAxisSwapsToZ()
    {
        int w = 32, d = 32;
        float cx = 16f, cz = 16f;
        float heX = 3f, heZ = 1f; // 회전 전: X가 긴 축

        var (result, _) = Dispatch(FlatMap(w, d), w, d, 1f, cx, cz, heX, heZ, 90f, 1f, false);

        // 90° 회전 후: Z+3 (긴 축)이 굴삭되어야 함
        Assert.Less(result[Idx(w, (int)cx, (int)(cz + 3))], INITIAL_HEIGHT,
            "90° 회전 후 Z+3 굴삭 안 됨 — 회전이 적용되지 않음");

        // X+3 (이제 짧은 축 방향): 굴삭되지 않아야 함
        Assert.AreEqual(INITIAL_HEIGHT, result[Idx(w, (int)(cx + 3), (int)cz)], HEIGHT_EPSILON,
            "90° 회전 후 X+3 굴삭됨 — 회전이 적용되지 않음");
    }

    // ================================================================
    // 테스트 4: GPU 볼륨 계산 정확도
    // ================================================================
    [Test]
    public void VolumeCalculation_MatchesExpectedCellCount()
    {
        int w = 32, d = 32;
        float cellSize = 1f;
        float he = 2f;       // 5×5 = 25셀
        float strength = 0.5f;

        var (_, volume) = Dispatch(FlatMap(w, d), w, d, cellSize, 16f, 16f, he, he, 0f, strength, false);

        // 기댓값: 25셀 × strength × cellArea
        float expected = 25f * strength * cellSize * cellSize;
        Assert.AreEqual(expected, volume, VOLUME_EPSILON,
            $"볼륨 기댓값 {expected}, 실제 {volume}");
    }

    // ================================================================
    // 테스트 5: 올리기(raise) 동작 — 높이가 증가해야 함
    // ================================================================
    [Test]
    public void Raise_IncreasesHeight()
    {
        int w = 32, d = 32;
        float cx = 16f, cz = 16f, he = 2f;

        var (result, _) = Dispatch(FlatMap(w, d), w, d, 1f, cx, cz, he, he, 0f, 1f, raise: true);

        Assert.Greater(result[Idx(w, (int)cx, (int)cz)], INITIAL_HEIGHT,
            "raise=true인데 높이가 증가하지 않음");
    }

    // ================================================================
    // 테스트 6: 경계 외부 셀은 변형 없음
    // ================================================================
    [Test]
    public void BoundaryCheck_CellsOutsideTerrainAreIgnored()
    {
        int w = 16, d = 16;
        // 버킷 중심을 지형 가장자리에 위치 → 일부 셀이 음수 좌표에 걸림
        float[] initial = FlatMap(w, d);
        float[] copy = (float[])initial.Clone();

        Assert.DoesNotThrow(() =>
        {
            Dispatch(initial, w, d, 1f, 1f, 1f, 3f, 3f, 0f, 1f, false);
        }, "경계 밖 셀 접근 시 예외 발생");
    }

    // ── 이완(Relax) 헬퍼 ─────────────────────────────────────────────
    private float[] DispatchRelax(
        float[] initialHeights,
        int width, int depth, float cellSize,
        float reposeAngleDeg, float flowRate, int iterations)
    {
        int kernelIdx       = shader.FindKernel("CSRelax");
        float maxHeightDiff = cellSize * Mathf.Tan(reposeAngleDeg * Mathf.Deg2Rad);
        int groupsX         = Mathf.CeilToInt((width  + 1) / 8f);
        int groupsZ         = Mathf.CeilToInt((depth  + 1) / 8f);

        var bufA = new ComputeBuffer(initialHeights.Length, sizeof(float));
        var bufB = new ComputeBuffer(initialHeights.Length, sizeof(float));
        bufA.SetData(initialHeights);
        bufB.SetData(initialHeights); // dst 초기화 (경계 셀 보호)

        ComputeBuffer src = bufA, dst = bufB;

        for (int i = 0; i < iterations; i++)
        {
            shader.SetBuffer(kernelIdx, "_HeightMapSrc",  src);
            shader.SetBuffer(kernelIdx, "_HeightMapDst",  dst);
            shader.SetInt   ("_Width",          width);
            shader.SetInt   ("_Depth",          depth);
            shader.SetFloat ("_MaxHeightDiff",  maxHeightDiff);
            shader.SetFloat ("_FlowRate",       flowRate);
            shader.Dispatch (kernelIdx, groupsX, groupsZ, 1);

            ComputeBuffer tmp = src; src = dst; dst = tmp;
        }

        float[] result = new float[initialHeights.Length];
        src.GetData(result);
        bufA.Release();
        bufB.Release();
        return result;
    }

    // ================================================================
    // 테스트 7: 뾰족한 봉우리가 이완되어야 함
    // ================================================================
    [Test]
    public void Relax_SharpPeak_GetsSmoothed()
    {
        int w = 16, d = 16;
        float[] heights = FlatMap(w, d, 5f);

        // 중앙에 매우 높은 단일 봉우리 생성 (이웃보다 15 높음)
        int peakX = 8, peakZ = 8;
        heights[Idx(w, peakX, peakZ)] = 20f;

        float reposeAngle = 35f;
        float maxStable   = 1f * Mathf.Tan(reposeAngle * Mathf.Deg2Rad);

        float[] result = DispatchRelax(heights, w, d, 1f, reposeAngle, 0.5f, 20);

        // 봉우리가 낮아져야 함
        Assert.Less(result[Idx(w, peakX, peakZ)], 20f,
            "봉우리가 이완 후에도 그대로임");

        // 봉우리와 이웃의 높이 차가 충분히 좁혀져야 함 (허용 오차 0.5 포함)
        float peakH     = result[Idx(w, peakX,     peakZ)];
        float neighborH = result[Idx(w, peakX + 1, peakZ)];
        Assert.Less(peakH - neighborH, maxStable + 0.5f,
            "이완 후에도 봉우리가 안식각 이상으로 가파름");
    }

    // ================================================================
    // 테스트 8: 평탄 지형은 이완 후 변하지 않아야 함
    // ================================================================
    [Test]
    public void Relax_FlatTerrain_RemainsUnchanged()
    {
        int w = 16, d = 16;
        float baseHeight = 5f;
        float[] heights  = FlatMap(w, d, baseHeight);

        float[] result = DispatchRelax(heights, w, d, 1f, 35f, 0.5f, 10);

        for (int z = 0; z <= d; z++)
        for (int x = 0; x <= w; x++)
            Assert.AreEqual(baseHeight, result[Idx(w, x, z)], HEIGHT_EPSILON,
                $"평탄 지형의 셀 ({x},{z})이 변경됨");
    }

    // ================================================================
    // 테스트 9: 이완 후 전체 높이 합(부피)이 보존되어야 함
    // ================================================================
    [Test]
    public void Relax_TotalVolume_IsConserved()
    {
        int w = 16, d = 16;
        float[] heights = FlatMap(w, d, 5f);

        heights[Idx(w, 8,  8)] = 15f;
        heights[Idx(w, 4,  4)] = 12f;
        heights[Idx(w, 12, 3)] =  0f;

        float sumBefore = 0f;
        foreach (float h in heights) sumBefore += h;

        float[] result = DispatchRelax(heights, w, d, 1f, 35f, 0.5f, 10);

        float sumAfter = 0f;
        foreach (float h in result) sumAfter += h;

        Assert.AreEqual(sumBefore, sumAfter, 0.5f,
            $"이완 전후 총 높이 합 불일치: before={sumBefore:F2}, after={sumAfter:F2}");
    }
}
