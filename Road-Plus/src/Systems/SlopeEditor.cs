// Slope tool (like Move It's "Slope objects" / Network Multitool's slope): re-grades an existing road between
// two nodes. The game's tool pipeline cannot move existing nodes (GenerateNodesSystem keeps their position),
// so this edits the node positions and edge curves directly, the way Move It does, then marks everything
// Updated so the game rebuilds geometry, lanes and terrain. One level of undo is kept.
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using TF2YolAraci.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace TF2YolAraci.Systems
{
    public sealed class SlopePath
    {
        public readonly List<Entity> nodes = new List<Entity>();   // A ... B
        public readonly List<Entity> edges = new List<Entity>();   // edges[i] joins nodes[i] and nodes[i + 1]
        public readonly List<bool> forward = new List<bool>();     // edge curve runs from nodes[i] to nodes[i + 1]
        public PolyCurve path;                                    // edge curves in path order (XZ used for distances)
        public readonly List<double> nodeDistance = new List<double>();
    }

    public sealed class SlopeEditor
    {
        private struct NodeBackup { public Entity entity; public Node node; public bool hasGeo; public NodeGeometry geo; public bool hasElev; public Elevation elev; }
        private struct EdgeBackup { public Entity entity; public Curve curve; public bool hasElev; public Elevation elev; }

        private readonly List<NodeBackup> m_UndoNodes = new List<NodeBackup>();
        private readonly List<EdgeBackup> m_UndoEdges = new List<EdgeBackup>();

        public bool CanUndo => m_UndoNodes.Count > 0 || m_UndoEdges.Count > 0;

        // ---- Path finding ---------------------------------------------------------------------------------------

        /// <summary>Shortest connected route along existing roads from node a to node b (Dijkstra, limited search).</summary>
        public static SlopePath FindPath(EntityManager em, Entity a, Entity b, int maxNodes = 4000)
        {
            if (a == b || a == Entity.Null || b == Entity.Null) return null;
            var dist = new Dictionary<Entity, double> { [a] = 0 };
            var prevNode = new Dictionary<Entity, Entity>();
            var prevEdge = new Dictionary<Entity, Entity>();
            var done = new HashSet<Entity>();
            var open = new List<Entity> { a };

            while (open.Count > 0 && done.Count < maxNodes)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++) if (dist[open[i]] < dist[open[bi]]) bi = i;
                Entity n = open[bi];
                open.RemoveAt(bi);
                if (!done.Add(n)) continue;
                if (n == b) break;
                if (!em.HasBuffer<ConnectedEdge>(n)) continue;

                DynamicBuffer<ConnectedEdge> list = em.GetBuffer<ConnectedEdge>(n, true);
                for (int i = 0; i < list.Length; i++)
                {
                    Entity e = list[i].m_Edge;
                    if (!NetSnap.IsUsable(em, e) || !em.HasComponent<Edge>(e) || !em.HasComponent<Curve>(e)) continue;
                    Edge edge = em.GetComponentData<Edge>(e);
                    Entity other = edge.m_Start == n ? edge.m_End : edge.m_End == n ? edge.m_Start : Entity.Null;
                    if (other == Entity.Null || done.Contains(other) || !NetSnap.IsUsable(em, other)) continue;
                    double d = dist[n] + em.GetComponentData<Curve>(e).m_Length;
                    if (!dist.TryGetValue(other, out double old) || d < old)
                    {
                        dist[other] = d;
                        prevNode[other] = n;
                        prevEdge[other] = e;
                        open.Add(other);
                    }
                }
            }
            if (!prevNode.ContainsKey(b)) return null;

            var result = new SlopePath();
            var nodes = new List<Entity>();
            for (Entity n = b; n != a; n = prevNode[n]) nodes.Add(n);
            nodes.Add(a);
            nodes.Reverse();
            result.nodes.AddRange(nodes);

            result.path = new PolyCurve();
            result.nodeDistance.Add(0);
            for (int i = 1; i < nodes.Count; i++)
            {
                Entity e = prevEdge[nodes[i]];
                Edge edge = em.GetComponentData<Edge>(e);
                bool fwd = edge.m_Start == nodes[i - 1];
                Bezier4x3 c = em.GetComponentData<Curve>(e).m_Bezier;
                if (!fwd) c = MathUtils.Invert(c);
                result.edges.Add(e);
                result.forward.Add(fwd);
                result.path.Add(ToCore(c));
                result.nodeDistance.Add(result.path.Length);
            }
            return result;
        }

        // ---- Planning -------------------------------------------------------------------------------------------

        /// <summary>New curves (in each edge's own direction) and node heights for a height profile along the path.</summary>
        public static void Plan(SlopePath p, List<HeightSample> profile, out List<Bezier4x3> newCurves, out List<float> nodeHeights)
        {
            nodeHeights = new List<float>(p.nodes.Count);
            for (int i = 0; i < p.nodes.Count; i++) nodeHeights.Add((float)RoadMath.HeightAt(profile, p.nodeDistance[i]));

            newCurves = new List<Bezier4x3>(p.edges.Count);
            for (int i = 0; i < p.edges.Count; i++)
            {
                Bezier b = p.path.pieces[i];
                var table = p.path.tables[i];
                double s0 = p.path.starts[i];
                double y13 = RoadMath.HeightAt(profile, s0 + RoadMath.DistanceAtT(table, 1.0 / 3));
                double y23 = RoadMath.HeightAt(profile, s0 + RoadMath.DistanceAtT(table, 2.0 / 3));
                RoadMath.FitY(ref b, nodeHeights[i], y13, y23, nodeHeights[i + 1]);
                Bezier4x3 c = ToGame(b);
                newCurves.Add(p.forward[i] ? c : MathUtils.Invert(c));
            }
        }

        // ---- Apply / undo ----------------------------------------------------------------------------------------

        public void Apply(EntityManager em, SlopePath p, List<Bezier4x3> newCurves, List<float> nodeHeights)
        {
            m_UndoNodes.Clear();
            m_UndoEdges.Clear();
            var touchedEdges = new HashSet<Entity>();
            var pathEdges = new HashSet<Entity>(p.edges);

            // 1) Edges on the path get their new curve.
            for (int i = 0; i < p.edges.Count; i++)
            {
                Entity e = p.edges[i];
                BackupEdge(em, e);
                Curve curve = em.GetComponentData<Curve>(e);
                float2 oldEnds = new float2(curve.m_Bezier.a.y, curve.m_Bezier.d.y);
                curve.m_Bezier = newCurves[i];
                curve.m_Length = MathUtils.Length(curve.m_Bezier);
                em.SetComponentData(e, curve);
                ShiftElevation(em, e, math.csum(new float2(curve.m_Bezier.a.y, curve.m_Bezier.d.y) - oldEnds) * 0.5f);
                touchedEdges.Add(e);
            }

            // 2) Moved nodes; other roads at those nodes follow with their end (the rest of their shape is kept).
            for (int i = 0; i < p.nodes.Count; i++)
            {
                Entity n = p.nodes[i];
                Node node = em.GetComponentData<Node>(n);
                float delta = nodeHeights[i] - node.m_Position.y;
                if (math.abs(delta) < 0.001f) continue;

                BackupNode(em, n);
                node.m_Position.y += delta;
                em.SetComponentData(n, node);
                if (em.HasComponent<NodeGeometry>(n))
                {
                    NodeGeometry geo = em.GetComponentData<NodeGeometry>(n);
                    geo.m_Bounds.min.y += delta; geo.m_Bounds.max.y += delta; geo.m_Offset += delta;
                    em.SetComponentData(n, geo);
                }
                ShiftElevation(em, n, delta);

                if (!em.HasBuffer<ConnectedEdge>(n)) continue;
                DynamicBuffer<ConnectedEdge> list = em.GetBuffer<ConnectedEdge>(n, true);
                var others = new List<Entity>();
                for (int k = 0; k < list.Length; k++) others.Add(list[k].m_Edge);
                foreach (Entity e in others)
                {
                    if (pathEdges.Contains(e) || !em.HasComponent<Edge>(e) || !em.HasComponent<Curve>(e)) continue;
                    Edge edge = em.GetComponentData<Edge>(e);
                    if (edge.m_Start != n && edge.m_End != n) continue;
                    if (!touchedEdges.Contains(e)) BackupEdge(em, e);
                    Curve curve = em.GetComponentData<Curve>(e);
                    if (edge.m_Start == n) { curve.m_Bezier.a.y += delta; curve.m_Bezier.b.y += delta * (2f / 3f); curve.m_Bezier.c.y += delta / 3f; }
                    else { curve.m_Bezier.d.y += delta; curve.m_Bezier.c.y += delta * (2f / 3f); curve.m_Bezier.b.y += delta / 3f; }
                    curve.m_Length = MathUtils.Length(curve.m_Bezier);
                    em.SetComponentData(e, curve);
                    ShiftElevation(em, e, delta * 0.5f);
                    touchedEdges.Add(e);
                }
            }

            MarkUpdated(em, p.nodes, touchedEdges);
        }

        public bool Undo(EntityManager em)
        {
            if (!CanUndo) return false;
            var nodes = new List<Entity>();
            var edges = new HashSet<Entity>();
            foreach (NodeBackup b in m_UndoNodes)
            {
                if (!em.Exists(b.entity) || !em.HasComponent<Node>(b.entity)) continue;
                em.SetComponentData(b.entity, b.node);
                if (b.hasGeo && em.HasComponent<NodeGeometry>(b.entity)) em.SetComponentData(b.entity, b.geo);
                if (b.hasElev && em.HasComponent<Elevation>(b.entity)) em.SetComponentData(b.entity, b.elev);
                nodes.Add(b.entity);
            }
            foreach (EdgeBackup b in m_UndoEdges)
            {
                if (!em.Exists(b.entity) || !em.HasComponent<Curve>(b.entity)) continue;
                em.SetComponentData(b.entity, b.curve);
                if (b.hasElev && em.HasComponent<Elevation>(b.entity)) em.SetComponentData(b.entity, b.elev);
                edges.Add(b.entity);
                Edge edge = em.GetComponentData<Edge>(b.entity);
                nodes.Add(edge.m_Start);
                nodes.Add(edge.m_End);
            }
            MarkUpdated(em, nodes, edges);
            m_UndoNodes.Clear();
            m_UndoEdges.Clear();
            return true;
        }

        private void BackupNode(EntityManager em, Entity n)
        {
            var b = new NodeBackup { entity = n, node = em.GetComponentData<Node>(n) };
            if (em.HasComponent<NodeGeometry>(n)) { b.hasGeo = true; b.geo = em.GetComponentData<NodeGeometry>(n); }
            if (em.HasComponent<Elevation>(n)) { b.hasElev = true; b.elev = em.GetComponentData<Elevation>(n); }
            m_UndoNodes.Add(b);
        }

        private void BackupEdge(EntityManager em, Entity e)
        {
            var b = new EdgeBackup { entity = e, curve = em.GetComponentData<Curve>(e) };
            if (em.HasComponent<Elevation>(e)) { b.hasElev = true; b.elev = em.GetComponentData<Elevation>(e); }
            m_UndoEdges.Add(b);
        }

        /// <summary>Elevated pieces (bridges) store their height above ground; keep it in step with the move.</summary>
        private static void ShiftElevation(EntityManager em, Entity e, float delta)
        {
            if (!em.HasComponent<Elevation>(e) || math.abs(delta) < 0.001f) return;
            Elevation elev = em.GetComponentData<Elevation>(e);
            elev.m_Elevation += delta;
            em.SetComponentData(e, elev);
        }

        /// <summary>Same tagging as Move It: nodes, their edges, the far nodes of those edges and road aggregates.</summary>
        private static void MarkUpdated(EntityManager em, IEnumerable<Entity> nodes, IEnumerable<Entity> edges)
        {
            var tag = new HashSet<Entity>();
            foreach (Entity n in nodes)
            {
                if (!em.Exists(n)) continue;
                tag.Add(n);
                if (!em.HasBuffer<ConnectedEdge>(n)) continue;
                DynamicBuffer<ConnectedEdge> list = em.GetBuffer<ConnectedEdge>(n, true);
                for (int i = 0; i < list.Length; i++)
                {
                    Entity e = list[i].m_Edge;
                    tag.Add(e);
                    if (em.HasComponent<Edge>(e)) { Edge edge = em.GetComponentData<Edge>(e); tag.Add(edge.m_Start); tag.Add(edge.m_End); }
                    if (em.HasComponent<Aggregated>(e)) tag.Add(em.GetComponentData<Aggregated>(e).m_Aggregate);
                }
            }
            foreach (Entity e in edges)
            {
                if (!em.Exists(e)) continue;
                tag.Add(e);
                if (em.HasComponent<Edge>(e)) { Edge edge = em.GetComponentData<Edge>(e); tag.Add(edge.m_Start); tag.Add(edge.m_End); }
            }
            foreach (Entity e in tag)
            {
                if (e == Entity.Null || !em.Exists(e)) continue;
                if (!em.HasComponent<Updated>(e)) em.AddComponent<Updated>(e);
                if (!em.HasComponent<BatchesUpdated>(e)) em.AddComponent<BatchesUpdated>(e);
            }
        }

        public static Bezier ToCore(Bezier4x3 c) => new Bezier(V(c.a), V(c.b), V(c.c), V(c.d));
        public static Bezier4x3 ToGame(Bezier b) => new Bezier4x3(F(b.a), F(b.b), F(b.c), F(b.d));
        private static Vec3 V(float3 f) => new Vec3(f.x, f.y, f.z);
        private static float3 F(Vec3 v) => new float3((float)v.x, (float)v.y, (float)v.z);
    }
}
