// Faint fresnel glow on a sphere's silhouette so the spherical volume reads as
// a 3D object. Additive in both color and alpha, like WindowRim, so it still
// shows over the passthrough punch-out.
Shader "CelestialWorkshop/SphereRim"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.45, 0.6, 1.0, 0.5)
        _Power ("Fresnel Power", Range(0.5, 16)) = 4
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
            Name "SphereRim"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One, One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Power;
            CBUFFER_END

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
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                float facing = saturate(dot(normalize(input.normalWS), viewDirection));
                half intensity = pow(1.0 - facing, _Power) * _Color.a;
                return half4(_Color.rgb * intensity, intensity);
            }
            ENDHLSL
        }
    }
}
