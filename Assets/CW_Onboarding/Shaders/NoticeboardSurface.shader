// Wood, parchment, nails and ink on the workshop noticeboard.
// Unlit with a fixed soft key light (no scene lights in passthrough MR), a
// little wrap so nothing goes black, and the board's global fade
// (_CW_BoardFadeOut: 0 = visible, 1 = gone; set by WorkshopOnboarding).
Shader "CelestialWorkshop/NoticeboardSurface"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor ("Emission (adds glow, e.g. gold leaf)", Color) = (0, 0, 0, 0)
        _Shade ("Lighting amount", Range(0, 1)) = 0.6
        _KeyDir ("Key light direction (world, toward light)", Vector) = (-0.35, 0.8, -0.5, 0)
        _Ambient ("Ambient", Range(0, 1)) = 0.62
        _Cutoff ("Alpha below this is discarded", Range(0, 1)) = 0.02
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _Shade;
                float4 _KeyDir;
                half _Ambient;
                half _Cutoff;
            CBUFFER_END

            // Global, deliberately not a material property.
            float _CW_BoardFadeOut;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half4 c = tex * _BaseColor * i.color;
                float3 n = normalize(i.normalWS);
                float3 l = normalize(_KeyDir.xyz);
                half wrap = saturate(dot(n, l) * 0.6 + 0.4);
                half light = lerp(1.0, _Ambient + (1.0 - _Ambient) * wrap * 1.25, _Shade);
                c.rgb *= light;
                c.rgb += _EmissionColor.rgb * tex.a;
                c.a *= saturate(1.0 - _CW_BoardFadeOut);
                clip(c.a - _Cutoff * saturate(1.0 - _CW_BoardFadeOut) - 1e-4);
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
