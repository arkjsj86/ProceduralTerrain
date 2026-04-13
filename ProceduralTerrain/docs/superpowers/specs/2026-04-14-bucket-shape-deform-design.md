# Bucket-Shape Terrain Deformation — Design Spec

**Date:** 2026-04-14  
**Branch:** feature/jhpark  
**Status:** Approved

---

## 목표

버킷(커서 큐브)의 실제 XZ 크기와 회전 방향에 따라 지형이 파이고, 파낸 부피만큼 버킷에 흙이 쌓이는 구조. 모든 연산은 GPU(Compute Shader)에서 처리.

---

## 현재 문제

| 항목 | 현재 | 목표 |
|------|------|------|
| 파내는 형태 | 항상 원형 (radius 기반) | 버킷 XZ 크기의 직사각형 |
| 버킷 회전 | 무시됨 | Y축 회전 반영 |
| 흙 적재량 | 고정값 `dirtPerDig` | GPU 계산 볼륨 |

---

## 아키텍처 변경

### 1. `TerrainDeformCompute.compute`

**제거되는 파라미터**
- `_Radius`

**추가되는 파라미터**
```hlsl
float _HalfExtentX;    // 버킷 X 절반 크기 (셀 단위)
float _HalfExtentZ;    // 버킷 Z 절반 크기 (셀 단위)
float _AngleY;         // 버킷 Y 회전각 (라디안)
int   _StartX;         // Dispatch 시작 셀 X
int   _StartZ;         // Dispatch 시작 셀 Z
RWStructuredBuffer<int> _VolumeAccum;  // 고정소수점 볼륨 누산기 (int * 10000)
```

**판정 로직 변경**
```hlsl
// 원형 거리 → OBB (Oriented Bounding Box) 판정
float cosA   = cos(-_AngleY);
float sinA   = sin(-_AngleY);
float localX = dx * cosA - dz * sinA;
float localZ = dx * sinA + dz * cosA;
if (abs(localX) > _HalfExtentX || abs(localZ) > _HalfExtentZ) return;
```

**볼륨 누산 (GPU InterlockedAdd)**
```hlsl
float delta = _Direction * _Strength;
_HeightMap[index] += delta;
InterlockedAdd(_VolumeAccum[0], (int)(abs(delta) * 10000.0));
```

고정소수점 스케일 10000: strength 최대값 3.0 × 최대 셀 수 ~1600 × 10000 = 48,000,000 → int.MaxValue(2.1B) 내 안전.

---

### 2. `TerrainDeformer.cs`

**시그니처 변경**
```csharp
// 기존
public void Deform(Vector3 worldPos, float radius, float strength, bool raise)

// 변경
public float Deform(Vector3 worldPos, float halfExtentXWorld, float halfExtentZWorld,
                    float angleYDeg, float strength, bool raise)
```

**볼륨 버퍼 관리**
- `ComputeBuffer _volumeBuffer` (int[1]) 추가
- `InitBuffer()`에서 생성
- `Deform()` 호출마다 dispatch 전 0으로 리셋
- `GetData()` 후 `rawInt / 10000f * cellSize²` 로 반환

**Dispatch 크기 계산**
```csharp
float cosA   = Mathf.Abs(Mathf.Cos(angleRad));
float sinA   = Mathf.Abs(Mathf.Sin(angleRad));
float boundX = halfExtentX * cosA + halfExtentZ * sinA;
float boundZ = halfExtentX * sinA + halfExtentZ * cosA;
int startX   = Mathf.FloorToInt(centerX - boundX);
int startZ   = Mathf.FloorToInt(centerZ - boundZ);
int groupsX  = Mathf.CeilToInt((boundX * 2f + 1f) / 8f);
int groupsZ  = Mathf.CeilToInt((boundZ * 2f + 1f) / 8f);
```

---

### 3. `TerrainCursor.cs`

**제거**
- `[SerializeField] private float dirtPerDig`
- `BrushRadius` 프로퍼티

**변경**
```csharp
// DigCoroutine 마지막
float volume = deformer.Deform(
    lastHitPoint,
    cursorScale.x * 0.5f,
    cursorScale.z * 0.5f,
    cursorCube.transform.eulerAngles.y,
    strength, false
);
dirtSystem.AddDirt(volume);

// DumpCoroutine 마지막 (반환값 무시)
deformer.Deform(
    lastHitPoint,
    cursorScale.x * 0.5f,
    cursorScale.z * 0.5f,
    cursorCube.transform.eulerAngles.y,
    strength, true
);
```

---

## 기대 동작

- `cursorScale = (4, 2, 4)` → 4×4 정사각형 구덩이
- `cursorScale = (6, 2, 2)` → 좁고 긴 트렌치
- 버킷 Y 45° 회전 → 트렌치도 45° 회전
- 깊이/강도가 높을수록 → 볼륨 증가 → 더 많은 흙 적재
- DirtSystem OverflowThreshold(버킷 XZ 면적)와 자연스럽게 연동

---

## 변경 파일 목록

| 파일 | 변경 유형 |
|------|-----------|
| `Assets/Shaders/TerrainDeformCompute.compute` | 수정 |
| `Assets/Scripts/TerrainDeformer.cs` | 수정 |
| `Assets/Scripts/TerrainCursor.cs` | 수정 |
