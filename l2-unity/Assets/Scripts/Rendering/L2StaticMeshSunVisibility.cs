using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the eight per-vertex sun visibility masks used by
/// UStaticMesh::Illuminate and InterpolateSunLight.
/// </summary>
public static class L2StaticMeshSunVisibility
{
    public const int SlotCount = 8;
    public const float HoursPerDay = 24f;
    public const float TraceLengthUu = 262144f * 1.375f;
    public const int MaxTraceHits = 64;

    public sealed class MaskSet
    {
        public readonly int VertexCount;
        public readonly byte[][] Slots;
        public readonly Vector3[] IncomingDirections;

        public MaskSet(int vertexCount)
        {
            VertexCount = vertexCount;
            Slots = new byte[SlotCount][];
            IncomingDirections = new Vector3[SlotCount];
            int byteCount = (vertexCount + 7) >> 3;
            for (int i = 0; i < SlotCount; i++)
                Slots[i] = new byte[byteCount];
        }

        public bool IsEnabled(int slot, int vertex)
        {
            return (Slots[slot][vertex >> 3] & (1 << (vertex & 7))) != 0;
        }

        public void SetEnabled(int slot, int vertex)
        {
            Slots[slot][vertex >> 3] |= (byte)(1 << (vertex & 7));
        }
    }

    /// <summary>
    /// Returns the center of an ActorStaticLight slot: 1.5, 4.5, ... 22.5
    /// for the original eight-slot configuration.
    /// </summary>
    public static float GetSlotCenterHours(int slot)
    {
        float slotHours = HoursPerDay / SlotCount;
        return slot * slotHours + slotHours * 0.5f;
    }

    /// <summary>
    /// GetSafeDirectionForIntMap: negate GetSunLightDirection, prevent an
    /// upward incoming ray, then normalize. Unity Y corresponds to UE Z.
    /// </summary>
    public static Vector3 GetIncomingDirection(int slot)
    {
        L2CelestialLut.EnsureLoaded();
        Vector3 incoming = -L2CelestialLut.SampleAt(GetSlotCenterHours(slot)).sunDirUnity;
        if (incoming.y > 0f)
            incoming.y = 0.0001f;
        return incoming.sqrMagnitude > 1e-8f ? incoming.normalized : Vector3.down;
    }

    public static MaskSet Bake(
        Mesh mesh,
        Transform meshTransform,
        PhysicsScene physicsScene,
        ICollection<Collider> ownerColliders,
        int collisionMask = Physics.DefaultRaycastLayers,
        float traceScale = 1f)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));
        if (meshTransform == null)
            throw new ArgumentNullException(nameof(meshTransform));
        if (!mesh.isReadable)
            throw new InvalidOperationException("Mesh must be readable: " + mesh.name);
        if (!physicsScene.IsValid())
            throw new ArgumentException("Physics scene is not valid.", nameof(physicsScene));

        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length != positions.Length)
            throw new InvalidOperationException("Mesh normals do not match vertices: " + mesh.name);

        var result = new MaskSet(positions.Length);
        var hits = new RaycastHit[MaxTraceHits];
        float traceLength = TraceLengthUu * L2StaticMeshVertexLight.UuToMeters * Mathf.Max(traceScale, 0.0001f);

        for (int slot = 0; slot < SlotCount; slot++)
        {
            Vector3 incoming = GetIncomingDirection(slot);
            result.IncomingDirections[slot] = incoming;
            Vector3 directionToSun = -incoming;

            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                Vector3 worldNormal = meshTransform.TransformDirection(normals[vertex]).normalized;

                // Illuminate rejects a vertex when dot(safeDirection, normal) > 0.
                if (Vector3.Dot(incoming, worldNormal) > 0f)
                    continue;

                Vector3 worldPosition = meshTransform.TransformPoint(positions[vertex]);
                if (!IsOccluded(
                        physicsScene,
                        worldPosition,
                        directionToSun,
                        traceLength,
                        collisionMask,
                        ownerColliders,
                        hits))
                {
                    result.SetEnabled(slot, vertex);
                }
            }
        }

        return result;
    }

    static bool IsOccluded(
        PhysicsScene physicsScene,
        Vector3 origin,
        Vector3 direction,
        float distance,
        int collisionMask,
        ICollection<Collider> ownerColliders,
        RaycastHit[] hits)
    {
        int count = physicsScene.Raycast(
            origin,
            direction,
            hits,
            distance,
            collisionMask,
            QueryTriggerInteraction.Ignore);

        if (count <= 0)
            return false;

        Array.Sort(hits, 0, count, RaycastHitDistanceComparer.Instance);
        int inspected = Mathf.Min(count, MaxTraceHits);
        for (int i = 0; i < inspected; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null)
                continue;
            if (ownerColliders != null && ownerColliders.Contains(collider))
                continue;
            return true;
        }

        return false;
    }

    sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
    {
        public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();

        public int Compare(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
