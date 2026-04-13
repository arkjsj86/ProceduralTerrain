using UnityEngine;

[RequireComponent(typeof(TerrainGenerator))]
public class TerrainDeformer : MonoBehaviour
{
    [SerializeField] private ComputeShader deformShader;

    private TerrainGenerator generator;
    private ComputeBuffer heightBuffer;
    private int kernelIndex;

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
        kernelIndex = deformShader.FindKernel("CSDeform");
    }

    /// <summary>
    /// 월드 좌표 기준으로 지형을 변형합니다.
    /// </summary>
    /// <param name="worldPos">변형 중심 월드 좌표</param>
    /// <param name="radius">영향 반경 (셀 단위)</param>
    /// <param name="strength">프레임당 변형 강도</param>
    /// <param name="raise">true = 올리기, false = 파기</param>
    public void Deform(Vector3 worldPos, float radius, float strength, bool raise)
    {
        Debug.Log($"[TerrainDeformer] Deform 진입 — buffer={(heightBuffer != null ? "OK" : "NULL")}, shader={(deformShader != null ? "OK" : "NULL")}");
        if (heightBuffer == null)
        {
            Debug.LogWarning("[TerrainDeformer] 버퍼가 초기화되지 않았습니다. InitBuffer()를 먼저 호출하세요.");
            return;
        }

        // 월드 좌표 → HeightMap 셀 좌표
        Vector3 local = worldPos - transform.position;
        float centerX = local.x / generator.CellSize;
        float centerZ = local.z / generator.CellSize;

        // 셰이더 파라미터 설정
        deformShader.SetBuffer(kernelIndex, "_HeightMap", heightBuffer);
        deformShader.SetInt  ("_Width",     generator.Width);
        deformShader.SetInt  ("_Depth",     generator.Depth);
        deformShader.SetFloat("_CenterX",   centerX);
        deformShader.SetFloat("_CenterZ",   centerZ);
        deformShader.SetFloat("_Radius",    radius);
        deformShader.SetFloat("_Strength",  strength);
        deformShader.SetFloat("_Direction", raise ? 1f : -1f);

        // 영향 범위를 커버하는 스레드 그룹 수 계산
        int diameter    = Mathf.CeilToInt(radius * 2f) + 1;
        int groupCount  = Mathf.CeilToInt(diameter / 8f);
        deformShader.Dispatch(kernelIndex, groupCount, groupCount, 1);

        // GPU → CPU HeightMap 동기화 후 Mesh 반영
        heightBuffer.GetData(generator.HeightMap);
        Debug.Log($"[TerrainDeformer] Dispatch 완료 — centerX={centerX:F1}, centerZ={centerZ:F1}, radius={radius}, strength={strength}, dir={( raise ? "+1" : "-1")}");
        generator.ApplyHeightMap();
    }

    private void OnDestroy()
    {
        heightBuffer?.Release();
    }
}
