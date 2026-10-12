// Style test D (temporary): the dither look from blender-work/_tools/dc_dither.py (v2, Bayer), used only by
// ModelArt while it bakes building sprites. Each surface is a palette colour pushed toward ink (shadow) or warm
// white (specular) by an 8x8 Bayer pattern in sprite pixels, so gradients dither smoothly.
Shader "DungeonConveyor/DitherBake"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Bright ("Brightness (-8..8)", Float) = 0
        _Spec ("Specular", Float) = 0
        _Glow ("Glow (no shading)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Bright, _Spec, _Glow;
            CBUFFER_END
            // Set by ModelArt for each render.
            float4 _DitherLightDir;   // towards the light
            float4 _DitherViewDir;    // towards the camera
            float _DitherGroundY, _DitherScale;

            static const half3 Ink = half3(0.0232, 0.0091, 0.0395);    // #2A1838 (linear)
            static const half3 White = half3(1.0, 0.8796, 0.7157);     // #FFF1DC (linear)

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            // Same ranking as dc_dither.py _ranks() family 0.
            float Bayer(uint x, uint y)
            {
                uint v = 0, xc = x ^ y;
                for (uint i = 0; i < 3; i++) v = (v << 2) | (((y >> i) & 1u) << 1) | ((xc >> i) & 1u);
                return (v + 0.5) / 64.0;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 l = normalize(_DitherLightDir.xyz);
                float ndl = dot(n, l);
                // Tone settings mirror dc_dither.py TONE (Blender lum 0.60..0.25 ~ N.L 0.39..0).
                float shade = smoothstep(0.39, 0.0, ndl) * 0.42 + smoothstep(0.0, -0.4, ndl) * 0.15;
                float h = (i.positionWS.y - _DitherGroundY) / max(_DitherScale, 0.0001);
                shade += smoothstep(0.55, 0.0, h) * 0.22;
                shade *= 1.0 - _Glow;
                float3 hv = normalize(l + normalize(_DitherViewDir.xyz));
                float spec = smoothstep(0.35, 0.8, pow(saturate(dot(n, hv)), 60.0)) * 0.75 * _Spec;
                float tone = clamp(_Bright / 8.0 - shade + spec, -1.0, 1.0);
                uint2 p = (uint2)i.positionCS.xy & 7u;
                float mask = Bayer(p.x, p.y) < abs(tone) ? 1.0 : 0.0;
                half3 target = tone < 0 ? Ink : White;
                return half4(lerp(_Color.rgb, target, mask), 1.0);
            }
            ENDHLSL
        }
    }
}
