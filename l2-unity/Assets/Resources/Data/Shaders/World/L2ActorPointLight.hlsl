#ifndef L2_ACTOR_POINT_LIGHT_INCLUDED
#define L2_ACTOR_POINT_LIGHT_INCLUDED

// В оригинале регистров пять: c222–c226, c227–c231, c232–c236.
// В снимке той же комнаты (20260929_165643) живым был только первый:
// c227 = 0.131 0.105 0.075, а c228–c231 были нули. Остальные четыре
// слота в кадре не применялись. Пока в сумму входит только слот 0,
// цвета 1–4 забиты нулём. Массив оставлен на пять, если кадр их покажет.
// Pos.xyz is world meters. Color.rgb is the ready client color (no extra 0.7).
// Atten.xyz is constant, linear-per-meter, radius in meters. Radius 0 is an empty slot.
// Linear is c232.y * 52.5 because distance here is meters, not L2 units.
float4 _L2ActorPointPos[5];
float4 _L2ActorPointColor[5];
float4 _L2ActorPointAtten[5];

float3 L2_ActorOnePoint(float4 pos, float4 color, float4 atten, float3 worldPos, float3 n)
{
    float radius = atten.z;
    float3 toLight = pos.xyz - worldPos;
    float dist2 = max(dot(toLight, toLight), 1e-10);
    float dist = sqrt(dist2);
    float3 dir = toLight / max(dist, 1e-5);
    float ndotl = saturate(dot(n, dir));
    float inside = step(dist, radius) * step(0.001, radius);
    float denom = max(atten.x + atten.y * dist, 1e-5);
    return color.rgb * ndotl * (inside / denom);
}

float3 L2_ActorPointSum(float3 worldPos, float3 worldNormal)
{
    float3 n = normalize(worldNormal);
    // Слоты 1–4 не складываются: в оригинале они есть, применения не видели.
    return L2_ActorOnePoint(_L2ActorPointPos[0], _L2ActorPointColor[0], _L2ActorPointAtten[0], worldPos, n);
}

#endif
