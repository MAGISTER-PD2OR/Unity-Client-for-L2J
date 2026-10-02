#ifndef L2_TERRAIN_COMMON_INCLUDED
#define L2_TERRAIN_COMMON_INCLUDED

// Color UV: world XZ in L2 UU / 128 / UScale, plus TerrainMatrix WPlane (xy of _Layer_UV).
// Splat UV: mesh UV0 rotated 90° around 0.5, then (i + 0.5) / 256.
// GenerateTriangles writes mask UV as (globalVertex + 0.5) / mapSize. Map size is 256.
// Stored UV is i/254, including the extra edge vertex i=255.
// Overlay alphamap: Texture2DArray slice i = layer i+1, mask in .r.
//
// UnTerrain.cpp:
//   TriangulateLayer(pass) — one triangle list per layer, not the full tile.
//   PassShouldRenderTriangle + IsTriangleAll(this, 0) — skip if this layer is 0.
//   IsTriangleAll(later, 255) — skip if a later layer is fully opaque.
//   GetLayerAlpha / GetAlpha_DXT — raw 0..255, no extra scale.
// Pixel version of that cull (full mesh still submitted). Slice 2 = SL_WR, skipped.

#define L2_TERRAIN_SPLAT_SLICES 9
#define L2_TERRAIN_WATER_SLICE 2

// Sun grid from L2unpacker Output/ExtractStoredIntensity.java (sector windows).
// The ray rebake (terrain-normals) is the emergency fallback only.
float4 _L2TerrainIntensityOrigin;
float4 _L2TerrainIntensityOrigin1;
float4 _L2TerrainIntensityOrigin2;
TEXTURE2D(_L2TerrainIntensityMap);
TEXTURE2D(_L2TerrainIntensityMap1);
TEXTURE2D(_L2TerrainIntensityMap2);

void L2_TerrainIntensityCell(float2 worldUu, float4 origin, out float gx, out float gy, out float grid)
{
    float cell = max(origin.z, 1.0);
    grid = max(origin.w, 1.0);
    gx = (worldUu.y - origin.x) / cell;
    gy = (worldUu.x - origin.y) / cell;
}

bool L2_TerrainIntensityInside(float gx, float gy, float grid)
{
    return gx >= 0.0 && gy >= 0.0 && gx <= grid - 1.0 && gy <= grid - 1.0;
}

float4 L2_TerrainIntensitySample(float2 worldUu)
{
    float gx;
    float gy;
    float grid;
    L2_TerrainIntensityCell(worldUu, _L2TerrainIntensityOrigin, gx, gy, grid);
    if (L2_TerrainIntensityInside(gx, gy, grid))
    {
        float2 uv = (float2(gx, gy) + 0.5) / grid;
        return SAMPLE_TEXTURE2D_LOD(_L2TerrainIntensityMap, sampler_PointClamp, uv, 0);
    }

    if (_L2TerrainIntensityOrigin1.w > 1.0)
    {
        L2_TerrainIntensityCell(worldUu, _L2TerrainIntensityOrigin1, gx, gy, grid);
        if (L2_TerrainIntensityInside(gx, gy, grid))
        {
            float2 uv = (float2(gx, gy) + 0.5) / grid;
            return SAMPLE_TEXTURE2D_LOD(_L2TerrainIntensityMap1, sampler_PointClamp, uv, 0);
        }
    }

    if (_L2TerrainIntensityOrigin2.w > 1.0)
    {
        L2_TerrainIntensityCell(worldUu, _L2TerrainIntensityOrigin2, gx, gy, grid);
        if (L2_TerrainIntensityInside(gx, gy, grid))
        {
            float2 uv = (float2(gx, gy) + 0.5) / grid;
            return SAMPLE_TEXTURE2D_LOD(_L2TerrainIntensityMap2, sampler_PointClamp, uv, 0);
        }
    }

    L2_TerrainIntensityCell(worldUu, _L2TerrainIntensityOrigin, gx, gy, grid);
    float2 fallbackUv = (float2(gx, gy) + 0.5) / grid;
    return SAMPLE_TEXTURE2D_LOD(_L2TerrainIntensityMap, sampler_PointClamp, fallbackUv, 0);
}

float L2_TerrainIntensityAt(float2 worldUu)
{
    return L2_TerrainIntensitySample(worldUu).r;
}

float2 L2_TerrainIntensityUv(float2 worldUu)
{
    float gx;
    float gy;
    float grid;
    L2_TerrainIntensityCell(worldUu, _L2TerrainIntensityOrigin, gx, gy, grid);
    return (float2(gx, gy) + 0.5) / grid;
}

// Blue: cell has no capture, brightness is the LUT fill.
// Dark red: cell was captured and its intensity byte is 0.
// Green: cell was captured with a nonzero byte. Brighter means a larger byte.
float3 L2_TerrainLightDebugColor(float2 worldUu)
{
    float4 sampleUv = L2_TerrainIntensitySample(worldUu);
    if (sampleUv.g < 0.5)
        return float3(0.12, 0.35, 0.95);
    if (sampleUv.r < (0.5 / 255.0))
        return float3(0.55, 0.04, 0.04);
    return float3(0.05, saturate(sampleUv.r * 2.0), 0.06);
}

float2 L2_TerrainLayerUv(float2 worldUu, float4 uvScale, float tileUu)
{
    float tile = max(tileUu, 1.0);
    float2 s = uvScale.xy;
    if (s.x < 0.001)
        s.x = 1;
    if (s.y < 0.001)
        s.y = 1;
    // L2 world X is Unity world Z. Grass grain follows that swap on every layer.
    return worldUu.yx / tile / s + uvScale.zw;
}

float2 L2_TerrainSplatUv(float2 meshUv)
{
    float2 uv = meshUv - 0.5;
    uv = float2(uv.y, -uv.x);
    uv += 0.5;
    return (uv * 254.0 + 0.5) / 256.0;
}

#endif
