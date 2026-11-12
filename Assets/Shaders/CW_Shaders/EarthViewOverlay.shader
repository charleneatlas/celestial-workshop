// Shows the Earth view render texture with no background: additive, so the
// camera's near-black clear adds nothing and only the constellation (and
// anything else bright in the view) appears, floating over whatever is behind.
// Black Cutoff removes the faint clear colour and MSAA edge noise first.
// Samples the screen mesh's UVs exactly like CRTScreen at rest, so the picture
// keeps the same orientation as the Earth View Disc.
Shader "CelestialWorkshop/EarthViewOverlay"
{
    Properties
    {
        [MainTexture] _BaseMap ("Earth View", 2D) = "black" {}
        _Intensity ("Intensity", Range(0, 4)) = 1.5
        _BlackCutoff ("Black Cutoff", Range(0, 0.5)) = 0.06
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            // After the sphere rim, so the stars sit on top of it.
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
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
                half _Intensity;
                half _BlackCutoff;
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
                half3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                c = max(c - _BlackCutoff, 0) / max(1 - _BlackCutoff, 1e-3);
                return half4(c * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
