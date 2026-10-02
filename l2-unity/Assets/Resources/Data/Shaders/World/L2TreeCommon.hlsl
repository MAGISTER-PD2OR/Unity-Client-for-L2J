#ifndef L2_TREE_COMMON_INCLUDED
#define L2_TREE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "L2Modulate2XLighting.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseColor;
    float _Cull;
    float _Cutoff;
    float _L2HasSunMask;
CBUFFER_END

float2 L2_TreeUv(float2 uv)
{
    float4 st = _BaseMap_ST;
    if (abs(st.x) + abs(st.y) < 0.0001)
        st = float4(1, 1, 0, 0);
    return uv * st.xy + st.zw;
}

float4 L2_TreeVertexColor(float4 color)
{
    float3 vtx = color.rgb;
    if (max(max(vtx.r, vtx.g), vtx.b) < 0.001)
        vtx = float3(1, 1, 1);
    float vtxA = color.a;
    if (vtxA < 0.001)
        vtxA = 1.0;
    return float4(vtx, vtxA);
}

// Foliage Color0 = vertex * (StaticMeshAmbient>>1 + direct sun). GPU: tex * Color0 * 2.
float3 L2_TreeColorRgb(float3 texRgb, float3 vertexRgb, float3 sun, float viewAbsZ)
{
    float3 ambient = L2_OrWhite(_L2StaticMeshAmbient.rgb);
    float3 color0 = saturate(vertexRgb * (ambient * 0.5 + sun));
    return L2_LinearFog(L2_Modulate2X(texRgb * _BaseColor.rgb, color0), viewAbsZ);
}

#endif
