// Thin glowing atmosphere around the planet: additive, strongest where the shell is seen edge-on.
Shader "DungeonConveyor/Atmosphere"
{
    Properties
    {
        _Color ("Colour", Color) = (0.35, 0.6, 1, 1)
        _Power ("Rim sharpness", Float) = 3
        _Strength ("Strength", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-10" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Power, _Strength;
            CBUFFER_END
            float4 _PlanetSunDir;

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

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 toCam = normalize(_WorldSpaceCameraPos - i.positionWS);
                float rim = pow(1 - saturate(dot(n, toCam)), _Power);
                float lit = 0.35 + 0.65 * saturate(dot(n, _PlanetSunDir.xyz) * 0.5 + 0.5);
                return half4(_Color.rgb * rim * lit * _Strength, 1);
            }
            ENDHLSL
        }
    }
}
