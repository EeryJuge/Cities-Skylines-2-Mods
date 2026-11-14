// Snapping to existing roads: turns a raycast hit into a node or a point on an edge, the same way the game's
// own road tool fills CoursePos.m_Entity / m_SplitPosition (NetToolSystem.GetCoursePos).
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TF2YolAraci.Systems
{
    public struct SnapPoint
    {
        public bool valid;        // snapped to an existing network
        public bool isNode;
        public Entity entity;     // node, or edge when the point lies inside an edge
        public float split;       // curve position on the edge (0 for nodes)
        public float3 position;
        public bool hasOutward;   // the node is a dead end: outward = direction the existing road continues in
        public float3 outward;

        public static SnapPoint Free(float3 position) => new SnapPoint { position = position };
    }

    public static class NetSnap
    {
        /// <summary>An existing, finished network piece (not a preview, not being deleted, not part of a building).</summary>
        public static bool IsUsable(EntityManager em, Entity e) =>
            e != Entity.Null && em.Exists(e) && !em.HasComponent<Temp>(e) && !em.HasComponent<Deleted>(e) && !em.HasComponent<Owner>(e);

        /// <summary>
        /// Converts the raycast hit. Near an edge end the point snaps to that node; elsewhere on the edge it becomes
        /// a split point (the game will create a junction there) unless nodesOnly is set, then the nearest end is used.
        /// </summary>
        public static SnapPoint Resolve(EntityManager em, ControlPoint hit, bool nodesOnly)
        {
            Entity e = hit.m_OriginalEntity;
            if (e != Entity.Null && em.HasComponent<Temp>(e))
                e = em.GetComponentData<Temp>(e).m_Original; // our own preview was hit: use what it replaces, if anything
            if (!IsUsable(em, e)) return SnapPoint.Free(hit.m_Position);

            if (em.HasComponent<Node>(e))
                return NodePoint(em, e);

            if (!em.HasComponent<Edge>(e) || !em.HasComponent<Curve>(e)) return SnapPoint.Free(hit.m_Position);

            Edge edge = em.GetComponentData<Edge>(e);
            Bezier4x3 curve = em.GetComponentData<Curve>(e).m_Bezier;
            float t = math.saturate(hit.m_CurvePosition);
            float3 onCurve = MathUtils.Position(curve, t);
            float snap = SnapDistance(em, e);

            float dStart = math.distance(onCurve.xz, curve.a.xz), dEnd = math.distance(onCurve.xz, curve.d.xz);
            if (nodesOnly || math.min(dStart, dEnd) <= snap)
                return NodePoint(em, dStart <= dEnd ? edge.m_Start : edge.m_End);

            return new SnapPoint { valid = true, entity = e, split = t, position = onCurve };
        }

        public static SnapPoint NodePoint(EntityManager em, Entity node)
        {
            var p = new SnapPoint { valid = true, isNode = true, entity = node, position = em.GetComponentData<Node>(node).m_Position };
            p.hasOutward = Outward(em, node, out p.outward);
            return p;
        }

        /// <summary>For a dead-end node (one road ends there): the direction that road would continue in.</summary>
        public static bool Outward(EntityManager em, Entity node, out float3 dir)
        {
            dir = default(float3);
            if (!em.HasBuffer<ConnectedEdge>(node)) return false;
            DynamicBuffer<ConnectedEdge> edges = em.GetBuffer<ConnectedEdge>(node, true);
            int count = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity e = edges[i].m_Edge;
                if (!IsUsable(em, e) || !em.HasComponent<Edge>(e)) continue;
                Edge edge = em.GetComponentData<Edge>(e);
                Bezier4x3 curve = em.GetComponentData<Curve>(e).m_Bezier;
                if (edge.m_Start == node) { dir = -MathUtils.StartTangent(curve); count++; }
                else if (edge.m_End == node) { dir = MathUtils.EndTangent(curve); count++; }
            }
            if (count != 1 || math.lengthsq(dir.xz) < 1e-6f) return false;
            dir = new float3(dir.x, 0f, dir.z);
            dir = math.normalize(dir);
            return true;
        }

        private static float SnapDistance(EntityManager em, Entity edge)
        {
            float width = 8f;
            if (em.HasComponent<PrefabRef>(edge))
            {
                Entity prefab = em.GetComponentData<PrefabRef>(edge).m_Prefab;
                if (em.HasComponent<NetGeometryData>(prefab)) width = em.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth;
            }
            return math.max(6f, width * 0.5f + 3f);
        }

        /// <summary>Finds a finished node at this position (used to continue a chain from the node the game just built).</summary>
        public static Entity FindNodeAt(EntityManager em, EntityQuery nodeQuery, float3 position, float radius)
        {
            NativeArray<Entity> entities = nodeQuery.ToEntityArray(Allocator.Temp);
            NativeArray<Node> nodes = nodeQuery.ToComponentDataArray<Node>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestDist = radius * radius;
            for (int i = 0; i < nodes.Length; i++)
            {
                float d = math.distancesq(nodes[i].m_Position, position);
                if (d <= bestDist) { bestDist = d; best = entities[i]; }
            }
            entities.Dispose();
            nodes.Dispose();
            return best;
        }
    }
}
