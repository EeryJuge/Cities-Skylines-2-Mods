using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Su derinliğini bir ızgarada örnekleyip kıyı çizgilerini kapalı çokgenler olarak çıkarır (marching squares).
    /// Her çokgen bir iç içelik derinliğiyle döner: 0 = deniz/göl, 1 = içindeki ada, 2 = adanın içindeki göl...
    /// </summary>
    internal static class WaterContours
    {
        public struct Loop
        {
            public int Depth;
            public List<float2> Points; // dünya x, z
        }

        private const int kMaxCells = 220;

        /// <param name="isWater">Bir noktadaki değer; 0'dan büyükse su sayılır.</param>
        public static List<Loop> Build(float2 min, float2 max, float cell, Func<float2, float> isWater)
        {
            var result = new List<Loop>();
            int nx = (int)math.ceil((max.x - min.x) / cell) + 1;
            int nz = (int)math.ceil((max.y - min.y) / cell) + 1;
            if (nx > kMaxCells || nz > kMaxCells)
            {
                cell = math.max((max.x - min.x), (max.y - min.y)) / (kMaxCells - 1);
                nx = (int)math.ceil((max.x - min.x) / cell) + 1;
                nz = (int)math.ceil((max.y - min.y) / cell) + 1;
            }
            if (nx < 2 || nz < 2) return result;

            // Kenarlara kara şeridi eklenir, böylece her kıyı çizgisi kapanır.
            int W = nx + 2, H = nz + 2;
            var g = new float[W, H];
            bool any = false;
            for (int i = 0; i < W; i++)
                for (int j = 0; j < H; j++)
                {
                    if (i == 0 || j == 0 || i == W - 1 || j == H - 1) { g[i, j] = -1f; continue; }
                    float v = isWater(new float2(min.x + (i - 1) * cell, min.y + (j - 1) * cell));
                    if (float.IsNaN(v)) v = -1f;
                    g[i, j] = v;
                    if (v > 0f) any = true;
                }
            if (!any) return result;

            var pos = new Dictionary<long, float2>();
            var adj = new Dictionary<long, List<long>>();

            Func<int, int, float2> world = (i, j) => new float2(min.x + (i - 1) * cell, min.y + (j - 1) * cell);
            Func<int, int, long> hKey = (i, j) => ((long)j * W + i) * 2;
            Func<int, int, long> vKey = (i, j) => ((long)j * W + i) * 2 + 1;

            Func<long, int, int, int, int, long> node = (key, i0, j0, i1, j1) =>
            {
                if (!pos.ContainsKey(key))
                {
                    float a = g[i0, j0], b = g[i1, j1];
                    float t = math.abs(a - b) < 1e-6f ? 0.5f : math.clamp(a / (a - b), 0f, 1f);
                    pos[key] = math.lerp(world(i0, j0), world(i1, j1), t);
                }
                return key;
            };
            Action<long, long> seg = (p, q) =>
            {
                List<long> l;
                if (!adj.TryGetValue(p, out l)) adj[p] = l = new List<long>(2);
                l.Add(q);
                if (!adj.TryGetValue(q, out l)) adj[q] = l = new List<long>(2);
                l.Add(p);
            };

            for (int i = 0; i < W - 1; i++)
                for (int j = 0; j < H - 1; j++)
                {
                    float a = g[i, j], b = g[i + 1, j], c = g[i + 1, j + 1], d = g[i, j + 1];
                    int idx = (a > 0 ? 1 : 0) | (b > 0 ? 2 : 0) | (c > 0 ? 4 : 0) | (d > 0 ? 8 : 0);
                    if (idx == 0 || idx == 15) continue;
                    // a sol alt, b sağ alt, c sağ üst, d sol üst
                    Func<long> B = () => node(hKey(i, j), i, j, i + 1, j);
                    Func<long> R = () => node(vKey(i + 1, j), i + 1, j, i + 1, j + 1);
                    Func<long> T = () => node(hKey(i, j + 1), i, j + 1, i + 1, j + 1);
                    Func<long> L = () => node(vKey(i, j), i, j, i, j + 1);
                    bool center = (a + b + c + d) * 0.25f > 0f;
                    switch (idx)
                    {
                        case 1: case 14: seg(L(), B()); break;
                        case 2: case 13: seg(B(), R()); break;
                        case 3: case 12: seg(L(), R()); break;
                        case 4: case 11: seg(R(), T()); break;
                        case 6: case 9: seg(B(), T()); break;
                        case 7: case 8: seg(L(), T()); break;
                        case 5: // a ve c su
                            if (center) { seg(B(), R()); seg(T(), L()); } else { seg(L(), B()); seg(R(), T()); }
                            break;
                        case 10: // b ve d su
                            if (center) { seg(L(), B()); seg(R(), T()); } else { seg(B(), R()); seg(T(), L()); }
                            break;
                    }
                }

            // Kenarları kapalı halkalara bağla
            var seen = new HashSet<long>();
            var loops = new List<List<float2>>();
            foreach (long start in adj.Keys)
            {
                if (seen.Contains(start)) continue;
                var pts = new List<float2>();
                long prev = -1, cur = start;
                for (int guard = 0; guard < 200000; guard++)
                {
                    seen.Add(cur);
                    pts.Add(pos[cur]);
                    List<long> nb = adj[cur];
                    long next = -1;
                    for (int k = 0; k < nb.Count; k++)
                        if (nb[k] != prev && !seen.Contains(nb[k])) { next = nb[k]; break; }
                    if (next < 0) break;
                    prev = cur; cur = next;
                }
                if (pts.Count < 4) continue;
                List<float2> simple = SimplifyClosed(pts, cell * 0.6f);
                if (simple.Count < 3 || math.abs(Area(simple)) < cell * cell * 6f) continue;
                loops.Add(simple);
            }

            // İç içelik: bir halka kaç halkanın içinde?
            for (int a = 0; a < loops.Count; a++)
            {
                int depth = 0;
                float2 p = loops[a][0];
                for (int b = 0; b < loops.Count; b++)
                    if (a != b && Contains(loops[b], p)) depth++;
                result.Add(new Loop { Depth = depth, Points = loops[a] });
            }
            result.Sort((x, y) => x.Depth.CompareTo(y.Depth));
            return result;
        }

        private static float Area(List<float2> p)
        {
            float s = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) s += (p[j].x * p[i].y) - (p[i].x * p[j].y);
            return s * 0.5f;
        }

        private static bool Contains(List<float2> poly, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                float2 a = poly[i], b = poly[j];
                if (((a.y > p.y) != (b.y > p.y)) && (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)) inside = !inside;
            }
            return inside;
        }

        private static List<float2> SimplifyClosed(List<float2> pts, float tol)
        {
            int n = pts.Count, far = 0;
            float best = -1f;
            for (int i = 1; i < n; i++)
            {
                float d = math.distancesq(pts[0], pts[i]);
                if (d > best) { best = d; far = i; }
            }
            var keep = new bool[n];
            keep[0] = keep[far] = true;
            Dp(pts, 0, far, tol, keep);
            var tail = new List<float2>(pts.GetRange(far, n - far)) { pts[0] };
            var keepTail = new bool[tail.Count];
            keepTail[0] = keepTail[tail.Count - 1] = true;
            Dp(tail, 0, tail.Count - 1, tol, keepTail);

            var outPts = new List<float2>();
            for (int i = 0; i < far; i++) if (keep[i]) outPts.Add(pts[i]);
            for (int i = 0; i < tail.Count - 1; i++) if (keepTail[i]) outPts.Add(tail[i]);
            return outPts;
        }

        private static void Dp(List<float2> p, int s, int e, float tol, bool[] keep)
        {
            if (e <= s + 1) return;
            float2 a = p[s], b = p[e], ab = b - a;
            float len = math.length(ab);
            int idx = -1;
            float max = tol;
            for (int i = s + 1; i < e; i++)
            {
                float d = len < 1e-4f ? math.distance(p[i], a) : math.abs(ab.x * (a.y - p[i].y) - ab.y * (a.x - p[i].x)) / len;
                if (d > max) { max = d; idx = i; }
            }
            if (idx < 0) return;
            keep[idx] = true;
            Dp(p, s, idx, tol, keep);
            Dp(p, idx, e, tol, keep);
        }
    }
}
