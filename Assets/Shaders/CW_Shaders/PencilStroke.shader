// Graphite pencil strokes for the Star Chart's Sketch style. StarChartLayout
// builds each stroke as a ribbon of quads: uv.x = distance along the stroke
// (local units), uv.y = 0..1 across it; vertex alpha carries pressure and the
// tapered ends. Here the across-stroke edge is softened and the graphite is
// broken up by paper grain and uneven pressure along the stroke.
// Unlit, alpha blended, both sides, no depth write.
Shader "CelestialWorkshop/PencilStroke"
{
    Properties
    {
        _BaseColor ("Graphite Color", Color) = (0.2, 0.2, 0.22, 1)
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.6
        _GrainScale ("Grain Scale (cells per unit)", Float) = 1800
        _GrainStrength ("Grain Strength", Range(0, 1)) = 0.45
        _PressureScale ("Pressure Variation Scale (per unit)", Float) = 120
        _PressureStrength ("Pressure Variation", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PencilStroke"
            Tags { "LightMode" = "UniversalForward" }

            // Separate alpha blend: the eye buffer keeps the true coverage, so passthrough
            // behind the chart shows through by exactly (1 - opacity).
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgeSoftness;
                float _GrainScale;
                half _GrainStrength;
                float _PressureScale;
                half _PressureStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 grainPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                // The chart lies in the object's XZ plane: grain is fixed to the paper.
                output.grainPos = input.positionOS.xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half across = 1.0h - abs(input.uv.y * 2.0h - 1.0h);
                half edge = smoothstep(0.0h, _EdgeSoftness, across);

                half grain = ValueNoise(input.grainPos * _GrainScale);
                half pressure = ValueNoise(float2(input.uv.x * _PressureScale, input.uv.y * 0.5));

                half alpha = input.color.a * _BaseColor.a * edge;
                alpha *= lerp(1.0h, grain, _GrainStrength);
                alpha *= lerp(1.0h, 0.55h + 0.45h * pressure, _PressureStrength);

                return half4(_BaseColor.rgb * input.color.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
