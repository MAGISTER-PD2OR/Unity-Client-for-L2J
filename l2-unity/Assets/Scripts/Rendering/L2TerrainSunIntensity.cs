using System;
using UnityEngine;

/// <summary>
/// ATerrainInfo intensity byte before the shadow ray.
/// GetSunLightDirection, GetSafeDirectionForIntMap, then the LightEffect 19
/// sample inside UTerrainSector::CalcLight. UE axes: X Y Z-up.
/// This class is not called. EMERGENCY ONLY: the same formula runs in
/// L2unpacker PackageCommands.writeIntensityGrid and TerrainSunShadow.
/// The grid Unity loads comes from Output/ExtractStoredIntensity.java
/// (L2TerrainIntensityGrid*_stored.bin). Do not rebake over those files.
/// </summary>
public static class L2TerrainSunIntensity
{
    public const float HoursPerDay = 24f;
    public const float DefaultAzimuthDegrees = 180f;
    public const float DefaultTiltDegrees = 30f;

    static readonly float PiOver18 = Bits(0x3E32B8C3);
    static readonly float PiOver6 = Bits(0x3F060A92);
    static readonly float HalfPi = Bits(0x3FC90FDB);
    static readonly float DegToRad = Bits(0x3C8EFA35);
    static readonly float SafeLengthSquared = Bits(0x322BCC77);
    static readonly float UpwardZClamp = Bits(0x38D1B717);

    static float Bits(uint value)
    {
        return BitConverter.Int32BitsToSingle(unchecked((int)value));
    }

    public static float SlotHours(int slot, int slotCount)
    {
        return HoursPerDay * (slot + 0.5f) / slotCount;
    }

    /// <summary>
    /// UL2NEnvManager::GetSunLightDirection. Azimuth and tilt are the
    /// fields at EnvManager+0x1B8 and +0x1BC, in degrees. Tilt is applied
    /// as a positive rotation about X.
    /// </summary>
    public static Vector3 SunDirection(float hours, float azimuthDegrees, float tiltDegrees)
    {
        float azimuth = azimuthDegrees * DegToRad;
        float elevation = hours >= 6f && hours < 24f
            ? (hours - 6f) * PiOver18 - HalfPi
            : hours * PiOver6 - HalfPi;

        float x = Mathf.Sin(elevation) * Mathf.Cos(azimuth);
        float y = Mathf.Sin(elevation) * Mathf.Sin(azimuth);
        float z = Mathf.Cos(elevation);

        float tilt = tiltDegrees * DegToRad;
        float cosTilt = Mathf.Cos(tilt);
        float sinTilt = Mathf.Sin(tilt);
        float rotatedY = y * cosTilt - z * sinTilt;
        float rotatedZ = y * sinTilt + z * cosTilt;
        return new Vector3(x, rotatedY, rotatedZ);
    }

    /// <summary>
    /// GetSafeDirectionForIntMap: negate the sun vector, keep Z from pointing
    /// upward, then normalize.
    /// </summary>
    public static Vector3 SafeDirection(float hours, float azimuthDegrees, float tiltDegrees)
    {
        Vector3 sun = SunDirection(hours, azimuthDegrees, tiltDegrees);
        float x = -sun.x;
        float y = -sun.y;
        float z = -sun.z;
        if (z > 0f)
            z = UpwardZClamp;

        float lengthSquared = x * x + y * y + z * z;
        if (lengthSquared < SafeLengthSquared)
            return new Vector3(0f, 0f, -1f);

        float inv = 1f / Mathf.Sqrt(lengthSquared);
        return new Vector3(x * inv, y * inv, z * inv);
    }

    /// <summary>
    /// CalcLight sunlight byte. q = dot(safeDirection, normal). The multiply
    /// order is sample * 255 * 0.5, then trunc toward zero.
    /// </summary>
    public static byte IntensityByte(Vector3 safeDirection, Vector3 normal)
    {
        float q = safeDirection.x * normal.x + safeDirection.y * normal.y + safeDirection.z * normal.z;
        float sample = q < 0f ? q * -2f : 0f;
        float scaled = sample * 255f * 0.5f;
        int truncated = (int)scaled;
        if (truncated > 255)
            truncated = 255;
        if (truncated < 0)
            truncated = 0;
        return (byte)truncated;
    }

    public static byte IntensityByte(int slot, int slotCount, Vector3 normal, float azimuthDegrees, float tiltDegrees)
    {
        Vector3 direction = SafeDirection(SlotHours(slot, slotCount), azimuthDegrees, tiltDegrees);
        return IntensityByte(direction, normal);
    }

    /// <summary>
    /// SetInterpolatedIntensityMap: trunc(A * (1 - alpha) + B * alpha).
    /// Alpha is (time - slotA) / (slotB - slotA), with the clock wrapped into
    /// the slot pair the same way UpdateShadow does.
    /// </summary>
    /// <summary>
    /// Client terrain vertex. R←HSV.X, G←Y, B←Z. Sun is trunc(plane * 0.5 * byte).
    /// Ambient is the byte shifted right by 1. A zero byte is ambient alone.
    /// </summary>
    public static void Color0Bytes(
        int intensityByte,
        float hsvX,
        float hsvY,
        float hsvZ,
        int ambientR,
        int ambientG,
        int ambientB,
        out int r,
        out int g,
        out int b)
    {
        int sunR = (int)(hsvX * 0.5f * intensityByte);
        int sunG = (int)(hsvY * 0.5f * intensityByte);
        int sunB = (int)(hsvZ * 0.5f * intensityByte);
        r = Mathf.Min(255, (ambientR >> 1) + sunR);
        g = Mathf.Min(255, (ambientG >> 1) + sunG);
        b = Mathf.Min(255, (ambientB >> 1) + sunB);
    }

    public static byte LerpByte(byte a, byte b, float alpha)
    {
        float mixed = a * (1f - alpha) + b * alpha;
        int truncated = (int)mixed;
        if (truncated > 255)
            truncated = 255;
        if (truncated < 0)
            truncated = 0;
        return (byte)truncated;
    }

    public static void SlotPair(float hours, int slotCount, out int slotA, out int slotB, out float alpha)
    {
        float wrapped = hours % HoursPerDay;
        if (wrapped < 0f)
            wrapped += HoursPerDay;

        float span = HoursPerDay / slotCount;
        float first = span * 0.5f;
        int index = wrapped < first ? slotCount - 1 : (int)((wrapped - first) / span);
        if (index >= slotCount)
            index = slotCount - 1;

        slotA = index;
        slotB = (index + 1) % slotCount;
        float timeA = SlotHours(slotA, slotCount);
        float timeB = SlotHours(slotB, slotCount);
        float time = wrapped;
        if (timeB < timeA)
            timeB += HoursPerDay;
        if (time < timeA)
            time += HoursPerDay;
        float width = timeB - timeA;
        alpha = width > 1e-6f ? (time - timeA) / width : 0f;
    }
}
