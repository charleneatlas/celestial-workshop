// Hand-carved wood for the small constellation sculptures (stars and rods).
// The scene has no real-time lights (passthrough MR), so like the noticeboard
// and diorama shaders this fakes a soft key light from a fixed world
// direction, with wrap so the shadow side never goes black. A tangent-space
// normal map carries the grain and knife marks; a faint, broad sheen stands
// in for a matte oiled finish (no metal, no reflections). Emission is the
// grab/hover highlight. One texture + one normal sample: cheap on Quest.
Shader "CelestialWorkshop/CarvedWood"
{
    Properties
    {
        _BaseMap ("Wood Albedo", 2D) = "white" {}
        _BaseColor ("Wood Tint", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Grain / Carving Normal", 2D) = "bump" {}
        _BumpScale ("Carving Depth", Range(0, 3)) = 1
        _KeyDir ("Key Light Direction (world, toward light)", Vector) = (-0.35, 0.8, -0.5, 0)
        _KeyColor ("Key Light Color", Color) = (1, 0.95, 0.86, 1)
        _Ambient ("Shadow Side Brightness", Range(0, 1)) = 0.45
        _Wrap ("Light Wrap", Range(0, 1)) = 0.35
        _Sheen ("Sheen (matte = 0)", Range(0, 0.5)) = 0.05
        _SheenTightness ("Sheen Tightness", Range(1, 64)) = 10
        [HDR] _EmissionColor ("Emission (highlight glow)", Color) = (0, 0, 0, 0)
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
            Name "CarvedWood"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                float4 _KeyDir;
                half4 _KeyColor;
                half _Ambient;
                half _Wrap;
                half _Sheen;
                half _SheenTightness;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normals.normalWS;
                output.tangentWS = float4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);

                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS = normalize(input.tangentWS.xyz);
                float3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
                float3 n = normalize(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));

                float3 l = _KeyDir.xyz;
                l = dot(l, l) > 1e-6 ? normalize(l) : float3(0, 1, 0);
                half ndl = dot(n, l);
                half diffuse = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                half3 lightColor = lerp(half3(_Ambient, _Ambient, _Ambient), _KeyColor.rgb, diffuse);

                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half sheen = pow(saturate(dot(n, normalize(l + v))), _SheenTightness) * _Sheen * saturate(ndl * 4.0h);

                half3 color = albedo * lightColor + sheen * _KeyColor.rgb + _EmissionColor.rgb;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
