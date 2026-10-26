// Feedback layer drawn over an enemy model (see EnemyLook): a dark ink outline (inverted hull pushed out along
// smoothed normals kept in UV3, never thinner than a pixel or so) and an overlay on the body itself for the hit flash,
// status tints (rim + wash) and glowing damage cracks. The flash is HDR white (_FlashEmission above 1). The model's own materials stay untouched. One material per
// enemy, all values in UnityPerMaterial, so the SRP Batcher still batches them.
Shader "DungeonConveyor/EnemyLook"
{
    Properties
    {
        _InkColor ("Ink colour", Color) = (0.106, 0.063, 0.125, 1)
        _InkWidth ("Ink width (world units)", Float) = 0.018
        _InkMinPixels ("Ink min width (pixels)", Float) = 1.5
        _UseSmooth ("Smoothed normals in UV3", Float) = 1
        _Flash ("Flash (0-1)", Float) = 0
        _FlashEmission ("Flash brightness above white (HDR)", Float) = 1
        _Tint ("Status tint (a = strength)", Color) = (0, 0, 0, 0)
        _Crack ("Cracks (0-1)", Float) = 0
        _CrackColor ("Crack glow", Color) = (1, 0.18, 0.388, 1)
        _CrackScale ("Crack cells per unit", Float) = 3.5
        _CrackWidth ("Crack width", Float) = 0.12
        _Glow ("Hostile glow rim (0-1)", Float) = 0
        _Fade ("Overlay fade", Float) = 1
    }
    SubShader
    {
        // Drawn at the start of the transparent queue: the models' depth is all there by then (the opaque queue would
        // need a DepthOnly pass with depth priming), and sprites (bars, shadows, Fx) still draw over it.
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-50" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _InkColor;
            float _InkWidth, _InkMinPixels, _UseSmooth;
            half _Flash, _FlashEmission;
            half4 _Tint;
            half _Crack;
            half4 _CrackColor;
            float _CrackScale, _CrackWidth;
            half _Glow, _Fade;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Ink"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 smoothOS : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 n = _UseSmooth > 0.5 ? v.smoothOS : v.normalOS;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = SafeNormalize(TransformObjectToWorldNormal(n, false));
                // World size of one pixel at this depth, so far-away enemies keep a readable line.
                float w = TransformWorldToHClip(posWS).w;
                float pixel = 2.0 * w / (UNITY_MATRIX_P[1][1] * _ScreenParams.y);
                o.positionCS = TransformWorldToHClip(posWS + nWS * max(_InkWidth, _InkMinPixels * pixel));
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(_InkColor.rgb, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 positionOS : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            // Distance to the nearest cell border (F2 - F1) and a random id of the nearest cell.
            float2 Voronoi(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float f1 = 8.0, f2 = 8.0, id = 0.0;
                for (int z = -1; z <= 1; z++)
                    for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                        {
                            float3 g = float3(x, y, z);
                            float3 h = Hash3(i + g);
                            float3 r = g + h - f;
                            float d = dot(r, r);
                            if (d < f1) { f2 = f1; f1 = d; id = h.x; }
                            else if (d < f2) f2 = d;
                        }
                return float2(sqrt(f2) - sqrt(f1), id);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(dot(n, v)), 2.0);

                // Status: a wash over the whole body, strongest at the rim.
                half3 col = _Tint.rgb;
                half a = _Tint.a * (0.35 + 0.65 * rim);

                // Cracks: cell borders in model space, more cells crack as _Crack rises.
                if (_Crack > 0.001)
                {
                    float2 vo = Voronoi(i.positionOS * _CrackScale);
                    half crack = (1.0 - smoothstep(0.0, _CrackWidth * (0.6 + 0.4 * _Crack), vo.x)) * step(vo.y, _Crack);
                    col = lerp(col, _CrackColor.rgb, crack);
                    a = max(a, crack);
                }
                half glow = _Glow * rim;
                col = lerp(col, _CrackColor.rgb, saturate(glow * 1.5));
                a = max(a, glow);

                // The flash goes white and past it (emission), so bloom and the grade can't grey it out.
                col = lerp(col, (1.0 + _FlashEmission).xxx, _Flash);
                a = max(a, _Flash);
                return half4(col, saturate(a) * _Fade);
            }
            ENDHLSL
        }
    }
}
