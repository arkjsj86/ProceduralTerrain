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
    private Vector3 lastHitPoint;
    private bool isHitting;

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        CreateCursorCube();
    }

    private void CreateCursorCube()
    {
        cursorCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cursorCube.name = "CursorCube";
        cursorCube.transform.SetParent(transform);
        cursorCube.transform.localScale = cursorScale;

        // Raycast에 방해되지 않도록 콜라이더 제거
        Destroy(cursorCube.GetComponent<Collider>());

        cursorCube.SetActive(false);
    }

    private void Update()
    {
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

        // 좌클릭 홀드 = 파기, 우클릭 홀드 = 올리기
        if (Mouse.current.leftButton.isPressed)
            deformer.Deform(lastHitPoint, cursorScale.x * 0.5f, strength, false);

        if (Mouse.current.rightButton.isPressed)
            deformer.Deform(lastHitPoint, cursorScale.x * 0.5f, strength, true);
    }

    public Vector3 CursorScale => cursorScale;
}
