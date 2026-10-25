// See-through placement preview of a building model: one flat colour, softly shaded by the sun so its shape reads.
Shader "DungeonConveyor/Ghost"
{
    Properties
    {
        _Color ("Colour", Color) = (0.6, 1, 0.6, 0.5)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            float4 _PlanetSunDir;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float ndl = saturate(dot(normalize(i.normalWS), _PlanetSunDir.xyz));
                return half4(_Color.rgb * (0.65 + 0.35 * ndl), _Color.a);
            }
            ENDHLSL
        }
    }
}
