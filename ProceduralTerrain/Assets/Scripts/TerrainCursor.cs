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

    // ── 굴삭 코루틴: 하강 → X축 90° 회전 → 상승 → 지형 변형 ─────────
    private System.Collections.IEnumerator DigCoroutine()
    {
        IsAnimating = true;

        Vector3 startPos    = cursorCube.transform.position;
        Quaternion startRot = cursorCube.transform.rotation;

        // 1단계: 하강 (cursorScale.y 절반만큼 땅속으로)
        float dropDepth = cursorScale.y * 0.5f;
        Vector3 downPos = startPos + Vector3.down * dropDepth;
        yield return MoveCoroutine(cursorCube.transform, startPos, downPos, 0.2f);

        // 2단계: 월드 X축 기준 90° 회전 (버킷이 흙을 퍼올리는 동작)
        Quaternion endRot = Quaternion.AngleAxis(90f, Vector3.right) * startRot;
        yield return RotateCoroutine(cursorCube.transform, startRot, endRot, 0.4f);

        // 3단계: 상승 (원래 위치로 복귀)
        yield return MoveCoroutine(cursorCube.transform, downPos, startPos, 0.2f);

        // 완료: 지형 파기 + 흙 누적
        deformer.Deform(lastHitPoint, BrushRadius, strength, false);
        dirtSystem.AddDirt(strength);

        // 큐브 회전을 원점으로 복귀
        cursorCube.transform.rotation = startRot;

        IsAnimating = false;
    }

    // ── 덤프 코루틴: Y축 360° 회전 → 흙 전량 방출 → 지형 복구 ──────
    private System.Collections.IEnumerator DumpCoroutine()
    {
        IsAnimating = true;

        Quaternion startRot = cursorCube.transform.rotation;
        float duration      = 1f;
        float elapsed       = 0f;
        bool  dumped        = false;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Y축 기준 0° → 360° 회전
            cursorCube.transform.rotation =
                Quaternion.AngleAxis(360f * t, Vector3.up) * startRot;

            // 180° 도달 시 흙 방출 (뒤집히는 시점)
            if (!dumped && t >= 0.5f)
            {
                dirtSystem.DumpAll();
                dumped = true;
            }

            yield return null;
        }

        // 회전 원점 복귀
        cursorCube.transform.rotation = startRot;

        // 지형 복구 (파낸 만큼 한 번 올리기)
        if (deformer != null)
            deformer.Deform(lastHitPoint, BrushRadius, strength, true);

        IsAnimating = false;
    }

    // ── 보조 코루틴: 위치 보간 ──────────────────────────────────────
    private System.Collections.IEnumerator MoveCoroutine(
        Transform t, Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            t.position = Vector3.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        t.position = to;
    }

    // ── 보조 코루틴: 회전 보간 ──────────────────────────────────────
    private System.Collections.IEnumerator RotateCoroutine(
        Transform t, Quaternion from, Quaternion to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            t.rotation = Quaternion.Slerp(from, to, elapsed / duration);
            yield return null;
        }
        t.rotation = to;
    }

    private float BrushRadius => Mathf.Max(cursorScale.x, cursorScale.z) * 0.5f;

    public Vector3 CursorScale => cursorScale;

    // 코루틴에서 IsAnimating 해제에 사용
    protected void SetAnimating(bool value) => IsAnimating = value;
}
