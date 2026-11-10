// Earth View Disc screen with an old CRT power off / power on effect.
// Opaque and unlit: at rest (_Height = _Width = 1, _Glow = 0, _Scanlines = 0,
// _Brightness = 1) it draws exactly _BaseMap * _BaseColor, like URP Unlit.
//
// Everything happens in the picture's own UV frame (the screen mesh maps the
// render texture planar, centered on uv 0.5), so the collapse line follows the
// picture's horizontal, like a real tube's scan direction.
//
//   _Height  vertical extent of the picture (1 = full, 0 = a line)
//   _Width   horizontal extent (1 = full, 0 = a dot)
//   _Glow    how far the visible picture is washed toward _GlowColor
//   _Energy  strength of the glowing line/dot and its halo
//   Outside the shrinking picture the screen shows _OffColor (dark glass).
Shader "CelestialWorkshop/CRTScreen"
{
    Properties
    {
        [MainTexture] _BaseMap ("Picture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _OffColor ("Off Color", Color) = (0.035, 0.045, 0.06, 1)
        _GlowColor ("Glow Color", Color) = (1, 0.8, 0.5, 1)
        _Height ("Height", Range(0, 1)) = 1
        _Width ("Width", Range(0, 1)) = 1
        _Glow ("Glow", Range(0, 1)) = 0
        _Energy ("Energy", Range(0, 1)) = 1
        _Brightness ("Brightness", Range(0, 2)) = 1
        _LineThickness ("Line Thickness (uv)", Range(0.001, 0.05)) = 0.006
        _HaloSize ("Halo Size (uv)", Range(0.001, 0.2)) = 0.03
        _Scanlines ("Scanline Strength", Range(0, 1)) = 0
        _ScanlineCount ("Scanline Count", Float) = 160
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _OffColor;
            half4 _GlowColor;
            half _Height;
            half _Width;
            half _Glow;
            half _Energy;
            half _Brightness;
            half _LineThickness;
            half _HaloSize;
            half _Scanlines;
            float _ScanlineCount;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "CRTScreen"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Centered picture coordinates, -0.5..0.5 across the screen.
                float2 p = input.uv - 0.5;

                // Half extents of the visible picture. The height never drops
                // below the line's thickness, so a collapsed picture is a line
                // and a collapsed line is a dot.
                float halfH = max(0.5 * _Height, _LineThickness);
                float halfW = max(0.5 * _Width, _LineThickness);

                // Distance outside the visible rectangle (0 inside), with a
                // soft, antialiased edge.
                float2 d = abs(p) - float2(halfW, halfH);
                float outside = length(max(d, 0.0));
                float edge = max(fwidth(outside), 1e-4);
                float inside = 1.0 - saturate(outside / edge);

                // The picture squashed into the rectangle (sampled from the
                // whole texture, so the full image is compressed, not cropped).
                float2 pictureUV = 0.5 + p * float2(0.5 / halfW, 0.5 / halfH);
                pictureUV = pictureUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
                half3 picture = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, pictureUV).rgb * _BaseColor.rgb;

                // Scanlines move with the picture, so they bunch up as it collapses.
                half scan = 0.5 + 0.5 * sin(pictureUV.y * _ScanlineCount * 6.2831853);
                half3 lit = lerp(picture, _GlowColor.rgb, _Glow) * _Brightness;
                lit *= 1.0 - _Scanlines * 0.45 * scan;

                // Glowing core (line/dot) plus a halo bleeding past its edges.
                half halo = _Energy * _Glow * exp(-outside / _HaloSize);
                half core = inside * lerp(1.0, _Energy, _Glow);
                half3 color = lerp(_OffColor.rgb, lit, core);
                color = lerp(color, _GlowColor.rgb * _Brightness, saturate(halo) * (1.0 - inside));

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
