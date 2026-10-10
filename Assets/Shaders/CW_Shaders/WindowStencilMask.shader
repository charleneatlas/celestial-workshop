// Writes stencil ref 1 inside a disc (UV radius 0.5 on a Quad) and draws no color.
// On a sphere mesh, set Clip Radius to 1 so the whole silhouette writes stencil.
// Objects on the WindowMasked layer are rendered by URP Render Objects features
// with stencil Equal 1, so they only appear through this disc.
Shader "CelestialWorkshop/WindowStencilMask"
{
    Properties
    {
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 1
        _ClipRadius ("Clip Radius (UV)", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WindowStencilMask"
            Tags { "LightMode" = "UniversalForward" }

            ColorMask 0
            ZWrite Off
            Cull Off

            Stencil
            {
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ClipRadius;
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
                clip(_ClipRadius - length(input.uv - 0.5));
                return 0;
            }
            ENDHLSL
        }
    }
}
