# Terrain Shader 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 높이 + 경사도 기반 단색 블렌딩 URP HLSL 셰이더를 구현하고, 침식 계곡을 어둡게 강조한다.

**Architecture:** TerrainGenerator가 메시 생성 시 오목도(concavity)를 계산해 정점 색 R채널에 저장. TerrainShader가 월드 좌표 높이와 노멀 기울기를 읽어 4색 smoothstep 블렌딩 후 정점색으로 계곡 어둠을 multiply한다.

**Tech Stack:** Unity URP, HLSL (Custom Shader), NUnit (EditMode 테스트), C# (TerrainGenerator 확장)

---

## 파일 구성

| 상태 | 경로 | 역할 |
|------|------|------|
| 신규 | `Assets/Shaders/TerrainShader.shader` | URP HLSL 셰이더 |
| 신규 | `Assets/Tests/EditMode/TerrainShaderTests.cs` | 오목도 계산 Edit Mode 테스트 |
| 신규 | `Assets/Tests/EditMode/TerrainShaderTests.asmdef` | 테스트 어셈블리 정의 |
| 수정 | `Assets/Scripts/TerrainGenerator.cs` | `ComputeConcavityColors()` 추가, BuildMesh/ApplyHeightMap 연동 |
| 수정 | `Assets/Material/Terrain_Empty.mat` | 셰이더 교체 + 파라미터 (Unity Editor 수동) |

---

## Task 1: 오목도 테스트 파일 작성 (실패 상태)

**Files:**
- Create: `Assets/Tests/EditMode/TerrainShaderTests.asmdef`
- Create: `Assets/Tests/EditMode/TerrainShaderTests.cs`

- [ ] **Step 1: asmdef 생성**

`Assets/Tests/EditMode/TerrainShaderTests.asmdef` 내용:

```json
{
    "name": "TerrainShaderTests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

> `overrideReferences: false` 로 설정해야 Assembly-CSharp(TerrainGenerator 포함)이 자동 참조된다.

- [ ] **Step 2: 테스트 파일 작성**

`Assets/Tests/EditMode/TerrainShaderTests.cs` 내용:

```csharp
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class TerrainShaderTests
{
    // ── 테스트 1: 평탄한 지형 → 오목도 전부 0 ─────────────────────────
    [Test]
    public void ComputeConcavityColors_FlatMap_AllZero()
    {
        // 2x2 셀 = 3x3 정점
        int w = 2, d = 2;
        float[] heights = new float[(w + 1) * (d + 1)];
        for (int i = 0; i < heights.Length; i++) heights[i] = 5f;

        Color[] colors = TerrainGenerator.ComputeConcavityColors(heights, w, d);

        foreach (var c in colors)
            Assert.AreEqual(0f, c.r, 0.001f, "평탄 지형의 오목도는 0이어야 합니다.");
    }

    // ── 테스트 2: 계곡(낮은 중심 정점) → 중심 오목도 > 0 ───────────────
    [Test]
    public void ComputeConcavityColors_Valley_CenterHasPositiveConcavity()
    {
        int w = 2, d = 2;
        float[] heights = new float[(w + 1) * (d + 1)];
        for (int i = 0; i < heights.Length; i++) heights[i] = 5f;
        // 중심 정점 index = z*(w+1)+x = 1*3+1 = 4
        heights[4] = 0f; // 주변보다 훨씬 낮음 = 계곡

        Color[] colors = TerrainGenerator.ComputeConcavityColors(heights, w, d);

        Assert.Greater(colors[4].r, 0f, "계곡 정점의 오목도는 양수여야 합니다.");
    }

    // ── 테스트 3: 봉우리(높은 중심 정점) → 중심 오목도 = 0 (클램프) ────
    [Test]
    public void ComputeConcavityColors_Peak_ZeroConcavity()
    {
        int w = 2, d = 2;
        float[] heights = new float[(w + 1) * (d + 1)];
        for (int i = 0; i < heights.Length; i++) heights[i] = 5f;
        heights[4] = 10f; // 주변보다 훨씬 높음 = 봉우리

        Color[] colors = TerrainGenerator.ComputeConcavityColors(heights, w, d);

        Assert.AreEqual(0f, colors[4].r, 0.001f, "봉우리 정점의 오목도는 0으로 클램프되어야 합니다.");
    }

    // ── 테스트 4: 반환 배열 크기가 정점 수와 일치 ──────────────────────
    [Test]
    public void ComputeConcavityColors_ReturnsSizeMatchesVertexCount()
    {
        int w = 5, d = 7;
        float[] heights = new float[(w + 1) * (d + 1)];

        Color[] colors = TerrainGenerator.ComputeConcavityColors(heights, w, d);

        Assert.AreEqual((w + 1) * (d + 1), colors.Length);
    }
}
```

- [ ] **Step 3: Unity에서 테스트 실행 (실패 확인)**

Window → General → Test Runner → EditMode 탭에서 `TerrainShaderTests` 선택 후 Run.  
예상: **FAIL** — `TerrainGenerator does not contain a definition for 'ComputeConcavityColors'`

---

## Task 2: ComputeConcavityColors 정적 메서드 구현

**Files:**
- Modify: `Assets/Scripts/TerrainGenerator.cs` (클래스 닫는 `}` 직전에 메서드 추가)

- [ ] **Step 1: 메서드 추가**

`TerrainGenerator.cs` 의 마지막 `}` 바로 앞(174번 줄 `}` 이후, 175번 줄 `}` 이전)에 삽입:

```csharp
    /// <summary>
    /// 각 정점의 오목도를 계산한다. 이웃 높이 평균 - 자신 높이가 클수록 계곡.
    /// 반환값 Color.r = 0(볼록/평탄) ~ 1(깊은 계곡)
    /// </summary>
    public static Color[] ComputeConcavityColors(float[] heightMap, int width, int depth)
    {
        int vertexCount = (width + 1) * (depth + 1);
        Color[] colors = new Color[vertexCount];

        for (int z = 0; z <= depth; z++)
        {
            for (int x = 0; x <= width; x++)
            {
                int i = z * (width + 1) + x;
                float self = heightMap[i];
                float neighborSum = 0f;
                int count = 0;

                if (x > 0)     { neighborSum += heightMap[i - 1];           count++; }
                if (x < width) { neighborSum += heightMap[i + 1];           count++; }
                if (z > 0)     { neighborSum += heightMap[i - (width + 1)]; count++; }
                if (z < depth) { neighborSum += heightMap[i + (width + 1)]; count++; }

                float avg = count > 0 ? neighborSum / count : self;
                float concavity = Mathf.Clamp01((avg - self) * 0.5f);
                colors[i] = new Color(concavity, 0f, 0f, 1f);
            }
        }

        return colors;
    }
```

- [ ] **Step 2: Unity에서 테스트 실행 (통과 확인)**

Test Runner → EditMode → `TerrainShaderTests` → Run.  
예상: **PASS** 4개

- [ ] **Step 3: 커밋**

```bash
git add ProceduralTerrain/Assets/Scripts/TerrainGenerator.cs
git add ProceduralTerrain/Assets/Tests/EditMode/TerrainShaderTests.cs
git add ProceduralTerrain/Assets/Tests/EditMode/TerrainShaderTests.asmdef
git commit -m "Test/Feat: 오목도 계산 메서드 추가 및 Edit Mode 테스트"
```

---

## Task 3: BuildMesh / ApplyHeightMap에 오목도 통합

**Files:**
- Modify: `Assets/Scripts/TerrainGenerator.cs:107-174`

- [ ] **Step 1: BuildMesh에 colors 할당 추가**

`BuildMesh()` 내 `mesh.RecalculateBounds();` 바로 위(154번 줄)에 추가:

```csharp
        mesh.colors = ComputeConcavityColors(HeightMap, width, depth);
```

결과적으로 `BuildMesh()` 끝부분:

```csharp
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.colors = ComputeConcavityColors(HeightMap, width, depth);  // ← 추가
        mesh.RecalculateBounds();

        GetComponent<MeshCollider>().sharedMesh = mesh;
```

- [ ] **Step 2: ApplyHeightMap에 colors 갱신 추가**

`ApplyHeightMap()` 내 `mesh.RecalculateBounds();` 바로 위(171번 줄)에 추가:

```csharp
        mesh.colors = ComputeConcavityColors(HeightMap, width, depth);
```

결과적으로 `ApplyHeightMap()`:

```csharp
    public void ApplyHeightMap()
    {
        if (mesh == null) return;

        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i].y = HeightMap[i];

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.colors = ComputeConcavityColors(HeightMap, width, depth);  // ← 추가
        mesh.RecalculateBounds();

        GetComponent<MeshCollider>().sharedMesh = mesh;
    }
```

- [ ] **Step 3: Unity에서 Play Mode로 지형 생성 — 오류 없는지 확인**

Console 창에 에러 없으면 정상. 아직 셰이더가 없으므로 시각적 변화는 없음.

- [ ] **Step 4: 커밋**

```bash
git add ProceduralTerrain/Assets/Scripts/TerrainGenerator.cs
git commit -m "Feat: BuildMesh/ApplyHeightMap에 오목도 정점 색 할당 연동"
```

---

## Task 4: TerrainShader.shader 작성

**Files:**
- Create: `Assets/Shaders/TerrainShader.shader`

셰이더는 자동화 테스트 불가(HLSL GPU 코드). Play Mode 시각 확인으로 검증.

- [ ] **Step 1: 셰이더 파일 작성**

`Assets/Shaders/TerrainShader.shader` 내용:

```hlsl
Shader "Custom/TerrainShader"
{
    Properties
    {
        _SandColor      ("Sand Color",        Color)        = (0.76, 0.70, 0.50, 1)
        _GrassColor     ("Grass Color",       Color)        = (0.30, 0.55, 0.20, 1)
        _RockColor      ("Rock Color",        Color)        = (0.45, 0.40, 0.35, 1)
        _SnowColor      ("Snow Color",        Color)        = (0.95, 0.95, 1.00, 1)
        _HeightGrass    ("Height Grass",      Float)        = 1.0
        _HeightRock     ("Height Rock",       Float)        = 4.0
        _HeightSnow     ("Height Snow",       Float)        = 7.0
        _SlopeThreshold ("Slope Threshold",   Range(0, 1))  = 0.7
        _BlendWidth     ("Blend Width",       Float)        = 0.5
        _ValleyDarkness ("Valley Darkness",   Range(0, 1))  = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ── Forward Lit Pass ─────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SandColor;
                float4 _GrassColor;
                float4 _RockColor;
                float4 _SnowColor;
                float  _HeightGrass;
                float  _HeightRock;
                float  _HeightSnow;
                float  _SlopeThreshold;
                float  _BlendWidth;
                float  _ValleyDarkness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs  = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = normInputs.normalWS;
                OUT.color      = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float  height   = IN.positionWS.y;
                float3 normalWS = normalize(IN.normalWS);
                float  slope    = dot(normalWS, float3(0, 1, 0)); // 1=수평, 0=수직

                // 높이 기반 색상 블렌딩
                float4 col = _SandColor;
                col = lerp(col, _GrassColor,
                    smoothstep(_HeightGrass - _BlendWidth, _HeightGrass + _BlendWidth, height));
                col = lerp(col, _RockColor,
                    smoothstep(_HeightRock  - _BlendWidth, _HeightRock  + _BlendWidth, height));
                col = lerp(col, _SnowColor,
                    smoothstep(_HeightSnow  - _BlendWidth, _HeightSnow  + _BlendWidth, height));

                // 급경사 → 바위색 오버라이드
                float slopeFactor = 1.0 - smoothstep(
                    _SlopeThreshold - 0.1, _SlopeThreshold + 0.1, slope);
                col = lerp(col, _RockColor, slopeFactor);

                // 계곡 어둠 (정점 색 R채널 = 오목도)
                float concavity = IN.color.r;
                col.rgb *= lerp(1.0, 1.0 - _ValleyDarkness, concavity);

                // Diffuse 조명 (ambient 0.2 포함)
                Light mainLight = GetMainLight();
                float ndotl     = saturate(dot(normalWS, mainLight.direction));
                col.rgb        *= mainLight.color * (ndotl * 0.8 + 0.2);

                return col;
            }
            ENDHLSL
        }

        // ── Shadow Caster Pass ───────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            ZWrite On

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            float3 _LightDirection;

            struct ShadowAttribs
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            float4 ShadowVert(ShadowAttribs IN) : SV_POSITION
            {
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 posCS  = TransformWorldToHClip(
                    ApplyShadowBias(posWS, normWS, _LightDirection));
                // 깊이 클램프 (일부 플랫폼)
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return posCS;
            }

            half4 ShadowFrag(float4 pos : SV_POSITION) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
```

- [ ] **Step 2: Unity 에디터에서 셰이더 컴파일 확인**

Project 창에서 `TerrainShader.shader` 선택 → Inspector 하단에 컴파일 에러 없으면 정상.  
에러 시 Console에서 줄 번호 확인 후 수정.

- [ ] **Step 3: 커밋**

```bash
git add ProceduralTerrain/Assets/Shaders/TerrainShader.shader
git commit -m "Feat: URP HLSL 지형 셰이더 추가 (높이/경사도 블렌딩 + 계곡 강조)"
```

---

## Task 5: 머티리얼에 셰이더 할당 (Unity Editor 수동)

**Files:**
- Modify: `Assets/Material/Terrain_Empty.mat` (Unity가 직렬화)

- [ ] **Step 1: Project 창에서 머티리얼 선택**

`Assets/Material/Terrain_Empty.mat` 클릭 → Inspector 열기.

- [ ] **Step 2: 셰이더 교체**

Inspector 상단 Shader 드롭다운 → `Custom/TerrainShader` 선택.

- [ ] **Step 3: 파라미터 설정**

Inspector에서 아래 값 입력:

| 항목 | 값 |
|------|-----|
| Sand Color | R:0.76 G:0.70 B:0.50 |
| Grass Color | R:0.30 G:0.55 B:0.20 |
| Rock Color | R:0.45 G:0.40 B:0.35 |
| Snow Color | R:0.95 G:0.95 B:1.00 |
| Height Grass | 1.0 |
| Height Rock | 4.0 |
| Height Snow | 7.0 |
| Slope Threshold | 0.7 |
| Blend Width | 0.5 |
| Valley Darkness | 0.4 |

- [ ] **Step 4: Play Mode에서 시각 검증**

아래 3가지 확인:
1. 지형이 높이에 따라 모래→풀→바위→눈 색으로 전환됨
2. 급경사면이 바위색으로 표시됨
3. 침식된 계곡 부분이 주변보다 어둡게 보임

파라미터가 맞지 않으면 Inspector 슬라이더로 조정 (HeightGrass/HeightRock/HeightSnow는 지형 진폭에 따라 다름).

- [ ] **Step 5: 커밋**

```bash
git add ProceduralTerrain/Assets/Material/Terrain_Empty.mat
git commit -m "Feat: TerrainShader를 Terrain_Empty 머티리얼에 적용"
```

---

## 완료 기준

- [ ] Edit Mode 테스트 4개 PASS
- [ ] Play Mode에서 4색 지형 레이어 시각 확인
- [ ] 급경사 바위 오버라이드 동작 확인
- [ ] 침식 계곡 어두움 확인
- [ ] Inspector에서 색상/높이 파라미터 실시간 반영 확인
