#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds world_bridge01 from the L2 noon capture so each vertex keeps its own in_Color0.
/// The imported FBX welds those splits, which is why painting it left most vertices blank.
/// Positions match the imported mesh: (-x, z, y) * 0.019. UV v is flipped.
/// </summary>
public static class L2BridgeVertexColorBake
{
    const string CsvPath = @"C:\unity\l2 client\decompile\v2\wooden\extract\lineage2\bridge_ia_verts.csv";
    const string IndexPath = @"C:\unity\l2 client\decompile\v2\wooden\extract\lineage2\bridge_ia_indices.txt";
    const string MeshPath = "Assets/Resources/Data/StaticMeshes/world_bridge_S/world_bridge01_l2color.asset";
    const string MaterialPath = "Assets/Resources/Data/Textures/world_bridge_t/Materials/world_bridge01.mat";
    const string LogPath = @"C:\unity\l2 client\decompile\v2\wooden\extract\lineage2\bridge_color_bake.txt";
    const float PositionScale = 0.019f;

    static readonly string[] PrefabPaths =
    {
        "Assets/Resources/Data/Maps/17_25/StaticMeshes.prefab",
        "Assets/Resources/Data/Maps/16_25/StaticMeshes.prefab",
    };

    struct SourceVert
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 Uv;
        public Color32 Color;
    }

    [MenuItem("Tools/L2 World/Bake Bridge Vertex Light")]
    public static void BakeFromCapture()
    {
        int code = 0;
        try
        {
            Bake();
        }
        catch (Exception ex)
        {
            File.AppendAllText(LogPath, "FAIL " + ex + Environment.NewLine);
            Debug.LogError(ex);
            code = 1;
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }

    static void Bake()
    {
        if (!File.Exists(CsvPath))
            throw new FileNotFoundException("Missing capture CSV", CsvPath);
        if (!File.Exists(IndexPath))
            throw new FileNotFoundException("Missing capture indices", IndexPath);

        Dictionary<int, SourceVert> source = LoadCsv(CsvPath);
        List<int> indices = LoadIndices(IndexPath);
        var remap = new Dictionary<int, int>(source.Count);
        var positions = new List<Vector3>(source.Count);
        var normals = new List<Vector3>(source.Count);
        var uvs = new List<Vector2>(source.Count);
        var colors = new List<Color32>(source.Count);
        var triangles = new List<int>(indices.Count);
        int nonzero = 0;

        for (int i = 0; i < indices.Count; i++)
        {
            int id = indices[i];
            SourceVert vert;
            if (!source.TryGetValue(id, out vert))
                throw new InvalidOperationException("Index " + id + " is missing from the vertex CSV.");
            int compact;
            if (!remap.TryGetValue(id, out compact))
            {
                compact = positions.Count;
                remap.Add(id, compact);
                positions.Add(ToUnityPosition(vert.Position));
                normals.Add(ToUnityDirection(vert.Normal));
                uvs.Add(new Vector2(vert.Uv.x, 1f - vert.Uv.y));
                colors.Add(vert.Color);
                if (vert.Color.r > 0)
                    nonzero++;
            }
            triangles.Add(compact);
        }

        var mesh = new Mesh();
        mesh.name = "world_bridge01_l2color";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        Mesh saved = SaveMesh(mesh);
        int filters = AssignMesh(saved);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
            throw new FileNotFoundException("Missing bridge material", MaterialPath);
        mat.SetFloat("_L2UseVertexLight", 1f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        string line =
            "mesh verts=" + positions.Count +
            " tris=" + (triangles.Count / 3) +
            " nonzeroR=" + nonzero +
            " filters=" + filters +
            " vertex light on";
        File.WriteAllText(LogPath, line + Environment.NewLine);
        Debug.Log(line);
    }

    static Vector3 ToUnityPosition(Vector3 p)
    {
        return new Vector3(-p.x, p.z, p.y) * PositionScale;
    }

    static Vector3 ToUnityDirection(Vector3 n)
    {
        return new Vector3(-n.x, n.z, n.y);
    }

    static Dictionary<int, SourceVert> LoadCsv(string path)
    {
        var map = new Dictionary<int, SourceVert>(2048);
        string[] lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            string[] c = lines[i].Split(',');
            if (c.Length < 12)
                continue;
            var vert = new SourceVert();
            vert.Position = new Vector3(Parse(c[1]), Parse(c[2]), Parse(c[3]));
            vert.Normal = new Vector3(Parse(c[4]), Parse(c[5]), Parse(c[6]));
            vert.Uv = new Vector2(Parse(c[7]), Parse(c[8]));
            vert.Color = new Color32(ToByte(c[9]), ToByte(c[10]), ToByte(c[11]), 255);
            map[int.Parse(c[0], CultureInfo.InvariantCulture)] = vert;
        }
        if (map.Count < 100)
            throw new InvalidOperationException("CSV has too few vertices: " + map.Count);
        return map;
    }

    static List<int> LoadIndices(string path)
    {
        string text = File.ReadAllText(path);
        string[] parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        var indices = new List<int>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
            indices.Add(int.Parse(parts[i], CultureInfo.InvariantCulture));
        if (indices.Count < 3 || indices.Count % 3 != 0)
            throw new InvalidOperationException("Index count is not a triangle list: " + indices.Count);
        return indices;
    }

    static Mesh SaveMesh(Mesh mesh)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(mesh, MeshPath);
        return mesh;
    }

    static int AssignMesh(Mesh mesh)
    {
        int count = 0;
        for (int p = 0; p < PrefabPaths.Length; p++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPaths[p]);
            if (prefab == null)
                throw new FileNotFoundException("Missing prefab", PrefabPaths[p]);
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshRenderer renderer = filters[i].GetComponent<MeshRenderer>();
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;
                if (renderer.sharedMaterial.name != "world_bridge01")
                    continue;
                filters[i].sharedMesh = mesh;
                EditorUtility.SetDirty(filters[i]);
                count++;
            }
            EditorUtility.SetDirty(prefab);
        }
        if (count == 0)
            throw new InvalidOperationException("No world_bridge01 mesh filters found.");
        return count;
    }

    static float Parse(string text)
    {
        return float.Parse(text, CultureInfo.InvariantCulture);
    }

    static byte ToByte(string text)
    {
        int byteValue = Mathf.RoundToInt(Parse(text) * 255f);
        if (byteValue < 0)
            return 0;
        if (byteValue > 255)
            return 255;
        return (byte)byteValue;
    }
}
#endif
