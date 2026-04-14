using UnityEngine;
using UnityEngine.InputSystem;

public class TerrainCursor : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private TerrainDeformer deformer;

    // 큐브 크기 (XZ = 버킷 굴삭 영역, Y = 시각적 높이)
    [SerializeField] private Vector3 cursorScale = new Vector3(4f, 2f, 4f);

    [Range(0.01f, 3f)]
    [SerializeField] private float strength = 0.5f;

    [Header("Dig")]
    // 하강 깊이 (cursorScale.y 대비 배율)
    [Range(0.5f, 3f)]
    [SerializeField] private float digDepthMultiplier = 1.5f;

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

    // ── 굴삭 코루틴: 세우기 → 하강 → 눕히기(퍼올리기) → 상승 ────────
    // --- → | → 하강 → 땅속에서 --- → 상승
    private System.Collections.IEnumerator DigCoroutine()
    {
        IsAnimating = true;

        Vector3    startPos    = cursorCube.transform.position;
        Quaternion startRot    = cursorCube.transform.rotation;
        Quaternion uprightRot  = Quaternion.AngleAxis(90f, Vector3.right) * startRot;

        float   dropDepth = cursorScale.y * digDepthMultiplier;
        Vector3 downPos   = startPos + Vector3.down * dropDepth;

        // 1단계: 수직으로 세우기 (--- → |)
        yield return RotateCoroutine(cursorCube.transform, startRot, uprightRot, 0.2f);

        // 2단계: 수직 상태로 하강 (| 땅속으로)
        yield return MoveCoroutine(cursorCube.transform, startPos, downPos, 0.2f);

        // 3단계: 땅속에서 수평으로 눕히기 (| → ---) ← 흙을 퍼올리는 동작
        yield return RotateCoroutine(cursorCube.transform, uprightRot, startRot, 0.4f);

        // 4단계: 수평 상태로 상승 (--- 원위치)
        yield return MoveCoroutine(cursorCube.transform, downPos, startPos, 0.2f);

        // 완료: 버킷 XZ 크기 + Y 회전 → 직사각형 지형 파기
        // 반환된 볼륨(GPU 계산)을 흙 적재량으로 사용
        float volume = deformer.Deform(
            lastHitPoint,
            cursorScale.x * 0.5f,
            cursorScale.z * 0.5f,
            cursorCube.transform.eulerAngles.y,
            strength,
            false
        );
        deformer.Relax();
        dirtSystem.AddDirt(volume);

        IsAnimating = false;
    }

    // ── 덤프 코루틴: Y축 360° 회전 → 흙 전량 방출 → 지형 복구 ──────
    private System.Collections.IEnumerator DumpCoroutine()
    {
        // 흙이 없으면 애니메이션 자체를 스킵
        if (dirtSystem.AccumulatedDirt <= 0f)
        {
            yield break;
        }

        IsAnimating = true;

        // DumpAll() 호출 전에 미리 캡처 (호출 후에는 0이 됨)
        float dirtToDump    = dirtSystem.AccumulatedDirt;
        Quaternion startRot = cursorCube.transform.rotation;
        float duration      = 1f;
        float elapsed       = 0f;
        bool  dumped        = false;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // X축 기준 0° → 360° 회전 (버킷이 앞으로 한 바퀴 — 굴삭과 동일 축)
            cursorCube.transform.rotation =
                Quaternion.AngleAxis(360f * t, Vector3.right) * startRot;

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

        // 지형 복구: 버킷에 쌓인 비율만큼만 올리기
        // 절반만 찼으면 절반만 복구 (strength × dirtToDump / OverflowThreshold)
        if (deformer != null)
        {
            float scaledStrength = strength * (dirtToDump / dirtSystem.OverflowThreshold);
            deformer.Deform(
                lastHitPoint,
                cursorScale.x * 0.5f,
                cursorScale.z * 0.5f,
                cursorCube.transform.eulerAngles.y,
                scaledStrength,
                true
            );
        }

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

    public Vector3 CursorScale => cursorScale;

    // 코루틴에서 IsAnimating 해제에 사용
    protected void SetAnimating(bool value) => IsAnimating = value;
}
