// Paper for the Star Chart's Sketch style: a see-through sheet the desk shows
// through. Aged look = the paper texture as is; Tracing look = the same sheet
// turned pale and cool (texture detail kept faintly, so grid and stains ghost
// through). StarChartLayout sets _Opacity and _Tracing per renderer.
// Unlit, alpha blended, drawn just before the other transparents so the pencil
// marks and labels always land on top of it.
Shader "CelestialWorkshop/ChartPaper"
{
    Properties
    {
        _BaseMap ("Paper", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.75
        _Tracing ("Tracing Look (0 aged, 1 tracing)", Range(0, 1)) = 0
        _TracingColor ("Tracing Paper Color", Color) = (0.93, 0.94, 0.94, 1)
        _TracingDetail ("Tracing: Texture Detail Kept", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ChartPaper"
            Tags { "LightMode" = "UniversalForward" }

            // Separate alpha blend: the eye buffer keeps the true coverage, so passthrough
            // behind the chart shows through by exactly (1 - opacity).
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Opacity;
                half _Tracing;
                half4 _TracingColor;
                half _TracingDetail;
            CBUFFER_END

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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 aged = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;

                // Tracing: keep the paper's light/dark detail (grid, creases) but
                // drop its warm color, relative to the paper's typical brightness.
                half luminance = dot(aged, half3(0.299h, 0.587h, 0.114h));
                half detail = saturate(luminance / 0.85h);
                half3 tracing = _TracingColor.rgb * lerp(1.0h, detail, _TracingDetail);

                half3 color = lerp(aged, tracing, _Tracing);
                return half4(color, saturate(_Opacity * _BaseColor.a));
            }
            ENDHLSL
        }
    }
}
