#ifndef L2_MODULATE2X_LIGHTING_INCLUDED
#define L2_MODULATE2X_LIGHTING_INCLUDED

// D3D9 FF: saturate(tex * Color0 * 2), then linear fog.
// Buildings: Color0 = vertex * (ambient>>1). Unchanged here.
// Terrain Color0 (UTerrainSector::SetIntensityMap):
//   (TerrainAmbient>>1) + HSVTerrainSunLight * 0.5 * (intensityByte/255)
// Vertex paint on L2 terrain is 0; Unity default white is ignored.

float4 _L2TerrainAmbient;
float4 _L2StaticMeshAmbient;
float4 _L2HsvTerrainLight;
float4 _L2HsvStaticMeshLight;
float4 _L2FogColor;
float _L2FogEnd;
float _L2FogScale;
float _L2FogEnable;
float _L2TerrainColor0Scale;
float _L2TerrainIntensity;
float _L2StaticMeshMaskSlot0;
float _L2StaticMeshMaskSlot1;
float _L2StaticMeshMaskBlend;
float _L2StaticMeshForceSunVisible;
float _L2SunMaskDebug;
float _L2TerrainLightDebug;
float4 _L2StaticMeshLightDirection;

float3 L2_OrWhite(float3 c)
{
    return max(max(c.r, c.g), c.b) < 0.001 ? float3(1, 1, 1) : c;
}

float L2_TerrainColor0Scale()
{
    return _L2TerrainColor0Scale > 0.001 ? _L2TerrainColor0Scale : 1.0;
}

// Static mesh Color0: (StaticMeshAmbient >> 1) + trunc(HSVStaticMeshSunLight * 32) + vertex.
// Plane is _L2HsvStaticMeshLight. 32 is dword_20DB5A44. Walls do not use this.
float3 L2_StaticMeshColor0(float3 vertexColor, float3 ambient)
{
    float3 sun = trunc(_L2HsvStaticMeshLight.rgb * 32.0) * (1.0 / 255.0);
    return saturate(vertexColor + ambient * 0.5 + sun);
}

float L2_StaticMeshMaskBit(float packedByte01, float slot)
{
    float packedByte = floor(saturate(packedByte01) * 255.0 + 0.5);
    return fmod(floor(packedByte / exp2(slot)), 2.0);
}

// Exact UStaticMesh::InterpolateSunLight contribution. Each selected slot is
// truncated independently to an FColor byte before the two colors are added.
float3 L2_StaticMeshMaskedSun(float packedByte01, float3 worldNormal)
{
    float visible0 = L2_StaticMeshMaskBit(packedByte01, _L2StaticMeshMaskSlot0);
    float visible1 = L2_StaticMeshMaskBit(packedByte01, _L2StaticMeshMaskSlot1);
    float lightDot = dot(
        normalize(worldNormal),
        normalize(_L2StaticMeshLightDirection.xyz));
    float intensity = max(0.0, -lightDot);
    float weight1 = saturate(_L2StaticMeshMaskBlend);
    float weight0 = 1.0 - weight1;
    float3 add0 = floor(
        _L2HsvStaticMeshLight.rgb * weight0 * intensity * 255.0) * visible0;
    float3 add1 = floor(
        _L2HsvStaticMeshLight.rgb * weight1 * intensity * 255.0) * visible1;
    return (add0 + add1) * (1.0 / 255.0);
}

// Trees use the same directional sun as static meshes, with every slot visible.
// Original foliage does not read the eight ActorStaticLight mask bits.
float3 L2_StaticMeshDirectSun(float3 worldNormal)
{
    return L2_StaticMeshMaskedSun(1.0, worldNormal);
}

// Debug view for the eight baked sun-visibility bits.
// Dark red: vertex has no mask bits.
// Blue: some bits exist, but neither active slot is set.
// Yellow: an active slot is set, but the face points away from the sun.
// Green: direct sun is applied; brighter means a larger vertex byte.
float3 L2_StaticMeshSunDebugColor(float packedByte01, float3 sun)
{
    float packed = floor(saturate(packedByte01) * 255.0 + 0.5);
    float slotHit = max(
        L2_StaticMeshMaskBit(packedByte01, _L2StaticMeshMaskSlot0),
        L2_StaticMeshMaskBit(packedByte01, _L2StaticMeshMaskSlot1));
    float sunAmount = max(sun.r, max(sun.g, sun.b));
    if (packed < 0.5)
        return float3(0.35, 0.0, 0.0);
    if (slotHit < 0.5)
        return float3(0.0, 0.16, 0.9);
    if (sunAmount < (0.5 / 255.0))
        return float3(0.95, 0.72, 0.05);
    return float3(0.05, saturate(sunAmount * 4.0), 0.08);
}

// SetIntensityMap writes Color0 per vertex, then the GPU interpolates it.
// The captured vertex at intensity 130 is R←plane.X, G←plane.Y, B←plane.Z.
// Ambient is a byte shifted right by 1.
// intensity01 is the spatial byte / 255. The scalar _L2TerrainIntensity is only a fallback.
float3 L2_TerrainColor0(float3 vertexColor, float3 ambient, float intensity01)
{
    float i = saturate(intensity01);
    float ib = i * 255.0;
    float along = ib / 255.0;
    float3 plane = _L2HsvTerrainLight.rgb;
    float3 sunByte = trunc(plane.rgb * 0.5 * along * 255.0);
    float3 ambByte = floor(saturate(ambient) * 255.0 + 0.5);
    float3 ambShift = floor(ambByte * 0.5);
    float3 vtx = vertexColor;
    if (min(vtx.r, min(vtx.g, vtx.b)) > 0.98)
        vtx = 0;
    float3 baseByte = floor(saturate(vtx) * 255.0 + 0.5);
    float3 color0 = min(baseByte + sunByte + ambShift, 255.0) * (1.0 / 255.0);
    return saturate(color0 * L2_TerrainColor0Scale());
}

float3 L2_Modulate2X(float3 albedo, float3 color0)
{
    return saturate(albedo * color0 * 2.0);
}

float3 L2_LinearFog(float3 rgb, float viewAbsZ)
{
    if (_L2FogEnable > 0.5)
    {
        float fogEnd = _L2FogEnd > 0.0 ? _L2FogEnd : (12288.0 / 52.5);
        float fogScale = _L2FogScale > 0.0 ? _L2FogScale : (52.5 / 8192.0);
        float f = saturate((fogEnd - viewAbsZ) * fogScale);
        rgb = lerp(_L2FogColor.rgb, rgb, f);
    }
    return rgb;
}

float3 L2_Modulate2XFog(float3 albedo, float3 vertexColor, float3 ambient, float viewAbsZ, float intensity01)
{
    return L2_LinearFog(L2_Modulate2X(albedo, L2_TerrainColor0(vertexColor, ambient, intensity01)), viewAbsZ);
}

#endif
