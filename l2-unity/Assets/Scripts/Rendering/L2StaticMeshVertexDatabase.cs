using System;
using UnityEngine;

/// <summary>
/// Original UStaticMesh positions/normals exported from the client .usx files.
/// Arrays are Base64-packed little-endian float32 xyz values.
/// </summary>
public static class L2StaticMeshVertexDatabase
{
    // Matches ModelImporter.globalScale on the original StaticMeshes FBX assets.
    public const float StaticMeshFbxScale = 0.019f;

    [Serializable]
    public sealed class MapData
    {
        public int meshCount;
        public int exported;
        public MeshData[] meshes;
    }

    [Serializable]
    public sealed class MeshData
    {
        public string mesh;
        public int vertexCount;
        public string positions;
        public string normals;
        public string uv0;
        public string error;

        public Vector3[] DecodePositions()
        {
            return DecodeVectors(positions, vertexCount, true);
        }

        public Vector3[] DecodeNormals()
        {
            return DecodeVectors(normals, vertexCount, false);
        }

        public Vector2[] DecodeUv0()
        {
            byte[] bytes = Convert.FromBase64String(uv0 ?? "");
            if (bytes.Length != vertexCount * 8)
                return Array.Empty<Vector2>();
            var result = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                int p = i * 8;
                result[i] = new Vector2(
                    ReadSingleLittleEndian(bytes, p),
                    ReadSingleLittleEndian(bytes, p + 4));
            }
            return result;
        }
    }

    public static MapData Parse(TextAsset asset)
    {
        if (asset == null)
            throw new ArgumentNullException(nameof(asset));
        MapData data = JsonUtility.FromJson<MapData>(asset.text);
        if (data == null || data.meshes == null)
            throw new InvalidOperationException("Invalid static-mesh vertex database: " + asset.name);
        return data;
    }

    static Vector3[] DecodeVectors(string encoded, int count, bool position)
    {
        byte[] bytes = Convert.FromBase64String(encoded ?? "");
        if (bytes.Length != count * 12)
            throw new InvalidOperationException(
                "Static-mesh vector buffer has " + bytes.Length + " bytes; expected " + (count * 12) + ".");

        var result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int p = i * 12;
            float x = ReadSingleLittleEndian(bytes, p);
            float y = ReadSingleLittleEndian(bytes, p + 4);
            float z = ReadSingleLittleEndian(bytes, p + 8);
            Vector3 converted = new Vector3(-x, z, y);
            result[i] = position
                ? converted * StaticMeshFbxScale
                : converted.normalized;
        }
        return result;
    }

    static float ReadSingleLittleEndian(byte[] bytes, int offset)
    {
        if (BitConverter.IsLittleEndian)
            return BitConverter.ToSingle(bytes, offset);
        var value = new byte[4];
        value[0] = bytes[offset + 3];
        value[1] = bytes[offset + 2];
        value[2] = bytes[offset + 1];
        value[3] = bytes[offset];
        return BitConverter.ToSingle(value, 0);
    }
}
