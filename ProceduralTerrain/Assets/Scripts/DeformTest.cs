using UnityEngine;

/// <summary>
/// TerrainDeformer 동작 확인용 임시 테스트 스크립트
/// 이 오브젝트의 위치를 중심으로 지형을 변형합니다.
/// 4단계(TerrainCursor) 구현 완료 후 삭제 예정
/// </summary>
public class DeformTest : MonoBehaviour
{
    [SerializeField] private TerrainDeformer deformer;

    [Range(1f, 20f)]
    [SerializeField] private float radius = 5f;

    [Range(0.01f, 0.5f)]
    [SerializeField] private float strength = 0.05f;

    private void Update()
    {
        if (deformer == null) return;

        // 좌클릭 홀드 = 파기
        if (Input.GetMouseButton(0))
            deformer.Deform(transform.position, radius, strength, false);

        // 우클릭 홀드 = 올리기
        if (Input.GetMouseButton(1))
            deformer.Deform(transform.position, radius, strength, true);
    }

    // 씬 뷰에서 브러시 범위 시각화
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
