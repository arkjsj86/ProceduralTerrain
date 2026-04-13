using UnityEngine;

public static class HydraulicErosion
{
    public static void Erode(float[] heightMap, int width, int depth, ErosionSettings settings, int seed = 0)
    {
        PrecomputeBrush(settings.erodeRadius,
            out int[] brushX, out int[] brushZ, out float[] brushWeights);

        System.Random rng = new System.Random(seed);

        for (int d = 0; d < settings.numDroplets; d++)
        {
            float posX     = (float)(rng.NextDouble() * width);
            float posZ     = (float)(rng.NextDouble() * depth);
            float dirX     = 0f;
            float dirZ     = 0f;
            float speed    = settings.startSpeed;
            float water    = settings.startWater;
            float sediment = 0f;

            for (int lifetime = 0; lifetime < settings.maxLifetime; lifetime++)
            {
                int nodeX = (int)posX;
                int nodeZ = (int)posZ;

                if (nodeX < 0 || nodeX >= width || nodeZ < 0 || nodeZ >= depth) break;

                // 현재 위치의 높이와 경사 (이중선형 보간)
                GetHeightAndGradient(heightMap, width, posX, posZ,
                    out float height, out float gradX, out float gradZ);

                // 관성 반영 방향 갱신
                dirX = dirX * settings.inertia - gradX * (1f - settings.inertia);
                dirZ = dirZ * settings.inertia - gradZ * (1f - settings.inertia);

                float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                if (len == 0f) break;
                dirX /= len;
                dirZ /= len;

                float oldPosX = posX;
                float oldPosZ = posZ;
                posX += dirX;
                posZ += dirZ;

                if (posX < 0 || posX >= width || posZ < 0 || posZ >= depth) break;

                // 이동 후 높이
                GetHeightAndGradient(heightMap, width, posX, posZ,
                    out float newHeight, out _, out _);

                // 고도 차: 음수 = 내리막, 양수 = 오르막
                float deltaHeight = newHeight - height;

                // 토사 운반 용량
                float capacity = Mathf.Max(-deltaHeight, settings.minSedimentCapacity)
                                 * speed * water * settings.sedimentCapacityFactor;

                if (sediment > capacity || deltaHeight > 0f)
                {
                    // 퇴적: 오르막이면 고도 차만큼, 아니면 초과분의 일부를 퇴적
                    float depositAmount = deltaHeight > 0f
                        ? Mathf.Min(deltaHeight, sediment)
                        : (sediment - capacity) * settings.depositSpeed;

                    sediment -= depositAmount;
                    DepositSediment(heightMap, width, depth, oldPosX, oldPosZ, depositAmount);
                }
                else
                {
                    // 침식: 용량 여유분만큼 깎되 내리막 높이차를 초과하지 않음
                    float erodeAmount = Mathf.Min(
                        (capacity - sediment) * settings.erodeSpeed, -deltaHeight);

                    sediment += erodeAmount;
                    ErodeTerrain(heightMap, width, depth,
                        nodeX, nodeZ, brushX, brushZ, brushWeights, erodeAmount);
                }

                // 속도 갱신: 내리막(-deltaHeight > 0) → 가속, 오르막 → 감속
                speed = Mathf.Sqrt(Mathf.Max(speed * speed - deltaHeight * settings.gravity, 0f));

                water *= 1f - settings.evaporateSpeed;
                if (water < 0.001f) break;
            }

            // 수명 종료 시 잔여 토사 전량 퇴적
            if (sediment > 0f)
                DepositSediment(heightMap, width, depth, posX, posZ, sediment);
        }
    }

    // erodeRadius 기반 브러시 오프셋과 정규화된 가중치 사전 계산
    private static void PrecomputeBrush(
        int radius,
        out int[] offsetsX, out int[] offsetsZ, out float[] weights)
    {
        int maxCount = (2 * radius + 1) * (2 * radius + 1);
        int[] tempX = new int[maxCount];
        int[] tempZ = new int[maxCount];
        float[] tempW = new float[maxCount];
        int count = 0;
        float weightSum = 0f;

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

    // float 좌표 기반 이중선형 보간으로 높이와 경사 계산
    private static void GetHeightAndGradient(
        float[] map, int width,
        float posX, float posZ,
        out float height, out float gradX, out float gradZ)
    {
        int x = (int)posX;
        int z = (int)posZ;
        float u = posX - x;
        float v = posZ - z;

        int stride = width + 1;
        float h00 = map[z * stride + x];
        float h10 = map[z * stride + x + 1];
        float h01 = map[(z + 1) * stride + x];
        float h11 = map[(z + 1) * stride + x + 1];

        gradX = (h10 - h00) * (1f - v) + (h11 - h01) * v;
        gradZ = (h01 - h00) * (1f - u) + (h11 - h10) * u;

        height = h00 * (1f - u) * (1f - v)
               + h10 * u       * (1f - v)
               + h01 * (1f - u) * v
               + h11 * u       * v;
    }

    // float 위치에서 이중선형으로 토사 퇴적 (4개 인접 격자에 분산)
    private static void DepositSediment(
        float[] map, int width, int depth,
        float posX, float posZ, float amount)
    {
        int x = (int)posX;
        int z = (int)posZ;
        if (x < 0 || x >= width || z < 0 || z >= depth) return;

        float u = posX - x;
        float v = posZ - z;
        int stride = width + 1;

        map[z * stride + x]             += amount * (1f - u) * (1f - v);
        map[z * stride + x + 1]         += amount * u        * (1f - v);
        map[(z + 1) * stride + x]       += amount * (1f - u) * v;
        map[(z + 1) * stride + x + 1]   += amount * u        * v;
    }

    // 브러시 가중치에 따라 주변 격자에 침식량 분산 적용
    private static void ErodeTerrain(
        float[] map, int width, int depth,
        int nodeX, int nodeZ,
        int[] brushX, int[] brushZ, float[] brushWeights,
        float amount)
    {
        for (int i = 0; i < brushX.Length; i++)
        {
            int bx = nodeX + brushX[i];
            int bz = nodeZ + brushZ[i];
            if (bx < 0 || bx > width || bz < 0 || bz > depth) continue;
            map[bz * (width + 1) + bx] -= brushWeights[i] * amount;
        }
    }
}
