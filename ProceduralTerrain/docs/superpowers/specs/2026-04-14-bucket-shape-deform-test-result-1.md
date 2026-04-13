# 1차 테스트 결과 — Bucket-Shape Terrain Deformation

**Date:** 2026-04-14  
**Unity Version:** 6000.3.5f2  
**Branch:** feature/jhpark

---

## 컴파일 테스트

| 항목 | 결과 |
|------|------|
| Unity batch mode exit code | **0 (성공)** |
| C# 컴파일 오류 (`error CS`) | **0건** |
| Assembly 오류 (`LogAssemblyErrors`) | **0건** |
| 컴파일 시간 | 578.877ms |
| 셰이더 컴파일러 기동 | 정상 (`UnityShaderCompiler.exe`) |

---

## 정적 코드 리뷰

### TerrainDeformCompute.compute

| 체크 항목 | 결과 |
|-----------|------|
| `InterlockedAdd` 타입 (`RWStructuredBuffer<int>`) | ✅ 유효한 HLSL DX11+ 문법 |
| OBB 판정 로직 (cos/sin 회전 역변환) | ✅ 수학적으로 정확 |
| `int _StartX`, `int _StartZ` 선언 | ✅ 음수 값 대응 |
| 경계 체크 (`cellX < 0 \|\| cellX > _Width`) | ✅ 경계 외부 return 처리 |
| 고정소수점 오버플로우 검증 | ✅ 최대 400셀 × strength 3.0 × 10000 = 12,000,000 → INT_MAX(2.1B) 대비 안전 |

### TerrainDeformer.cs

| 체크 항목 | 결과 |
|-----------|------|
| `volumeBuffer` 초기화/해제 | ✅ `InitBuffer()` 생성, `OnDestroy()` 해제 |
| dispatch 전 `volumeRaw[0] = 0` 리셋 | ✅ 매 호출마다 초기화 |
| Dispatch 크기 최솟값 | ✅ `CeilToInt((boundX*2+1)/8)` → 최소 1 보장 |
| `float` 반환 | ✅ `(rawInt / 10000f) * cellArea` |
| 구 `radius` 파라미터 완전 제거 | ✅ |

### TerrainCursor.cs

| 체크 항목 | 결과 |
|-----------|------|
| `BrushRadius` 제거 | ✅ 참조 없음 확인 |
| `dirtPerDig` 제거 | ✅ 참조 없음 확인 |
| 두 `Deform()` 호출 신규 시그니처 사용 | ✅ |
| `dirtSystem.AddDirt(volume)` GPU 볼륨 사용 | ✅ |
| Editor 파일 영향 없음 | ✅ (`TerrainGeneratorEditor.cs` 무관) |

---

## 발견된 이슈

**없음.** 컴파일 오류, 타입 불일치, 참조 누락 모두 없음.

---

## 동작 검증 (정적 분석)

**버킷 `4×4`, rotation 0°, strength 0.5, cellSize 1 기준:**
- OBB 커버 셀: 5×5 = 25셀 (halfExtent=2이므로 [-2,2] 범위)
- 예상 volume: 25 × 0.5 × 1.0 (cellArea) = **12.5**
- DirtSystem OverflowThreshold: 4 × 4 = **16**
- 2회 굴삭 후 overflow 발생 (12.5 + 12.5 = 25 > 16) → **자연스러운 버킷 포화 동작**

**버킷 `6×2`, rotation 0°, strength 0.5:**
- halfExtentX = 3, halfExtentZ = 1
- 커버 셀: 7×3 = 21셀
- volume: 21 × 0.5 = **10.5**
- OverflowThreshold: 6 × 2 = **12**
- 좁고 긴 트렌치 형태 → **기대 동작 일치**

---

## 결론

이슈 없음. 수정 없이 커밋 진행.
