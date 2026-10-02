#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Remaps serialized UStaticMeshInstance mask bits from original .usx vertex
/// indices onto the imported Unity FBX topology by local position and normal.
/// </summary>
public static class L2StaticMeshSunMaskRemapper
{
    const float PositionCell = 0.0005f;
    const float MaxPositionError = 0.002f;

    [MenuItem("Tools/L2 World/Sun Masks/Build Remapped 17_25 Database")]
    public static void Build()
    {
        BuildMap("17_25");
    }

    [MenuItem("Tools/L2 World/Sun Masks/Build Remapped 16_24 and 16_25")]
    public static void BuildNeighbors()
    {
        BuildMap("16_24");
        BuildMap("16_25");
    }

    public static void BuildMap(string map)
    {
        string maskPath = "Assets/Resources/Data/Maps/" + map + "/Meta/StaticMeshSunMasks.json";
        string vertexPath = "Assets/Resources/Data/Maps/" + map + "/Meta/StaticMeshVertices.json";
        string prefabPath = "Assets/Resources/Data/Maps/" + map + "/StaticMeshes.prefab";
        string scenePath = "Assets/Resources/Scenes/" + map + ".unity";
        string outputPath = "Assets/Resources/Data/Maps/" + map + "/Meta/StaticMeshSunMasksRemapped.json";

        TextAsset maskAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(maskPath);
        TextAsset vertexAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(vertexPath);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (maskAsset == null || vertexAsset == null || prefab == null)
            throw new InvalidOperationException("Sun mask inputs or StaticMeshes.prefab are missing.");

        L2StaticMeshSunMaskDatabase.MapData masks =
            L2StaticMeshSunMaskDatabase.Parse(maskAsset);
        L2StaticMeshVertexDatabase.MapData vertices =
            L2StaticMeshVertexDatabase.Parse(vertexAsset);

        var sourceByMesh = new Dictionary<string, L2StaticMeshVertexDatabase.MeshData>(
            StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < vertices.meshes.Length; i++)
        {
            L2StaticMeshVertexDatabase.MeshData mesh = vertices.meshes[i];
            if (mesh != null && string.IsNullOrEmpty(mesh.error))
                sourceByMesh[mesh.mesh] = mesh;
        }

        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        Dictionary<MeshFilter, MeshFilter> sceneFilterByPrefab =
            BuildSceneFilterMap(scenePath, prefabPath, out Scene openedScene, out bool closeOpenedScene);
        var filtersByMesh = new Dictionary<string, List<MeshFilter>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (!filtersByMesh.TryGetValue(filter.gameObject.name, out List<MeshFilter> list))
            {
                list = new List<MeshFilter>();
                filtersByMesh.Add(filter.gameObject.name, list);
            }
            list.Add(filter);
        }

        var remapByMesh = new Dictionary<Mesh, int[]>();
        var used = new HashSet<MeshFilter>();
        var output = new List<L2StaticMeshSunMaskRuntime.ActorData>();
        int missingActor = 0;
        int failedTopology = 0;
        int maxCandidates = 0;
        float maxError = 0f;

        for (int i = 0; i < masks.actors.Length; i++)
        {
            L2StaticMeshSunMaskDatabase.ActorData actor = masks.actors[i];
            if (actor == null || !actor.HasMasks ||
                !filtersByMesh.TryGetValue(actor.mesh, out List<MeshFilter> candidates))
                continue;

            MeshFilter filter = ClosestUnused(candidates, actor.UnityPosition, used, out float actorError);
            if (filter == null || actorError > 0.05f || filter.sharedMesh == null)
            {
                missingActor++;
                continue;
            }
            used.Add(filter);
            sceneFilterByPrefab.TryGetValue(filter, out MeshFilter sceneFilter);
            Mesh targetMesh = sceneFilter != null && sceneFilter.sharedMesh != null
                ? sceneFilter.sharedMesh
                : filter.sharedMesh;

            if (!sourceByMesh.TryGetValue(actor.mesh, out L2StaticMeshVertexDatabase.MeshData source))
            {
                failedTopology++;
                continue;
            }

            if (!remapByMesh.TryGetValue(targetMesh, out int[] remap))
            {
                remap = BuildVertexRemap(
                    source,
                    targetMesh,
                    out float meshError,
                    out int meshCandidates);
                maxError = Mathf.Max(maxError, meshError);
                maxCandidates = Mathf.Max(maxCandidates, meshCandidates);
                remapByMesh.Add(targetMesh, remap);
            }

            if (remap == null || remap.Length != targetMesh.vertexCount)
            {
                failedTopology++;
                continue;
            }

            byte[][] slots = new byte[L2StaticMeshSunVisibility.SlotCount][];
            for (int slot = 0; slot < slots.Length; slot++)
                slots[slot] = actor.DecodeMask(slot);

            var visibility = new byte[remap.Length];
            for (int vertex = 0; vertex < remap.Length; vertex++)
            {
                int sourceVertex = remap[vertex];
                byte bits = 0;
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    if (sourceVertex < slots[slot].Length * 8 &&
                        (slots[slot][sourceVertex >> 3] & (1 << (sourceVertex & 7))) != 0)
                        bits |= (byte)(1 << slot);
                }
                visibility[vertex] = bits;
            }

            output.Add(new L2StaticMeshSunMaskRuntime.ActorData
            {
                actor = actor.actor,
                mesh = actor.mesh,
                location = actor.location,
                vertexCount = visibility.Length,
                visibility = Convert.ToBase64String(visibility),
                hasUnityPosition = sceneFilter != null,
                unityPosition = sceneFilter != null
                    ? sceneFilter.transform.position
                    : filter.transform.position
            });
        }

        var database = new L2StaticMeshSunMaskRuntime.MapData
        {
            map = map,
            actors = output.ToArray()
        };
        File.WriteAllText(outputPath, JsonUtility.ToJson(database, false));
        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
        if (closeOpenedScene && openedScene.IsValid())
            EditorSceneManager.CloseScene(openedScene, true);
        Debug.Log(
            "Remapped static-mesh sun masks: actors=" + output.Count +
            " missingActor=" + missingActor +
            " failedTopology=" + failedTopology +
            " uniqueMeshes=" + remapByMesh.Count +
            " maxPositionError=" + maxError.ToString("G6") +
            " maxCoincidentCandidates=" + maxCandidates +
            " output=" + outputPath);
    }

    static Dictionary<MeshFilter, MeshFilter> BuildSceneFilterMap(
        string scenePath,
        string prefabPath,
        out Scene scene,
        out bool closeOpenedScene)
    {
        scene = SceneManager.GetSceneByPath(scenePath);
        closeOpenedScene = !scene.IsValid() || !scene.isLoaded;
        if (closeOpenedScene)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        var result = new Dictionary<MeshFilter, MeshFilter>();
        if (!scene.IsValid() || !scene.isLoaded)
            return result;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int r = 0; r < roots.Length; r++)
        {
            MeshFilter[] sceneFilters = roots[r].GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < sceneFilters.Length; i++)
            {
                MeshFilter sceneFilter = sceneFilters[i];
                MeshFilter prefabFilter =
                    PrefabUtility.GetCorrespondingObjectFromSourceAtPath(
                        sceneFilter,
                        prefabPath);
                if (prefabFilter != null)
                    result[prefabFilter] = sceneFilter;
            }
        }
        return result;
    }

    static MeshFilter ClosestUnused(
        List<MeshFilter> candidates,
        Vector3 position,
        HashSet<MeshFilter> used,
        out float bestDistance)
    {
        MeshFilter best = null;
        bestDistance = float.PositiveInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            MeshFilter candidate = candidates[i];
            if (used.Contains(candidate))
                continue;
            float distance = Vector3.Distance(candidate.transform.position, position);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    static int[] BuildVertexRemap(
        L2StaticMeshVertexDatabase.MeshData source,
        Mesh unityMesh,
        out float maxError,
        out int maxCandidates)
    {
        Vector3[] sourcePositions = source.DecodePositions();
        Vector3[] sourceNormals = source.DecodeNormals();
        Vector2[] sourceUv = source.DecodeUv0();
        Vector3[] unityPositions = unityMesh.vertices;
        Vector3[] unityNormals = unityMesh.normals;
        Vector2[] unityUv = unityMesh.uv;
        bool requireUniqueSource = sourcePositions.Length == unityPositions.Length;
        var usedSource = requireUniqueSource ? new bool[sourcePositions.Length] : null;
        var cells = new Dictionary<Cell, List<int>>();
        for (int i = 0; i < sourcePositions.Length; i++)
        {
            Cell cell = Cell.From(sourcePositions[i]);
            if (!cells.TryGetValue(cell, out List<int> list))
            {
                list = new List<int>();
                cells.Add(cell, list);
            }
            list.Add(i);
        }

        var result = new int[unityPositions.Length];
        maxError = 0f;
        maxCandidates = 0;
        for (int vertex = 0; vertex < unityPositions.Length; vertex++)
        {
            Cell center = Cell.From(unityPositions[vertex]);
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            float bestNormalDot = float.NegativeInfinity;
            float bestUvDistance = float.PositiveInfinity;
            int candidateCount = 0;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                var nearby = new Cell(center.x + x, center.y + y, center.z + z);
                if (!cells.TryGetValue(nearby, out List<int> indices))
                    continue;
                for (int n = 0; n < indices.Count; n++)
                {
                    int sourceIndex = indices[n];
                    if (requireUniqueSource && usedSource[sourceIndex])
                        continue;
                    float distance = Vector3.Distance(unityPositions[vertex], sourcePositions[sourceIndex]);
                    if (distance > MaxPositionError)
                        continue;
                    candidateCount++;
                    float normalDot = unityNormals.Length == unityPositions.Length
                        ? Vector3.Dot(unityNormals[vertex].normalized, sourceNormals[sourceIndex])
                        : 0f;
                    float uvDistance = float.PositiveInfinity;
                    if (sourceUv.Length == sourcePositions.Length && unityUv.Length == unityPositions.Length)
                    {
                        Vector2 uv = unityUv[vertex];
                        Vector2 original = sourceUv[sourceIndex];
                        uvDistance = Mathf.Min(
                            (uv - original).sqrMagnitude,
                            (uv - new Vector2(original.x, 1f - original.y)).sqrMagnitude);
                    }

                    bool closerPosition = distance < bestDistance - 0.00001f;
                    bool samePosition = Mathf.Abs(distance - bestDistance) <= 0.00001f;
                    bool closerNormal = normalDot > bestNormalDot + 0.0001f;
                    bool sameNormal = Mathf.Abs(normalDot - bestNormalDot) <= 0.0001f;
                    if (closerPosition ||
                        (samePosition && (closerNormal ||
                            (sameNormal && uvDistance < bestUvDistance))))
                    {
                        best = sourceIndex;
                        bestDistance = distance;
                        bestNormalDot = normalDot;
                        bestUvDistance = uvDistance;
                    }
                }
            }

            if (best < 0)
                return null;
            result[vertex] = best;
            if (requireUniqueSource)
                usedSource[best] = true;
            maxError = Mathf.Max(maxError, bestDistance);
            maxCandidates = Mathf.Max(maxCandidates, candidateCount);
        }
        return result;
    }

    readonly struct Cell : IEquatable<Cell>
    {
        public readonly int x;
        public readonly int y;
        public readonly int z;

        public Cell(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Cell From(Vector3 value)
        {
            return new Cell(
                Mathf.RoundToInt(value.x / PositionCell),
                Mathf.RoundToInt(value.y / PositionCell),
                Mathf.RoundToInt(value.z / PositionCell));
        }

        public bool Equals(Cell other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is Cell other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = x;
                hash = (hash * 397) ^ y;
                return (hash * 397) ^ z;
            }
        }
    }
}
#endif
