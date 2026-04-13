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

    [Header("Overflow")]
    // 넘침 시 방출할 파티클 수 (누적량 1단위 당)
    [Range(1, 20)]
    [SerializeField] private int overflowParticlesPerUnit = 5;

    // 현재 쌓인 흙의 양
    public float AccumulatedDirt { get; private set; }

    // 넘침 기준: 큐브 XZ 면적
    public float OverflowThreshold { get; private set; }

    private ParticleSystem pilePS;
    private ParticleSystem overflowPS;
    private Transform cubeTransform;

    // 로컬 좌표 기준 파티클 캐시 (TransformPoint 적용 전)
    private ParticleSystem.Particle[] cachedParticles;
    // SetParticles용 월드 좌표 작업 버퍼 (매 프레임 재사용)
    private ParticleSystem.Particle[] workParticles;
    private int cachedCount = -1;

    // ── 초기화 ────────────────────────────────────────────────────
    public void Initialize(Transform cube, Vector3 cubeScale)
    {
        cubeTransform = cube;
        OverflowThreshold = cubeScale.x * cubeScale.z;

        SetupParticleSystem();
        SetupOverflowParticleSystem();
    }

    // ── 흙 추가 (파기 시 호출) ─────────────────────────────────────
    public void AddDirt(float amount)
    {
        float before = AccumulatedDirt;
        AccumulatedDirt += amount;

        // OverflowThreshold를 초과한 만큼 낙하 파티클로 방출
        if (AccumulatedDirt > OverflowThreshold)
        {
            float excess = AccumulatedDirt - Mathf.Max(before, OverflowThreshold);
            AccumulatedDirt = OverflowThreshold; // 더미는 최대치로 고정
            EmitOverflow(excess);
        }
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

        // 작업 버퍼 준비 (크기 부족 시에만 재할당)
        if (workParticles == null || workParticles.Length < targetCount)
            workParticles = new ParticleSystem.Particle[targetCount];

        // cachedParticles(로컬 좌표)를 workParticles(월드 좌표)로 복사
        // → cachedParticles 원본은 변경하지 않으므로 매 프레임 안전하게 재사용 가능
        for (int i = 0; i < targetCount; i++)
        {
            workParticles[i]          = cachedParticles[i];
            workParticles[i].position = cubeTransform.TransformPoint(cachedParticles[i].position);
        }

        pilePS.SetParticles(workParticles, targetCount);
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

            particles[i].position          = new Vector3(lx, ly, lz); // TransformPoint 전
            particles[i].startLifetime     = 999f;
            particles[i].remainingLifetime = 999f;
            particles[i].startSize         = Random.Range(0.06f, 0.14f);
            particles[i].startColor        = dirtColor * Random.Range(0.85f, 1.15f);
        }

        return particles;
    }

    // ── 오버플로우: 큐브 위에서 랜덤 방향으로 튀어나가는 낙하 파티클 ──
    private void EmitOverflow(float excessAmount)
    {
        if (overflowPS == null || cubeTransform == null) return;

        int count = Mathf.Max(1, Mathf.RoundToInt(excessAmount * overflowParticlesPerUnit));

        // EmitParams로 개별 파티클의 초기 위치·속도 지정
        var emitParams = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            // 큐브 상단 임의 위치 → 월드 좌표
            float lx = Random.Range(-0.45f, 0.45f);
            float lz = Random.Range(-0.45f, 0.45f);
            emitParams.position = cubeTransform.TransformPoint(new Vector3(lx, 0.5f, lz));

            // 측면으로 퍼지는 초기 속도 (중력은 overflowPS.main.gravityModifier가 담당)
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float hSpeed = Random.Range(1f, 4f);
            emitParams.velocity = new Vector3(
                Mathf.Cos(angle) * hSpeed,
                Random.Range(0.5f, 2f),
                Mathf.Sin(angle) * hSpeed
            );

            emitParams.startSize  = Random.Range(0.07f, 0.16f);
            emitParams.startColor = dirtColor * Random.Range(0.8f, 1.2f);

            overflowPS.Emit(emitParams, 1);
        }
    }

    private void SetupParticleSystem()
    {
        pilePS = gameObject.AddComponent<ParticleSystem>();

        var main = pilePS.main;
        main.loop            = false;
        main.playOnAwake     = false;
        main.startLifetime   = 999f;
        main.startSpeed      = 0f;
        main.maxParticles    = 10000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // 코드로 직접 SetParticles 하므로 자동 방출 비활성화
        var emission = pilePS.emission;
        emission.enabled = false;

        var renderer = pilePS.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch; // 작은 덩어리감

        pilePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void SetupOverflowParticleSystem()
    {
        // 오버플로우 전용 GameObject (pilePS와 분리하여 독립 수명 관리)
        var overflowGO = new GameObject("DirtOverflow");
        overflowGO.transform.SetParent(transform);

        overflowPS = overflowGO.AddComponent<ParticleSystem>();

        var main = overflowPS.main;
        main.loop            = false;
        main.playOnAwake     = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSpeed      = 0f;          // EmitParams.velocity로 직접 지정
        main.maxParticles    = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 2f;          // 중력 효과로 자연스러운 낙하

        // Emit()으로만 방출
        var emission = overflowPS.emission;
        emission.enabled = false;

        var renderer = overflowPS.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;

        overflowPS.Play(); // 재생 상태여야 Emit() 동작
    }

    private void OnDestroy()
    {
        if (pilePS != null) pilePS.Clear();
    }
}
