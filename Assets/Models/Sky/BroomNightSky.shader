// Ночное небо для курсовой «Полёты на метле».
// Слои (снизу вверх):
//   1. Облака и луна — кубмап из AllSkyFree «Cold Night» (можно подставить любой другой кубмап).
//   2. Процедурные звёзды — два слоя: много тусклых и немного ярких (HDR, ловятся Bloom'ом),
//      с мерцанием, цветовой температурой, затуханием у горизонта и перекрытием облаками.
//   3. Млечный Путь — полоса из шума с пылевыми прожилками и тёплым ядром.
//   4. Свечение у горизонта (airglow), чтобы небо плавно переходило в туман сцены.
// Работает в URP, поддерживает Single Pass Instanced VR.
Shader "Skybox/Broom Night Sky (Stars)"
{
    Properties
    {
        [Header(Clouds and Moon)]
        [NoScaleOffset] _CloudCube ("Clouds and Moon (Cubemap)", Cube) = "black" {}
        _CloudRotation ("Rotation", Range(0, 360)) = 0
        _CloudExposure ("Exposure", Range(0, 8)) = 2.4
        _CloudTint ("Tint", Color) = (0.93, 0.96, 1, 1)
        _HighlightBoost ("Moon Glow Boost (HDR)", Range(0, 20)) = 6
        _CloudMaskMip ("Cloud Mask Blur (mip)", Range(0, 8)) = 5

        [Header(Field Stars)]
        _StarScaleA ("Grid Scale", Float) = 90
        _StarDensityA ("Density", Range(0, 1)) = 0.45
        _StarSizeA ("Size (min, max)", Vector) = (0.02, 0.05, 0, 0)
        _StarBrightnessA ("Brightness", Float) = 2.6
        _StarMagPowA ("Faint/Bright Ratio", Range(0.5, 10)) = 5

        [Header(Bright Stars)]
        _StarScaleB ("Grid Scale", Float) = 40
        _StarDensityB ("Density", Range(0, 1)) = 0.07
        _StarSizeB ("Size (min, max)", Vector) = (0.008, 0.02, 0, 0)
        _StarBrightnessB ("Brightness (HDR)", Float) = 22
        _StarMagPowB ("Faint/Bright Ratio", Range(0.5, 10)) = 2.5
        _StarHaloB ("Halo", Range(0, 3)) = 0.6

        [Header(Star Behaviour)]
        _TwinkleSpeed ("Twinkle Speed", Float) = 2.5
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.45
        _HorizonFade ("Horizon Fade (start, end)", Vector) = (-0.02, 0.22, 0, 0)
        _CloudOcclusion ("Hidden By Clouds", Range(0, 1)) = 1
        _CloudRatio ("Cloud Detect (min, max)", Vector) = (1.05, 1.45, 0, 0)
        _MoonMask ("Hidden By Moon Glow (min, max)", Vector) = (0.03, 0.18, 0, 0)

        [Header(Milky Way)]
        [Toggle(_MILKYWAY_ON)] _MilkyWay ("Enable", Float) = 1
        _MilkyWayNormal ("Band Plane Normal", Vector) = (0.35, 0.55, -0.76, 0)
        _MilkyWayWidth ("Width", Range(0.02, 0.5)) = 0.13
        _MilkyWayIntensity ("Intensity", Range(0, 1)) = 0.08
        _MilkyWayColor ("Color", Color) = (0.81, 0.89, 1, 1)
        _MilkyWayCoreColor ("Core Color", Color) = (1, 0.97, 0.92, 1)
        _MilkyWayDust ("Dust Lanes", Range(0, 1)) = 0.75
        _MilkyWayStarBoost ("Extra Stars In Band", Range(0, 1)) = 0.45

        [Header(Horizon Airglow)]
        _AirglowColor ("Color", Color) = (0.35, 0.44, 0.53, 1)
        _AirglowIntensity ("Intensity", Range(0, 0.5)) = 0.035
        _AirglowWidth ("Width", Range(0.01, 0.5)) = 0.09
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma shader_feature_local_fragment _MILKYWAY_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_CloudCube);
            SAMPLER(sampler_CloudCube);

            CBUFFER_START(UnityPerMaterial)
                float _CloudRotation;
                float _CloudExposure;
                float4 _CloudTint;
                float _HighlightBoost;
                float _CloudMaskMip;

                float _StarScaleA;
                float _StarDensityA;
                float4 _StarSizeA;
                float _StarBrightnessA;
                float _StarMagPowA;

                float _StarScaleB;
                float _StarDensityB;
                float4 _StarSizeB;
                float _StarBrightnessB;
                float _StarMagPowB;
                float _StarHaloB;

                float _TwinkleSpeed;
                float _TwinkleAmount;
                float4 _HorizonFade;
                float _CloudOcclusion;
                float4 _CloudRatio;
                float4 _MoonMask;

                float4 _MilkyWayNormal;
                float _MilkyWayWidth;
                float _MilkyWayIntensity;
                float4 _MilkyWayColor;
                float4 _MilkyWayCoreColor;
                float _MilkyWayDust;
                float _MilkyWayStarBoost;

                float4 _AirglowColor;
                float _AirglowIntensity;
                float _AirglowWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirWS      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---------- hashes & noise (Dave Hoskins, "Hash without Sine") ----------
            float3 Hash33(float3 p3)
            {
                p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yxx) * p3.zyx);
            }

            float Hash13(float3 p3)
            {
                p3 = frac(p3 * 0.1031);
                p3 += dot(p3, p3.zyx + 31.32);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));
                float y0 = lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y);
                float y1 = lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y);
                return lerp(y0, y1, f.z);
            }

            float Fbm(float3 p, int octaves)
            {
                float sum = 0.0;
                float amp = 0.5;
                [unroll(5)]
                for (int o = 0; o < octaves; o++)
                {
                    sum += amp * ValueNoise(p);
                    p = p * 2.03 + 11.7;
                    amp *= 0.5;
                }
                return sum;
            }

            float Lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            float3 RotateY(float3 v, float angleDeg)
            {
                float s, c;
                sincos(radians(angleDeg), s, c);
                return float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
            }

            float3 StarColor(float r)
            {
                const float3 cool  = float3(0.62, 0.76, 1.00);
                const float3 white = float3(1.00, 0.97, 0.93);
                const float3 warm  = float3(1.00, 0.76, 0.52);
                float3 c = lerp(cool, white, smoothstep(0.0, 0.6, r));
                return lerp(c, warm, smoothstep(0.85, 1.0, r));
            }

            static const float3 kCellOffsets[8] =
            {
                float3(0, 0, 0), float3(1, 0, 0), float3(0, 1, 0), float3(1, 1, 0),
                float3(0, 0, 1), float3(1, 0, 1), float3(0, 1, 1), float3(1, 1, 1)
            };

            // Один слой звёзд. Направление разбивается на 3D-сетку, в каждой ячейке может быть
            // одна звезда. Проверяем 2x2x2 ближайших ячейки, поэтому звёзды не обрезаются по краям.
            // pixelAngle — угловой размер пикселя: не даём звезде стать меньше пикселя (нет мерцания-лесенки).
            float3 StarLayer(float3 dir, float pixelAngle, float scale, float density, float2 sizeRange,
                             float brightness, float magPow, float halo, float time, float twinkleAmount)
            {
                float3 p = dir * scale;
                float3 baseCell = floor(p - 0.5);
                float footprint = pixelAngle * scale;
                float3 acc = 0;

                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    float3 cell = baseCell + kCellOffsets[k];
                    float presence = Hash13(cell + 17.31);
                    if (presence < density)
                    {
                        float3 jitter = Hash33(cell);
                        float r2 = Hash13(cell + 41.7);
                        float r3 = Hash13(cell + 73.13);
                        float r4 = Hash13(cell + 5.79);

                        float dist = length(p - (cell + jitter));
                        float size = lerp(sizeRange.x, sizeRange.y, r2 * r2);
                        float radius = max(size, footprint * 0.5);
                        float x = dist / radius;
                        float sizeRatio = size / radius;

                        float magnitude = 0.04 + 0.96 * pow(r3, magPow);
                        float core = sizeRatio * sizeRatio * exp(-0.5 * x * x);
                        float glow = halo * 0.04 * exp(-0.125 * x * x) * magnitude;

                        float twinkle = 1.0 - twinkleAmount * (0.5 + 0.5 * sin(time * (0.6 + 1.4 * r2) + r3 * 43.0));

                        acc += StarColor(r4) * (brightness * magnitude * (core + glow) * twinkle);
                    }
                }
                return acc;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.dirWS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 dir = normalize(input.dirWS);
                float pixelAngle = max(length(ddx(dir)), length(ddy(dir)));

                // 1. Облака и луна
                float3 cloudDir = RotateY(dir, -_CloudRotation);
                float3 sky = SAMPLE_TEXTURECUBE(_CloudCube, sampler_CloudCube, cloudDir).rgb;
                float3 skyBlur = SAMPLE_TEXTURECUBE_LOD(_CloudCube, sampler_CloudCube, cloudDir, _CloudMaskMip).rgb;
                float L = Lum(sky);
                float Lblur = Lum(skyBlur);

                float3 color = sky * _CloudExposure * _CloudTint.rgb;
                color *= 1.0 + _HighlightBoost * smoothstep(0.12, 0.6, L);

                // 4. Свечение у горизонта
                color += _AirglowColor.rgb * _AirglowIntensity * exp(-abs(dir.y) / _AirglowWidth);

                // Насколько звёзды видны в этой точке: ниже горизонта нет, за облаками и рядом с луной — меньше
                float horizon = smoothstep(_HorizonFade.x, _HorizonFade.y, dir.y);
                float cloudRatio = L / max(Lblur, 1e-5);
                float visibility = horizon;
                visibility *= 1.0 - _CloudOcclusion * smoothstep(_CloudRatio.x, _CloudRatio.y, cloudRatio);
                visibility *= 1.0 - smoothstep(_MoonMask.x, _MoonMask.y, Lblur);

                if (visibility > 0.001)
                {
                    float time = _Time.y * _TwinkleSpeed;
                    float twinkleAmount = _TwinkleAmount * lerp(1.0, 0.5, saturate(dir.y));

                    float band = 0.0;
                    float3 milkyWay = 0;

                #if defined(_MILKYWAY_ON)
                    // 3. Млечный Путь
                    float3 bandNormal = normalize(_MilkyWayNormal.xyz);
                    float h = dot(dir, bandNormal);
                    float w = _MilkyWayWidth;
                    band = exp(-h * h / (2.0 * w * w));
                    if (band > 0.002)
                    {
                        float clouds = Fbm(dir * 3.0, 5);
                        float dust = Fbm(dir * 8.0 + 13.0, 4);
                        float grain = Fbm(dir * 45.0 + 3.1, 3);
                        float mw = band * max(clouds * 1.6 - 0.35, 0.0);
                        mw *= 1.0 - _MilkyWayDust * smoothstep(0.42, 0.62, dust) * band;
                        mw *= 0.55 + 0.9 * smoothstep(0.35, 0.75, grain);
                        float wc = w * 0.45;
                        float core = exp(-h * h / (2.0 * wc * wc));
                        milkyWay = lerp(_MilkyWayColor.rgb, _MilkyWayCoreColor.rgb, core) * mw * _MilkyWayIntensity;
                    }
                #endif

                    // 2. Звёзды
                    float3 stars = StarLayer(dir, pixelAngle, _StarScaleA, _StarDensityA + _MilkyWayStarBoost * band,
                                             _StarSizeA.xy, _StarBrightnessA, _StarMagPowA, 0.0, time, twinkleAmount);
                    stars += StarLayer(dir, pixelAngle, _StarScaleB, _StarDensityB,
                                       _StarSizeB.xy, _StarBrightnessB, _StarMagPowB, _StarHaloB, time * 0.7, twinkleAmount * 0.6);

                    color += (stars + milkyWay) * visibility;
                }

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
