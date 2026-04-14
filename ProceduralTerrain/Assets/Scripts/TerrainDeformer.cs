using UnityEngine;

[RequireComponent(typeof(TerrainGenerator))]
public class TerrainDeformer : MonoBehaviour
{
    [SerializeField] private ComputeShader deformShader;

    private TerrainGenerator generator;
    private ComputeBuffer heightBuffer;
    private ComputeBuffer volumeBuffer;
    private int kernelIndex;

    private readonly int[] volumeRaw = new int[1];

    [Header("Relaxation")]
    [Range(5f, 60f)]
    [SerializeField] private float reposeAngleDeg = 35f;

    [Range(1, 30)]
    [SerializeField] private int relaxIterations = 10;

    [Range(0.1f, 0.9f)]
    [SerializeField] private float flowRate = 0.5f;

    private ComputeBuffer relaxBuffer;
    private int relaxKernel;

    // Awake는 모든 Start() 이전에 실행되므로
    // TerrainGenerator.Start() → InitBuffer() 호출 시점에 generator가 준비됨
    private void Awake()
    {
        generator = GetComponent<TerrainGenerator>();
    }

    private void Start()
    {
        if (generator.HeightMap == null)
        {
            Debug.LogWarning("[TerrainDeformer] HeightMap이 아직 생성되지 않았습니다. TerrainGenerator의 실행 순서를 확인하세요.");
            return;
        }

        InitBuffer();
    }

    // TerrainGenerator에서 지형 생성 완료 후 호출
    // HeightMap 데이터를 GPU 버퍼에 올려두고 이후 변형 시 재사용
    public void InitBuffer()
    {
        if (deformShader == null)
        {
            Debug.LogWarning("[TerrainDeformer] Deform Compute Shader가 할당되지 않았습니다.");
            return;
        }

        heightBuffer?.Release();
        heightBuffer = new ComputeBuffer(generator.HeightMap.Length, sizeof(float));
        heightBuffer.SetData(generator.HeightMap);

        volumeBuffer?.Release();
        volumeBuffer = new ComputeBuffer(1, sizeof(int));

        kernelIndex = deformShader.FindKernel("CSDeform");

        relaxBuffer?.Release();
        relaxBuffer = new ComputeBuffer(generator.HeightMap.Length, sizeof(float));
        relaxKernel = deformShader.FindKernel("CSRelax");
    }

    /// <summary>
    /// 월드 좌표 기준으로 버킷 형태(직사각형)에 맞게 지형을 변형합니다.
    /// </summary>
    /// <param name="worldPos">버킷 중심 월드 좌표</param>
    /// <param name="halfExtentXWorld">버킷 X 절반 크기 (월드 단위)</param>
    /// <param name="halfExtentZWorld">버킷 Z 절반 크기 (월드 단위)</param>
    /// <param name="angleYDeg">버킷 Y 회전각 (도)</param>
    /// <param name="strength">프레임당 변형 강도</param>
    /// <param name="raise">true = 올리기, false = 파기</param>
    /// <returns>GPU에서 계산한 변형 부피 (월드 단위³)</returns>
    public float Deform(Vector3 worldPos, float halfExtentXWorld, float halfExtentZWorld,
                        float angleYDeg, float strength, bool raise)
    {
        if (heightBuffer == null)
        {
            Debug.LogWarning("[TerrainDeformer] 버퍼가 초기화되지 않았습니다. InitBuffer()를 먼저 호출하세요.");
            return 0f;
        }

        // 월드 좌표 → HeightMap 셀 좌표
        Vector3 local = worldPos - transform.position;
        float centerX = local.x / generator.CellSize;
        float centerZ = local.z / generator.CellSize;

        // 월드 단위 → 셀 단위
        float halfExtentX = halfExtentXWorld / generator.CellSize;
        float halfExtentZ = halfExtentZWorld / generator.CellSize;
        float angleRad    = angleYDeg * Mathf.Deg2Rad;

        // 회전된 OBB의 AABB 계산 → Dispatch 범위 결정
        float cosA   = Mathf.Abs(Mathf.Cos(angleRad));
        float sinA   = Mathf.Abs(Mathf.Sin(angleRad));
        float boundX = halfExtentX * cosA + halfExtentZ * sinA;
        float boundZ = halfExtentX * sinA + halfExtentZ * cosA;

        int startX    = Mathf.FloorToInt(centerX - boundX);
        int startZ    = Mathf.FloorToInt(centerZ - boundZ);
        int groupsX   = Mathf.CeilToInt((boundX * 2f + 1f) / 8f);
        int groupsZ   = Mathf.CeilToInt((boundZ * 2f + 1f) / 8f);

        // 볼륨 누산기 리셋
        volumeRaw[0] = 0;
        volumeBuffer.SetData(volumeRaw);

        // 셰이더 파라미터 설정
        deformShader.SetBuffer(kernelIndex, "_HeightMap",    heightBuffer);
        deformShader.SetBuffer(kernelIndex, "_VolumeAccum",  volumeBuffer);
        deformShader.SetInt   ("_Width",        generator.Width);
        deformShader.SetInt   ("_Depth",        generator.Depth);
        deformShader.SetFloat ("_CenterX",      centerX);
        deformShader.SetFloat ("_CenterZ",      centerZ);
        deformShader.SetFloat ("_HalfExtentX",  halfExtentX);
        deformShader.SetFloat ("_HalfExtentZ",  halfExtentZ);
        deformShader.SetFloat ("_AngleY",       angleRad);
        deformShader.SetInt   ("_StartX",       startX);
        deformShader.SetInt   ("_StartZ",       startZ);
        deformShader.SetFloat ("_Strength",     strength);
        deformShader.SetFloat ("_Direction",    raise ? 1f : -1f);

        deformShader.Dispatch(kernelIndex, groupsX, groupsZ, 1);

        // GPU → CPU HeightMap 동기화 후 Mesh 반영
        heightBuffer.GetData(generator.HeightMap);
        generator.ApplyHeightMap();

        // GPU 볼륨 읽기: 고정소수점(×10000) → 월드 단위³
        volumeBuffer.GetData(volumeRaw);
        float cellArea = generator.CellSize * generator.CellSize;
        return (volumeRaw[0] / 10000f) * cellArea;
    }

    /// <summary>
    /// 안식각 기반 경사 이완을 GPU에서 N회 실행합니다.
    /// 굴삭(Deform) 직후 호출하면 뾰족한 지형이 자연스럽게 흘러내립니다.
    /// </summary>
    public void Relax()
    {
        if (heightBuffer == null || relaxBuffer == null) return;

        float maxHeightDiff = generator.CellSize * Mathf.Tan(reposeAngleDeg * Mathf.Deg2Rad);
        int groupsX = Mathf.CeilToInt((generator.Width  + 1) / 8f);
        int groupsZ = Mathf.CeilToInt((generator.Depth  + 1) / 8f);

        ComputeBuffer src = heightBuffer;
        ComputeBuffer dst = relaxBuffer;

        for (int i = 0; i < relaxIterations; i++)
        {
            deformShader.SetBuffer(relaxKernel, "_HeightMapSrc",  src);
            deformShader.SetBuffer(relaxKernel, "_HeightMapDst",  dst);
            deformShader.SetInt   ("_Width",          generator.Width);
            deformShader.SetInt   ("_Depth",          generator.Depth);
            deformShader.SetFloat ("_MaxHeightDiff",  maxHeightDiff);
            deformShader.SetFloat ("_FlowRate",       flowRate);
            deformShader.Dispatch (relaxKernel, groupsX, groupsZ, 1);

            // ping-pong: src ↔ dst
            ComputeBuffer tmp = src;
            src = dst;
            dst = tmp;
        }

        // 최종 결과는 src에 있음 (마지막 dispatch 후 swap된 쪽)
        src.GetData(generator.HeightMap);
        generator.ApplyHeightMap();

        // 이후 Deform이 heightBuffer를 기준으로 동작하므로 동기화
        if (src != heightBuffer)
            heightBuffer.SetData(generator.HeightMap);
    }

    private void OnDestroy()
    {
        heightBuffer?.Release();
        volumeBuffer?.Release();
        relaxBuffer?.Release();
    }
}
