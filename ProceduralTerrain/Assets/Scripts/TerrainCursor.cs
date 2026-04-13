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

    // 애니메이션 진행 중 커서 추적·입력 차단
    public bool IsAnimating { get; private set; }

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        CreateCursorCube();

        dirtSystem = cursorCube.AddComponent<DirtSystem>();
        dirtSystem.Initialize(cursorCube.transform, cursorScale);
    }

    private void CreateCursorCube()
    {
        cursorCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cursorCube.name = "CursorCube";
        cursorCube.transform.SetParent(transform);
        cursorCube.transform.localScale = cursorScale;

        Destroy(cursorCube.GetComponent<Collider>());

        cursorCube.GetComponent<MeshRenderer>().material = CreateTransparentMaterial();

        cursorCube.SetActive(false);
    }

    private Material CreateTransparentMaterial()
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
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
        // 애니메이션 중에는 커서 추적·입력 모두 차단
        if (IsAnimating) return;

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

        // 단일 클릭 감지 (홀드 아님)
        if (Mouse.current.leftButton.wasPressedThisFrame)
            StartCoroutine(DigCoroutine());

        if (Mouse.current.rightButton.wasPressedThisFrame)
            StartCoroutine(DumpCoroutine());
    }

    // 굴삭 코루틴 (Commit 3에서 구현)
    private System.Collections.IEnumerator DigCoroutine()
    {
        IsAnimating = true;
        yield return null; // placeholder
        IsAnimating = false;
    }

    // 덤프 코루틴 (Commit 4에서 구현)
    private System.Collections.IEnumerator DumpCoroutine()
    {
        IsAnimating = true;
        yield return null; // placeholder
        IsAnimating = false;
    }

    private float BrushRadius => Mathf.Max(cursorScale.x, cursorScale.z) * 0.5f;

    public Vector3 CursorScale => cursorScale;

    // 코루틴에서 IsAnimating 해제에 사용
    protected void SetAnimating(bool value) => IsAnimating = value;
}
