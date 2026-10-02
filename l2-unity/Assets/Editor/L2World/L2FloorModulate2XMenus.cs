#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Floor / terrain Modulate2X: same shader as buildings, Floor mode on
/// (world UV / 128 UU + _L2TerrainAmbient + fog).
/// Prefer selection; else layer StaticMeshFloor.
/// </summary>
public static class L2FloorModulate2XMenus
{
    const string ShaderName = "L2/World/StaticMeshModulate2X";
    const string TerrainShaderName = "L2/World/TerrainBaseModulate2X";
    const string TerrainShaderNameUnified = "L2/World/TerrainModulate2X";
    const string TerrainShaderNameLegacy = "L2/World/TerrainSplatModulate2X";
    const string LitName = "Universal Render Pipeline/Lit";
    const string TargetLayerName = "StaticMeshFloor";
    const float WorldUvPerTileUu = 128f;

    [MenuItem("L2/World/Floor/Apply Modulate2X to selection (or StaticMeshFloor)")]
    public static void Apply()
    {
        Shader shader = Shader.Find(ShaderName);
        Shader terrainShader = Shader.Find(TerrainShaderName);
        if (shader == null && terrainShader == null)
        {
            EditorUtility.DisplayDialog("L2 Floor Modulate2X", "Shaders not found.", "OK");
            return;
        }

        PushGlobalsForPreview();
        int swapped = Swap(revertToLit: false);
        Debug.Log("[L2 Floor Modulate2X] Applied to " + swapped + " materials (Floor / Terrain splat).");
    }

    [MenuItem("L2/World/Floor/Revert selection (or StaticMeshFloor) to URP Lit")]
    public static void Revert()
    {
        int swapped = Swap(revertToLit: true);
        Debug.Log("[L2 Floor Modulate2X] Reverted " + swapped + " materials.");
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

    static int Swap(bool revertToLit)
    {
        Shader simpleFloor = Shader.Find(ShaderName);
        Shader terrainSplat = Shader.Find(TerrainShaderName);
        Shader alphamap = Shader.Find("Shader Graphs/TerrainAlphamap");
        Shader lit = Shader.Find(LitName);

        List<Renderer> renderers = CollectRenderers(out string scope);
        if (renderers.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "L2 Floor Modulate2X",
                "Nothing to process.\n\n" +
                "Select floor mesh(es), OR create layer\n" +
                "\"" + TargetLayerName + "\" and put floor on it.\n\n" +
                "Open scenes: " + ListOpenSceneNames(),
                "OK");
            return 0;
        }

        var seen = new HashSet<Material>();
        int count = 0;
        var skipped = new StringBuilder();

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            Material[] mats = renderer.sharedMaterials;
            bool rendererChanged = false;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null || mat.shader == null)
                    continue;
                if (!seen.Add(mat))
                    continue;

                string reason;
                if (!ShouldSwap(mat, revertToLit, out reason))
                {
                    if (skipped.Length < 1000)
                    {
                        skipped.Append(renderer.name);
                        skipped.Append("@");
                        skipped.Append(renderer.gameObject.scene.name);
                        skipped.Append(" → ");
                        skipped.Append(mat.shader.name);
                        skipped.Append(" (");
                        skipped.Append(reason);
                        skipped.Append("); ");
                    }
                    continue;
                }

                bool splatMat = IsSplatTerrainMat(mat);
                Shader target;
                if (revertToLit)
                    target = splatMat ? (alphamap != null ? alphamap : lit) : lit;
                else
                    target = splatMat ? terrainSplat : simpleFloor;

                if (target == null)
                    continue;

                Undo.RecordObject(mat, revertToLit ? "Revert L2 Floor Modulate2X" : "Apply L2 Floor Modulate2X");
                Texture tex = ResolveBaseTexture(mat);
                Color baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                float cull = mat.HasProperty("_Cull") ? mat.GetFloat("_Cull") : 2f;
                mat.shader = target;
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
                    mat.SetFloat("_Cull", cull);

                if (!revertToLit)
                {
                    if (mat.HasProperty("_L2FloorMode"))
                        mat.SetFloat("_L2FloorMode", 1f);
                    if (mat.HasProperty("_L2WorldUvPerTileUu"))
                        mat.SetFloat("_L2WorldUvPerTileUu", WorldUvPerTileUu);
                }
                else
                {
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", 0f);
                    if (mat.HasProperty("_EnvironmentReflections"))
                        mat.SetFloat("_EnvironmentReflections", 0f);
                    if (mat.HasProperty("_SpecularHighlights"))
                        mat.SetFloat("_SpecularHighlights", 0f);
                }

                EditorUtility.SetDirty(mat);
                count++;
                rendererChanged = true;
            }

            if (rendererChanged)
            {
                Undo.RecordObject(renderer, "L2 Floor Modulate2X renderer flags");
                renderer.shadowCastingMode = revertToLit ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = revertToLit;
                if (!revertToLit)
                    L2TerrainLayerStack.TryAttach(renderer);
                EditorUtility.SetDirty(renderer);
                EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            }
        }

        Debug.Log(
            "[L2 Floor Modulate2X] Scope: " + scope +
            ". Open scenes: " + ListOpenSceneNames() +
            ". Renderers: " + renderers.Count +
            ". Swapped materials: " + count +
            (count == 0 && skipped.Length > 0 ? ". Skipped: " + skipped : "."));

        if (count == 0 && skipped.Length > 0)
        {
            EditorUtility.DisplayDialog(
                "L2 Floor Modulate2X",
                "Found " + renderers.Count + " renderer(s) but none matched.\n\n" + skipped,
                "OK");
        }

        AssetDatabase.SaveAssets();
        return count;
    }

    static bool IsSplatTerrainMat(Material mat)
    {
        if (mat.HasProperty("_Layer_0") || mat.HasProperty("_Splatmaps"))
            return true;
        string name = mat.shader != null ? mat.shader.name : "";
        return name.Contains("TerrainAlphamap")
            || name == TerrainShaderName
            || name == TerrainShaderNameUnified
            || name == TerrainShaderNameLegacy;
    }

    static Texture ResolveBaseTexture(Material mat)
    {
        if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
            return mat.GetTexture("_BaseMap");
        if (mat.mainTexture != null)
            return mat.mainTexture;
        if (mat.HasProperty("_MainTex"))
            return mat.GetTexture("_MainTex");
        // TerrainAlphamapDouble often uses _Layer_0
        if (mat.HasProperty("_Layer_0"))
            return mat.GetTexture("_Layer_0");
        return null;
    }

    static List<Renderer> CollectRenderers(out string scope)
    {
        var list = new List<Renderer>();
        var seen = new HashSet<Renderer>();

        GameObject[] selected = Selection.gameObjects;
        if (selected != null && selected.Length > 0)
        {
            scope = "Hierarchy selection (" + selected.Length + " root(s))";
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] == null)
                    continue;
                Renderer[] childRenderers = selected[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < childRenderers.Length; r++)
                {
                    if (seen.Add(childRenderers[r]))
                        list.Add(childRenderers[r]);
                }
            }
            return list;
        }

        int layer = LayerMask.NameToLayer(TargetLayerName);
        if (layer < 0)
        {
            scope = "no selection and layer \"" + TargetLayerName + "\" missing";
            return list;
        }

        scope = "layer " + TargetLayerName + " in all open scenes";
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Renderer[] childRenderers = roots[r].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < childRenderers.Length; i++)
                {
                    Renderer renderer = childRenderers[i];
                    if (!IsUnderTargetLayer(renderer.transform, layer))
                        continue;
                    if (seen.Add(renderer))
                        list.Add(renderer);
                }
            }
        }

        return list;
    }

    static string ListOpenSceneNames()
    {
        if (SceneManager.sceneCount == 0)
            return "(none)";

        var sb = new StringBuilder();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (i > 0)
                sb.Append(", ");
            sb.Append(scene.name);
            if (!scene.isLoaded)
                sb.Append("(unloaded)");
        }
        return sb.ToString();
    }

    static bool IsUnderTargetLayer(Transform t, int layer)
    {
        while (t != null)
        {
            if (t.gameObject.layer == layer)
                return true;
            t = t.parent;
        }
        return false;
    }

    static bool ShouldSwap(Material mat, bool revertToLit, out string reason)
    {
        string name = mat.shader.name;
        if (revertToLit)
        {
            if (name == ShaderName && mat.HasProperty("_L2FloorMode") && mat.GetFloat("_L2FloorMode") > 0.5f)
            {
                reason = null;
                return true;
            }
            if (name == TerrainShaderName || name == TerrainShaderNameUnified || name == TerrainShaderNameLegacy)
            {
                reason = null;
                return true;
            }
            reason = "not Floor/Terrain Modulate2X";
            return false;
        }

        if (name.Contains("SpeedTree") || name.Contains("Particles"))
        {
            reason = "SpeedTree/Particles";
            return false;
        }

        if (name == LitName ||
            name == "Universal Render Pipeline/Unlit" ||
            name == "Unlit/Texture" ||
            name == "Hidden/InternalErrorShader" ||
            name.Contains("TerrainAlphamap"))
        {
            if (mat.HasProperty("_WorkflowMode") && mat.GetFloat("_WorkflowMode") < 0.5f &&
                mat.HasProperty("_SpecGlossMap") && mat.GetTexture("_SpecGlossMap") != null)
            {
                reason = "specular props";
                return false;
            }
            reason = null;
            return true;
        }

        reason = "shader not Lit/Unlit/TerrainAlphamap";
        return false;
    }
}
#endif
