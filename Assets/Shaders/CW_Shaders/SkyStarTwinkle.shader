// The sky constellation (seen through the Earth View Camera): glowing stars
// with gentle twinkle and colour variety, and soft-edged lines. Additive, like
// the URP Unlit material it replaces; everything is computed here on the GPU,
// so there's no per-frame script or material change.
//
// Stars vs lines: each generated star renderer carries _TwinkleSeed (0..1) in
// its property block, set once when the constellation is built. Renderers
// without one (the connecting cylinders) use the material's -1 and draw as lines.
//
// Stars: the sphere is drawn _GlowSize times larger and shaded as a bright,
// whitish core inside a soft glow tinted from a small star palette (warm gold,
// pale yellow, white, blue-white) picked by the seed. The palette blends in by
// _ColorVariety x _PaletteMix; the highlight controller sets _PaletteMix to 0
// in the near-solve and solved states, so those tints read as before.
// Seeds below _TwinkleFraction also twinkle (brightness within 1 +/- Amount;
// the sRGB Earth view image shows Amount 0.3 as ~16% peak-to-peak).
//
// Lines: the cylinder is drawn at least _MinLinePixels in radius on screen
// (fainter by the same factor, so its weight stays the same) and faded toward
// its edges. A sub-pixel line otherwise drops pixels and breaks into moving
// dashes, worse once the image is resampled on the display.
//
// Marked stars swap to the highlight material and stay solid and steady.
//
// Inside the sphere only: NearFieldSkyProxy sets the global _SkySphere (centre,
// radius) once when the globe is placed. Outside it (the intro copy on its way
// from the workstation, over passthrough) stars and lines draw plain, like the
// original solid look; the glow, colours, twinkle and soft lines fade in over
// the outer _SphereFade of the radius as they travel in.
Shader "CelestialWorkshop/SkyStarTwinkle"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)

        [Header(Star glow)]
        _GlowSize ("Glow size (x star radius)", Range(1, 6)) = 3.5
        _CoreSize ("Core size (x star radius)", Range(0.1, 1.5)) = 0.55
        _CoreBrightness ("Core brightness", Range(0, 4)) = 1.6
        _CoreWhiteness ("Core whiteness", Range(0, 1)) = 0.6
        _GlowStrength ("Glow strength", Range(0, 3)) = 1.3
        _GlowFalloff ("Glow falloff", Range(0.5, 4)) = 1.1
        _ColorVariety ("Colour variety (star palette)", Range(0, 1)) = 0.9

        [Header(Twinkle)]
        _TwinkleAmount ("Amount (brightness +/-)", Range(0, 0.45)) = 0.3
        _TwinkleSpeed ("Speed", Range(0, 4)) = 0.9
        _TwinkleFraction ("Share of stars that twinkle", Range(0, 1)) = 0.35

        [Header(Lines)]
        _LineSoftWidth ("Line width (x, soft edges)", Range(1, 4)) = 1.4
        _MinLinePixels ("Min line half-width (pixels)", Range(0.5, 4)) = 3
        _LineSharpness ("Line edge sharpness", Range(0.5, 6)) = 1
        _LineBrightness ("Line brightness", Range(0, 2)) = 0.75

        [Header(Sphere)]
        _SphereFade ("Glow fades in over this share of the sphere radius", Range(0.02, 1)) = 0.25

        [HideInInspector] _TwinkleSeed ("Seed (per star, from code; -1 = line)", Float) = -1
        [HideInInspector] _PaletteMix ("Palette mix (per state, from code)", Float) = 1

        [Header(Blending (as URP Unlit))]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlendAlpha ("Src Alpha", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlendAlpha ("Dst Alpha", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Toggle] _ZWrite ("ZWrite", Float) = 0
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
            Name "SkyStarTwinkle"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            ZWrite [_ZWrite]
            Cull [_Cull]

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
                half _GlowSize;
                half _CoreSize;
                half _CoreBrightness;
                half _CoreWhiteness;
                half _GlowStrength;
                half _GlowFalloff;
                half _ColorVariety;
                half _TwinkleAmount;
                half _TwinkleSpeed;
                half _TwinkleFraction;
                half _LineSoftWidth;
                half _MinLinePixels;
                half _LineSharpness;
                half _LineBrightness;
                float _TwinkleSeed;
                half _PaletteMix;
                half _SphereFade;
            CBUFFER_END

            // Global, set once by NearFieldSkyProxy: xyz = globe centre, w = radius (0 = not placed yet).
            float4 _SkySphere;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 starTint : TEXCOORD3;   // stars: rgb = halo colour, a = twinkle multiplier
                float3 lineInfo : TEXCOORD4;  // x = drawn radius (world, lines), y = intensity compensation, z = inside-sphere blend (0 plain .. 1 full look)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Smooth, irregular flicker in -1..1: three incommensurate sines,
            // each star at its own phase and pace.
            half Twinkle(float seed)
            {
                float phase = frac(seed * 91.7) * 6.2831853;
                float pace = lerp(0.75, 1.3, frac(seed * 37.3));
                float t = _Time.y * _TwinkleSpeed * pace;
                return (half)(0.5 * sin(t * 1.9 + phase) +
                              0.3 * sin(t * 3.1 + phase * 1.7) +
                              0.2 * sin(t * 5.3 + phase * 2.3));
            }

            // A small palette of naked-eye star colours.
            half3 Palette(float seed)
            {
                float pick = frac(seed * 53.17 + 0.31);
                if (pick < 0.28) return half3(1.0, 0.80, 0.52);   // warm gold
                if (pick < 0.52) return half3(1.0, 0.93, 0.80);   // pale yellow-white
                if (pick < 0.78) return half3(0.94, 0.97, 1.0);   // white
                return half3(0.72, 0.84, 1.0);                    // blue-white
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                bool isStar = _TwinkleSeed >= 0.0;
                float3 positionOS = input.positionOS.xyz;
                float3 centerWS = UNITY_MATRIX_M._m03_m13_m23;

                // 1 well inside the globe, fading to 0 at its shell (plain outside).
                float inside = 0.0;
                if (_SkySphere.w > 0.0)
                {
                    float depth = _SkySphere.w - distance(centerWS, _SkySphere.xyz);
                    inside = saturate(depth / max(1e-4, _SkySphere.w * _SphereFade));
                }

                output.lineInfo = float3(0.0, 1.0, inside);
                if (isStar)
                {
                    positionOS *= lerp(1.0, _GlowSize, inside);   // room for the glow around the sphere
                }
                else
                {
                    // Cylinder (radius 0.5 in object space): draw it at least
                    // _MinLinePixels pixels in radius on screen, fading it by the
                    // same factor, so a thin line stays continuous instead of
                    // breaking into dashes; then soften its edges.
                    float radiusScale = length(UNITY_MATRIX_M._m00_m10_m20);
                    float nominal = 0.5 * radiusScale;
                    float w = max(1e-5, TransformWorldToHClip(centerWS).w);
                    float pixelWorld = 2.0 * w / max(1e-5, abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                    float soft = max(nominal * _LineSoftWidth, _MinLinePixels * pixelWorld);
                    float drawn = lerp(nominal, soft, inside);      // plain: the cylinder as built
                    positionOS.xz *= drawn / max(1e-6, nominal);
                    output.lineInfo = float3(drawn, lerp(1.0, saturate(nominal * _LineSoftWidth / soft), inside), inside);
                }

                float3 positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = positionWS;

                half3 halo = _BaseColor.rgb;
                half twinkle = 1.0h;
                if (isStar)
                {
                    half level = max(dot(_BaseColor.rgb, half3(0.3333h, 0.3333h, 0.3333h)) * 2.0h, 0.5h);
                    halo = lerp(_BaseColor.rgb, Palette(_TwinkleSeed) * level, _ColorVariety * _PaletteMix);
                    half active = _TwinkleSeed < _TwinkleFraction ? 1.0h : 0.0h;
                    twinkle = 1.0h + _TwinkleAmount * active * Twinkle(_TwinkleSeed) * (half)inside;
                }

                output.starTint = half4(halo, twinkle);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float3 eye = GetCameraPositionWS();
                float3 rayDir = normalize(input.positionWS - eye);
                float3 center = UNITY_MATRIX_M._m03_m13_m23;

                half3 rgb;
                half alpha = _BaseColor.a * baseTex.a;
                if (_TwinkleSeed >= 0.0)
                {
                    // How close this view ray passes to the star's centre, in units
                    // of the star's own radius (the sphere mesh has radius 0.5).
                    float starRadius = 0.5 * length(UNITY_MATRIX_M._m00_m10_m20);
                    half r = (half)(length(cross(center - eye, rayDir)) / max(1e-6, starRadius));
                    half inside = (half)input.lineInfo.z;
                    half drawnSize = lerp(1.0h, _GlowSize, inside);
                    half edgeFade = 1.0h - smoothstep(drawnSize * 0.6h, drawnSize * 0.97h, r);
                    half core = exp(-(r * r) / max(1e-3h, _CoreSize * _CoreSize));
                    half glow = exp(-r * _GlowFalloff) * _GlowStrength;
                    half3 halo = input.starTint.rgb;
                    half3 coreColor = lerp(halo, half3(1.0h, 1.0h, 1.0h), _CoreWhiteness) * _CoreBrightness;
                    half3 glowing = (coreColor * core + halo * glow) * edgeFade * input.starTint.a;
                    // Plain: the original solid dot in the state colour.
                    half3 plain = _BaseColor.rgb * saturate((1.0h - r) * 8.0h);
                    rgb = lerp(plain, glowing, inside);
                }
                else
                {
                    // Soft line profile across the line: how close the view ray
                    // passes to the cylinder's axis (object Y), relative to the
                    // drawn radius, so it doesn't depend on the line's direction.
                    float3 axis = normalize(UNITY_MATRIX_M._m01_m11_m21);
                    float3 toAxis = center - eye;
                    float3 across = cross(rayDir, axis);
                    float denom = max(1e-6, length(across));
                    float axisDistance = abs(dot(toAxis, across)) / denom;
                    float drawnRadius = input.lineInfo.x;
                    half profile = pow(saturate(1.0h - (half)(axisDistance / max(1e-6, drawnRadius))), _LineSharpness);
                    half inside = (half)input.lineInfo.z;
                    // Plain (outside the globe): the solid cylinder in the state colour.
                    rgb = _BaseColor.rgb * lerp(1.0h, _LineBrightness, inside);
                    alpha *= lerp(1.0h, profile * (half)input.lineInfo.y, inside);
                }

                return half4(rgb * baseTex.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
