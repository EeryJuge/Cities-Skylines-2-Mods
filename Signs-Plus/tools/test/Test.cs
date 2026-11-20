using System; using System.IO; using System.Globalization; using SignsPlus.Data; using SignsPlus.Rendering;
// Oyunsuz deneme: levha görüntüsünü ham RGBA dosyasına yazar.
//   mono test.exe                          → dört örnek levha (t1..t4.raw)
//   mono test.exe <yazı> <kategori 0-5> <genişlik m> <yükseklik m> <çıktı.raw>
static class Test {
    static void Dump(UnityEngine.Texture2D t, string path) {
        using (var f = File.Create(path)) { f.Write(BitConverter.GetBytes(t.width),0,4); f.Write(BitConverter.GetBytes(t.height),0,4);
            foreach (var c in t.px) { f.WriteByte(c.r); f.WriteByte(c.g); f.WriteByte(c.b); f.WriteByte(c.a);} } }
    static void Main(string[] a) {
        if (a.Length == 3 && a[0] == "std") {   // mono test.exe std <spec> <çıktı.raw>
            Dump(StandardSigns.Compose(a[1], 0.9f, 160, "std"), a[2]);
            return;
        }
        if (a.Length == 5) {
            var inv = CultureInfo.InvariantCulture;
            Dump(SignComposer.Compose(a[0], (SignCategory)int.Parse(a[1]), float.Parse(a[2], inv), float.Parse(a[3], inv), 160, "x"), a[4]);
            return;
        }
        string[][] samples = {
            new[]{ "Yakacık|Sancaktepe|[otoyol]Edirne {n:O-3}|[otoyol]Ankara {n:O-4}|{↗}", "2", "3.0", "2.2" },
            new[]{ "{n:E 80} {n:O-2}|Kavacık {s:kopru}|Fatih Sultan Mehmet|[x0.7]Köprüsü|{↓}", "0", "6.0", "2.5" },
            new[]{ "Bergama{>}25 km|[turistik]{s:cami}Pergamon{>}27 km|Balıkesir{>}102 km|İzmir{>}105 km", "1", "3.0", "2.2" },
            new[]{ "{s:ucak}Havalimanı{>}{ok:sag}|{s:merkez}Şehir Merkezi{>}{ok:sol}|[kgm]{n:K 11} Çıkış{>}2 km", "1", "4.0", "2.5" },
            new[]{ "İpsala {n:D-110}|Gümrük|[sinirdisi]Yunanistan {s:ucak:k}|{sagust}", "1", "3.0", "2.0" },
            new[]{ "{s:yaris}İstanbul Park{>}{ok:sagust}", "3", "3.5", "1.0" },
            new[]{ "{s:otoyol:k} {s:hastane:k} {s:otopark:k} {s:ilkyardim} {s:feribot} {s:benzin}|{s:tren}{s:otobus}{s:kamyon}{s:dinlenme}", "0", "4.0", "2.0" },
        };
        for (int i = 0; i < samples.Length; i++) {
            var x = samples[i]; var inv = CultureInfo.InvariantCulture;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var t = SignComposer.Compose(x[0], (SignCategory)int.Parse(x[1]), float.Parse(x[2], inv), float.Parse(x[3], inv), 160, "t" + (i + 1));
            Console.WriteLine("t" + (i + 1) + " " + t.width + "x" + t.height + " " + sw.ElapsedMilliseconds + " ms");
            Dump(t, "t" + (i + 1) + ".raw");
        }
        Console.WriteLine("ok");
    }
}
