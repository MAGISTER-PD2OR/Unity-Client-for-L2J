using System;
using UnityEngine;

/// <summary>
/// Exact UStaticMeshInstance ActorStaticLight masks extracted from a map package.
/// One actor stores eight bit arrays, one bit per original static-mesh vertex.
/// </summary>
public static class L2StaticMeshSunMaskDatabase
{
    [Serializable]
    public sealed class MapData
    {
        public bool ok;
        public string command;
        public int actorCount;
        public int rowCount;
        public int withMasks;
        public ActorData[] actors;
    }

    [Serializable]
    public sealed class ActorData
    {
        public string actor;
        public int actorExportIndex;
        public string mesh;
        public string instance;
        public string location;
        public string rotation;
        public string drawScale;
        public string drawScale3D;
        public string sunAffect;
        public string shadowCast;
        public int nativeOffset;
        public int maskBytes;
        public string[] masks;
        public string[] hashes;
        public int[] setBits;
        public string error;

        public bool HasMasks
        {
            get
            {
                return maskBytes > 0 &&
                    masks != null &&
                    masks.Length == L2StaticMeshSunVisibility.SlotCount;
            }
        }

        public byte[] DecodeMask(int slot)
        {
            if (!HasMasks || slot < 0 || slot >= masks.Length)
                return Array.Empty<byte>();
            byte[] bytes = Convert.FromBase64String(masks[slot]);
            if (bytes.Length != maskBytes)
                throw new InvalidOperationException(
                    actor + " slot " + slot + " has " + bytes.Length +
                    " bytes, expected " + maskBytes + ".");
            return bytes;
        }

        public Vector3 UnityPosition
        {
            get
            {
                Vector3 ue = ParseVector(location);
                return L2StaticMeshVertexLight.ToUnityPosition(ue);
            }
        }
    }

    public static MapData Parse(TextAsset asset)
    {
        if (asset == null)
            throw new ArgumentNullException(nameof(asset));
        MapData data = JsonUtility.FromJson<MapData>(asset.text);
        if (data == null || data.actors == null)
            throw new InvalidOperationException("Invalid static-mesh mask database: " + asset.name);
        return data;
    }

    public static Vector3 ParseVector(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Vector3.zero;
        return new Vector3(Field(text, "X"), Field(text, "Y"), Field(text, "Z"));
    }

    static float Field(string text, string name)
    {
        int start = text.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return 0f;
        start += name.Length + 1;
        int end = start;
        while (end < text.Length && text[end] != ',' && text[end] != ')')
            end++;
        float value;
        return float.TryParse(
            text.Substring(start, end - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value)
            ? value
            : 0f;
    }
}
