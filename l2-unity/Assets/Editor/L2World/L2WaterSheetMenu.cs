#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a prefab for Ertheia_watersheet01. Does not place it on a map.
/// </summary>
public static class L2WaterSheetMenu
{
    const string ModelPath = "Assets/Resources/Data/StaticMeshes/Ertheia_V_S/Ertheia_watersheet01.obj";
    const string MatPath = "Assets/Resources/Data/StaticMeshes/Ertheia_V_S/Ertheia_water_sh.mat";
    const string PrefabPath = "Assets/Resources/Data/StaticMeshes/Ertheia_V_S/Ertheia_watersheet01.prefab";

    [MenuItem("L2/World/Water/Create Ertheia watersheet prefab")]
    public static void CreatePrefab()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (model == null || mat == null)
        {
            EditorUtility.DisplayDialog(
                "L2 Water",
                "Import Ertheia_watersheet01.obj and Ertheia_water_sh.mat first.",
                "OK");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "L2 Water",
            "Prefab saved. Map 17_25 was not changed.",
            "OK");
    }
}
#endif
