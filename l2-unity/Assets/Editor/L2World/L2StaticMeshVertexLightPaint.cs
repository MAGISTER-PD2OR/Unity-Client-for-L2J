#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Paints world_bridge01 the way UStaticMesh::CalculateStaticMeshLighting does:
/// each placed Light / NMovableSunLight adds SampleIntensity into the vertex color.
/// </summary>
public static class L2StaticMeshVertexLightPaint
{
    const string MapT3d = "Assets/Resources/Data/Maps/17_25/Meta/17_25.t3d";
    const string MeshFolder = "Assets/Resources/Data/StaticMeshes/world_bridge_S";
    const string LogPath = @"C:\unity\l2 client\decompile\v2\wooden\extract\lineage2\bridge_vertex_light.txt";

    static readonly string[] PrefabPaths =
    {
        "Assets/Resources/Data/Maps/17_25/StaticMeshes.prefab",
        "Assets/Resources/Data/Maps/16_25/StaticMeshes.prefab",
    };

    [MenuItem("Tools/L2 World/Paint Bridge From Map Lights")]
    public static void PaintBridges()
    {
        string t3d = Path.Combine(Application.dataPath, "Resources/Data/Maps/17_25/Meta/17_25.t3d");
        List<L2StaticMeshVertexLight.Light> lights = L2StaticMeshVertexLight.LoadMapLights(t3d);
        if (lights.Count == 0)
        {
            Debug.LogError("No lights in " + MapT3d);
            return;
        }

        int painted = 0;
        int serial = 0;
        File.WriteAllText(LogPath, "lights=" + lights.Count + System.Environment.NewLine);
        for (int p = 0; p < PrefabPaths.Length; p++)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPaths[p]) == null)
                continue;
            painted += PaintPrefab(PrefabPaths[p], lights, ref serial);
        }

        AssetDatabase.SaveAssets();
        string summary = "Vertex light painted bridges=" + painted + " lights=" + lights.Count;
        File.AppendAllText(LogPath, summary + System.Environment.NewLine);
        Debug.Log(summary);
    }

    static int PaintPrefab(string prefabPath, List<L2StaticMeshVertexLight.Light> lights, ref int serial)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        int painted = 0;
        try
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshRenderer renderer = filters[i].GetComponent<MeshRenderer>();
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;
                if (renderer.sharedMaterial.name != "world_bridge01")
                    continue;
                Mesh source = filters[i].sharedMesh;
                if (source == null || !source.isReadable)
                {
                    Debug.LogWarning("Skip unreadable mesh on " + filters[i].name);
                    continue;
                }

                Vector3[] pos = source.vertices;
                Vector3[] nrm = source.normals;
                Color32[] colors = L2StaticMeshVertexLight.Paint(
                    pos,
                    nrm,
                    filters[i].transform.localToWorldMatrix,
                    lights,
                    1f);
                int nonzero = 0;
                int maxByte = 0;
                for (int c = 0; c < colors.Length; c++)
                {
                    if (colors[c].r > 0)
                        nonzero++;
                    if (colors[c].r > maxByte)
                        maxByte = colors[c].r;
                }

                Mesh copy = UnityEngine.Object.Instantiate(source);
                copy.name = "world_bridge01_vl_" + serial;
                copy.colors32 = colors;
                string assetPath = MeshFolder + "/world_bridge01_vl_" + serial + ".asset";
                filters[i].sharedMesh = SaveMesh(copy, assetPath);
                serial++;
                painted++;
                string line =
                    filters[i].name +
                    " verts=" + colors.Length +
                    " nonzeroR=" + nonzero +
                    " maxR=" + maxByte +
                    " at " + filters[i].transform.position;
                File.AppendAllText(LogPath, line + System.Environment.NewLine);
                Debug.Log(line);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return painted;
    }

    static Mesh SaveMesh(Mesh mesh, string assetPath)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(mesh, assetPath);
        return mesh;
    }
}
#endif
