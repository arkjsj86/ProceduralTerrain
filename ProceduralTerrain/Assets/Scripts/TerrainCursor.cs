using UnityEngine;
using UnityEngine.InputSystem;

public class TerrainCursor : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private TerrainDeformer deformer;

    // 큐브 크기 (XZ = 브러시 영역, Y = 시각적 높이)
    [SerializeField] private Vector3 cursorScale = new Vector3(4f, 2f, 4f);

    [Range(0.01f, 0.5f)]
    [SerializeField] private float strength = 0.05f;

    private GameObject cursorCube;
    private DirtSystem dirtSystem;
    private Vector3 lastHitPoint;
    private bool isHitting;

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        CreateCursorCube();

        // DirtSystem을 큐브 오브젝트에 부착 → 파티클이 큐브 기준 좌표로 쌓임
        dirtSystem = cursorCube.AddComponent<DirtSystem>();
        dirtSystem.Initialize(cursorCube.transform, cursorScale);
    }

    private void CreateCursorCube()
    {
        cursorCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cursorCube.name = "CursorCube";
        cursorCube.transform.SetParent(transform);
        cursorCube.transform.localScale = cursorScale;

        // Raycast에 방해되지 않도록 콜라이더 제거
        Destroy(cursorCube.GetComponent<Collider>());

        // 반투명 재질 적용 (브러시 영역을 시각적으로 표시)
        cursorCube.GetComponent<MeshRenderer>().material = CreateTransparentMaterial();

        cursorCube.SetActive(false);
    }

    private Material CreateTransparentMaterial()
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        // URP Lit 투명 모드 설정
        mat.SetFloat("_Surface", 1f);              // 0=Opaque, 1=Transparent
        mat.SetFloat("_Blend", 0f);                // Alpha blending
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetColor("_BaseColor", new Color(0.4f, 0.7f, 1f, 0.25f));
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }

    private void Update()
    {
        cursorCube.transform.localScale = cursorScale;
        UpdateCursorPosition();
        HandleInput();
    }

    private void UpdateCursorPosition()
    {
        Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            isHitting = true;
            lastHitPoint = hit.point;
            cursorCube.SetActive(true);
            cursorCube.transform.position = hit.point + Vector3.up * (cursorScale.y * 0.5f);
        }
        else
        {
            isHitting = false;
            cursorCube.SetActive(false);
        }
    }

    private void HandleInput()
    {
        if (!isHitting || deformer == null) return;

        float radius = BrushRadius;

        // 좌클릭 홀드 = 파기, 우클릭 홀드 = 올리기
        if (Mouse.current.leftButton.isPressed)
        {
            deformer.Deform(lastHitPoint, radius, strength, false);
            dirtSystem?.AddDirt(strength);
        }

        if (Mouse.current.rightButton.isPressed)
            deformer.Deform(lastHitPoint, radius, strength, true);
    }

    // XZ 중 큰 쪽의 절반을 반경으로 사용
    // Inspector에서 cursorScale을 바꾸면 브러시 영역이 즉시 반영됨
    private float BrushRadius => Mathf.Max(cursorScale.x, cursorScale.z) * 0.5f;

    public Vector3 CursorScale => cursorScale;
}
