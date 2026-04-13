using UnityEngine;
using UnityEngine.InputSystem;

public class TerrainCursor : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;

    // 큐브 크기 (XZ = 브러시 영역, Y = 시각적 높이)
    [SerializeField] private Vector3 cursorScale = new Vector3(4f, 2f, 4f);

    private GameObject cursorCube;

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
    }

    private void UpdateCursorPosition()
    {
        Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            cursorCube.SetActive(true);
            // 큐브 하단이 지형 표면에 닿도록 Y 오프셋 적용
            cursorCube.transform.position = hit.point + Vector3.up * (cursorScale.y * 0.5f);
        }
        else
        {
            cursorCube.SetActive(false);
        }
    }

    public Vector3 CursorScale => cursorScale;
}
