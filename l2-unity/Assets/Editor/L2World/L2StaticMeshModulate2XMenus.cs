#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Swap URP Lit → D3D9 Modulate2X across all open scenes (e.g. additive 17_25).
/// Prefer Hierarchy selection; else layer StaticMeshBuildings.
/// </summary>
public static class L2StaticMeshModulate2XMenus
{
    const string ShaderName = "L2/World/StaticMeshModulate2X";
    const string LitName = "Universal Render Pipeline/Lit";
    const string TargetLayerName = "StaticMeshBuildings";

    [MenuItem("L2/World/Apply Modulate2X to selection (or StaticMeshBuildings)")]
    public static void Apply()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog("L2 Modulate2X", "Shader not found: " + ShaderName, "OK");
            return;
        }

        PushGlobalsForPreview();
        int swapped = Swap(shader, revertToLit: false);
        Debug.Log("[L2 Modulate2X] Applied to " + swapped + " materials.");
    }

    [MenuItem("L2/World/Revert selection (or StaticMeshBuildings) to URP Lit")]
    public static void Revert()
    {
        Shader lit = Shader.Find(LitName);
        if (lit == null)
        {
            EditorUtility.DisplayDialog("L2 Modulate2X", "URP Lit not found.", "OK");
            return;
        }

        int swapped = Swap(lit, revertToLit: true);
        Debug.Log("[L2 Modulate2X] Reverted " + swapped + " materials.");
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

    static int Swap(Shader target, bool revertToLit)
    {
        List<Renderer> renderers = CollectRenderers(out string scope);
        if (renderers.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "L2 Modulate2X",
                "Nothing to process.\n\n" +
                "Open / load scene 17_25 (additive OK),\n" +
                "select the lighthouse in Hierarchy, OR put layer\n" +
                "\"" + TargetLayerName + "\" on it.\n\n" +
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

                Undo.RecordObject(mat, revertToLit ? "Revert L2 Modulate2X" : "Apply L2 Modulate2X");
                string srcShaderName = mat.shader.name;
                Texture tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
                if (tex == null && mat.HasProperty("_MainTex"))
                    tex = mat.GetTexture("_MainTex");
                Color baseColor = mat.HasProperty("_BaseColor")
                    ? mat.GetColor("_BaseColor")
                    : (mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white);
                bool fromMasked = srcShaderName.Contains("SpeedTree");
                float cull = mat.HasProperty("_Cull") ? mat.GetFloat("_Cull") : 2f;
                if (fromMasked)
                    cull = 0f;
                float cutoff = 0.5f;
                if (mat.HasProperty("_AlphaClipThreshold"))
                    cutoff = mat.GetFloat("_AlphaClipThreshold");
                else if (mat.HasProperty("_Cutoff"))
                    cutoff = mat.GetFloat("_Cutoff");
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
                        mat.SetFloat("_L2FloorMode", 0f);
                    if (mat.HasProperty("_L2WorldUvPerTileUu"))
                        mat.SetFloat("_L2WorldUvPerTileUu", 128f);
                    if (mat.HasProperty("_AlphaClip"))
                        mat.SetFloat("_AlphaClip", fromMasked ? 1f : 0f);
                    if (mat.HasProperty("_Cutoff"))
                        mat.SetFloat("_Cutoff", cutoff);
                }
                if (revertToLit)
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
                Undo.RecordObject(renderer, "L2 Modulate2X renderer flags");
                renderer.shadowCastingMode = revertToLit ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = revertToLit;
                EditorUtility.SetDirty(renderer);
                EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            }
        }

        Debug.Log(
            "[L2 Modulate2X] Scope: " + scope +
            ". Open scenes: " + ListOpenSceneNames() +
            ". Renderers: " + renderers.Count +
            ". Swapped materials: " + count +
            (count == 0 && skipped.Length > 0 ? ". Skipped: " + skipped : "."));

        if (count == 0 && skipped.Length > 0)
        {
            EditorUtility.DisplayDialog(
                "L2 Modulate2X",
                "Found " + renderers.Count + " renderer(s) but none used Lit.\n\n" + skipped,
                "OK");
        }

        AssetDatabase.SaveAssets();
        return count;
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
            // Buildings revert: skip Floor-mode mats (use Floor menu).
            if (name == ShaderName)
            {
                if (mat.HasProperty("_L2FloorMode") && mat.GetFloat("_L2FloorMode") > 0.5f)
                {
                    reason = "floor mode (use Floor revert)";
                    return false;
                }
                reason = null;
                return true;
            }
            reason = "not Modulate2X";
            return false;
        }

        if (name.Contains("Particles"))
        {
            reason = "Particles";
            return false;
        }

        if (name == LitName ||
            name == "Universal Render Pipeline/Unlit" ||
            name == "Unlit/Texture" ||
            name == "Hidden/InternalErrorShader" ||
            name.Contains("SpeedTree8"))
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

        reason = "shader not Lit/Unlit";
        return false;
    }
}
#endif
