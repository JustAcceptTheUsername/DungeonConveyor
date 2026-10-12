// Style test D2 (temporary): full-screen ordered dither, applied after post-processing by the "DitherScreen"
// Full Screen Pass renderer feature on PC_Renderer / Mobile_Renderer. Brightness is quantised to a few levels
// with an 8x8 Bayer pattern in screen pixels while the hue is kept, so the dots stay crisp at any zoom.
// Off unless _DitherScreenOn = 1 (set by GameArt from BuildingCatalog.ScreenDither; ModelArt turns it off
// while it bakes sprites). The screen-overlay UI is drawn afterwards and is never dithered.
Shader "DungeonConveyor/DitherScreen"
{
    Properties
    {
        _DitherLevels ("Brightness levels", Range(2, 16)) = 6
        _DitherPixel ("Dot size (screen pixels)", Range(1, 4)) = 1
        _DitherStrength ("Pattern strength", Range(0, 1)) = 1
        _DitherContrast ("Contrast", Range(0.5, 2)) = 1.1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "DitherScreen"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _DitherLevels, _DitherPixel, _DitherStrength, _DitherContrast;
            float _DitherScreenOn;

            float Bayer(uint x, uint y)
            {
                uint v = 0, xc = x ^ y;
                for (uint i = 0; i < 3; i++) v = (v << 2) | (((y >> i) & 1u) << 1) | ((xc >> i) & 1u);
                return (v + 0.5) / 64.0;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, i.texcoord);
                if (_DitherScreenOn < 0.5) return src;
                float3 c = saturate(LinearToSRGB(src.rgb));
                c = saturate((c - 0.5) * _DitherContrast + 0.5);
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                uint2 p = (uint2)(i.positionCS.xy / max(_DitherPixel, 1.0)) & 7u;
                float t = (Bayer(p.x, p.y) - 0.5) * _DitherStrength;
                float n = max(_DitherLevels - 1.0, 1.0);
                float q = saturate(floor(lum * n + t + 0.5) / n);
                // Shift the brightness by the quantisation step (additive, so dark colours can't blow up into
                // saturated specks the way a multiplicative hue-preserving gain does).
                float3 o = saturate(c + (q - lum));
                return half4(SRGBToLinear(o), src.a);
            }
            ENDHLSL
        }
    }
}
