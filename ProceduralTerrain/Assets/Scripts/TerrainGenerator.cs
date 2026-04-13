using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainGenerator : MonoBehaviour
{
    [SerializeField] private int width = 128;
    [SerializeField] private int depth = 128;

    // 셀 하나의 월드 크기
    [SerializeField] private float cellSize = 1f;

    [SerializeField] public NoiseSettings noiseSettings = new NoiseSettings();

    [SerializeField] public bool applyErosion = true;
    [SerializeField] public ErosionSettings erosionSettings = new ErosionSettings();

    [SerializeField] private bool useGPUErosion = true;
    [SerializeField] private ComputeShader erosionComputeShader;

    // 침식 알고리즘(2단계)과 Compute Shader(3단계)에서 직접 접근
    public float[] HeightMap { get; private set; }

    public int Width    => width;
    public int Depth    => depth;
    public float CellSize => cellSize;

    private Mesh mesh;

    private void Start()
    {
        GenerateTerrain();
    }

    public void GenerateTerrain()
    {
        HeightMap = new float[(width + 1) * (depth + 1)];
        GenerateHeightMap();
        if (applyErosion)
            RunErosion();
        BuildMesh();
    }

    private void RunErosion()
    {
        if (useGPUErosion)
        {
            if (erosionComputeShader == null)
            {
                Debug.LogWarning("[TerrainGenerator] Erosion Compute Shader가 할당되지 않았습니다. CPU로 폴백합니다.");
                HydraulicErosion.Erode(HeightMap, width, depth, erosionSettings);
                return;
            }

            new ComputeErosion(erosionComputeShader)
                .Erode(HeightMap, width, depth, erosionSettings);
        }
        else
        {
            HydraulicErosion.Erode(HeightMap, width, depth, erosionSettings);
        }
    }

    private void GenerateHeightMap()
    {
        // 시드 기반 옥타브별 랜덤 오프셋 생성
        System.Random rng = new System.Random(noiseSettings.seed);
        Vector2[] octaveOffsets = new Vector2[noiseSettings.octaves];
        for (int o = 0; o < noiseSettings.octaves; o++)
        {
            float ox = (float)(rng.NextDouble() * 200000 - 100000) + noiseSettings.offset.x;
            float oz = (float)(rng.NextDouble() * 200000 - 100000) + noiseSettings.offset.y;
            octaveOffsets[o] = new Vector2(ox, oz);
        }

        for (int z = 0; z <= depth; z++)
        {
            for (int x = 0; x <= width; x++)
            {
                float noiseHeight = 0f;
                float currentAmplitude = noiseSettings.amplitude;
                float currentFrequency = noiseSettings.frequency;

                for (int o = 0; o < noiseSettings.octaves; o++)
                {
                    float sampleX = (x / (float)width) * currentFrequency + octaveOffsets[o].x;
                    float sampleZ = (z / (float)depth) * currentFrequency + octaveOffsets[o].y;

                    // [0,1] → [-1,1] 리맵: 봉우리와 계곡이 모두 나타남
                    float perlin = Mathf.PerlinNoise(sampleX, sampleZ) * 2f - 1f;
                    noiseHeight += perlin * currentAmplitude;

                    currentAmplitude *= noiseSettings.persistence;
                    currentFrequency *= noiseSettings.lacunarity;
                }

                HeightMap[z * (width + 1) + x] = noiseHeight;
            }
        }
    }

    private void BuildMesh()
    {
        int vertexCount = (width + 1) * (depth + 1);

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[width * depth * 6];

        for (int z = 0; z <= depth; z++)
        {
            for (int x = 0; x <= width; x++)
            {
                int i = z * (width + 1) + x;
                vertices[i] = new Vector3(x * cellSize, HeightMap[i], z * cellSize);
                uvs[i] = new Vector2((float)x / width, (float)z / depth);
            }
        }

        int t = 0;
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = z * (width + 1) + x;
                triangles[t++] = i;
                triangles[t++] = i + width + 1;
                triangles[t++] = i + 1;

                triangles[t++] = i + 1;
                triangles[t++] = i + width + 1;
                triangles[t++] = i + width + 2;
            }
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = "ProceduralTerrain" };
            GetComponent<MeshFilter>().mesh = mesh;
        }

        // 128*128 = 16641 vertices, UInt16 범위(65535) 내이므로 기본값 유지
        // resolution 확장 시 UInt32로 전환 필요
        mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // HeightMap 수정 후 Mesh를 다시 그릴 때 호출 (런타임 침식에서 사용)
    public void ApplyHeightMap()
    {
        if (mesh == null) return;

        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i].y = HeightMap[i];

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}
