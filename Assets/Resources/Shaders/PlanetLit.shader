// Planet ground: vertex colours lit by the sun that follows the camera (CameraController sets _PlanetSunDir and
// _PlanetSunColor), plus a cool ambient so the far side never goes black.
Shader "DungeonConveyor/PlanetLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            float4 _PlanetSunDir;     // towards the sun
            float4 _PlanetSunColor;
            float4 _PlanetAmbient;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float ndl = saturate(dot(normalize(i.normalWS), _PlanetSunDir.xyz));
                half3 light = _PlanetAmbient.rgb + _PlanetSunColor.rgb * ndl;
                return half4(i.color.rgb * light, 1);
            }
            ENDHLSL
        }
    }
}
