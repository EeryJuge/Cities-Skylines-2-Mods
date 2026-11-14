// TF2YolAraci - oyundan bağımsız yol matematiği.
// Bu dosya Cities: Skylines II DLL'lerine dokunmaz; bu yüzden oyun dışında derlenip test edilebilir.
// Oyun tarafı (TF2RoadToolSystem) Unity.Mathematics tiplerini buradaki Vec3'e çevirip kullanır.

using System;
using System.Collections.Generic;

namespace TF2YolAraci.Core
{
    public struct Vec3
    {
        public double x, y, z;
        public Vec3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.x * s, a.y * s, a.z * s);

        /// <summary>Yatay düzlemdeki (XZ) uzunluk.</summary>
        public double LengthXZ => Math.Sqrt(x * x + z * z);
        public Vec3 FlatXZ => new Vec3(x, 0, z);
        public Vec3 NormalizedXZ { get { double l = LengthXZ; return l < 1e-9 ? new Vec3(0, 0, 0) : new Vec3(x / l, 0, z / l); } }
        public static double DotXZ(Vec3 a, Vec3 b) => a.x * b.x + a.z * b.z;
        public static double CrossY(Vec3 a, Vec3 b) => a.z * b.x - a.x * b.z;
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    /// <summary>Kübik Bezier eğrisi; oyunun Bezier4x3'ünün karşılığı.</summary>
    public struct Bezier
    {
        public Vec3 a, b, c, d;
        public Bezier(Vec3 a, Vec3 b, Vec3 c, Vec3 d) { this.a = a; this.b = b; this.c = c; this.d = d; }

        public Vec3 Position(double t)
        {
            double u = 1 - t;
            return a * (u * u * u) + b * (3 * u * u * t) + c * (3 * u * t * t) + d * (t * t * t);
        }

        public Vec3 Tangent(double t)
        {
            double u = 1 - t;
            return (b - a) * (3 * u * u) + (c - b) * (6 * u * t) + (d - c) * (3 * t * t);
        }

        /// <summary>De Casteljau ile [t0, t1] aralığını ayrı bir Bezier olarak keser.</summary>
        public Bezier Cut(double t0, double t1)
        {
            Bezier right = Split(this, t0).Item2;
            double local = t0 >= 1 ? 0 : (t1 - t0) / (1 - t0);
            return Split(right, local).Item1;
        }

        static Tuple<Bezier, Bezier> Split(Bezier z, double t)
        {
            Vec3 ab = Lerp(z.a, z.b, t), bc = Lerp(z.b, z.c, t), cd = Lerp(z.c, z.d, t);
            Vec3 abc = Lerp(ab, bc, t), bcd = Lerp(bc, cd, t), m = Lerp(abc, bcd, t);
            return Tuple.Create(new Bezier(z.a, ab, abc, m), new Bezier(m, bcd, cd, z.d));
        }

        static Vec3 Lerp(Vec3 p, Vec3 q, double t) => p + (q - p) * t;
    }

    public enum HeightMode { Terrain, Graded, Flat }
    public enum SmoothingLevel { Light, Medium, Strong }
    public enum RoadClass { Highway, Ramp, Other, Rail }

    [Flags]
    public enum SampleIssue
    {
        None = 0,
        TooSteep = 1,      // eğim, yol tipinin sınırını aşıyor
        Floating = 2,      // köprü değilken araziden fazla yukarıda
        Buried = 4,        // tünel değilken arazinin içinde
    }

    public struct HeightSample
    {
        public double distance;   // eğri boyunca mesafe (m)
        public Vec3 position;     // dünya konumu, y = yolun yüksekliği
        public double terrain;    // o noktadaki arazi yüksekliği
        public double slope;      // önceki örnekten bu örneğe eğim (0.05 = %5)
        public SampleIssue issues;
    }

    public class RoadSettings
    {
        public double highwaySlopeLimit = 0.05;
        public double rampSlopeLimit = 0.10;     // otoyol rampaları ana otoyoldan dik olabilir
        public double otherSlopeLimit = 0.10;
        public double railSlopeLimit = 0.05;     // tren, tramvay ve metro rayları
        public double floatingThreshold = 4.0;   // m; bunun üstü "havada"
        public double buriedThreshold = 1.0;     // m; bunun altı "gömülü"
        public double maxSegmentLength = 80.0;   // m; uzun yol bu boyda segmentlere bölünür
        public double sampleSpacing = 4.0;       // m; yükseklik örnekleme aralığı
        public double lightWindow = 16.0, mediumWindow = 48.0, strongWindow = 120.0; // yumuşatma pencereleri (m)
        public double maxTurnDegrees = 90.0;     // tek parçada izin verilen en büyük dönüş (teğet ile kiriş arası)
        public double earthworkMax = 6.0;        // m; yol-arazi farkı bunun altındaysa arazi eğimle düzeltilir (dolgu / yarma)

        public double SlopeLimit(RoadClass c) =>
            c == RoadClass.Highway ? highwaySlopeLimit : c == RoadClass.Ramp ? rampSlopeLimit
            : c == RoadClass.Rail ? railSlopeLimit : otherSlopeLimit;

        /// <summary>Eğimli modda kullanılabilen adımlar: sınırı aşmayan ±%5 / ±%10 adımları (otoyolda ±%5).</summary>
        public double[] AllowedGrades(RoadClass c)
        {
            double limit = SlopeLimit(c);
            var list = new List<double>();
            foreach (double g in new[] { -0.10, -0.05, 0.05, 0.10 })
                if (Math.Abs(g) <= limit + 1e-9) list.Add(g);
            return list.ToArray();
        }

        public double SmoothingWindow(SmoothingLevel l) =>
            l == SmoothingLevel.Light ? lightWindow : l == SmoothingLevel.Medium ? mediumWindow : strongWindow;
    }

    public static class RoadMath
    {
        /// <summary>
        /// TF2 tarzı eğri: başlangıç yönü verilmişse o yöne teğet çıkan ve bitiş noktasından geçen
        /// dairesel yayın Bezier yaklaşımı; verilmemişse düz çizgi. Yalnızca XZ düzlemi; y sonradan atanır.
        /// Dönüş çok büyükse (teğet ile kiriş arası açı sınırı aşıyorsa) false döner.
        /// </summary>
        public static bool BuildCurve(Vec3 start, Vec3? startDirection, Vec3 end, RoadSettings s, out Bezier curve, out Vec3 endDirection)
        {
            Vec3 p0 = start.FlatXZ, p3 = end.FlatXZ;
            Vec3 chord = p3 - p0;
            double len = chord.LengthXZ;
            Vec3 c = chord.NormalizedXZ;
            curve = default(Bezier);
            endDirection = c;
            if (len < 1e-3) return false;

            if (startDirection == null || startDirection.Value.LengthXZ < 1e-9)
            {
                curve = new Bezier(p0, p0 + chord * (1.0 / 3), p0 + chord * (2.0 / 3), p3);
                return true;
            }

            Vec3 d0 = startDirection.Value.NormalizedXZ;
            double cosA = Clamp(Vec3.DotXZ(d0, c), -1, 1);
            double alpha = Math.Acos(cosA); // teğet ile kiriş arası açı = dönüşün yarısı
            if (alpha * 180.0 / Math.PI > s.maxTurnDegrees + 1e-9) return false;

            // Daire yayında bitiş teğeti, başlangıç teğetinin kirişe göre yansımasıdır.
            Vec3 d3 = c * (2 * cosA) - d0;
            endDirection = d3.NormalizedXZ;

            // Yay için Bezier tutamak boyu: (4/3)·tan(θ/4)·R, θ = 2α, R = |kiriş| / (2·sin α).
            double h = alpha < 1e-6 ? len / 3.0 : (4.0 / 3.0) * Math.Tan(alpha / 2) * len / (2 * Math.Sin(alpha));
            curve = new Bezier(p0, p0 + d0 * h, p3 - endDirection * h, p3);
            return true;
        }

        /// <summary>Eğrinin yay uzunluğu tablosu: t ve mesafe çiftleri.</summary>
        public static List<KeyValuePair<double, double>> ArcTable(Bezier b, int steps = 256)
        {
            var table = new List<KeyValuePair<double, double>>(steps + 1) { new KeyValuePair<double, double>(0, 0) };
            Vec3 prev = b.Position(0);
            double acc = 0;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                Vec3 p = b.Position(t);
                acc += (p - prev).LengthXZ;
                table.Add(new KeyValuePair<double, double>(t, acc));
                prev = p;
            }
            return table;
        }

        public static double TAtDistance(List<KeyValuePair<double, double>> table, double dist)
        {
            if (dist <= 0) return 0;
            double total = table[table.Count - 1].Value;
            if (dist >= total) return 1;
            int lo = 0, hi = table.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (table[mid].Value < dist) lo = mid; else hi = mid; }
            double span = table[hi].Value - table[lo].Value;
            double f = span < 1e-12 ? 0 : (dist - table[lo].Value) / span;
            return table[lo].Key + (table[hi].Key - table[lo].Key) * f;
        }

        /// <summary>
        /// Yükseklik profili. terrainAt: (x, z) → arazi yüksekliği. startHeight: başlangıç düğümünün yüksekliği.
        /// endHeight: bitişte mevcut bir düğüme bağlanılıyorsa onun yüksekliği (Arazi modunda uca sabitlenir).
        /// grade: yalnızca Eğimli modda kullanılır (ör. 0.05).
        /// endOffset: oyuncunun Page Up / Page Down ile bitiş düğümüne verdiği yükseklik farkı (m). Başlangıç sabit kalır;
        /// fark yol boyunca doğrusal dağıtılır, yani her modda bitiş düğümü tam bu kadar yükselir.
        /// </summary>
        public static List<HeightSample> BuildProfile(Bezier curve, Func<double, double, double> terrainAt, double startHeight,
            double? endHeight, HeightMode mode, double grade, SmoothingLevel smoothing, RoadClass roadClass, RoadSettings s,
            double endOffset = 0)
            => BuildProfile(new PolyCurve(curve), terrainAt, startHeight, endHeight, mode, grade, smoothing, roadClass, s, endOffset);

        /// <summary>Birden çok parçalı yol (ör. spiral) için yükseklik profili; parametreler tek eğrili sürümle aynı.</summary>
        public static List<HeightSample> BuildProfile(PolyCurve path, Func<double, double, double> terrainAt, double startHeight,
            double? endHeight, HeightMode mode, double grade, SmoothingLevel smoothing, RoadClass roadClass, RoadSettings s,
            double endOffset = 0)
        {
            double total = path.Length;
            int n = Math.Max(2, (int)Math.Ceiling(total / s.sampleSpacing) + 1);
            var samples = new List<HeightSample>(n);
            for (int i = 0; i < n; i++)
            {
                double dist = total * i / (n - 1);
                Vec3 p = path.PositionAt(dist);
                samples.Add(new HeightSample { distance = dist, position = p, terrain = terrainAt(p.x, p.z) });
            }

            double[] heights = new double[n];
            if (mode == HeightMode.Flat)
            {
                for (int i = 0; i < n; i++) heights[i] = startHeight;
            }
            else if (mode == HeightMode.Graded)
            {
                for (int i = 0; i < n; i++) heights[i] = startHeight + grade * samples[i].distance;
            }
            else
            {
                double window = s.SmoothingWindow(smoothing);
                double[] smooth = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double sum = 0; int cnt = 0;
                    for (int j = 0; j < n; j++)
                        if (Math.Abs(samples[j].distance - samples[i].distance) <= window / 2) { sum += samples[j].terrain; cnt++; }
                    smooth[i] = sum / cnt;
                }
                // Uçları sabitle: başlangıç düğüm yüksekliğine, bitiş (varsa) bağlanılan düğüme;
                // aradaki fark mesafeye göre doğrusal dağıtılır, böylece yumuşatılmış şekil korunur.
                double endTarget = endHeight ?? smooth[n - 1];
                double d0 = startHeight - smooth[0], d1 = endTarget - smooth[n - 1];
                for (int i = 0; i < n; i++)
                {
                    double f = total < 1e-9 ? 0 : samples[i].distance / total;
                    heights[i] = smooth[i] + d0 * (1 - f) + d1 * f;
                }
            }

            if (Math.Abs(endOffset) > 1e-9)
            {
                for (int i = 0; i < n; i++)
                    heights[i] += endOffset * (total < 1e-9 ? 0 : samples[i].distance / total);
            }

            double limit = s.SlopeLimit(roadClass);
            for (int i = 0; i < n; i++)
            {
                HeightSample h = samples[i];
                h.position.y = heights[i];
                if (i > 0)
                {
                    double run = h.distance - samples[i - 1].distance;
                    h.slope = run < 1e-9 ? 0 : (heights[i] - heights[i - 1]) / run;
                }
                if (Math.Abs(h.slope) > limit + 1e-9) h.issues |= SampleIssue.TooSteep;
                double above = heights[i] - h.terrain;
                if (above > s.floatingThreshold) h.issues |= SampleIssue.Floating;
                if (above < -s.buriedThreshold) h.issues |= SampleIssue.Buried;
                samples[i] = h;
            }
            if (n > 1) { var first = samples[0]; first.slope = samples[1].slope; samples[0] = first; }
            return samples;
        }

        /// <summary>Eğimli modda seçilen adım bu yol tipi için izinli mi?</summary>
        public static bool IsGradeAllowed(double grade, RoadClass roadClass, RoadSettings s)
        {
            if (Math.Abs(grade) < 1e-9) return true; // %0 = Düz
            foreach (double g in s.AllowedGrades(roadClass)) if (Math.Abs(g - grade) < 1e-9) return true;
            return false;
        }

        public struct Segment
        {
            public Bezier curve;           // y'si profilden doldurulmuş 3B eğri
            public double startDistance, endDistance;
            public SampleIssue issues;     // bu segmentteki örneklerin sorunları (birleşik)
        }

        /// <summary>Eğriyi en fazla maxSegmentLength boyunda, eşit uzunluklu segmentlere böler ve profil yüksekliklerini eğrilere yerleştirir.</summary>
        public static List<Segment> Segmentize(Bezier curve, List<HeightSample> profile, RoadSettings s)
            => Segmentize(new PolyCurve(curve), profile, s);

        /// <summary>Her parçayı ayrı ayrı en fazla maxSegmentLength boyunda eşit segmentlere böler; segmentler parça sınırını aşmaz.</summary>
        public static List<Segment> Segmentize(PolyCurve path, List<HeightSample> profile, RoadSettings s)
        {
            var result = new List<Segment>();
            double offset = 0;
            for (int pi = 0; pi < path.pieces.Count; pi++)
            {
                Bezier curve = path.pieces[pi];
                var table = path.tables[pi];
                double total = table[table.Count - 1].Value;
                int count = Math.Max(1, (int)Math.Ceiling(total / s.maxSegmentLength - 1e-9));
                for (int k = 0; k < count; k++)
                {
                    double l0 = total * k / count, l1 = total * (k + 1) / count;
                    Bezier seg = curve.Cut(TAtDistance(table, l0), TAtDistance(table, l1));
                    double s0 = offset + l0, s1 = offset + l1;
                    // Yükseklikler, eğrinin t = 1/3 ve 2/3 noktalarının gerçek yol mesafesinden okunur.
                    var segTable = ArcTable(seg, 64);
                    double segLen = segTable[segTable.Count - 1].Value;
                    double d13 = DistanceAtT(segTable, 1.0 / 3), d23 = DistanceAtT(segTable, 2.0 / 3);
                    double scale = segLen < 1e-9 ? 0 : (s1 - s0) / segLen;
                    double y0 = HeightAt(profile, s0), y3 = HeightAt(profile, s1);
                    double y13 = HeightAt(profile, s0 + d13 * scale), y23 = HeightAt(profile, s0 + d23 * scale);
                    FitY(ref seg, y0, y13, y23, y3);

                    SampleIssue issues = SampleIssue.None;
                    foreach (var p in profile) if (p.distance >= s0 - 1e-6 && p.distance <= s1 + 1e-6) issues |= p.issues;
                    result.Add(new Segment { curve = seg, startDistance = s0, endDistance = s1, issues = issues });
                }
                offset += total;
            }
            return result;
        }

        /// <summary>Yay uzunluğu tablosunda t parametresine karşılık gelen mesafe.</summary>
        public static double DistanceAtT(List<KeyValuePair<double, double>> table, double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return table[table.Count - 1].Value;
            for (int i = 1; i < table.Count; i++)
            {
                if (table[i].Key >= t)
                {
                    var a = table[i - 1]; var b = table[i];
                    double f = (t - a.Key) / Math.Max(1e-12, b.Key - a.Key);
                    return a.Value + (b.Value - a.Value) * f;
                }
            }
            return table[table.Count - 1].Value;
        }

        public static double HeightAt(List<HeightSample> profile, double dist)
        {
            if (dist <= profile[0].distance) return profile[0].position.y;
            for (int i = 1; i < profile.Count; i++)
            {
                if (dist <= profile[i].distance)
                {
                    var a = profile[i - 1]; var b = profile[i];
                    double f = (dist - a.distance) / Math.Max(1e-12, b.distance - a.distance);
                    return a.position.y + (b.position.y - a.position.y) * f;
                }
            }
            return profile[profile.Count - 1].position.y;
        }

        /// <summary>Bezier'in y kontrol değerlerini, eğri t=0, 1/3, 2/3, 1'de istenen yüksekliklerden geçecek şekilde çözer.</summary>
        public static void FitY(ref Bezier b, double y0, double y13, double y23, double y3)
        {
            // B(1/3) = (8a + 12b + 6c + d)/27,  B(2/3) = (a + 6b + 12c + 8d)/27
            double r1 = 27 * y13 - 8 * y0 - y3;   // 12b + 6c
            double r2 = 27 * y23 - y0 - 8 * y3;   // 6b + 12c
            double bY = (2 * r1 - r2) / 18.0;
            double cY = (2 * r2 - r1) / 18.0;
            b.a.y = y0; b.b.y = bY; b.c.y = cY; b.d.y = y3;
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
