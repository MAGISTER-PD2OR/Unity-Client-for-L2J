#ifndef L2_WATER_COMMON_INCLUDED
#define L2_WATER_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "L2Modulate2XLighting.hlsl"

// Ertheia_water_sh, from Engine UTexOscillator::GetMatrix / UTexture::Tick.
// textureFactor is full BSPAmbient (not >>1). Color0 is unused.
// t0 = ocean-s (V pan, frozen after 1s, then ocn_rot). t1 and t3 = TexOscillator0.
// t2 and t4 = base1. Output alpha is t4.a * BSPAmbient.a (not *2).
float4 _L2BspAmbient;

TEXTURE2D(_WaterMap);
SAMPLER(sampler_WaterMap);
TEXTURE2D(_DetailMap);
SAMPLER(sampler_DetailMap);

CBUFFER_START(UnityPerMaterial)
    float4 _WaterMap_ST;
    float4 _DetailMap_ST;
    float _Cull;
    float _Cutoff;
CBUFFER_END

#define L2_WATER_PI 3.14159265
#define L2_WATER_TAU 6.28318530

float4 L2_WaterFactor()
{
    float4 bsp = _L2BspAmbient;
    bsp.rgb = L2_OrWhite(bsp.rgb);
    if (bsp.a < 0.001)
        bsp.a = 1.0;
    return bsp;
}

// Ertheia_watersheet01 is 1000x1000 UU with UV 0-1.
// Unity X = UE Y, Unity Z = UE X. On that sheet u grows toward -Z and v toward +X.
// WaterSurface.mesh UVs are 0-1 across the whole sea, so mesh UV would stretch one frame
// over the map. This scale is one ocean tile per 1000 UU. The fragment keeps the
// fractional tile, then runs pan and ocn_rot inside it.
float2 L2_WaterSheetUv(float3 positionWS)
{
    return float2(-positionWS.z, positionWS.x) * (52.5 / 1000.0);
}

// OT_Pan. Phase is in cycles. sin(2*pi*(phase + frac(time*rate))) * amplitude.
float L2_WaterPan(float time, float rate, float phase, float amplitude)
{
    float cycle = frac(phase + time * rate);
    return amplitude * sin(L2_WATER_TAU * cycle);
}

// OT_StretchRepeat. Phase uses 2*pi, the fractional time uses pi.
// scale = 1 + amplitude * sin(phase*2*pi + frac(time*rate)*pi)
float L2_WaterStretchRepeat(float time, float rate, float phase, float amplitude)
{
    float s = sin(phase * L2_WATER_TAU + frac(time * rate) * L2_WATER_PI);
    return 1.0 + amplitude * s;
}

// TexOscillator0: both axes OT_Pan, rate 0.1, phase 0.1, amplitude 0.05. Offsets 0.
float2 L2_WaterOscillator0(float2 uv, float time)
{
    float o = L2_WaterPan(time, 0.1, 0.1, 0.05);
    return uv + o;
}

// ocean-s live GetMatrix (WaterLog 2026-09-22):
// U OT_StretchRepeat rate 0 amp 0, so U stays 1.
// V OT_Pan rate 0.05 phase 0.3 amp 0.05.
// Triggered clamp: StopAfterPeriod 0.05 / max(rate) 0.05 = 1s, then the
// matrix freezes. At t=1, ZPlane.Y = 0.040451.
// UOffset/VOffset 10 stay on the object and are not in the returned matrix.
float2 L2_WaterOceanS(float2 uv, float time)
{
    float t = min(time, 1.0);
    float ov = L2_WaterPan(t, 0.05, 0.3, 0.05);
    return float2(uv.x, uv.y + ov);
}

// ocn_rot Yaw=4096. Saved matrix, row-vector (u,v) * R.
float2 L2_WaterRotate(float2 uv)
{
    const float c = 0.923880;
    const float s = 0.382683;
    return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
}

// ocean_atlas.png: 5 columns, 4 rows, frame 0 is ocean011 at the top of the PNG.
float2 L2_WaterAtlasUv(float2 uv, float time)
{
    float2 g = frac(uv);
    float frame = floor(time * 9.0);
    frame = frame - 20.0 * floor(frame / 20.0);
    float col = frame - 5.0 * floor(frame / 5.0);
    float row = floor(frame / 5.0);
    return float2((col + g.x) / 5.0, 1.0 - (row + 1.0 - g.y) / 4.0);
}

// Stage alpha through base1 weights the third ocean add. Output alpha is base1.a.
half4 L2_WaterCombine(half4 c0, half4 c1, half4 baseMap, half4 c3, float4 factor)
{
    half3 rgb = c0.rgb * c1.rgb;
    half aw = c0.a;
    rgb = saturate(rgb + baseMap.rgb);
    aw *= baseMap.a;
    rgb = saturate(rgb + aw * c3.rgb);
    half a = baseMap.a * (half)factor.a;
    rgb = saturate(rgb * (half3)factor.rgb * (half)2.0);
    return half4(rgb, a);
}

#endif
