using UnityEngine;

public class ComputeErosion
{
    // Compute Shader의 HEIGHT_SCALE과 반드시 동일해야 함
    private const int HEIGHT_SCALE = 100000;
    private const int THREAD_GROUP_SIZE = 64;

    private readonly ComputeShader shader;
    private readonly int kernelIndex;

    public ComputeErosion(ComputeShader shader)
    {
        this.shader  = shader;
        kernelIndex  = shader.FindKernel("CSErosion");
    }

    public void Erode(float[] heightMap, int width, int depth, ErosionSettings settings, int seed = 0)
    {
        PrecomputeBrush(settings.erodeRadius,
            out int[] brushX, out int[] brushZ, out float[] brushWeights);

        Vector2[] seeds   = GenerateSeeds(settings.numDroplets, width, depth, seed);
        int[]     heightInt = ToIntMap(heightMap);

        // ── 버퍼 생성 ──────────────────────────────────────────────────
        ComputeBuffer heightBuffer      = new ComputeBuffer(heightInt.Length,        sizeof(int));
        ComputeBuffer seedBuffer        = new ComputeBuffer(seeds.Length,            sizeof(float) * 2);
        ComputeBuffer brushOffsetBuffer = new ComputeBuffer(brushX.Length,           sizeof(int) * 2);
        ComputeBuffer brushWeightBuffer = new ComputeBuffer(brushWeights.Length,     sizeof(float));

        try
        {
            // ── 데이터 업로드 ───────────────────────────────────────────
            heightBuffer.SetData(heightInt);
            seedBuffer.SetData(seeds);
            brushOffsetBuffer.SetData(BuildBrushOffsets(brushX, brushZ));
            brushWeightBuffer.SetData(brushWeights);

            // ── 버퍼 바인딩 ─────────────────────────────────────────────
            shader.SetBuffer(kernelIndex, "_HeightMap",    heightBuffer);
            shader.SetBuffer(kernelIndex, "_RandomSeeds",  seedBuffer);
            shader.SetBuffer(kernelIndex, "_BrushOffsets", brushOffsetBuffer);
            shader.SetBuffer(kernelIndex, "_BrushWeights", brushWeightBuffer);

            // ── 파라미터 전달 ───────────────────────────────────────────
            shader.SetInt("_Width",       width);
            shader.SetInt("_Depth",       depth);
            shader.SetInt("_NumDroplets", settings.numDroplets);
            shader.SetInt("_MaxLifetime", settings.maxLifetime);
            shader.SetInt("_BrushCount",  brushX.Length);

            shader.SetFloat("_Inertia",                settings.inertia);
            shader.SetFloat("_SedimentCapacityFactor", settings.sedimentCapacityFactor);
            shader.SetFloat("_MinSedimentCapacity",    settings.minSedimentCapacity);
            shader.SetFloat("_DepositSpeed",           settings.depositSpeed);
            shader.SetFloat("_ErodeSpeed",             settings.erodeSpeed);
            shader.SetFloat("_EvaporateSpeed",         settings.evaporateSpeed);
            shader.SetFloat("_Gravity",                settings.gravity);
            shader.SetFloat("_StartSpeed",             settings.startSpeed);
            shader.SetFloat("_StartWater",             settings.startWater);

            // ── GPU 실행 ────────────────────────────────────────────────
            int threadGroups = Mathf.CeilToInt(settings.numDroplets / (float)THREAD_GROUP_SIZE);
            shader.Dispatch(kernelIndex, threadGroups, 1, 1);

            // ── 결과 수신 및 float 복원 ────────────────────────────────
            heightBuffer.GetData(heightInt);
            FromIntMap(heightInt, heightMap);
        }
        finally
        {
            // 예외 발생 여부와 관계없이 버퍼 해제
            heightBuffer.Release();
            seedBuffer.Release();
            brushOffsetBuffer.Release();
            brushWeightBuffer.Release();
        }
    }

    // ── 헬퍼 메서드 ──────────────────────────────────────────────────────

    private static Vector2[] GenerateSeeds(int count, int width, int depth, int seed)
    {
        System.Random rng = new System.Random(seed);
        Vector2[] seeds = new Vector2[count];
        for (int i = 0; i < count; i++)
            seeds[i] = new Vector2(
                (float)(rng.NextDouble() * width),
                (float)(rng.NextDouble() * depth));
        return seeds;
    }

    private static int[] ToIntMap(float[] heightMap)
    {
        int[] result = new int[heightMap.Length];
        for (int i = 0; i < heightMap.Length; i++)
            result[i] = Mathf.RoundToInt(heightMap[i] * HEIGHT_SCALE);
        return result;
    }

    private static void FromIntMap(int[] source, float[] target)
    {
        for (int i = 0; i < target.Length; i++)
            target[i] = source[i] / (float)HEIGHT_SCALE;
    }

    // Vector2Int 배열로 변환하여 HLSL int2 레이아웃에 맞춤
    private static Vector2Int[] BuildBrushOffsets(int[] brushX, int[] brushZ)
    {
        Vector2Int[] offsets = new Vector2Int[brushX.Length];
        for (int i = 0; i < brushX.Length; i++)
            offsets[i] = new Vector2Int(brushX[i], brushZ[i]);
        return offsets;
    }

    private static void PrecomputeBrush(
        int radius,
        out int[] offsetsX, out int[] offsetsZ, out float[] weights)
    {
        int maxCount  = (2 * radius + 1) * (2 * radius + 1);
        int[]   tempX = new int[maxCount];
        int[]   tempZ = new int[maxCount];
        float[] tempW = new float[maxCount];
        int     count = 0;
        float   weightSum = 0f;

        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > radius) continue;

                float w = radius > 0 ? 1f - dist / radius : 1f;
                tempX[count] = dx;
                tempZ[count] = dz;
                tempW[count] = w;
                weightSum += w;
                count++;
            }
        }

        offsetsX = new int[count];
        offsetsZ = new int[count];
        weights  = new float[count];
        for (int i = 0; i < count; i++)
        {
            offsetsX[i] = tempX[i];
            offsetsZ[i] = tempZ[i];
            weights[i]  = tempW[i] / weightSum;
        }
    }
}
