# ProceduralTerrain

Unity URP 기반 절차적 지형 생성 프로젝트.  
펄린 노이즈 높이맵부터 GPU 가속 침식, 버킷 굴삭, 지형 셰이더까지 단계적으로 구현한 학습 및 데모 프로젝트입니다.

## 환경

| 항목 | 버전 |
|------|------|
| Unity | 6000.3.5f2 |
| Render Pipeline | Universal Render Pipeline (URP) |
| Input System | Unity New Input System |

---

## 구현 기능

### 1단계: 절차적 높이맵 생성

- **fBm(Fractal Brownian Motion) 노이즈** — 옥타브 중첩으로 복잡한 지형 생성
- Inspector에서 실시간 조절 가능한 파라미터:

| 파라미터 | 설명 |
|----------|------|
| Octaves (1~8) | 노이즈 레이어 수 |
| Frequency | 노이즈 주파수 |
| Amplitude | 높이 진폭 |
| Lacunarity | 옥타브별 주파수 증가 배율 |
| Persistence | 옥타브별 진폭 감소 배율 |
| Seed | 랜덤 시드 |
| Offset | 노이즈 샘플링 위치 오프셋 |

---

### 2단계: 수압 침식 (Hydraulic Erosion)

- **입자 기반 물방울 시뮬레이션** — 경사면을 따라 이동하며 토사 침식/퇴적
- CPU 구현 (`HydraulicErosion.cs`)

| 파라미터 | 설명 |
|----------|------|
| Num Droplets | 물방울 수 (기본 50,000) |
| Max Lifetime | 물방울 최대 수명 |
| Inertia | 방향 관성 (1=고정, 0=즉시 반응) |
| Sediment Capacity Factor | 토사 운반 용량 계수 |
| Erode / Deposit Speed | 침식·퇴적 속도 |
| Evaporate Speed | 증발 속도 |
| Erode Radius | 침식 영향 반경 |

---

### 3단계: GPU 가속 침식 (Compute Shader)

- CPU 침식 로직을 Compute Shader로 이식 (`HydraulicErosionCompute.compute`)
- `ComputeBuffer`로 높이맵 데이터를 GPU에 전송/수신
- 50,000개 물방울 병렬 처리

---

### 4단계: 지형 셰이더 (URP HLSL)

- 높이 + 경사도 기반 **4색 자동 블렌딩** (`Custom/TerrainShader`)
- 침식 계곡 자동 감지 및 어둡게 강조 (정점 색 R채널 활용)
- Shadow Caster Pass 포함 (Cascade Shadow Map 지원)
- Inspector에서 실시간 조절:

| 파라미터 | 설명 |
|----------|------|
| Sand / Grass / Rock / Snow Color | 각 레이어 색상 |
| Height Grass / Rock / Snow | 레이어 전환 높이 |
| Slope Threshold | 급경사 바위 오버라이드 기준 |
| Blend Width | 레이어 전환 부드러움 |
| Valley Darkness | 계곡 어둠 강도 |

**오목도(Concavity) 계산:** 메시 생성 시 4방향 이웃 평균과 자신의 높이 차이를 정규화하여 정점 색에 저장. 지형 스케일에 무관하게 자연스러운 계곡 강조.

---

### 굴삭 시스템 (Bucket Excavation)

- **OBB(Oriented Bounding Box) 기반 버킷 굴삭** — Compute Shader로 GPU에서 처리
- **GPU 볼륨 측정** — 굴삭된 토사량 실시간 계산
- **안식각 기반 경사 이완(Slope Relaxation)** — 굴삭 후 자연스러운 붕괴 표현
- **DirtSystem** — 굴삭된 흙 퇴적 및 덤프 처리
- **DigCoroutine / DumpCoroutine** — 버킷 애니메이션 (하강→90° 회전→상승→덤프)

---

### 5단계: Custom Editor 도구화

- **Tools > ProceduralTerrain > Create Terrain** — 씬에 지형 오브젝트 자동 생성
- **Inspector Preview 버튼** — 에디터에서 즉시 지형 재생성
- **Inspector Save Mesh 버튼** — 생성된 메시를 `.asset` 파일로 저장 (다른 프로젝트 이식용)

---

## 프로젝트 구조

```
Assets/
  Scripts/
    TerrainGenerator.cs      # 높이맵 생성 + 메시 빌드 + 오목도 계산
    HydraulicErosion.cs      # CPU 수압 침식
    ComputeErosion.cs        # GPU 수압 침식 (Compute Shader 래퍼)
    TerrainDeformer.cs       # GPU 기반 지형 변형 (굴삭)
    DirtSystem.cs            # 흙 퇴적/덤프 시스템
    TerrainCursor.cs         # 커서 인터랙션 (New Input System)
    NoiseSettings.cs         # 노이즈 파라미터
    ErosionSettings.cs       # 침식 파라미터
  Shaders/
    TerrainShader.shader              # URP HLSL 지형 셰이더
    HydraulicErosionCompute.compute   # GPU 침식 커널
    TerrainDeformCompute.compute      # GPU 굴삭 커널
  Editor/
    TerrainGeneratorEditor.cs  # Custom Editor (Preview / Save Mesh 버튼)
  Material/
    Terrain_Empty.mat          # TerrainShader 적용 머티리얼
  Tests/
    EditMode/
      TerrainDeformComputeTests.cs   # GPU 굴삭 Edit Mode 테스트
    ShaderTests/
      TerrainShaderTests.cs          # 오목도 계산 Edit Mode 테스트
```

---

## 빠른 시작

1. Unity 6000.3.5f2 이상으로 프로젝트 열기
2. **Tools > ProceduralTerrain > Create Terrain** 으로 지형 오브젝트 생성
3. Inspector에서 파라미터 조절 후 **Preview** 버튼으로 재생성
4. 결과물은 **Save Mesh** 버튼으로 `.asset` 저장 가능

---

## 테스트 실행

**Window > General > Test Runner > EditMode** 탭에서 실행:

- `TerrainDeformComputeTests` — OBB 굴삭, 볼륨 계산 GPU 통합 테스트
- `TerrainShaderTests` — 오목도 계산 단위 테스트 (평탄/계곡/봉우리/배열 크기)
