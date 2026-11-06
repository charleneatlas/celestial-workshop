// Flat-color miniature shading for the observer diorama. The scene has no
// real-time lights, so this fakes one: a single "sun" direction set globally
// by ObserverDiorama (_DioramaLightDir, world space, pointing toward the
// light) so the light tilts with the diorama. Unlit otherwise; opaque, writes
// alpha 1 so it occludes passthrough.
Shader "CelestialWorkshop/DioramaToon"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Shade ("Shadow Side Brightness", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "DioramaToon"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Shade;
            CBUFFER_END

            float4 _DioramaLightDir;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 light = _DioramaLightDir.xyz;
                light = dot(light, light) > 1e-6 ? normalize(light) : float3(0, 1, 0);
                half diffuse = saturate(dot(normalize(input.normalWS), light));
                half brightness = lerp(_Shade, 1.0h, diffuse);
                return half4(_BaseColor.rgb * brightness, 1.0h);
            }
            ENDHLSL
        }
    }
}
