// Soft glowing band centered on UV radius 0.5 of a Quad, drawn over the hard
// stencil edge of the circular window. Additive in both color and alpha so the
// rim still shows when the background is cleared to transparent for passthrough.
Shader "CelestialWorkshop/WindowRim"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.6, 0.75, 1.0, 1.0)
        _Radius ("Radius (UV)", Range(0.1, 0.5)) = 0.42
        _Width ("Width (UV)", Range(0.001, 0.2)) = 0.015
        _Softness ("Softness (UV)", Range(0.001, 0.2)) = 0.05
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
            Name "WindowRim"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One, One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Radius;
                float _Width;
                float _Softness;
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
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float distanceFromEdge = abs(length(input.uv - 0.5) - _Radius);
                float band = 1.0 - smoothstep(_Width, _Width + _Softness, distanceFromEdge);
                half intensity = band * _Color.a;
                return half4(_Color.rgb * intensity, intensity);
            }
            ENDHLSL
        }
    }
}
