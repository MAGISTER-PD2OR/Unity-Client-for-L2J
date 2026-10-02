Shader "Hidden/L2/YebisPost"
{
    Properties
    {
        _BloomThreshold ("Bloom Threshold", Float) = 0.66
        _BloomRemap ("Bloom Remap", Float) = 2
        _BloomIntensity ("Bloom Intensity", Float) = 0.18
        _KawaseOffset ("Kawase Offset", Float) = 1
        _AdaptLerp ("Adapt Lerp", Float) = 0.08
        _YebisDebug ("Debug", Float) = 0
        _Exposure ("Exposure", Float) = 0.5
        _UseExposure ("Use Exposure", Float) = 1
        _Mapping0 ("Mapping0", Float) = 18
        _MiddleGray ("Middle Gray", Float) = 0.18
        _AdaptationScale ("Adaptation Scale", Float) = 0.85
        _PrePostLumaScale ("PrePost Luma Scale", Float) = 1
        _AdaptedOverride ("Adapted Override", Float) = 0
        _ColorGamma ("Color Gamma", Float) = 1.6
        _ColorSaturation ("Saturation", Float) = 0.95
        _FilmicA ("Filmic A", Float) = 2.6581414
        _FilmicB ("Filmic B", Float) = 0.66499311
        _MatrixBias ("Matrix Bias", Float) = 0.005
        _GainMin ("Gain Min", Float) = 0.01
        _GainMax ("Gain Max", Float) = 8
        _ContrastMix ("Contrast Mix", Float) = 1
        _Pre3898 ("Pre 3898", Float) = 1
        _CombineGain ("Combine Gain", Float) = 4.7551184
        _FirstMatrix ("First Matrix", Float) = 1
        _SecondMatrix ("Second Matrix", Float) = 1
        _DisplayGamma ("Display Gamma", Float) = 1
        _CaptureFormula ("Capture Formula", Float) = 1
        _GradeStage ("Grade Stage", Float) = 0
    }
    // Live grade is the confirmed combine draw: first matrix, filmic, black-glare bias, gamma 0.625.
    // _CaptureFormula = 0 restores the older exposure / Pre3898 / SatMix assembly.
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_BloomTex);
        TEXTURE2D(_AdaptedTex);
        TEXTURE2D(_BloomExcludeMask);

        float _BloomThreshold;
        float _BloomRemap;
        float _BloomIntensity;
        float _BloomExclude;
        float _KawaseOffset;
        float4 _KawaseTexelSize;
        float _AdaptLerp;
        float _YebisDebug;
        float _Exposure;
        float _UseExposure;
        float _Mapping0;
        float _MiddleGray;
        float _AdaptationScale;
        float _PrePostLumaScale;
        float _AdaptedOverride;
        float _ColorGamma;
        float _ColorSaturation;
        float _FilmicA;
        float _FilmicB;
        float _MatrixBias;
        float _GainMin;
        float _GainMax;
        float _ContrastMix;
        float _Pre3898;
        float _CombineGain;
        float _FirstMatrix;
        float _SecondMatrix;
        float _DisplayGamma;
        float _CaptureFormula;
        float _GradeStage;
        float _LumaGrid;

        float L2Yebis_Luma(float3 c)
        {
            return dot(c, float3(0.299, 0.587, 0.114));
        }

        float2 L2Yebis_BlitUV(float2 uv)
        {
            return uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
        }

        float L2Yebis_GridLuma(int n)
        {
            float acc = 0.0;
            float inv = 1.0 / n;
            [unroll]
            for (int y = 0; y < n; y++)
            {
                [unroll]
                for (int x = 0; x < n; x++)
                {
                    float2 uv = L2Yebis_BlitUV(float2((x + 0.5) * inv, (y + 0.5) * inv));
                    acc += L2Yebis_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb);
                }
            }
            return acc / (n * n);
        }

        float3 L2Yebis_SatMix(float3 c, float sat)
        {
            float luma = L2Yebis_Luma(c);
            return lerp(luma.xxx, c, sat);
        }

        float L2Yebis_Gain(float adapted)
        {
            // Absolute gain. At middle gray 0.18 this is about 9, then clamped to _GainMax.
            // Do not multiply this on top of the 4.75 matrix. That double scale was the hall hack.
            // The exposure test uses L2Yebis_ExposureScale, the ratio of this gain to the middle-gray gain.
            float adaptedUse = _AdaptedOverride > 0.01 ? _AdaptedOverride : adapted;
            float luma = max(adaptedUse, 0.02) * max(_PrePostLumaScale, 1e-3);
            float effective = lerp(_MiddleGray, luma, saturate(_AdaptationScale));
            float gain = _Exposure * _Mapping0 * _MiddleGray / max(effective, 0.02);
            return clamp(gain, _GainMin, _GainMax);
        }

        // 1 at middle gray and for anything brighter. Darker frames (room, night) go above 1.
        // The whole-frame average includes the sky. A scale below 1 darkened the ground
        // when looking up and when sunlit canopy filled the meter between trees.
        // fExposure and fMappingFactor cancel in the ratio.
        float L2Yebis_ExposureScale(float adapted)
        {
            float adaptedUse = _AdaptedOverride > 0.01 ? _AdaptedOverride : adapted;
            float luma = max(adaptedUse, 0.02) * max(_PrePostLumaScale, 1e-3);
            float effective = lerp(_MiddleGray, luma, saturate(_AdaptationScale));
            float refLuma = max(_MiddleGray, 0.02) * max(_PrePostLumaScale, 1e-3);
            float effectiveRef = lerp(_MiddleGray, refLuma, saturate(_AdaptationScale));
            return max(effectiveRef / max(effective, 0.02), 1.0);
        }

        // EID 3898, c.f[0]=(-1.96,10,0.01,5) c.f[1]=(1,1,1,1). Pixel-matched to village PNG.
        float3 L2Yebis_Pre3898(float3 scene)
        {
            float3 r1 = (scene - 1.96) * scene + 1.0;
            float3 t = sqrt(abs(r1));
            return 5.0 * (scene + t - 1.0);
        }

        // EID 4833 f[4..6] before filmic. Green diag 4.88 — grass lift vs scalar 4.76.
        float3 L2Yebis_FirstMatrix(float3 c)
        {
            float3 o;
            o.r = dot(c, float3(4.7551184, 0.17701194, 0.0178695)) + _MatrixBias;
            o.g = dot(c, float3(0.052618481, 4.8795118, 0.0178695)) + _MatrixBias;
            o.b = dot(c, float3(0.052618481, 0.17701194, 4.7203693)) + _MatrixBias;
            return o;
        }

        float3 L2Yebis_Filmic(float3 boosted)
        {
            // EID 4833/4524: y = sat((1-e) * (1-b*e)^2), e = exp(-A * max(x,0))
            // A=2.658 B=0.665 from c.f[14], not a pixel-fit 0.4.
            float3 x = max(boosted, 0.0);
            float3 e = exp(-_FilmicA * x);
            float3 oneMinusBe = 1.0 - _FilmicB * e;
            return saturate((1.0 - e) * oneMinusBe * oneMinusBe);
        }

        // EID 4833 f[7..9] after filmic, before gamma. r1.w=1 so .w is +0.005 bias.
        float3 L2Yebis_SecondMatrix(float3 c)
        {
            float3 o;
            o.r = dot(c, float3(0.9510237, 0.035402387, 0.0035739001)) + 0.005;
            o.g = dot(c, float3(0.010523696, 0.97590238, 0.0035739001)) + 0.005;
            o.b = dot(c, float3(0.010523696, 0.035402387, 0.94407392)) + 0.005;
            return saturate(o);
        }

        // Client draw 0A08B2C83DB6048D, before combine. Not part of the combine shader.
        // c0 = (-1.96, 10, 0.01, 5), c1 = (1,1,1,1). Checked on the saved 8-bit input
        // against the saved half-float output, max abs under 0.0005.
        float3 L2Yebis_SceneToFloat(float3 scene)
        {
            const float c0x = -1.96000004;
            const float c0w = 5.0;
            float3 r1 = scene + c0x;
            float3 r2 = scene;
            r1 = r1 * r2 + 1.0;
            r2 = sqrt(r1);
            return (scene + r2 - 1.0) * c0w;
        }

        // Client draw 194B515BA980A639, checked against the saved backbuffer to 1 byte level.
        // scene -> c4-c6 -> filmic c14 -> second matrix of glare -> F+(1-F)*G -> pow 0.625.
        // This pass has no client glare texture. Black glare still adds the 0.005 bias.
        // Kawase bloom stays a later add and is not this glare slot.
        void L2Yebis_CaptureParts(float3 scene, out float3 mapped, out float3 beforePow, out float3 graded)
        {
            const float kFloor = 5.96046448e-8;
            const float bias = 0.00499999523;
            float4 sceneH = float4(scene, 1.0);
            float3 x;
            x.r = dot(sceneH, float4(4.75511837, 0.177011937, 0.0178695004, bias));
            x.g = dot(sceneH, float4(0.0526184812, 4.87951183, 0.0178695004, bias));
            x.b = dot(sceneH, float4(0.0526184812, 0.177011937, 4.72036934, bias));
            mapped = max(x, kFloor);

            const float filmicA = 2.65814137;
            const float filmicB = 0.664993107;
            float3 e = exp(-filmicA * mapped);
            float3 oneMinusBe = 1.0 - filmicB * e;
            float3 filmic = saturate((1.0 - e) * oneMinusBe * oneMinusBe);

            float4 glareH = float4(0.0, 0.0, 0.0, 1.0);
            float3 g;
            g.r = saturate(dot(glareH, float4(0.951023698, 0.0354023874, 0.00357390009, bias)));
            g.g = saturate(dot(glareH, float4(0.0105236964, 0.975902379, 0.00357390009, bias)));
            g.b = saturate(dot(glareH, float4(0.0105236964, 0.0354023874, 0.944073915, bias)));
            beforePow = filmic + (1.0 - filmic) * g;

            float3 safe = max(beforePow, kFloor);
            graded = saturate(exp2(log2(safe) * 0.625));
        }

        float3 L2Yebis_GradeCapture(float3 scene)
        {
            float3 mapped, beforePow, graded;
            L2Yebis_CaptureParts(scene, mapped, beforePow, graded);
            return graded;
        }

        // 1 = sample, 2 = matrix, 3 = value that enters the power, 4 = after the power.
        // Coefficients stay the confirmed combine. 0 keeps L2Yebis_GradeCapture.
        float3 L2Yebis_CaptureStage(float3 scene, float stage)
        {
            float3 mapped, beforePow, graded;
            L2Yebis_CaptureParts(scene, mapped, beforePow, graded);
            if (stage < 1.5)
                return scene;
            if (stage < 2.5)
                return mapped;
            if (stage < 3.5)
                return beforePow;
            return graded;
        }

        // Previous live assembly. Exposure, Pre3898, SatMix 0.95, then the second matrix
        // on the graded scene instead of on a glare texture.
        float3 L2Yebis_GradeLegacy(float3 scene, float adapted)
        {
            // Test 2026-09-27. Before: adapted luma was measured and ignored, scene entered the curve as-is.
            // Now: scale by exposure relative to middle gray 0.18, then the same matrix and filmic.
            // _UseExposure = 0 restores that old fixed grade.
            float3 exposed = _UseExposure > 0.5 ? scene * L2Yebis_ExposureScale(adapted) : scene;
            float3 working = _Pre3898 > 0.5 ? L2Yebis_Pre3898(exposed) : exposed;
            float3 sat = L2Yebis_SatMix(working, _ColorSaturation);
            // Test 2026-09-27. _FirstMatrix > 0.5 is the live client 3x3.
            // The old look is the other branch: sat * _CombineGain (4.7551184) on R, G and B.
            float3 boosted = _FirstMatrix > 0.5
                ? L2Yebis_FirstMatrix(sat)
                : sat * _CombineGain + _MatrixBias.xxx;
            float3 graded = L2Yebis_Filmic(boosted);
            if (_SecondMatrix > 0.5)
                graded = L2Yebis_SecondMatrix(graded);
            graded = saturate(lerp(scene, graded, saturate(_ContrastMix)));

            // f[12]=0.625 → pow(x, 1/1.6). Then Option.ini Gamma=0.8 → pow(x, 1/0.8).
            float g = max(_ColorGamma, 1e-3);
            graded = exp2(log2(max(graded, 1e-5)) * (1.0 / g));
            float dg = max(_DisplayGamma, 1e-3);
            if (dg < 0.999 || dg > 1.001)
                graded = exp2(log2(max(graded, 1e-5)) * (1.0 / dg));
            return saturate(graded);
        }

        float3 L2Yebis_Grade(float3 scene, float adapted)
        {
            if (_CaptureFormula > 0.5)
                return L2Yebis_GradeCapture(scene);
            return L2Yebis_GradeLegacy(scene, adapted);
        }

        half3 L2Yebis_BrightExtract(half3 c)
        {
            // Source is already graded. Threshold 0.66 = 0.6 + 10% cutoff.
            half luma = (half)L2Yebis_Luma(c);
            half knee = max(1e-4h, (half)(1.0 - _BloomThreshold));
            half w = saturate((luma - (half)_BloomThreshold) / knee);
            return saturate(c * w * (half)_BloomRemap);
        }
        ENDHLSL

        Pass
        {
            Name "ExtractBloom"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragExtract

            half4 FragExtract(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 graded = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 extracted = L2Yebis_BrightExtract(graded);
                if (_BloomExclude > 0.5)
                {
                    half mask = SAMPLE_TEXTURE2D(_BloomExcludeMask, sampler_LinearClamp, uv).r;
                    extracted *= 1.0h - saturate(mask);
                }
                return half4(extracted, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "KawaseBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragKawase

            half4 FragKawase(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 d = _KawaseTexelSize.xy * _KawaseOffset;
                half4 s = half4(0, 0, 0, 0);
                s += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( d.x,  d.y));
                s += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-d.x,  d.y));
                s += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( d.x, -d.y));
                s += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-d.x, -d.y));
                return s * 0.25h;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DownsampleLuma"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLuma

            half4 FragLuma(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_LumaGrid >= 2.0)
                {
                    float acc = L2Yebis_GridLuma(8);
                    return half4(acc, acc, acc, 1);
                }

                float2 uv = input.texcoord;
                float2 d = _KawaseTexelSize.xy * 0.5;
                float luma = 0.0;
                luma += L2Yebis_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-d.x, -d.y)).rgb);
                luma += L2Yebis_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( d.x, -d.y)).rgb);
                luma += L2Yebis_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-d.x,  d.y)).rgb);
                luma += L2Yebis_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( d.x,  d.y)).rgb);
                luma *= 0.25;
                return half4(luma, luma, luma, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "AdaptLuma"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAdapt

            half4 FragAdapt(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float current = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, L2Yebis_BlitUV(float2(0.5, 0.5))).r;
                float prev = SAMPLE_TEXTURE2D(_AdaptedTex, sampler_LinearClamp, float2(0.5, 0.5)).r;
                float adapted = lerp(prev, current, saturate(_AdaptLerp));
                return half4(adapted, adapted, adapted, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Combine"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCombine

            half4 FragCombine(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 bloom = SAMPLE_TEXTURE2D_X(_BloomTex, sampler_LinearClamp, uv).rgb;
                float adapted = SAMPLE_TEXTURE2D(_AdaptedTex, sampler_LinearClamp, float2(0.5, 0.5)).r;

                // 5 = 8x8 luma of the scene (bypasses the 1x1 chain).
                // 4 = adapted luma as gray. 3 = gain/8 as gray.
                if (_YebisDebug > 4.5)
                {
                    float live = L2Yebis_GridLuma(8);
                    return half4(live.xxx, 1);
                }
                if (_YebisDebug > 3.5)
                    return half4(adapted.xxx, 1);
                if (_YebisDebug > 2.5)
                    return half4((_CombineGain / 8.0).xxx, 1);

                // Grade only. Bloom is extracted from this buffer, then AddBloom.
                // Debug 1/2 must not run here or temp becomes black (bloom tex still empty).
                if (_CaptureFormula > 0.5 && _GradeStage > 0.5)
                    return half4(L2Yebis_CaptureStage(scene, _GradeStage), 1);
                return half4(L2Yebis_Grade(scene, adapted), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Copy"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCopy

            half4 FragCopy(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "AddBloom"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAddBloom

            half4 FragAddBloom(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 graded = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 bloom = SAMPLE_TEXTURE2D_X(_BloomTex, sampler_LinearClamp, uv).rgb;
                if (_YebisDebug > 1.5)
                    return half4(saturate(bloom), 1);
                return half4(saturate(graded + bloom * _BloomIntensity), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SceneToFloat"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSceneToFloat

            float4 FragSceneToFloat(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                return float4(L2Yebis_SceneToFloat(scene), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
