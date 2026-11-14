// Oyun olmadan çalışan testler: mcs src/Core/RoadMath.cs tests/RoadMathTests.cs && mono RoadMathTests.exe
using System;
using System.Linq;
using TF2YolAraci.Core;

static class RoadMathTests
{
    static int failed = 0, passed = 0;

    static void Check(bool ok, string name)
    {
        if (ok) { passed++; Console.WriteLine("  OK   " + name); }
        else { failed++; Console.WriteLine("  HATA " + name); }
    }

    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

    static int Main()
    {
        var s = new RoadSettings();
        Func<double, double, double> flatGround = (x, z) => 10.0;

        Console.WriteLine("Eğri");
        {
            RoadMath.BuildCurve(new Vec3(0, 0, 0), null, new Vec3(90, 0, 0), s, out var c, out _);
            Check(Near(c.b.x, 30, 1e-9) && Near(c.c.x, 60, 1e-9), "başlangıç yönü yoksa düz çizgi, tutamaklar 1/3 ve 2/3");

            RoadMath.BuildCurve(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(100, 0, 0), s, out c, out var dEnd);
            Check(Near(dEnd.x, 1, 1e-9) && Near(c.b.x, 100.0 / 3, 1e-6), "yön kirişle aynıysa düz çizgi");

            // Çeyrek daire: (0,0)'dan +x yönüyle çık, (R,R)'ye var. R = 50.
            double R = 50;
            bool ok = RoadMath.BuildCurve(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(R, 0, R), s, out c, out dEnd);
            Check(ok, "90° dönüş kabul edilir");
            Check(Near(dEnd.x, 0, 1e-9) && Near(dEnd.z, 1, 1e-9), "çeyrek dairede bitiş yönü +z");
            Vec3 center = new Vec3(0, 0, R);
            double worst = 0;
            for (int i = 0; i <= 100; i++) worst = Math.Max(worst, Math.Abs((c.Position(i / 100.0) - center).LengthXZ - R));
            Check(worst / R < 0.0003, $"çeyrek daire yarıçap hatası < %0.03 (en kötü {worst / R * 100:0.0000}%)");
            Vec3 t0 = c.Tangent(0).NormalizedXZ;
            Check(Near(t0.x, 1, 1e-9) && Near(t0.z, 0, 1e-9), "başlangıçta teğet sürekliliği korunur");

            ok = RoadMath.BuildCurve(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(-50, 0, 10), s, out c, out _);
            Check(!ok, "geriye dönük (90°'den büyük) dönüş reddedilir");
        }

        Console.WriteLine("Yükseklik modları");
        {
            RoadMath.BuildCurve(new Vec3(0, 0, 0), null, new Vec3(200, 0, 0), s, out var c, out _);

            var flat = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Flat, 0, SmoothingLevel.Medium, RoadClass.Other, s);
            Check(flat.All(p => Near(p.position.y, 10, 1e-9) && p.issues == SampleIssue.None), "Düz mod: her yerde başlangıç yüksekliği, sorun yok");

            var up5 = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Graded, 0.05, SmoothingLevel.Medium, RoadClass.Highway, s);
            Check(Near(up5.Last().position.y, 20, 1e-6), "Eğimli +%5: 200 m sonra 10 m yukarıda");
            Check(up5.All(p => (p.issues & SampleIssue.TooSteep) == 0), "otoyolda %5 sınırı aşmaz");
            Check(up5.Last().issues.HasFlag(SampleIssue.Floating), "araziden 10 m yukarıda kalan uç 'havada' işaretlenir");

            var up10hw = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Graded, 0.10, SmoothingLevel.Medium, RoadClass.Highway, s);
            Check(up10hw.Any(p => p.issues.HasFlag(SampleIssue.TooSteep)), "otoyolda %10 eğim fazla dik sayılır");
            Check(!RoadMath.IsGradeAllowed(0.10, RoadClass.Highway, s) && RoadMath.IsGradeAllowed(-0.05, RoadClass.Highway, s), "otoyolda yalnızca ±%5 adımları izinli");
            Check(RoadMath.IsGradeAllowed(-0.10, RoadClass.Other, s) && !RoadMath.IsGradeAllowed(0.07, RoadClass.Other, s), "diğer yollarda ±%5/±%10 izinli, ara değer değil");

            var down10 = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Graded, -0.10, SmoothingLevel.Medium, RoadClass.Other, s);
            Check(down10.Last().issues.HasFlag(SampleIssue.Buried), "−%10 ile araziye giren uç 'gömülü' işaretlenir");
        }

        Console.WriteLine("Rampa");
        {
            Check(s.SlopeLimit(RoadClass.Ramp) > s.SlopeLimit(RoadClass.Highway), "rampa sınırı otoyoldan büyük");
            Check(RoadMath.IsGradeAllowed(0.10, RoadClass.Ramp, s), "rampada +%10 adımı izinli");
            RoadMath.BuildCurve(new Vec3(0, 0, 0), null, new Vec3(100, 0, 0), s, out var c, out _);
            var ramp = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Graded, 0.08, SmoothingLevel.Medium, RoadClass.Ramp, s);
            var hw = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Graded, 0.08, SmoothingLevel.Medium, RoadClass.Highway, s);
            Check(!ramp.Any(p => p.issues.HasFlag(SampleIssue.TooSteep)) && hw.Any(p => p.issues.HasFlag(SampleIssue.TooSteep)), "%8 eğim rampada geçer, ana otoyolda fazla dik");
        }

        Console.WriteLine("Arazi modu ve yumuşatma");
        {
            RoadMath.BuildCurve(new Vec3(0, 0, 0), null, new Vec3(400, 0, 0), s, out var c, out _);
            // Engebeli zemin: 10 m tabana 2 m dalgalanma (30 m dalga boyu)
            Func<double, double, double> bumpy = (x, z) => 10 + 2 * Math.Sin(x / 30.0 * 2 * Math.PI);
            Func<System.Collections.Generic.List<HeightSample>, double> Rough = p => p.Skip(1).Sum(q => Math.Abs(q.slope));

            var light = RoadMath.BuildProfile(c, bumpy, 10, null, HeightMode.Terrain, 0, SmoothingLevel.Light, RoadClass.Other, s);
            var strong = RoadMath.BuildProfile(c, bumpy, 10, null, HeightMode.Terrain, 0, SmoothingLevel.Strong, RoadClass.Other, s);
            Check(Rough(strong) < Rough(light) * 0.5, $"Güçlü yumuşatma tümsekleri Hafif'ten çok daha fazla düzler ({Rough(strong):0.00} < {Rough(light):0.00})");
            Check(Near(light[0].position.y, 10, 1e-9), "Arazi modunda başlangıç düğüm yüksekliğine sabitlenir");

            var pinned = RoadMath.BuildProfile(c, bumpy, 10, 14.0, HeightMode.Terrain, 0, SmoothingLevel.Medium, RoadClass.Other, s);
            Check(Near(pinned.Last().position.y, 14, 1e-9), "bitişte bağlanılan düğümün yüksekliğine sabitlenir");

            // Dik yamaç: 100 m içinde 30 m yükselen zemin (%30)
            Func<double, double, double> cliff = (x, z) => x < 150 ? 0 : x > 250 ? 30 : (x - 150) * 0.3;
            var hw = RoadMath.BuildProfile(c, cliff, 0, null, HeightMode.Terrain, 0, SmoothingLevel.Strong, RoadClass.Highway, s);
            Check(hw.Any(p => p.issues.HasFlag(SampleIssue.TooSteep)), "yumuşatma ne olursa olsun sınırı aşan yamaç kırmızı işaretlenir");
        }

        Console.WriteLine("Bitiş düğümü yüksekliği (Page Up / Page Down)");
        {
            RoadMath.BuildCurve(new Vec3(0, 0, 0), null, new Vec3(100, 0, 0), s, out var c, out _);
            var flatUp = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Flat, 0, SmoothingLevel.Medium, RoadClass.Other, s, 3);
            Check(Near(flatUp[0].position.y, 10, 1e-9) && Near(flatUp.Last().position.y, 13, 1e-9), "Düz modda +3 m: başlangıç 10 m'de kalır, bitiş 13 m'ye çıkar");

            var gradedUp = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Graded, 0.05, SmoothingLevel.Medium, RoadClass.Other, s, 2);
            Check(Near(gradedUp[0].position.y, 10, 1e-9) && Near(gradedUp.Last().position.y, 17, 1e-6), "Eğimli +%5 ve +2 m: bitiş 10 + 5 + 2 = 17 m");

            var terrainDown = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Terrain, 0, SmoothingLevel.Medium, RoadClass.Other, s, -1.5);
            Check(Near(terrainDown[0].position.y, 10, 1e-9) && Near(terrainDown.Last().position.y, 8.5, 1e-9), "Arazi modunda −1,5 m: yalnızca bitiş alçalır");
        }

        Console.WriteLine("Segmentlere bölme");
        {
            RoadMath.BuildCurve(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(150, 0, 150), s, out var c, out _);
            var prof = RoadMath.BuildProfile(c, flatGround, 10, null, HeightMode.Graded, 0.05, SmoothingLevel.Medium, RoadClass.Other, s);
            var segs = RoadMath.Segmentize(c, prof, s);
            double arc = RoadMath.ArcTable(c).Last().Value;
            Check(segs.Count == (int)Math.Ceiling(arc / s.maxSegmentLength), $"{arc:0} m yol {segs.Count} segmente bölünür (en fazla {s.maxSegmentLength} m)");
            bool joined = true;
            for (int i = 1; i < segs.Count; i++)
            {
                var gap = segs[i].curve.a - segs[i - 1].curve.d;
                if (Math.Sqrt(gap.x * gap.x + gap.y * gap.y + gap.z * gap.z) > 1e-6) joined = false;
            }
            Check(joined, "segmentler uç uca boşluksuz birleşir (y dahil)");
            bool yOk = segs.All(g => Near(g.curve.Position(0.5).y, RoadMath.HeightAt(prof, (g.startDistance + g.endDistance) / 2), 0.05));
            Check(yOk, "segment ortasındaki yükseklik profille uyuşur (5 cm içinde)");
            var g0 = segs[0].curve;
            double d13 = RoadMath.DistanceAtT(RoadMath.ArcTable(g0, 64), 1.0 / 3);
            Check(Near(g0.Position(1.0 / 3).y, RoadMath.HeightAt(prof, d13), 0.01), "FitY 1/3 noktasında, gerçek yol mesafesinde oturur");
        }


        Console.WriteLine("Bağlama: düz, yumuşak, yay");
        {
            var A = new Vec3(0, 20, 0); var B = new Vec3(200, 30, 100);
            ConnectMath.Build(ConnectShape.Straight, A, new Vec3(1, 0, 0), B, null, 10, 0.1, 50, false, 8, out var p, out _, out var err);
            Check(err == null && p.pieces.Count == 1 && Near((p.End - B.FlatXZ).LengthXZ, 0, 1e-9), "düz: tek parça, uca varır");

            ConnectMath.Build(ConnectShape.Smooth, A, new Vec3(1, 0, 0), B, new Vec3(0, 0, 1), 10, 0.1, 50, false, 8, out p, out _, out err);
            Check(err == null && Near(p.pieces[0].Tangent(0).NormalizedXZ.x, 1, 1e-9) && Near(p.pieces[0].Tangent(1).NormalizedXZ.z, 1, 1e-9),
                "yumuşak: başlangıçta mevcut yolun yönünde çıkar, bitişte gelen yolun yönünde varır");

            bool ok = ConnectMath.Build(ConnectShape.Arc, A, new Vec3(1, 0, 0), new Vec3(-100, 0, 100), null, 0, 0.1, 50, false, 8, out p, out _, out err);
            // 270°'lik dönüş değil: (−100, 100) noktası için teğet-kiriş açısı 135°, yani 270° dönüş.
            Check(ok && p.pieces.Count >= 6, $"yay: 270° dönüş 45°'lik {p.pieces.Count} parçaya bölünür");
            double R = 100; // (0,0) → (−100,100), +x yönüyle: merkez (0,100), yarıçap 100
            double worst = 0;
            foreach (var pc in p.pieces) for (int i = 0; i <= 20; i++) worst = Math.Max(worst, Math.Abs((pc.Position(i / 20.0) - new Vec3(0, 0, 100)).LengthXZ - R));
            Check(worst < 0.01, $"yay: daireden sapma 1 cm altında (en kötü {worst * 100:0.000} cm)");
            Check(Near((p.End - new Vec3(-100, 0, 100)).LengthXZ, 0, 1e-9) && Near((p.Start - A.FlatXZ).LengthXZ, 0, 1e-9), "yay: tam iki uçtan geçer");
            bool smooth = true;
            for (int i = 1; i < p.pieces.Count; i++)
                if (Vec3.DotXZ(p.pieces[i - 1].Tangent(1).NormalizedXZ, p.pieces[i].Tangent(0).NormalizedXZ) < 0.9999) smooth = false;
            Check(smooth, "yay: parça geçişlerinde kırık yok");
            Check(!ConnectMath.Build(ConnectShape.Arc, A, new Vec3(1, 0, 0), new Vec3(-100, 0, 1), null, 0, 0.1, 50, false, 8, out _, out _, out err) && err != null,
                "yay: hedef tam arkadaysa hata mesajı verir");
        }

        Console.WriteLine("Bağlama: spiral");
        {
            var A = new Vec3(0, 0, 0); var B = new Vec3(150, 40, 0);
            bool ok = ConnectMath.Build(ConnectShape.Spiral, A, new Vec3(1, 0, 0), B, null, 40, 0.05, 50, false, 8, out var p, out var info, out var err);
            Check(ok && err == null, "40 m yükseklik farkı %5 sınırla spirale sığar");
            Check(info.turns >= 1 && info.grade <= 0.05 + 1e-9, $"{info.turns} tur, eğim %{info.grade * 100:0.00} (sınır %5)");
            Check(Near(40 / p.Length, info.grade, 1e-6), "eğim = yükseklik farkı / toplam uzunluk");
            Check(Near((p.End - B.FlatXZ).LengthXZ, 0, 1e-6) && Near((p.Start - A).LengthXZ, 0, 1e-9), "spiral iki uca da varır");
            Check(Near(p.pieces[0].Tangent(0).NormalizedXZ.x, 1, 1e-9), "spirale başlangıç yolunun yönünde girer");
            bool smooth = true;
            for (int i = 1; i < p.pieces.Count; i++)
                if (Vec3.DotXZ(p.pieces[i - 1].Tangent(1).NormalizedXZ, p.pieces[i].Tangent(0).NormalizedXZ) < 0.9999) smooth = false;
            Check(smooth, "spiral: tüm parça geçişleri teğet (çıkış doğrusu dahil)");
            Check(info.risePerTurn >= 8, $"turlar arası {info.risePerTurn:0.0} m: yol kendi altından geçebilir");

            var prof = ConnectMath.EvenProfile(p, (x, z) => 0, 0, 40, EvenHeight.Even, SmoothingLevel.Medium, RoadClass.Other, s);
            Check(Near(prof[0].position.y, 0, 1e-9) && Near(prof.Last().position.y, 40, 1e-6) && prof.All(h => h.slope <= 0.05 + 1e-6),
                "spiral profili: 0 → 40 m sabit eğim, sınırın altında");
            var segs = RoadMath.Segmentize(p, prof, s);
            bool joined = true;
            for (int i = 1; i < segs.Count; i++)
            {
                var gap = segs[i].curve.a - segs[i - 1].curve.d;
                if (Math.Sqrt(gap.x * gap.x + gap.y * gap.y + gap.z * gap.z) > 1e-6) joined = false;
            }
            Check(joined && Near(segs.Last().curve.d.y, 40, 1e-6), $"spiral {segs.Count} segmente bölünür, boşluk yok, bitiş 40 m");

            Check(!ConnectMath.Build(ConnectShape.Spiral, A, new Vec3(1, 0, 0), new Vec3(30, 40, 30), null, 40, 0.05, 50, false, 8, out _, out _, out err) && err != null,
                "bitiş spiralin içindeyse hata mesajı");
            Check(!ConnectMath.Build(ConnectShape.Spiral, A, new Vec3(1, 0, 0), B, null, 40, 0.05, 15, false, 8, out _, out _, out err) && err != null && err.Contains("apart"),
                "yarıçap çok küçükse turlar sığmaz uyarısı");
            ConnectMath.Build(ConnectShape.Spiral, A, new Vec3(1, 0, 0), new Vec3(150, -40, 0), null, -40, 0.05, 50, true, 8, out var pDown, out var infoDown, out err);
            Check(err == null && infoDown.turns >= 1, "aşağı inen ve ters yöne dönen spiral de kurulur");
        }

        Console.WriteLine("Eğim aracı profilleri");
        {
            var path = new PolyCurve(ConnectMath.Line(new Vec3(0, 0, 0), new Vec3(100, 0, 0)));
            path.Add(ConnectMath.Line(new Vec3(100, 0, 0), new Vec3(200, 0, 0)));
            Func<double, double, double> bumpy = (x, z) => 10 + 3 * Math.Sin(x / 10);
            var even = ConnectMath.EvenProfile(path, bumpy, 10, 20, EvenHeight.Even, SmoothingLevel.Medium, RoadClass.Other, s);
            Check(Near(RoadMath.HeightAt(even, 100), 15, 1e-6), "düz eğim: ortadaki düğüm 10 ile 20 arasının tam ortasında (15 m)");
            var level = ConnectMath.EvenProfile(path, bumpy, 10, 20, EvenHeight.Level, SmoothingLevel.Medium, RoadClass.Other, s);
            Check(level.All(h => Near(h.position.y, 10, 1e-9)), "düzleştir: her yer başlangıç yüksekliğinde");
            var terr = ConnectMath.EvenProfile(path, bumpy, 10, 20, EvenHeight.Terrain, SmoothingLevel.Medium, RoadClass.Other, s);
            Check(Near(terr[0].position.y, 10, 1e-9) && Near(terr.Last().position.y, 20, 1e-9), "araziye uydur: iki uç sabit kalır");
        }

        Console.WriteLine($"\n{passed} geçti, {failed} başarısız");
        return failed == 0 ? 0 : 1;
    }
}
