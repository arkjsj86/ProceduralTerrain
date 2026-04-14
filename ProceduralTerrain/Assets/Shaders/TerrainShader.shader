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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR

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
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 color       : COLOR;
                float4 shadowCoord : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs  = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS  = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normInputs.normalWS;
                OUT.color       = IN.color;
                OUT.shadowCoord = GetShadowCoord(posInputs);
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

                // Diffuse 조명 (ambient 0.2 포함, 그림자 수신 포함)
                Light mainLight = GetMainLight(IN.shadowCoord);
                float ndotl     = saturate(dot(normalWS, mainLight.direction));
                col.rgb        *= mainLight.color * (ndotl * 0.8 + 0.2) * mainLight.shadowAttenuation;

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

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
