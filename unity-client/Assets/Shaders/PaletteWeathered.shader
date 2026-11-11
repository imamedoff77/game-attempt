// Palette-coloured models with a weathering layer: dust that climbs from the ground, soft grime noise,
// darker undersides and a little edge brightening. Everything is computed from the model-space position,
// so the wear stays glued to a unit while it drives around. Lit + shadows through the URP PBR path.
Shader "IronDoctrine/PaletteWeathered"
{
    Properties
    {
        [MainTexture] _BaseMap ("Palette", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        _DustColor ("Dust colour", Color) = (0.78, 0.64, 0.42, 1)
        _DustHeight ("Dust height", Float) = 0.45
        _DustAmount ("Dust amount", Range(0,1)) = 0.5
        _GrimeAmount ("Grime amount", Range(0,1)) = 0.22
        _GrimeScale ("Grime scale", Float) = 7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _DustColor;
                float _DustHeight;
                float _DustAmount;
                float _GrimeAmount;
                float _GrimeScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x),
                         lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                         lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionOS = input.positionOS.xyz;
                o.normalOS = input.normalOS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 nOS = normalize(i.normalOS);

                // grime: two octaves of noise in model space, so it reads as stains and uneven paint
                float n = ValueNoise(i.positionOS * _GrimeScale) * 0.65 + ValueNoise(i.positionOS * _GrimeScale * 2.7) * 0.35;
                albedo *= 1.0 - _GrimeAmount * (1.0 - n * 1.6);

                // dust: climbs the model from the ground, more on side faces than on the top
                float h = saturate(1.0 - i.positionOS.y / max(_DustHeight, 0.01));
                float dust = h * h * _DustAmount * (0.55 + 0.9 * n);
                albedo = lerp(albedo, albedo * 0.45 + _DustColor.rgb * 0.55, saturate(dust));

                // undersides and the lowest strip are darker (cheap ambient occlusion)
                float down = saturate(-nOS.y);
                albedo *= 1.0 - 0.45 * down;
                albedo *= 1.0 - 0.18 * h;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = normalize(i.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.smoothness = 0.08;
                s.metallic = 0;
                s.occlusion = 1;

                half4 color = UniversalFragmentPBR(inputData, s);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
