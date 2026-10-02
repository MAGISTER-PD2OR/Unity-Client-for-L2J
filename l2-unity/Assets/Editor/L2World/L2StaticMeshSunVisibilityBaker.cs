#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Validation and baking entry points for the per-instance ActorStaticLight masks.
/// The first target is StaticMeshActor1 in 17_25 because its original eight masks
/// were captured directly from UStaticMeshInstance+0x40.
/// </summary>
public static class L2StaticMeshSunVisibilityBaker
{
    const string MapPrefab = "Assets/Resources/Data/Maps/17_25/17_25.prefab";
    const string StaticMeshesPrefab = "Assets/Resources/Data/Maps/17_25/StaticMeshes.prefab";
    const string WestMapPrefab = "Assets/Resources/Data/Maps/16_25/UnityTerrain/16_25.prefab";
    const string WestStaticMeshesPrefab = "Assets/Resources/Data/Maps/16_25/StaticMeshes.prefab";
    const string BridgeMeshName = "world_bridge_S.world_bridge01";
    const string ReferenceMasks =
        @"C:\games\Lineage II HighElfes\ADEU-P464-D20240703-P-240117-240724-1\system\logs\StaticMeshLight_interpolate_masks_world_bridge01_StaticMeshActor1.csv";
    const string ReferenceVertices =
        @"C:\games\Lineage II HighElfes\ADEU-P464-D20240703-P-240117-240724-1\system\logs\StaticMeshLight_interpolate_world_bridge01_StaticMeshActor1.csv";

    // StaticMeshActor1 Location=(-88291.4375,253024.4375,-3769.292969).
    static readonly Vector3 BridgeActor1Position =
        new Vector3(253024.4375f, -3769.292969f, -88291.4375f) * L2StaticMeshVertexLight.UuToMeters;

    [MenuItem("Tools/L2 World/Sun Masks/Validate 17_25 Bridge Actor1")]
    public static void ValidateBridgeActor1()
    {
        if (!File.Exists(ReferenceMasks) || !File.Exists(ReferenceVertices))
        {
            Debug.LogError("Reference bridge CSV files were not found.");
            return;
        }

        GameObject mapAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefab);
        GameObject staticAsset = AssetDatabase.LoadAssetAtPath<GameObject>(StaticMeshesPrefab);
        GameObject westMapAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WestMapPrefab);
        GameObject westStaticAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WestStaticMeshesPrefab);
        if (mapAsset == null || staticAsset == null || westMapAsset == null || westStaticAsset == null)
        {
            Debug.LogError("17_25/16_25 terrain or StaticMeshes prefab is missing.");
            return;
        }

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Scene preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            PrefabUtility.InstantiatePrefab(mapAsset, preview);
            PrefabUtility.InstantiatePrefab(westMapAsset, preview);
            PrefabUtility.InstantiatePrefab(westStaticAsset, preview);
            GameObject staticRoot = (GameObject)PrefabUtility.InstantiatePrefab(staticAsset, preview);
            PhysicsScene physicsScene = preview.GetPhysicsScene();
            Physics.SyncTransforms();

            MeshFilter target = FindBridgeActor1(staticRoot);
            if (target == null || target.sharedMesh == null)
            {
                Debug.LogError("Could not find world_bridge01 StaticMeshActor1 in preview scene.");
                return;
            }

            LogPhysicsSanity(physicsScene, target);
            L2StaticMeshSunVisibility.MaskSet generated = L2StaticMeshSunVisibility.Bake(
                target.sharedMesh,
                target.transform,
                physicsScene,
                null,
                ~0);

            CompareWithReference(generated, ReferenceMasks, ReferenceVertices, target);
        }
        finally
        {
            if (!Application.isBatchMode)
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
    }

    static void LogPhysicsSanity(PhysicsScene physicsScene, MeshFilter target)
    {
        var hits = new RaycastHit[64];
        int downCount = physicsScene.Raycast(
            target.transform.position + Vector3.up * 100f,
            Vector3.down,
            hits,
            1000f,
            ~0,
            QueryTriggerInteraction.Ignore);
        string names = string.Empty;
        for (int i = 0; i < downCount && i < hits.Length; i++)
        {
            if (hits[i].collider != null)
                names += (names.Length == 0 ? string.Empty : ",") + hits[i].collider.name;
        }
        Debug.Log("Sun-mask physics sanity: downHits=" + downCount + " names=" + names);
    }

    [MenuItem("Tools/L2 World/Sun Masks/Report Selected Mesh Masks")]
    public static void ReportSelectedMeshMasks()
    {
        GameObject selected = Selection.activeGameObject;
        MeshFilter filter = selected != null ? selected.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null)
        {
            Debug.LogError("Select a GameObject with a readable MeshFilter.");
            return;
        }

        L2StaticMeshSunVisibility.MaskSet masks = L2StaticMeshSunVisibility.Bake(
            filter.sharedMesh,
            filter.transform,
            selected.scene.GetPhysicsScene(),
            null,
            ~0);

        Debug.Log(BuildCountReport("Selected " + selected.name, masks));
    }

    static MeshFilter FindBridgeActor1(GameObject staticRoot)
    {
        MeshFilter best = null;
        float bestDistance = float.PositiveInfinity;
        MeshFilter[] filters = staticRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (!string.Equals(filter.gameObject.name, BridgeMeshName, StringComparison.OrdinalIgnoreCase))
                continue;

            float distance = Vector3.Distance(filter.transform.position, BridgeActor1Position);
            if (distance < bestDistance)
            {
                best = filter;
                bestDistance = distance;
            }
        }

        if (bestDistance > 0.05f)
        {
            Debug.LogWarning(
                "Nearest bridge is " + bestDistance.ToString("F6", CultureInfo.InvariantCulture) +
                "m from StaticMeshActor1 location.");
        }

        return best;
    }

    static void CompareWithReference(
        L2StaticMeshSunVisibility.MaskSet generated,
        string masksCsvPath,
        string verticesCsvPath,
        MeshFilter target)
    {
        var enabled = new HashSet<long>();
        int maxVertex = -1;
        string[] lines = File.ReadAllLines(masksCsvPath);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split(',');
            if (columns.Length < 3)
                continue;

            int slot;
            int vertex;
            int value;
            if (!int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) ||
                !int.TryParse(columns[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out vertex) ||
                !int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                continue;
            }

            maxVertex = Mathf.Max(maxVertex, vertex);
            if (value != 0)
                enabled.Add(Key(slot, vertex));
        }

        int mismatches = 0;
        var mismatchPerSlot = new int[L2StaticMeshSunVisibility.SlotCount];
        var generatedCounts = new int[L2StaticMeshSunVisibility.SlotCount];
        var referenceCounts = new int[L2StaticMeshSunVisibility.SlotCount];
        var referenceFacingViolations = new int[L2StaticMeshSunVisibility.SlotCount];
        Vector3[] normals = target.sharedMesh.normals;
        float maxPositionError;
        float maxNormalError;
        int[] originalToUnity = BuildOriginalToUnityMap(
            target.sharedMesh,
            verticesCsvPath,
            out maxPositionError,
            out maxNormalError);

        for (int slot = 0; slot < L2StaticMeshSunVisibility.SlotCount; slot++)
        {
            for (int originalVertex = 0; originalVertex < generated.VertexCount; originalVertex++)
            {
                int unityVertex = originalToUnity[originalVertex];
                bool actual = generated.IsEnabled(slot, unityVertex);
                bool expected = enabled.Contains(Key(slot, originalVertex));
                Vector3 worldNormal = target.transform.TransformDirection(normals[unityVertex]).normalized;
                if (actual)
                    generatedCounts[slot]++;
                if (expected)
                {
                    referenceCounts[slot]++;
                    if (Vector3.Dot(generated.IncomingDirections[slot], worldNormal) > 0f)
                        referenceFacingViolations[slot]++;
                }
                if (actual != expected)
                {
                    mismatches++;
                    mismatchPerSlot[slot]++;
                }
            }
        }

        string report =
            "L2 sun-mask validation: " + target.gameObject.name +
            " vertices=" + generated.VertexCount +
            " referenceMaxVertex=" + maxVertex +
            " mapMaxPosError=" + maxPositionError.ToString("G6", CultureInfo.InvariantCulture) +
            " mapMaxNormalError=" + maxNormalError.ToString("G6", CultureInfo.InvariantCulture) +
            " mismatches=" + mismatches +
            Environment.NewLine;

        for (int slot = 0; slot < L2StaticMeshSunVisibility.SlotCount; slot++)
        {
            report +=
                "slot " + slot +
                " t=" + L2StaticMeshSunVisibility.GetSlotCenterHours(slot).ToString("F1", CultureInfo.InvariantCulture) +
                " generated=" + generatedCounts[slot] +
                " reference=" + referenceCounts[slot] +
                " mismatch=" + mismatchPerSlot[slot] +
                " referenceBackface=" + referenceFacingViolations[slot] +
                " incoming=" + generated.IncomingDirections[slot] +
                Environment.NewLine;
        }

        if (mismatches == 0)
            Debug.Log(report);
        else
            Debug.LogWarning(report);
    }

    static int[] BuildOriginalToUnityMap(
        Mesh unityMesh,
        string verticesCsvPath,
        out float maxPositionError,
        out float maxNormalError)
    {
        Vector3[] unityPositions = unityMesh.vertices;
        Vector3[] unityNormals = unityMesh.normals;
        var originalPositions = new Vector3[unityPositions.Length];
        var originalNormals = new Vector3[unityPositions.Length];
        string[] lines = File.ReadAllLines(verticesCsvPath);
        for (int line = 1; line < lines.Length; line++)
        {
            string[] c = lines[line].Split(',');
            if (c.Length < 7)
                continue;
            int index = int.Parse(c[0], CultureInfo.InvariantCulture);
            if (index < 0 || index >= originalPositions.Length)
                continue;

            Vector3 uePosition = new Vector3(Parse(c[1]), Parse(c[2]), Parse(c[3]));
            Vector3 ueNormal = new Vector3(Parse(c[4]), Parse(c[5]), Parse(c[6]));
            // The bridge FBX/capture conversion is mesh-local (-X,Z,Y), not
            // the actor/world conversion (Y,Z,X).
            originalPositions[index] = new Vector3(-uePosition.x, uePosition.z, uePosition.y) * 0.019f;
            originalNormals[index] = new Vector3(-ueNormal.x, ueNormal.z, ueNormal.y).normalized;
        }

        var map = new int[originalPositions.Length];
        maxPositionError = 0f;
        maxNormalError = 0f;
        for (int original = 0; original < originalPositions.Length; original++)
        {
            int best = 0;
            float bestPosition = float.PositiveInfinity;
            float bestNormal = float.PositiveInfinity;
            for (int unity = 0; unity < unityPositions.Length; unity++)
            {
                float positionError = (unityPositions[unity] - originalPositions[original]).sqrMagnitude;
                if (positionError > bestPosition + 1e-10f)
                    continue;

                float normalError = (unityNormals[unity].normalized - originalNormals[original]).sqrMagnitude;
                if (positionError < bestPosition - 1e-10f || normalError < bestNormal)
                {
                    best = unity;
                    bestPosition = positionError;
                    bestNormal = normalError;
                }
            }

            map[original] = best;
            maxPositionError = Mathf.Max(maxPositionError, Mathf.Sqrt(bestPosition));
            maxNormalError = Mathf.Max(maxNormalError, Mathf.Sqrt(bestNormal));
        }

        return map;
    }

    static float Parse(string text)
    {
        return float.Parse(text, CultureInfo.InvariantCulture);
    }

    static string BuildCountReport(string title, L2StaticMeshSunVisibility.MaskSet masks)
    {
        string report = title + " vertices=" + masks.VertexCount;
        for (int slot = 0; slot < L2StaticMeshSunVisibility.SlotCount; slot++)
        {
            int count = 0;
            for (int vertex = 0; vertex < masks.VertexCount; vertex++)
            {
                if (masks.IsEnabled(slot, vertex))
                    count++;
            }
            report += " slot" + slot + "=" + count;
        }
        return report;
    }

    static long Key(int slot, int vertex)
    {
        return ((long)slot << 32) | (uint)vertex;
    }
}
#endif
