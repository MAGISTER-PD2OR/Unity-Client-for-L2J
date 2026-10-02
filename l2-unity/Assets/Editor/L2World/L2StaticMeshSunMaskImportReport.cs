#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verifies that extracted UStaticMeshInstance masks can be associated with
/// the placed Unity meshes before any prefab or mesh assets are modified.
/// </summary>
public static class L2StaticMeshSunMaskImportReport
{
    const string DatabasePath =
        "Assets/Resources/Data/Maps/17_25/Meta/StaticMeshSunMasks.json";
    const string StaticMeshesPrefab =
        "Assets/Resources/Data/Maps/17_25/StaticMeshes.prefab";

    [MenuItem("Tools/L2 World/Sun Masks/Analyze Imported 17_25 Masks")]
    public static void AnalyzeImportedMasks()
    {
        TextAsset databaseAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(DatabasePath);
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(StaticMeshesPrefab);
        if (databaseAsset == null || prefabAsset == null)
        {
            Debug.LogError("StaticMeshSunMasks.json or StaticMeshes.prefab is missing.");
            return;
        }

        L2StaticMeshSunMaskDatabase.MapData database =
            L2StaticMeshSunMaskDatabase.Parse(databaseAsset);
        MeshFilter[] filters = prefabAsset.GetComponentsInChildren<MeshFilter>(true);
        var filtersByMesh = new Dictionary<string, List<MeshFilter>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            List<MeshFilter> list;
            if (!filtersByMesh.TryGetValue(filter.gameObject.name, out list))
            {
                list = new List<MeshFilter>();
                filtersByMesh.Add(filter.gameObject.name, list);
            }
            list.Add(filter);
        }

        var used = new HashSet<MeshFilter>();
        int withMasks = 0;
        int matched = 0;
        int positionMismatch = 0;
        int missingMesh = 0;
        int vertexCompatible = 0;
        int vertexMismatch = 0;
        float maxMatchedDistance = 0f;
        string bridgeLine = "bridge not found";

        for (int i = 0; i < database.actors.Length; i++)
        {
            L2StaticMeshSunMaskDatabase.ActorData actor = database.actors[i];
            if (actor == null || !actor.HasMasks)
                continue;
            withMasks++;

            List<MeshFilter> candidates;
            if (!filtersByMesh.TryGetValue(actor.mesh, out candidates))
            {
                missingMesh++;
                continue;
            }

            MeshFilter best = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 expectedPosition = actor.UnityPosition;
            for (int c = 0; c < candidates.Count; c++)
            {
                MeshFilter candidate = candidates[c];
                if (used.Contains(candidate))
                    continue;
                float distance = Vector3.Distance(candidate.transform.position, expectedPosition);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            if (best == null || bestDistance > 0.05f)
            {
                positionMismatch++;
                continue;
            }

            used.Add(best);
            matched++;
            maxMatchedDistance = Mathf.Max(maxMatchedDistance, bestDistance);
            Mesh mesh = best.sharedMesh;
            int expectedMaskBytes = mesh != null ? (mesh.vertexCount + 7) >> 3 : 0;
            if (expectedMaskBytes == actor.maskBytes)
                vertexCompatible++;
            else
                vertexMismatch++;

            if (string.Equals(actor.actor, "StaticMeshActor1", StringComparison.OrdinalIgnoreCase))
            {
                bridgeLine =
                    "bridge mesh=" + actor.mesh +
                    " instance=" + actor.instance +
                    " verts=" + (mesh != null ? mesh.vertexCount : 0) +
                    " maskBytes=" + actor.maskBytes +
                    " hashes=" + string.Join(",", actor.hashes ?? Array.Empty<string>());
            }
        }

        Debug.Log(
            "Imported static-mesh masks: databaseActors=" + database.actorCount +
            " withMasks=" + withMasks +
            " prefabFilters=" + filters.Length +
            " matched=" + matched +
            " vertexCompatible=" + vertexCompatible +
            " vertexMismatch=" + vertexMismatch +
            " missingMesh=" + missingMesh +
            " positionMismatch=" + positionMismatch +
            " maxDistance=" + maxMatchedDistance.ToString("G6") +
            Environment.NewLine +
            bridgeLine);
    }
}
#endif
