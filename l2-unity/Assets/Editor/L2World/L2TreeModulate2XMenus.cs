#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tree Modulate2X: bark/stump Blend Off, leaves SrcAlpha + alphatest &gt; 0.
/// </summary>
public static class L2TreeModulate2XMenus
{
    const string BarkShaderName = "L2/World/TreeModulate2X";
    const string LeafShaderName = "L2/World/TreeLeafModulate2X";
    const string SpeakingTreeMaterials =
        "Assets/Resources/Data/Textures/speaking_tree_t/Materials";

    [MenuItem("L2/World/Trees/Apply Modulate2X to speaking_tree_t")]
    public static void ApplySpeakingTreeFolder()
    {
        ApplyFolder(SpeakingTreeMaterials);
    }

    [MenuItem("L2/World/Trees/Apply Modulate2X to selected materials or meshes")]
    public static void ApplySelection()
    {
        if (!FindShaders(out Shader bark, out Shader leaf))
            return;

        PushGlobalsForPreview();
        var mats = CollectSelectedMaterials();
        if (mats.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "L2 Tree Modulate2X",
                "Select tree materials in Project, or tree meshes in Hierarchy.",
                "OK");
            return;
        }

        int swapped = ApplyMaterials(bark, leaf, mats);
        DisableShadowsOnOpenScenes(mats);
        AssetDatabase.SaveAssets();
        Debug.Log("[L2 Tree Modulate2X] Applied to " + swapped + " selected material(s).");
    }

    static bool FindShaders(out Shader bark, out Shader leaf)
    {
        bark = Shader.Find(BarkShaderName);
        leaf = Shader.Find(LeafShaderName);
        if (bark == null || leaf == null)
        {
            EditorUtility.DisplayDialog(
                "L2 Tree Modulate2X",
                "Shader not found: " + BarkShaderName + " / " + LeafShaderName,
                "OK");
            return false;
        }

        return true;
    }

    static void ApplyFolder(string folder)
    {
        if (!FindShaders(out Shader bark, out Shader leaf))
            return;

        PushGlobalsForPreview();
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
        var mats = new List<Material>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (mat != null)
                mats.Add(mat);
        }

        int swapped = ApplyMaterials(bark, leaf, mats);
        DisableShadowsOnOpenScenes(mats);
        AssetDatabase.SaveAssets();
        Debug.Log("[L2 Tree Modulate2X] Applied to " + swapped + " materials in " + folder + ".");
    }

    static void PushGlobalsForPreview()
    {
        float hours = 12f;
        WorldClock clock = WorldClock.Instance;
        if (clock != null)
            hours = clock.WorldHours;
        L2EnvWorldLut.EnsureLoaded();
        L2EnvWorldLut.PushGlobals(hours, Color.white, Color.white);
    }

    static int ApplyMaterials(Shader bark, Shader leaf, List<Material> mats)
    {
        int count = 0;
        for (int i = 0; i < mats.Count; i++)
        {
            Material mat = mats[i];
            if (mat == null)
                continue;

            bool isLeaf = IsLeafMaterial(mat);
            Texture tex = ResolveBaseTexture(mat);
            Color baseColor = mat.HasProperty("_BaseColor")
                ? mat.GetColor("_BaseColor")
                : (mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white);

            Undo.RecordObject(mat, "Apply L2 Tree Modulate2X");
            mat.shader = isLeaf ? leaf : bark;
            mat.shaderKeywords = System.Array.Empty<string>();
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", tex);
                mat.mainTexture = tex;
            }

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", baseColor);

            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", isLeaf ? 0f : 2f);
            if (mat.HasProperty("_Cutoff"))
                mat.SetFloat("_Cutoff", 0f);

            mat.renderQueue = isLeaf ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            mat.SetOverrideTag("RenderType", isLeaf ? "Transparent" : "Opaque");
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            count++;
        }

        return count;
    }

    static bool IsLeafMaterial(Material mat)
    {
        string name = mat.name;
        if (string.IsNullOrEmpty(name))
            name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(mat));
        return name.IndexOf("leaf", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static Texture ResolveBaseTexture(Material mat)
    {
        if (mat.HasProperty("_MainTex"))
        {
            Texture t = mat.GetTexture("_MainTex");
            if (t != null)
                return t;
        }

        if (mat.HasProperty("_BaseMap"))
        {
            Texture t = mat.GetTexture("_BaseMap");
            if (t != null)
                return t;
        }

        return mat.mainTexture;
    }

    static List<Material> CollectSelectedMaterials()
    {
        var seen = new HashSet<Material>();
        var list = new List<Material>();

        Object[] selected = Selection.objects;
        if (selected != null)
        {
            for (int i = 0; i < selected.Length; i++)
            {
                Material mat = selected[i] as Material;
                if (mat != null && seen.Add(mat))
                    list.Add(mat);
            }
        }

        GameObject[] gos = Selection.gameObjects;
        if (gos != null)
        {
            for (int i = 0; i < gos.Length; i++)
            {
                if (gos[i] == null)
                    continue;
                Renderer[] renderers = gos[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Material[] shared = renderers[r].sharedMaterials;
                    for (int m = 0; m < shared.Length; m++)
                    {
                        if (shared[m] != null && seen.Add(shared[m]))
                            list.Add(shared[m]);
                    }
                }
            }
        }

        return list;
    }

    static void DisableShadowsOnOpenScenes(List<Material> treeMats)
    {
        var want = new HashSet<Material>(treeMats);
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Renderer[] renderers = roots[r].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    Material[] shared = renderer.sharedMaterials;
                    bool hit = false;
                    for (int m = 0; m < shared.Length; m++)
                    {
                        if (shared[m] != null && want.Contains(shared[m]))
                        {
                            hit = true;
                            break;
                        }
                    }

                    if (!hit)
                        continue;

                    Undo.RecordObject(renderer, "L2 Tree Modulate2X renderer flags");
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    EditorUtility.SetDirty(renderer);
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
        }
    }
}
#endif
