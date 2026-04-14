# 4단계: 지형 셰이더 및 시각화 설계

**날짜:** 2026-04-14  
**브랜치:** feature/jhpark

---

## 목표

높이 + 경사도 기반 색상 블렌딩 셰이더를 구현해 절차적 지형에 시각적 레이어를 부여한다.  
추가로 수압 침식 결과로 생긴 계곡/오목 지형을 어둡게 강조한다.

---

## 결정 사항

| 항목 | 결정 |
|------|------|
| 구현 방식 | HLSL Custom Shader (URP Lit 기반) |
| 텍스처 에셋 | 없음 — 단색(solid color)으로만 레이어 구분 |
| 블렌딩 기준 | 높이 + 경사도 조합 |
| 계곡 강조 | 포함 (오목도 → 정점 색 R채널 → 셰이더 multiply) |

---

## 아키텍처

### 파일 구성

```
Assets/
  Shaders/
    TerrainShader.shader       ← 신규
  Material/
    Terrain_Empty.mat          ← 셰이더 교체 + 파라미터 설정
Scripts/
  TerrainGenerator.cs          ← BuildMesh()에 오목도 계산 추가
```

### 데이터 흐름

```
TerrainGenerator.BuildMesh()
  └─ 오목도 계산 (이웃 평균 높이 - 자신 높이)
  └─ mesh.colors[i].r = concavity (0~1 클램프)

TerrainShader.shader (Fragment)
  └─ worldPos.y → 높이 구간 판별
  └─ dot(normal, float3(0,1,0)) → 경사도 판별
  └─ smoothstep 블렌딩으로 색상 결정
  └─ vertexColor.r → 어두움 계수 multiply
```

---

## 상세 설계

### TerrainShader.shader

**프로퍼티 (Inspector 조절 가능):**

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `_SandColor` | Color | 모래색 (낮은 높이) |
| `_GrassColor` | Color | 풀색 (중간 높이) |
| `_RockColor` | Color | 바위색 (높은 높이 or 급경사) |
| `_SnowColor` | Color | 눈색 (최고 높이) |
| `_HeightGrass` | Float | 풀 시작 높이 |
| `_HeightRock` | Float | 바위 시작 높이 |
| `_HeightSnow` | Float | 눈 시작 높이 |
| `_SlopeThreshold` | Float | 급경사 판정 기준 (dot값, 기본 0.7) |
| `_BlendWidth` | Float | 구간 전환 부드러움 폭 |
| `_ValleyDarkness` | Float | 계곡 어두움 강도 (0~1) |

**블렌딩 로직 (pseudo-HLSL):**

```hlsl
float height = worldPos.y;
float slope  = dot(normalize(normal), float3(0,1,0)); // 1=수평, 0=수직

// 높이 기반 기본 색
float4 col = _SandColor;
col = lerp(col, _GrassColor, smoothstep(_HeightGrass - _BlendWidth, _HeightGrass + _BlendWidth, height));
col = lerp(col, _RockColor,  smoothstep(_HeightRock  - _BlendWidth, _HeightRock  + _BlendWidth, height));
col = lerp(col, _SnowColor,  smoothstep(_HeightSnow  - _BlendWidth, _HeightSnow  + _BlendWidth, height));

// 급경사 오버라이드
float slopeFactor = 1.0 - smoothstep(_SlopeThreshold - 0.1, _SlopeThreshold + 0.1, slope);
col = lerp(col, _RockColor, slopeFactor);

// 계곡 어둠 overlay
float concavity = i.color.r; // 정점 색 R채널
col.rgb *= lerp(1.0, 1.0 - _ValleyDarkness, concavity);
```

**URP 호환:** `Tags { "RenderPipeline" = "UniversalPipeline" }`, `#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"` 사용.  
라이팅은 URP Simple Lit 수준 (diffuse only) 적용.

---

### TerrainGenerator.cs 수정

`BuildMesh()` 내에서 정점 루프 이후, `mesh.colors` 할당 추가:

```csharp
// 오목도 계산 (4-이웃 평균 높이 - 자신 높이, 정규화)
Color[] colors = new Color[vertexCount];
for (int z = 0; z <= depth; z++)
{
    for (int x = 0; x <= width; x++)
    {
        int i = z * (width + 1) + x;
        float self = HeightMap[i];
        float neighborSum = 0f; int count = 0;
        if (x > 0)     { neighborSum += HeightMap[i - 1];           count++; }
        if (x < width) { neighborSum += HeightMap[i + 1];           count++; }
        if (z > 0)     { neighborSum += HeightMap[i - (width + 1)]; count++; }
        if (z < depth) { neighborSum += HeightMap[i + (width + 1)]; count++; }
        float avg = count > 0 ? neighborSum / count : self;
        float concavity = Mathf.Clamp01((avg - self) * 2f); // 오목할수록 1
        colors[i] = new Color(concavity, 0, 0, 1);
    }
}
mesh.colors = colors;
```

`ApplyHeightMap()` 에서도 동일 로직으로 colors 갱신 필요.

---

### Terrain_Empty.mat

- Shader → `Custom/TerrainShader` 로 교체
- 기본 파라미터 값:

| 항목 | 기본값 |
|------|--------|
| SandColor | (0.76, 0.70, 0.50, 1) |
| GrassColor | (0.30, 0.55, 0.20, 1) |
| RockColor | (0.45, 0.40, 0.35, 1) |
| SnowColor | (0.95, 0.95, 1.00, 1) |
| HeightGrass | 1.0 |
| HeightRock | 4.0 |
| HeightSnow | 7.0 |
| SlopeThreshold | 0.7 |
| BlendWidth | 0.5 |
| ValleyDarkness | 0.4 |

---

## 범위 외 (이번 단계 제외)

- 텍스처 샘플링 (이후 확장 가능)
- Triplanar 매핑 (급경사 UV 늘어남 방지)
- 노멀맵
- LOD / 청크 시스템

---

## 완료 기준

1. 런타임에 지형이 높이에 따라 4색으로 자연스럽게 전환됨
2. 급경사 면이 바위색으로 표시됨
3. 침식 계곡이 주변보다 어둡게 보임
4. Inspector에서 색상/높이 파라미터 실시간 조절 가능
