# Slope Relaxation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 굴삭 직후 안식각(Angle of Repose)을 초과하는 경사를 GPU에서 N회 이완시켜 뾰족한 지형을 자연스럽게 흘러내리게 한다.

**Architecture:** `CSRelax` 커널을 `TerrainDeformCompute.compute`에 추가. 더블 버퍼(ping-pong) 방식으로 데이터 경쟁 없이 N회 반복 Dispatch. `TerrainDeformer.Relax()`가 커널을 실행하고 `TerrainCursor.DigCoroutine`에서 `Deform()` 직후 호출.

**Tech Stack:** Unity 6000.3.5f2, HLSL Compute Shader (SM 5.0), C#, Unity Test Framework (NUnit)

---

## 파일 맵

| 파일 | 변경 유형 | 역할 |
|------|-----------|------|
| `Assets/Shaders/TerrainDeformCompute.compute` | 수정 | `CSRelax` 커널 추가 |
| `Assets/Scripts/TerrainDeformer.cs` | 수정 | `relaxBuffer`, `Relax()` 추가 |
| `Assets/Scripts/TerrainCursor.cs` | 수정 | `DigCoroutine`에서 `Relax()` 호출 |
| `Assets/Tests/EditMode/TerrainDeformComputeTests.cs` | 수정 | 이완 테스트 3개 추가 |

---

## Task 1: CSRelax 커널 추가

**Files:**
- Modify: `Assets/Shaders/TerrainDeformCompute.compute`

### 알고리즘

더블 버퍼 방식. 각 스레드(= 셀 1개):
1. `_HeightMapSrc`에서 자신과 4방향 이웃 높이를 읽음
2. 이웃과의 높이 차가 `_MaxHeightDiff`(= `cellSize × tan(reposeAngle)`) 초과 시  
   `transfer = (diff - _MaxHeightDiff) × flowRate × 0.5f` 만큼 이동
3. 결과를 `_HeightMapDst`에 씀

`0.5f` 인수: 양쪽에서 동시에 처리하므로 절반씩 이동해야 총량이 보존됨.

- [ ] **Step 1: CSRelax 커널 추가**

`Assets/Shaders/TerrainDeformCompute.compute` 하단에 추가:

```hlsl
#pragma kernel CSRelax

// 더블 버퍼: src=읽기 전용, dst=쓰기 전용
StructuredBuffer<float>   _HeightMapSrc;
RWStructuredBuffer<float> _HeightMapDst;

// _Width, _Depth 는 CSDeform과 공유 (파일 전역 선언)

// 안식각 초과 판정 기준 높이 차 (= cellSize × tan(reposeAngle))
float _MaxHeightDiff;

// 한 pass당 이동 비율 (0~1, 보통 0.5)
float _FlowRate;

[numthreads(8, 8, 1)]
void CSRelax(uint3 id : SV_DispatchThreadID)
{
    int cellX = (int)id.x;
    int cellZ = (int)id.y;

    if (cellX > _Width || cellZ > _Depth) return;

    int   idx  = cellZ * (_Width + 1) + cellX;
    float myH  = _HeightMapSrc[idx];
    float net  = 0.0;

    // 4방향 이웃 순서: 좌, 우, 앞, 뒤
    int nx[4]; nx[0] = cellX - 1; nx[1] = cellX + 1; nx[2] = cellX;     nx[3] = cellX;
    int nz[4]; nz[0] = cellZ;     nz[1] = cellZ;     nz[2] = cellZ - 1; nz[3] = cellZ + 1;

    for (int i = 0; i < 4; i++)
    {
        if (nx[i] < 0 || nx[i] > _Width || nz[i] < 0 || nz[i] > _Depth) continue;

        float nH   = _HeightMapSrc[nz[i] * (_Width + 1) + nx[i]];
        float diff = myH - nH;

        if (diff > _MaxHeightDiff)
            net -= (diff - _MaxHeightDiff) * _FlowRate * 0.5;
        else if (-diff > _MaxHeightDiff)
            net += (-diff - _MaxHeightDiff) * _FlowRate * 0.5;
    }

    _HeightMapDst[idx] = myH + net;
}
```

- [ ] **Step 2: 전체 파일 구조 확인**

파일 최상단 `#pragma kernel` 목록:
```hlsl
#pragma kernel CSDeform
#pragma kernel CSRelax
```
두 줄이 모두 있는지 확인. `_Width`, `_Depth`, `_Strength` 등 CSDeform 전역 변수는 그대로 유지.

---

## Task 2: TerrainDeformer.cs Relax() 추가

**Files:**
- Modify: `Assets/Scripts/TerrainDeformer.cs`

- [ ] **Step 1: 필드 추가**

`kernelIndex` 선언 아래에 추가:

```csharp
private ComputeBuffer relaxBuffer;
private int relaxKernel;
```

Inspector 파라미터 (클래스 상단 SerializeField 영역):

```csharp
[Header("Relaxation")]
[Range(5f, 60f)]
[SerializeField] private float reposeAngleDeg = 35f;

[Range(1, 30)]
[SerializeField] private int relaxIterations = 10;

[Range(0.1f, 0.9f)]
[SerializeField] private float flowRate = 0.5f;
```

- [ ] **Step 2: InitBuffer()에 relaxBuffer/relaxKernel 초기화 추가**

기존 `kernelIndex = deformShader.FindKernel("CSDeform");` 아래에:

```csharp
relaxBuffer?.Release();
relaxBuffer = new ComputeBuffer(generator.HeightMap.Length, sizeof(float));
relaxKernel = deformShader.FindKernel("CSRelax");
```

- [ ] **Step 3: Relax() 메서드 추가**

`Deform()` 메서드 아래에 추가:

```csharp
/// <summary>
/// 안식각 기반 경사 이완을 N회 실행합니다.
/// 굴삭(Deform) 직후 호출하면 뾰족한 지형이 자연스럽게 흘러내립니다.
/// </summary>
public void Relax()
{
    if (heightBuffer == null || relaxBuffer == null) return;

    float maxHeightDiff = generator.CellSize * Mathf.Tan(reposeAngleDeg * Mathf.Deg2Rad);
    int groupsX = Mathf.CeilToInt((generator.Width  + 1) / 8f);
    int groupsZ = Mathf.CeilToInt((generator.Depth  + 1) / 8f);

    ComputeBuffer src = heightBuffer;
    ComputeBuffer dst = relaxBuffer;

    for (int i = 0; i < relaxIterations; i++)
    {
        deformShader.SetBuffer(relaxKernel, "_HeightMapSrc", src);
        deformShader.SetBuffer(relaxKernel, "_HeightMapDst", dst);
        deformShader.SetInt   ("_Width",        generator.Width);
        deformShader.SetInt   ("_Depth",        generator.Depth);
        deformShader.SetFloat ("_MaxHeightDiff", maxHeightDiff);
        deformShader.SetFloat ("_FlowRate",      flowRate);
        deformShader.Dispatch (relaxKernel, groupsX, groupsZ, 1);

        // ping-pong: src ↔ dst
        ComputeBuffer tmp = src;
        src = dst;
        dst = tmp;
    }

    // 최종 결과는 src에 있음 (마지막 dispatch 후 swap된 쪽)
    src.GetData(generator.HeightMap);
    generator.ApplyHeightMap();

    // 이후 Deform이 heightBuffer를 기준으로 동작하므로 동기화
    if (src != heightBuffer)
        heightBuffer.SetData(generator.HeightMap);
}
```

- [ ] **Step 4: OnDestroy()에 relaxBuffer 해제 추가**

```csharp
private void OnDestroy()
{
    heightBuffer?.Release();
    volumeBuffer?.Release();
    relaxBuffer?.Release();   // 추가
}
```

---

## Task 3: TerrainCursor.cs DigCoroutine 수정

**Files:**
- Modify: `Assets/Scripts/TerrainCursor.cs`

- [ ] **Step 1: DigCoroutine 마지막 부분 수정**

기존:
```csharp
float volume = deformer.Deform(
    lastHitPoint,
    cursorScale.x * 0.5f,
    cursorScale.z * 0.5f,
    cursorCube.transform.eulerAngles.y,
    strength,
    false
);
dirtSystem.AddDirt(volume);
```

변경 후:
```csharp
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
```

`Relax()`는 `Deform()` 직후, `AddDirt()` 이전에 호출. 굴삭 → 이완 → 흙 적재 순서.

---

## Task 4: 이완 테스트 추가

**Files:**
- Modify: `Assets/Tests/EditMode/TerrainDeformComputeTests.cs`

기존 `Dispatch()` 헬퍼를 재사용. 이완 전용 헬퍼 추가 후 3개 테스트 작성.

- [ ] **Step 1: DispatchRelax() 헬퍼 추가**

기존 `Dispatch()` 메서드 아래에:

```csharp
private float[] DispatchRelax(
    float[] initialHeights,
    int width, int depth, float cellSize,
    float reposeAngleDeg, float flowRate, int iterations)
{
    int kernelIdx = shader.FindKernel("CSRelax");
    float maxHeightDiff = cellSize * Mathf.Tan(reposeAngleDeg * Mathf.Deg2Rad);
    int groupsX = Mathf.CeilToInt((width  + 1) / 8f);
    int groupsZ = Mathf.CeilToInt((depth  + 1) / 8f);

    var bufA = new ComputeBuffer(initialHeights.Length, sizeof(float));
    var bufB = new ComputeBuffer(initialHeights.Length, sizeof(float));
    bufA.SetData(initialHeights);
    bufB.SetData(initialHeights); // dst도 초기화 (경계 셀 보호)

    ComputeBuffer src = bufA, dst = bufB;

    for (int i = 0; i < iterations; i++)
    {
        shader.SetBuffer(kernelIdx, "_HeightMapSrc", src);
        shader.SetBuffer(kernelIdx, "_HeightMapDst", dst);
        shader.SetInt   ("_Width",         width);
        shader.SetInt   ("_Depth",         depth);
        shader.SetFloat ("_MaxHeightDiff", maxHeightDiff);
        shader.SetFloat ("_FlowRate",      flowRate);
        shader.Dispatch (kernelIdx, groupsX, groupsZ, 1);

        ComputeBuffer tmp = src; src = dst; dst = tmp;
    }

    float[] result = new float[initialHeights.Length];
    src.GetData(result);
    bufA.Release();
    bufB.Release();
    return result;
}
```

- [ ] **Step 2: 테스트 1 — 뾰족한 봉우리가 이완되어야 함**

```csharp
[Test]
public void Relax_SharpPeak_GetsSmoothed()
{
    int w = 16, d = 16;
    float[] heights = FlatMap(w, d, 5f);

    // 중앙에 매우 높은 단일 봉우리 생성
    int peakX = 8, peakZ = 8;
    heights[Idx(w, peakX, peakZ)] = 20f; // 이웃보다 15 높음

    float reposeAngle = 35f;
    float maxStable = 1f * Mathf.Tan(reposeAngle * Mathf.Deg2Rad); // ~0.7f

    float[] result = DispatchRelax(heights, w, d, 1f, reposeAngle, 0.5f, 20);

    // 봉우리가 낮아져야 함
    Assert.Less(result[Idx(w, peakX, peakZ)], 20f,
        "봉우리가 이완 후에도 그대로임");

    // 봉우리와 이웃의 높이 차가 안식각 이하로 좁혀져야 함
    float peakH     = result[Idx(w, peakX, peakZ)];
    float neighborH = result[Idx(w, peakX + 1, peakZ)];
    Assert.Less(peakH - neighborH, maxStable + 0.5f,
        "이완 후에도 봉우리가 안식각 이상으로 가파름");
}
```

- [ ] **Step 3: 테스트 2 — 평탄 지형은 변하지 않아야 함**

```csharp
[Test]
public void Relax_FlatTerrain_RemainsUnchanged()
{
    int w = 16, d = 16;
    float baseHeight = 5f;
    float[] heights = FlatMap(w, d, baseHeight);

    float[] result = DispatchRelax(heights, w, d, 1f, 35f, 0.5f, 10);

    // 모든 셀이 초기값 유지 (안식각 미초과)
    for (int z = 0; z <= d; z++)
    for (int x = 0; x <= w; x++)
        Assert.AreEqual(baseHeight, result[Idx(w, x, z)], HEIGHT_EPSILON,
            $"평탄 지형의 셀 ({x},{z})이 변경됨");
}
```

- [ ] **Step 4: 테스트 3 — 이완 후 전체 높이 합이 보존되어야 함**

```csharp
[Test]
public void Relax_TotalVolume_IsConserved()
{
    int w = 16, d = 16;
    float[] heights = FlatMap(w, d, 5f);

    // 다양한 높이 변화 생성
    heights[Idx(w, 8, 8)] = 15f;
    heights[Idx(w, 4, 4)] = 12f;
    heights[Idx(w, 12, 3)] = 0f;

    float sumBefore = 0f;
    for (int i = 0; i < heights.Length; i++) sumBefore += heights[i];

    float[] result = DispatchRelax(heights, w, d, 1f, 35f, 0.5f, 10);

    float sumAfter = 0f;
    for (int i = 0; i < result.Length; i++) sumAfter += result[i];

    Assert.AreEqual(sumBefore, sumAfter, 0.5f,
        $"이완 전후 총 높이 합 불일치: before={sumBefore:F2}, after={sumAfter:F2}");
}
```

---

## Task 5: 컴파일 + 테스트 실행 + 커밋

- [ ] **Step 1: Unity batch mode 컴파일**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Unity.exe" \
  -batchmode -quit \
  -projectPath "D:/project/ProceduralTerrain/ProceduralTerrain" \
  -logFile /tmp/compile.log 2>/dev/null; echo "EXIT:$?"
```

Expected: `EXIT:0`, `grep -c "error CS" /tmp/compile.log` → `0`

- [ ] **Step 2: UTF Edit Mode 테스트 실행**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Unity.exe" \
  -batchmode -runTests -testPlatform EditMode \
  -projectPath "D:/project/ProceduralTerrain/ProceduralTerrain" \
  -logFile /tmp/test.log \
  -testResults /tmp/test_results.xml 2>/dev/null; echo "EXIT:$?"
```

Expected: `result="Passed"`, `passed="9"`, `failed="0"` (기존 6 + 신규 3)

- [ ] **Step 3: 커밋**

```bash
cd "D:/project/ProceduralTerrain"
git add \
  ProceduralTerrain/Assets/Shaders/TerrainDeformCompute.compute \
  ProceduralTerrain/Assets/Scripts/TerrainDeformer.cs \
  ProceduralTerrain/Assets/Scripts/TerrainCursor.cs \
  ProceduralTerrain/Assets/Tests/EditMode/TerrainDeformComputeTests.cs \
  ProceduralTerrain/docs/
git commit -m "Feat: 안식각 기반 경사 이완(Slope Relaxation) 추가

- CSRelax 커널: 4방향 이웃 안식각 체크 + 더블 버퍼 ping-pong
- TerrainDeformer: Relax() 메서드, reposeAngleDeg/relaxIterations/flowRate Inspector 노출
- TerrainCursor: DigCoroutine에서 Deform() 직후 Relax() 호출
- 테스트: 봉우리 이완, 평탄 불변, 부피 보존 3개 추가 (총 9/9 Passed)"
```
