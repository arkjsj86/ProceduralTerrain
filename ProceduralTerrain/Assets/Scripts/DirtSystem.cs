using UnityEngine;

public class DirtSystem : MonoBehaviour
{
    [Header("Pile")]
    [Range(5f, 100f)]
    [SerializeField] private float particlesPerUnit = 30f;

    // 큐브가 꽉 찼을 때의 더미 최대 높이 (큐브 로컬 Y 단위)
    [Range(0f, 1f)]
    [SerializeField] private float maxPileHeight = 0.4f;

    [SerializeField] private Color dirtColor = new Color(0.45f, 0.3f, 0.15f);

    // 현재 쌓인 흙의 양
    public float AccumulatedDirt { get; private set; }

    // 넘침 기준: 큐브 XZ 면적 (커밋 3에서 사용)
    public float OverflowThreshold { get; private set; }

    private ParticleSystem pilePS;
    private Transform cubeTransform;

    // 파티클 위치/크기/색상을 count 변경 시에만 재계산하여 매 프레임 지터 방지
    private ParticleSystem.Particle[] cachedParticles;
    private int cachedCount = -1;

    // ── 초기화 ────────────────────────────────────────────────────
    public void Initialize(Transform cube, Vector3 cubeScale)
    {
        cubeTransform = cube;
        OverflowThreshold = cubeScale.x * cubeScale.z;

        SetupParticleSystem();
    }

    // ── 흙 추가 (파기 시 호출) ─────────────────────────────────────
    public void AddDirt(float amount)
    {
        AccumulatedDirt += amount;
        // 넘침 처리는 커밋 3에서 추가
    }

    // ── 매 프레임: 큐브 이동에 맞춰 파티클 위치 갱신 ────────────────
    private void LateUpdate()
    {
        if (cubeTransform == null || pilePS == null) return;
        UpdatePileDisplay();
    }

    private void UpdatePileDisplay()
    {
        float dirtForPile  = Mathf.Min(AccumulatedDirt, OverflowThreshold);
        int   targetCount  = Mathf.FloorToInt(dirtForPile * particlesPerUnit);
        targetCount = Mathf.Min(targetCount, pilePS.main.maxParticles);

        // count 변경 시에만 위치/외형 재계산
        if (targetCount != cachedCount)
        {
            cachedParticles = BuildParticleCache(targetCount, dirtForPile);
            cachedCount = targetCount;
        }

        if (targetCount == 0)
        {
            pilePS.Clear();
            return;
        }

        // cubeTransform.TransformPoint으로 큐브 로컬 → 월드 좌표 변환
        // → 큐브가 이동/회전해도 파티클이 항상 큐브 상단에 붙어있음
        for (int i = 0; i < targetCount; i++)
        {
            cachedParticles[i].position =
                cubeTransform.TransformPoint(cachedParticles[i].position);
        }

        pilePS.SetParticles(cachedParticles, targetCount);
    }

    // 큐브 로컬 공간 기준으로 파티클 위치/외형 사전 계산
    // (TransformPoint 적용 전 상태로 저장)
    private ParticleSystem.Particle[] BuildParticleCache(int count, float dirtForPile)
    {
        var particles = new ParticleSystem.Particle[count];
        float fillRatio = OverflowThreshold > 0f ? dirtForPile / OverflowThreshold : 0f;

        for (int i = 0; i < count; i++)
        {
            // 큐브 단위 로컬 좌표: X,Z → [-0.45, 0.45], Y → 상단(0.5) + 더미 높이
            float lx = Random.Range(-0.45f, 0.45f);
            float lz = Random.Range(-0.45f, 0.45f);
            float ly = 0.5f + Random.Range(0f, maxPileHeight * fillRatio);

            particles[i].position        = new Vector3(lx, ly, lz); // TransformPoint 전
            particles[i].startLifetime   = 999f;
            particles[i].remainingLifetime = 999f;
            particles[i].startSize       = Random.Range(0.06f, 0.14f);
            particles[i].startColor      = dirtColor * Random.Range(0.85f, 1.15f);
        }

        return particles;
    }

    private void SetupParticleSystem()
    {
        pilePS = gameObject.AddComponent<ParticleSystem>();

        var main = pilePS.main;
        main.loop           = false;
        main.playOnAwake    = false;
        main.startLifetime  = 999f;
        main.startSpeed     = 0f;
        main.maxParticles   = 10000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // 코드로 직접 SetParticles 하므로 자동 방출 비활성화
        var emission = pilePS.emission;
        emission.enabled = false;

        var renderer = pilePS.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch; // 작은 덩어리감

        pilePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDestroy()
    {
        if (pilePS != null) pilePS.Clear();
    }
}
