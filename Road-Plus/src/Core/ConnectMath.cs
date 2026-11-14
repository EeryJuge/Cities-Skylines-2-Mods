// Road+ - game-independent math for multi-piece paths and the Connect tool (straight, smooth, arc, spiral).
// Like RoadMath.cs, this file does not touch the game DLLs and is unit tested outside the game.

using System;
using System.Collections.Generic;

namespace TF2YolAraci.Core
{
    /// <summary>A path made of several Bezier pieces joined end to end (XZ lengths).</summary>
    public sealed class PolyCurve
    {
        public readonly List<Bezier> pieces = new List<Bezier>();
        public readonly List<List<KeyValuePair<double, double>>> tables = new List<List<KeyValuePair<double, double>>>();
        public readonly List<double> starts = new List<double>();
        public double Length { get; private set; }

        public PolyCurve() { }
        public PolyCurve(Bezier single) { Add(single); }

        public void Add(Bezier piece)
        {
            var table = RoadMath.ArcTable(piece);
            pieces.Add(piece);
            tables.Add(table);
            starts.Add(Length);
            Length += table[table.Count - 1].Value;
        }

        public Vec3 Start => pieces[0].a;
        public Vec3 End => pieces[pieces.Count - 1].d;

        public Vec3 PositionAt(double dist)
        {
            int i = PieceAt(dist);
            return pieces[i].Position(RoadMath.TAtDistance(tables[i], dist - starts[i]));
        }

        public Vec3 DirectionAt(double dist)
        {
            int i = PieceAt(dist);
            return pieces[i].Tangent(RoadMath.TAtDistance(tables[i], dist - starts[i])).NormalizedXZ;
        }

        private int PieceAt(double dist)
        {
            for (int i = pieces.Count - 1; i > 0; i--) if (dist >= starts[i]) return i;
            return 0;
        }
    }

    public enum ConnectShape { Straight, Smooth, Arc, Spiral }

    /// <summary>How heights are set between two fixed ends (Connect and Slope tools).</summary>
    public enum EvenHeight { Even, Terrain, Level }

    public sealed class SpiralInfo
    {
        public int turns;            // full loops
        public double radius;
        public double risePerTurn;   // height gained or lost per full loop (m)
        public double grade;         // resulting grade along the path
    }

    public static class ConnectMath
    {
        public const double kMaxArcPieceRadians = Math.PI / 4; // 45°: Bezier arcs stay very accurate

        /// <summary>
        /// Builds the XZ path between two points. startDir: direction leaving the start (for example the direction
        /// of the road that ends there). endDir: direction arriving at the end. Both may be null.
        /// Returns false with a message when the shape cannot be built.
        /// </summary>
        public static bool Build(ConnectShape shape, Vec3 start, Vec3? startDir, Vec3 end, Vec3? endDir,
            double heightDiff, double slopeLimit, double spiralRadius, bool flipSpiral, double clearance,
            out PolyCurve path, out SpiralInfo spiral, out string error)
        {
            path = new PolyCurve();
            spiral = null;
            error = null;
            Vec3 a = start.FlatXZ, d = end.FlatXZ;
            Vec3 chord = d - a;
            double len = chord.LengthXZ;
            if (len < 1) { error = "The two points are too close."; return false; }
            Vec3 c = chord.NormalizedXZ;
            Vec3 d0 = Valid(startDir) ? startDir.Value.NormalizedXZ : c;
            Vec3 d3 = Valid(endDir) ? endDir.Value.NormalizedXZ : c;

            switch (shape)
            {
                case ConnectShape.Straight:
                    path.Add(Line(a, d));
                    return true;

                case ConnectShape.Smooth:
                {
                    if (!Valid(startDir) && !Valid(endDir)) { path.Add(Line(a, d)); return true; }
                    double h = len / 3.0;
                    path.Add(new Bezier(a, a + d0 * h, d - d3 * h, d));
                    return true;
                }

                case ConnectShape.Arc:
                {
                    // Circular arc that leaves the start along its road; without a start direction it arrives along the end road.
                    if (Valid(startDir))
                    {
                        if (!ArcPieces(a, d0, d, path)) { error = "Turn too sharp for one arc. Try Smooth or Spiral."; return false; }
                        return true;
                    }
                    if (Valid(endDir))
                    {
                        var back = new PolyCurve();
                        if (!ArcPieces(d, d3 * -1, a, back)) { error = "Turn too sharp for one arc. Try Smooth or Spiral."; return false; }
                        for (int i = back.pieces.Count - 1; i >= 0; i--) path.Add(Reverse(back.pieces[i]));
                        return true;
                    }
                    path.Add(Line(a, d));
                    return true;
                }

                default:
                    return Spiral(a, Valid(startDir) ? d0 : c, d, heightDiff, slopeLimit, spiralRadius, flipSpiral, clearance,
                        out path, out spiral, out error);
            }
        }

        /// <summary>
        /// Helix: leave the start along startDir onto a circle of the given radius, loop as many full turns as the
        /// slope limit needs for the height difference, then leave the circle on a straight line to the end.
        /// </summary>
        public static bool Spiral(Vec3 a, Vec3 d0, Vec3 end, double heightDiff, double slopeLimit, double radius, bool flip,
            double clearance, out PolyCurve path, out SpiralInfo info, out string error)
        {
            path = new PolyCurve();
            info = null;
            error = null;
            Vec3 b = end.FlatXZ;
            Vec3 normal = new Vec3(-d0.z, 0, d0.x); // perpendicular to the start direction in XZ

            // Put the circle on the side of the end point (or the other side when flipped).
            double side = Vec3.DotXZ(b - a, normal) >= 0 ? 1 : -1;
            if (flip) side = -side;

            Vec3 center = a + normal * (radius * side);
            Vec3 v = b - center;
            double dist = v.LengthXZ;
            if (dist < radius * 1.05)
            {
                error = "The end point is inside the spiral. Move it further away or make the radius smaller.";
                return false;
            }

            double psi0 = Math.Atan2(a.z - center.z, a.x - center.x);
            // Travel direction on the circle: +1 = angle increasing.
            double sigma = Vec3.DotXZ(d0, new Vec3(-Math.Sin(psi0), 0, Math.Cos(psi0))) >= 0 ? 1 : -1;

            double phi = Math.Atan2(v.z, v.x);
            double beta = Math.Acos(radius / dist);
            double exitPsi = double.NaN;
            foreach (double cand in new[] { phi + beta, phi - beta })
            {
                Vec3 p = new Vec3(center.x + radius * Math.Cos(cand), 0, center.z + radius * Math.Sin(cand));
                Vec3 vel = new Vec3(-Math.Sin(cand), 0, Math.Cos(cand)) * sigma;
                if (Vec3.DotXZ(vel, b - p) > 0) { exitPsi = cand; break; }
            }
            if (double.IsNaN(exitPsi)) { error = "Could not leave the spiral towards the end point."; return false; }

            double exitAngle = Mod2Pi(sigma * (exitPsi - psi0));
            double lineLen = Math.Sqrt(Math.Max(0, dist * dist - radius * radius));
            double rise = Math.Abs(heightDiff);
            double limit = Math.Max(1e-4, slopeLimit * 0.98);

            int turns = 0;
            while (rise / (radius * (exitAngle + 2 * Math.PI * turns) + lineLen) > limit && turns < 30) turns++;
            double totalAngle = exitAngle + 2 * Math.PI * turns;
            double total = radius * totalAngle + lineLen;
            double grade = total < 1e-9 ? 0 : rise / total;
            if (grade > limit + 1e-9)
            {
                error = "Height difference too large even for 30 loops. Make the radius bigger.";
                return false;
            }

            info = new SpiralInfo { turns = turns, radius = radius, grade = grade, risePerTurn = grade * 2 * Math.PI * radius };
            if (turns > 0 && info.risePerTurn < clearance)
            {
                error = "Loops are only " + info.risePerTurn.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        + " m apart. Make the radius bigger so the road fits under itself.";
                return false;
            }

            // Arc pieces of at most 45°, then the straight exit.
            int count = Math.Max(1, (int)Math.Ceiling(totalAngle / kMaxArcPieceRadians - 1e-9));
            double step = totalAngle / count;
            double handle = 4.0 / 3.0 * Math.Tan(step / 4) * radius;
            for (int i = 0; i < count; i++)
            {
                double t0 = psi0 + sigma * step * i, t1 = psi0 + sigma * step * (i + 1);
                Vec3 p0 = OnCircle(center, radius, t0), p1 = OnCircle(center, radius, t1);
                Vec3 v0 = new Vec3(-Math.Sin(t0), 0, Math.Cos(t0)) * sigma;
                Vec3 v1 = new Vec3(-Math.Sin(t1), 0, Math.Cos(t1)) * sigma;
                path.Add(new Bezier(p0, p0 + v0 * handle, p1 - v1 * handle, p1));
            }
            if (lineLen > 0.5) path.Add(Line(path.End, b));
            return true;
        }

        /// <summary>
        /// Circular arc leaving p0 along dir and ending at p3, split into pieces of at most 45°.
        /// Turns of up to 340° are allowed; false when the end lies straight behind the start.
        /// </summary>
        public static bool ArcPieces(Vec3 p0, Vec3 dir, Vec3 p3, PolyCurve path)
        {
            Vec3 chord = p3 - p0;
            double len = chord.LengthXZ;
            Vec3 d0 = dir.NormalizedXZ;
            double cosA = Math.Max(-1, Math.Min(1, Vec3.DotXZ(d0, chord.NormalizedXZ)));
            double alpha = Math.Acos(cosA);                 // tangent-chord angle = half of the turn
            if (alpha < 1e-4) { path.Add(Line(p0, p3)); return true; }
            if (alpha > Math.PI * 170.0 / 180.0) return false;

            Vec3 normal = new Vec3(-d0.z, 0, d0.x);
            double side = Vec3.DotXZ(chord, normal) >= 0 ? 1 : -1;
            double radius = len / (2 * Math.Sin(alpha));
            Vec3 center = p0 + normal * (radius * side);
            double psi0 = Math.Atan2(p0.z - center.z, p0.x - center.x);
            double sigma = Vec3.DotXZ(d0, new Vec3(-Math.Sin(psi0), 0, Math.Cos(psi0))) >= 0 ? 1 : -1;
            double total = 2 * alpha;

            int count = Math.Max(1, (int)Math.Ceiling(total / kMaxArcPieceRadians - 1e-9));
            double step = total / count;
            double handle = 4.0 / 3.0 * Math.Tan(step / 4) * radius;
            for (int i = 0; i < count; i++)
            {
                double t0 = psi0 + sigma * step * i, t1 = psi0 + sigma * step * (i + 1);
                Vec3 q0 = OnCircle(center, radius, t0), q1 = OnCircle(center, radius, t1);
                if (i == 0) q0 = p0.FlatXZ;
                if (i == count - 1) q1 = p3.FlatXZ;
                Vec3 v0 = new Vec3(-Math.Sin(t0), 0, Math.Cos(t0)) * sigma;
                Vec3 v1 = new Vec3(-Math.Sin(t1), 0, Math.Cos(t1)) * sigma;
                path.Add(new Bezier(q0, q0 + v0 * handle, q1 - v1 * handle, q1));
            }
            return true;
        }

        public static Bezier Line(Vec3 a, Vec3 d) => new Bezier(a, a + (d - a) * (1.0 / 3), a + (d - a) * (2.0 / 3), d);
        public static Bezier Reverse(Bezier b) => new Bezier(b.d, b.c, b.b, b.a);

        static Vec3 OnCircle(Vec3 c, double r, double ang) => new Vec3(c.x + r * Math.Cos(ang), 0, c.z + r * Math.Sin(ang));
        static double Mod2Pi(double x) { double t = x % (2 * Math.PI); return t < 0 ? t + 2 * Math.PI : t; }
        static bool Valid(Vec3? v) => v.HasValue && v.Value.LengthXZ > 1e-6;

        /// <summary>
        /// Height profile between two fixed end heights. Even: constant grade; Level: start height everywhere
        /// (the end moves to it); Terrain: smoothed terrain with both ends pinned.
        /// </summary>
        public static List<HeightSample> EvenProfile(PolyCurve path, Func<double, double, double> terrainAt, double startHeight,
            double endHeight, EvenHeight mode, SmoothingLevel smoothing, RoadClass roadClass, RoadSettings s)
        {
            switch (mode)
            {
                case EvenHeight.Level:
                    return RoadMath.BuildProfile(path, terrainAt, startHeight, null, HeightMode.Flat, 0, smoothing, roadClass, s);
                case EvenHeight.Terrain:
                    return RoadMath.BuildProfile(path, terrainAt, startHeight, endHeight, HeightMode.Terrain, 0, smoothing, roadClass, s);
                default:
                    return RoadMath.BuildProfile(path, terrainAt, startHeight, null, HeightMode.Flat, 0, smoothing, roadClass, s,
                        endHeight - startHeight);
            }
        }
    }
}
