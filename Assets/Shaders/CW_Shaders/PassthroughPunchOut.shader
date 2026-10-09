// Drawn from inside a large camera-centered sphere in passthrough mode. Wherever
// only the skybox has been drawn (depth test) and the circular window's stencil
// is not set, it writes transparent black so the passthrough underlay shows.
// The sky inside the window and any nearer geometry are left untouched.
Shader "CelestialWorkshop/PassthroughPunchOut"
{
    Properties
    {
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PassthroughPunchOut"
            Tags { "LightMode" = "UniversalForward" }

            Blend One Zero
            ZWrite Off
            ZTest LEqual
            Cull Front

            Stencil
            {
                Ref [_StencilRef]
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
